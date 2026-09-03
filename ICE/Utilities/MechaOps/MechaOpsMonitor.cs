using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Text;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 目前出現在 PetHotbar 上、且形狀可解析的機甲技能。
/// <para>
/// <paramref name="RecastTotal"/> / <paramref name="RecastRemaining"/> 是快照當下的重置時間（秒），
/// 由 <see cref="MechaOpsMonitor.SnapshotTick"/> 標記取樣時刻；顯示端要自己扣掉經過的牆鐘時間，
/// 才不會因為 250ms 的掃描節流而讀數跳動。<c>RecastTotal &lt;= 0</c> 代表這技能沒有重置時間或取不到。
/// </para>
/// </summary>
internal sealed record MechaCandidate(
    uint ActionId,
    string Name,
    MechaAoeShape Shape,
    float RecastTotal,
    float RecastRemaining);

/// <summary>
/// 一個「被 status 把關」的機甲技能的 proc 狀態（台服目前只有
/// 42037 強力胡蘿蔔加農砲 ← Status 4405 胡蘿蔔授權）。
/// <paramref name="RemainingSeconds"/> 只有在該 status 真的掛在本機玩家身上時才有值；
/// 若判定是靠 <c>IsActionHighlighted</c> 得到的，這裡會是 0。
/// </summary>
internal sealed record MechaProcState(
    uint ActionId,
    string ActionName,
    uint StatusId,
    string StatusName,
    bool Ready,
    float RemainingSeconds);

/// <summary>
/// 目前這場機甲事件的進度快照。
///
/// 🔑 這裡的每一個欄位都是 <c>WKSMechaEvent</c> 裡的**純量**（int／uint／旗標位元），
/// 取樣端絕不把讀到的值當指標用，也絕不去碰同一個結構裡的三個指標欄位
/// （<c>MapMarkerPtrs</c> 0x3910、<c>CurrentStateHandler</c> 0x50C8、
/// <c>WKSMechaEventDataRowPtr</c> 0x50D0）。
/// 因此就算台服的欄位語意跟國際服不同，最壞情況只是「數字不對」，不會是越界或崩潰。
///
/// <para>
/// ✅ 五個時間戳的時間基準**已經離線證明**（台服 7.20 ffxiv_dx11.exe，2026-08-03）：
/// 遊戲自己的 <c>WKSMechaEvent::IsPilotRegistrationTimeframeOpen</c>（0x1419AB2A0）與
/// <c>IsTeleportTimeframeOpen</c>（0x1419AB2E0）就是拿 <c>call 0x1400D9D30</c> 的回傳值
/// 去跟 <c>+0x50EC</c>／<c>+0x50E0</c>／<c>+0x50F0</c> 直接 <c>cmp</c>。
/// 而 CS 的 <c>Framework.GetServerTime()</c>（特徵碼 <c>E8 ?? ?? ?? ?? 03 07</c>）在同一個
/// 執行檔裡**唯一命中並解析到同一個 0x1400D9D30**。
/// 所以正確的比較基準是伺服器時間，不是本機的 <c>DateTimeOffset.UtcNow</c>——
/// 後者在使用者系統時鐘有偏差時會給出錯的倒數。
/// </para>
///
/// <paramref name="ServerTimeAtSample"/> 是取樣當下的伺服器秒數（取不到時為 0），
/// <paramref name="SampledTick"/> 是同一刻的 <see cref="Environment.TickCount64"/>。
/// 顯示端用 <see cref="ServerTimeNow"/> 把它外推到繪製當下，這樣倒數不會被
/// 250ms 的取樣節流卡成一格一格跳，也不需要在繪製執行緒上呼叫任何遊戲函式。
/// </summary>
internal sealed record MechaEventDetail(
    WKSMechaEventFlag Flags,
    uint DataRowId,
    int Progress,
    int ProgressMax,
    int Contribution,
    int PersonalProgress,
    int PersonalProgressMax,
    int EventStart,
    int EventEnd,
    int RegistrationStart,
    int RegistrationEnd,
    int TeleportStart,
    long ServerTimeAtSample,
    long SampledTick)
{
    /// <summary>
    /// 取樣時的伺服器秒數＋從那時起經過的牆鐘秒數。
    /// <c>0</c> 代表「這一輪拿不到伺服器時間」，顯示端要退回顯示原始整數。
    /// </summary>
    public long ServerTimeNow => ServerTimeAtSample <= 0
        ? 0
        : ServerTimeAtSample + (Environment.TickCount64 - SampledTick) / 1000L;

    /// <summary>
    /// 駕駛報名是否還開放。**照抄遊戲自己的判定**
    /// （<c>IsPilotRegistrationTimeframeOpen</c> @ 台服 0x1419AB2A0）：
    /// <code>
    ///   mov eax, [rcx+50DCh]   ; Flags
    ///   shr eax, 8             ; bit 8 = PilotRegistrationOpen
    ///   test al, 1
    ///   je  false
    ///   call 1400D9D30         ; = Framework.GetServerTime()
    ///   cmp eax, [rbx+50ECh]   ; PilotRegistrationEndTimestamp
    ///   jae false              ; now >= end -> 關閉
    /// </code>
    /// ⚠️ 遊戲**沒有**檢查開始時間戳（+0x50E8），這裡也不檢查。
    /// <paramref name="nowServer"/> 為 0（拿不到伺服器時間）時回 <c>null</c>＝無法判定。
    /// </summary>
    public bool? IsRegistrationOpen(long nowServer)
    {
        if ((Flags & WKSMechaEventFlag.PilotRegistrationOpen) == 0)
            return false;
        if (nowServer <= 0 || RegistrationEnd <= 0)
            return null;
        return nowServer < RegistrationEnd;
    }

    /// <summary>
    /// 協助員傳送是否還開放。同樣照抄遊戲自己的判定
    /// （<c>IsTeleportTimeframeOpen</c> @ 台服 0x1419AB2E0）：
    /// <code>
    ///   mov eax, [rcx+50DCh]   ; Flags
    ///   shr eax, 9             ; bit 9 = GroundSupportTeleportOpen
    ///   test al, 1
    ///   je  false
    ///   call 1400D9D30
    ///   cmp eax, [rbx+50E0h]   ; EventStartTimestamp
    ///   jae false              ; now >= 事件開始 -> 關閉
    ///   call 1400D9D30
    ///   cmp eax, [rbx+50F0h]   ; TeleportStartTimestamp
    ///   jbe false              ; now <= 傳送開始 -> 還沒開
    /// </code>
    /// 🔑 所以傳送視窗是 <c>(TeleportStart, EventStart)</c> —— **結束時間就是事件開始時間**。
    /// 舊版註解寫的「傳送只有開始時間戳、沒有結束，所以一律無法判定」是錯的。
    /// </summary>
    public bool? IsTeleportOpen(long nowServer)
    {
        if ((Flags & WKSMechaEventFlag.GroundSupportTeleportOpen) == 0)
            return false;
        if (nowServer <= 0 || EventStart <= 0 || TeleportStart <= 0)
            return null;
        return nowServer > TeleportStart && nowServer < EventStart;
    }
}

/// <summary>
/// 機甲行動偵察（P0）＋繪製快照的生產者。
/// 掛在 Framework.Update（ICE.Tick）；遊戲結構一律在這裡讀，
/// 繪製執行緒（<see cref="MechaAoeOverlay"/>）只讀本類別發布的不可變快照。
///
/// 離線無法證明、留待實機校準的假設（不成立時的退化行為都是「畫錯或不畫，不會崩」）：
///  1. 機甲技能的實際載體是 RaptureHotbarModule.PetHotbar、slot 型別是 Action——
///     若實際走別的 hotbar 或別的 slot 型別，候選永遠是空的 → 不繪製；
///     診斷 log 會顯示 16 格的原始內容（含型別），一看便知該改哪裡。
///  2. PetHotbar 是模組內定長內嵌陣列（非指標鏈），就算版本位移錯了也只會讀到
///     垃圾 id → 查不到表 → 不繪製；不會解參考野指標。
///  3. 「離開機甲後 PetHotbar 會清空或換內容」——若殘留，繪製會多畫（僅顯示問題），
///     診斷快照同樣看得出來。
///  4. （P3）「胡蘿蔔授權」這個 status 掛在本機玩家身上、而不是掛在機甲那具 BattleChara 上。
///     不成立時只會少掉剩餘秒數，提示本身仍然正確——見 <see cref="ResolveProc"/> 的備援設計。
///  5. （P3）六技共用重置群組（75／42261 是 76）的情況下，GetRecastTime/Elapsed 回的是
///     「使用者在熱鍵上看到的那個」冷卻。不成立時是讀數不準（顯示問題），不會崩。
///  6. ~~（P4）<c>WKSMechaEvent</c> 各欄位的語意沿用上游 CS，台服沒有反編譯驗證過。~~
///     ✅ 2026-08-03 已在台服 7.20 的 ffxiv_dx11.exe 上驗證（見下方「離線已證實」）。
///     即使如此，讀取範圍仍被 <see cref="IsInsideEventArray"/> 鎖在模組自有配置內。
///  7. ~~（P4）五個時間戳的 epoch 沒有離線證明。~~
///     ✅ 同上，已證實與 <c>Framework.GetServerTime()</c> 同基準，見 <see cref="MechaEventDetail"/>。
///
/// 📌 離線已證實（台服 7.20 <c>ffxiv_dx11.exe</c>，2026-08-03，capstone 反組譯）：
///  - <c>WKSManager+0xE20 = MechaEventModule</c>：全 .text 裡從該偏移做的 88 個 qword 載入，
///    有 42 個緊接著呼叫進 MechaEventModule 的程式碼區間 [0x141909000,0x14190F000)；
///    對照組 <c>+0xE50</c>(MissionModule)／<c>+0xE38</c> 各 0 個。
///  - <c>WKSMechaEventModule</c>：遊戲自己在 0x14190BC06 的存取順序就是
///    <c>test byte [rcx+0A2A4h],1</c>（Flags 的 HasCurrentEvent 位元）→
///    <c>mov rbx,[rcx+0A290h]</c>（CurrentEvent，qword 指標）→ <c>test rbx,rbx</c>，
///    跟 <see cref="TryReadEventDetail"/> 的前三關完全一致。
///  - <c>WKSMechaEvent</c>：<c>GetEventProgressPercentage</c>（0x1419AB330）讀 <c>+0x5104</c>／
///    <c>+0x5108</c>＝EventProgress／EventProgressMax；旗標位元 8／9 與時間戳
///    <c>+0x50E0</c>／<c>+0x50EC</c>／<c>+0x50F0</c> 見 <see cref="MechaEventDetail"/> 的兩個判定。
///  - 本輪用到的三個 ActionManager MemberFunction 特徵碼在台服**各自唯一命中**：
///    GetRecastTime→0x1408A6120、GetRecastTimeElapsed→0x1408A6070、
///    IsActionHighlighted→0x1408A2960（對照組 GetActionStatus→0x1408A08A0）。
///
/// 📌 特徵碼失準時不會產生 AccessViolation（已讀 CS 的 InteropGenerator 原始碼證實）：
/// 產生器會在每個 MemberFunction 呼叫前插入 null 檢查，解析失敗時走
/// <c>InteropGenerator.Runtime.ThrowHelper.ThrowNullAddress</c> 丟出一般的
/// <c>InvalidOperationException</c>，而不是拿 0 當函式指標去呼叫。
/// 這個受管理例外會被 <see cref="Tick"/> 的 try/catch 接住（每 60 秒記錄一次），
/// 退化行為是「疊加層與狀態視窗停止更新」，不是把遊戲帶走。
/// 本輪新增的三個 MemberFunction（GetRecastTime／GetRecastTimeElapsed／
/// IsActionHighlighted）也都已離線比對台服 7.20 的 ffxiv_dx11.exe，各自唯一命中。
/// </summary>
internal static unsafe class MechaOpsMonitor
{
    /// <summary>
    /// 目前的機甲技能快照。Framework 執行緒寫入、繪製執行緒讀取；
    /// 一律整個 List 換參考發布，發布後不再修改內容。
    /// </summary>
    public static IReadOnlyList<MechaCandidate> ActiveCandidates => activeCandidates;
    private static List<MechaCandidate> activeCandidates = [];

    /// <summary>
    /// 上一次發布快照的時刻（<see cref="Environment.TickCount64"/>）。
    /// 顯示端用它把 <see cref="MechaCandidate.RecastRemaining"/> 外推到當下，
    /// 讓倒數在 250ms 的掃描節流之間仍然是平滑的。
    /// </summary>
    public static long SnapshotTick { get; private set; }

    /// <summary>
    /// 目前「被 status 把關」的機甲技能的 proc 狀態快照。發布方式同 <see cref="ActiveCandidates"/>。
    /// </summary>
    public static IReadOnlyList<MechaProcState> ActiveProcs => activeProcs;
    private static List<MechaProcState> activeProcs = [];

    /// <summary>
    /// 目前畫得出來的目標點位快照。發布方式同 <see cref="ActiveCandidates"/>。
    ///
    /// ⚠️ 這一份跟其他快照不同，是<b>每幀</b>重新取樣的（見 <see cref="SampleTargets"/>）——
    /// 技能形狀與冷卻慢 250ms 沒差，但目標位置慢 250ms 就會讓「有沒有蓋到」的判定失真。
    /// </summary>
    public static IReadOnlyList<MechaTarget> ActiveTargets => activeTargets;
    private static List<MechaTarget> activeTargets = [];

    /// <summary>
    /// <c>WKSMechaEventModule.Flags</c> 的最新取樣值。只有 <see cref="EventFlagsValid"/>
    /// 為 true 時才有意義。
    /// </summary>
    public static WKSEventModuleFlag EventFlags { get; private set; }

    /// <summary>上一次取樣時 WKSManager 與 MechaEventModule 都拿得到。</summary>
    public static bool EventFlagsValid { get; private set; }

    /// <summary>
    /// 「身上有沒有駕駛申請書」。<c>null</c> ＝這一輪讀不到（模組資料還沒送到），
    /// 顯示端必須畫成「?」而不是「無」。
    ///
    /// 🔴 <b>它不是背包道具，所以沒有 <c>GetInventoryItemCount</c> 這條路。</b>
    /// 台服 <c>Item</c> 與 <c>EventItem</c> 兩張表裡<b>沒有任何一列</b>的名字含「申請書」
    /// （exd dump 與 live sqpack 兩邊各查過一次，都是 0 筆），它是伺服器直接送給
    /// <c>WKSMechaEventModule</c> 的一個旗標。附帶好處：既有的「過區時
    /// <c>GetInventoryItemCount</c> 回 0」那個坑在這條路上不存在。
    ///
    /// 🔬 <b>資料來源</b>（台服 7.20 <c>ffxiv_dx11.exe</c> 離線反組譯，2026-08-08）：
    /// <c>WKSMechaEventModule + 0xA2A9</c> 的一個 byte。CS 沒有替它命名，但遊戲自己拿它做兩件事：
    /// <code>
    /// // (1) 申請駕駛員的閘門 @ 0x140F8B4CD
    ///   mov   rcx, [rbx+108h]         ; WKSMechaEventModule
    ///   mov   eax, [rcx+0A2A4h]       ; Flags（＝CS 的 WKSEventModuleFlag）
    ///   shr   eax, 1 / test al, 1
    ///   jne   已經報名過了
    ///   cmp   byte [rcx+0A2A9h], 0
    ///   jne   繼續申請
    ///   mov   edx, 2A8Eh / call ShowLogMessage
    ///         ; LogMessage 10894 =「未持有駕駛申請書，無法申請。」
    ///
    /// // (2) 面板上那一格數字 @ 0x140F8B240
    ///   mov   edx, 41BCh              ; Addon 16828 =「UNKNOWN/UNKNOWN」
    ///   mov   r9d, 1                  ; 第二個參數<b>固定是 1</b> ← 上限就是一張
    ///   cmp   byte [rcx+0A2A9h], r8b  ; r8b = 0
    ///   setne r8b                     ; 第一個參數 ＝ 有沒有
    ///   call  FormatAddonText
    /// </code>
    /// ⇒ 遊戲自己就是把它畫成「0/1」或「1/1」，正好對上使用者說的
    /// 「駕駛申請書身上只能帶一張」。另外三處（0x140F89713／0x140F8B431／0x140F8BD16）
    /// 也全部拿它當「能不能申請」的前置條件，五處用法一致。
    /// </summary>
    public static bool? PilotTicketHeld { get; private set; }

    /// <summary>
    /// 這一輪判定的參與身份。判準是<b>手上有哪些機甲技能</b>（見 <see cref="ResolveRole"/>）。
    ///
    /// 🔑 <b>為什麼不用 <c>WKSEventModuleFlag.PilotApplicationAccepted</c></b>：那是「申請有沒有
    /// 中籤」，而 CS 對同一組結構裡的其他旗標明文註記「時間過了也不會被清掉」，我們無法離線
    /// 證明它在事件結束後會歸零。萬一它是黏著的，下一場事件就會把協助員誤判成駕駛員——
    /// 而那個方向的誤判正是使用者回報的「目標不一樣」。技能清單是<b>當下</b>的事實，
    /// 沒有這個問題。旗標仍然照樣印進診斷，讓實機資料自己說話。
    /// </summary>
    public static MechaRole Role { get; private set; } = MechaRole.Unknown;

    /// <summary>
    /// 目前這場機甲事件的進度快照，<c>null</c> 代表「這一輪讀不到」——
    /// 沒有 <see cref="WKSEventModuleFlag.HasCurrentEvent"/>、指標是 null，
    /// 或指標沒有通過 <see cref="IsInsideEventArray"/> 的範圍驗證。
    /// 三種情況顯示端一律不畫（不做任何降級顯示）。
    /// 發布方式同 <see cref="ActiveCandidates"/>：整個換參考，發布後不再修改。
    /// </summary>
    public static MechaEventDetail? EventDetail => eventDetail;
    private static MechaEventDetail? eventDetail;

    /// <summary>
    /// 機甲事件的<b>排程</b>快照：<c>_events</c> 兩格裡每一格「有填開始時間戳」的那些。
    /// 與 <see cref="EventDetail"/> 不同，這一份**不限於進行中的那場**——
    /// 使用者要的「下一次機甲事件是幾點」就是從這裡算出來的。
    /// 發布方式同 <see cref="ActiveCandidates"/>：整個換參考，發布後不再修改。
    /// </summary>
    public static IReadOnlyList<MechaScheduleEntry> Schedule => schedule;
    private static List<MechaScheduleEntry> schedule = [];

    /// <summary>
    /// 緊急事件（紅色警報）的狀態快照，<c>null</c> ＝這一輪讀不到或功能沒開。
    /// ⚠️ 只有 <c>C.ShowMechaEmergency</c> 開著時才會去取樣（那是部署閘門，預設關）。
    /// </summary>
    public static MechaEmergencyState? Emergency => emergency;
    private static MechaEmergencyState? emergency;

    private static string lastSignature = "";
    private static bool wasActive;

    public static void Tick()
    {
        try
        {
            TickInner();
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaOpsMonitorError", 60_000))
                IceLogging.Error($"MechaOpsMonitor tick failed: {ex}", "[MechaOps]");
        }
    }

    private static void TickInner()
    {
        // 🔑 這兩關刻意放在節流之前：離開宇宙區域／登出時要立刻清空，
        //    不能讓最多 250ms 的節流把一份過期的目標清單留在畫面上。
        if (!PlayerHelper.IsInCosmicZone() || !Player.Available)
        {
            Deactivate();
            return;
        }

        // 目的指示的 ObjectTable 解析每幀做：標記座標 250ms 更新一次沒差，
        // 但「對上的那個物件現在在哪」慢 250ms 就會讓方向箭頭指偏。
        // ⚠️ 只讀受管理 API，不碰任何原生結構。
        //
        // 🔑 順序有意義：它要先跑，SampleTargets 才拿得到**這一幀**的
        //    ConfirmedObjectIds／BaseId 白名單。反過來的話目標分級會固定慢一幀，
        //    表現就是任務目標在剛出現時閃一下才被認出來。
        MechaObjectiveTracker.ResolveFrame();

        // 目標點位每幀取樣（不進節流）——理由見 ActiveTargets 的註解。
        SampleTargets();

        if (!EzThrottler.Throttle("MechaOpsMonitorScan", 250))
            return;

        // 事件狀態要在機甲階段之外也能顯示（報名 → 中籤 → 加入），所以在
        // PetHotbar 的檢查之前就取樣。
        ReadEventFlags();

        // 「報名視窗開了」的一次性通知。純通知，不做任何遊戲動作（見該類別的註解）。
        // 🔑 放在這裡而不是繪製端：通知必須在使用者沒開任何機甲視窗時也送得出去，
        //    而它吃的就是上一行剛發布的排程快照，不再自己讀任何遊戲結構。
        MechaSignupNotifier.Tick(schedule);

        // 緊急事件跟機甲模組是兩條獨立的資料源（它走 AgentWKSAnnounce），
        // 所以就算 WKSManager／MechaEventModule 取不到也要照樣試。
        emergency = ReadEmergency();

        var module = RaptureHotbarModule.Instance();
        if (module == null)
        {
            DeactivateSkills();
            return;
        }

        var am = ActionManager.Instance();
        var candidates = new List<MechaCandidate>();
        var procs = new List<MechaProcState>();
        var signature = new StringBuilder(128);
        var dump = new StringBuilder(512);

        for (var i = 0u; i < 16; i++)
        {
            var slot = module->PetHotbar.GetHotbarSlot(i);
            if (slot == null)
                continue;

            var type = slot->CommandType;
            var id = slot->CommandId;
            if (type == RaptureHotbarModule.HotbarSlotType.Empty || id == 0)
            {
                signature.Append("-|");
                continue;
            }

            uint status = uint.MaxValue;
            var name = "?";
            if (type == RaptureHotbarModule.HotbarSlotType.Action)
            {
                if (am != null)
                    status = am->GetActionStatus(ActionType.Action, id);

                if (MechaActionShapes.TryResolve(id, out var shape, out name)
                    && MechaActionShapes.IsMechaCandidate(id))
                {
                    // 冷卻：一律走 ActionManager 的標準 API（傳 ActionType + ActionId 兩個純量，
                    // 由遊戲自己解算重置群組），完全不碰 WKS 結構、也不自己讀 RecastDetail 陣列。
                    // ⚠️ 台服 Action 表裡這六技共用重置群組 75（42261 是 76），而群組 75/76 是
                    //    「特殊內容動作」的公用池（104 個動作共用）。這裡不去猜群組語意——
                    //    GetRecastTime/Elapsed 回什麼就顯示什麼，與遊戲熱鍵上的轉圈一致。
                    float recastTotal = 0f, recastRemaining = 0f;
                    if (am != null)
                    {
                        recastTotal = am->GetRecastTime(ActionType.Action, id);
                        if (recastTotal > 0f)
                        {
                            var elapsed = am->GetRecastTimeElapsed(ActionType.Action, id);
                            recastRemaining = Math.Clamp(recastTotal - elapsed, 0f, recastTotal);
                        }
                    }

                    candidates.Add(new MechaCandidate(id, name, shape, recastTotal, recastRemaining));

                    // proc 提示（例如「胡蘿蔔授權」把關「強力胡蘿蔔加農砲」）。
                    if (MechaActionShapes.TryGetProcStatus(id, out var procStatusId, out var procStatusName))
                    {
                        var (ready, remaining) = ResolveProc(am, id, procStatusId);
                        procs.Add(new MechaProcState(id, name, procStatusId, procStatusName, ready, remaining));
                    }
                }
            }

            signature.Append((int)type).Append(':').Append(id).Append(':').Append(status).Append('|');
            dump.Append($"\n  slot{i:00} type={type} id={id} apparent={slot->ApparentActionId}({slot->ApparentSlotType}) name={name} status={status}");
        }

        // 快照發布：換參考，不就地修改。
        // 先寫時刻再換清單——顯示端最壞情況是用「稍早的時刻」去外推新清單，
        // 誤差方向是「倒數顯示得比實際少一點」，不會出現負數（顯示端有 clamp）。
        SnapshotTick = Environment.TickCount64;
        activeCandidates = candidates;
        activeProcs = procs;

        // 身份要在候選發布之後才算——它就是從這一份清單推出來的。
        Role = ResolveRole(candidates);
        ReportRole();

        // ---- 偵察診斷 ----
        // 只在「機甲技能可用期間」輸出；狀態變化時輸出一次，不每幀。
        // P2 實機校準已完成（形狀貼合、42258 扇形 90° 正確、PetHotbar 確認就是載體），
        // 所以 16 格原始傾印從 Information 降到 Debug；Information 只留一行摘要。
        // ⚠️ 之後若又要請使用者回傳原始格位，記得他的記錄等級會濾掉 Debug/Verbose，
        //    屆時要臨時把該行改回 Information，不要叫他去調記錄等級。
        if (candidates.Count == 0)
        {
            if (wasActive)
            {
                IceLogging.Info("機甲技能已從 PetHotbar 消失（機甲階段結束或假設 1/3 不成立）", "[MechaOps]");
                wasActive = false;
                lastSignature = "";
            }
            return;
        }

        wasActive = true;
        var sig = signature.ToString();
        if (sig == lastSignature)
            return;
        if (!EzThrottler.Throttle("MechaOpsDump", 1000))
            return; // 這輪先不印也不更新 lastSignature，下輪補印最終狀態
        lastSignature = sig;

        var pos = Player.Position;
        var rot = Player.Object.Rotation;
        var target = Svc.Targets.Target;

        // 🔴 這一行會進 IceLogging 的 LogSystem —— 那是一份 3000 筆的歷史紀錄，
        //    而且 UI 上有「複製記錄到剪貼簿」。目標可能是**其他玩家**，
        //    所以名字一律先過 MechaPrivacy（預設縮寫成「F. L.」）再寫進去。
        //    ⚠️ 遮蔽必須發生在字串**進入 log 之前**，不能只在顯示端做——
        //    紀錄一旦寫進去就補不回來了。
        var targetText = target == null
            ? "無"
            : $"{MechaPrivacy.Sanitize(target.Name.ToString(), target.ObjectKind, target.GameObjectId == Player.Object.GameObjectId)}"
              + $" @ ({target.Position.X:F1}, {target.Position.Y:F1}, {target.Position.Z:F1})";

        var candidateText = string.Join("; ", candidates.Select(c => $"{c.ActionId} {c.Name} [{c.Shape.Kind} {c.Shape.Primary:F0}/{c.Shape.HalfWidth * 2:F0}]"));

        // Information：一行摘要（幾個候選、哪些 id）。
        IceLogging.Info(
            $"機甲技能可用：{candidates.Count} 個 — {string.Join(", ", candidates.Select(c => $"{c.ActionId} {c.Name}"))}",
            "[MechaOps]");

        // Debug：完整 16 格傾印，只在需要重新校準時才需要。
        IceLogging.Debug(
            $"PetHotbar 偵察快照：{dump}\n" +
            $"  PetHotbarMode={module->PetHotbarMode}\n" +
            $"  玩家 pos=({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}) rot={rot:F3}rad\n" +
            $"  目標：{targetText}\n" +
            $"  繪製候選：{candidateText}",
            "[MechaOps]");
    }

    /// <summary>
    /// 列舉目前值得畫出來的目標，抄成純值快照發布。
    ///
    /// 🔴🔴 <b>絕不跨幀保存原生指標。</b> 這裡拿到的 <c>IGameObject</c> 只在這個迴圈裡活著，
    /// 抄走的是 <c>(GameObjectId, DataId, 名稱, 位置, hitbox, 種類)</c> 這些**純值**；
    /// <c>IGameObject</c> 本身與它的 <c>Address</c> 一個都不留。
    /// （<c>Address</c> 在建構時凍結、永不重解析，<c>IsValid()</c> 只檢查有沒有登入，
    ///  兩者都不是防護；物件被回收後解參考就是攔不住的 AccessViolationException。）
    ///
    /// 🔑 <b>過濾是語意的，不是二元的</b>（2026-08-06 改）。使用者回報：機甲任務
    /// 「有害菌床驅除指令」的目標<b>沒有名字</b>，而把「只顯示可選取的物件」關掉之後
    /// <b>整片場景與 NPC 都灌進來</b>。兩個症狀是同一個根因——舊碼唯一的判準是
    /// <c>IsTargetable</c>，而那個旗標既擋掉了真正的任務目標，也放行了所有場景裝飾。
    ///
    /// 現在的判準分三層（<see cref="MechaTargetTier"/>）：
    ///  1. <b>已確認</b>：這一幀有目的指示標記對上它 → <b>一律列出</b>，不受任何過濾影響；
    ///  2. <b>疑似</b>：<c>BaseId</c> 跟確認過的目標相同，或落在資料表白名單裡 → 同樣一律列出；
    ///  3. <b>其他</b>：才套用使用者的過濾（可選取／玩家／雜訊）。
    ///
    /// ⚠️ 仍然<b>不去猜</b>第 3 層裡哪一個是目標——過度篩選的失敗形式是「該顯示的沒顯示」，
    /// 那比多顯示幾個糟得多。新增的雜訊過濾只在使用者已經把「只顯示可選取的物件」
    /// 關掉時才生效，而且可以再關掉（<c>MechaTargetsHideSceneryAndNpcs</c>）。
    ///
    /// ⚠️ 每幀跑一次。成本是「掃一次 ObjectTable ＋ 讀幾個受管理屬性」，
    /// 而且整段被三個條件擋著（總開關、目標開關、機甲技能真的在 PetHotbar 上），
    /// 非機甲期間完全不會執行。
    /// </summary>
    private static void SampleTargets()
    {
        // 沒開、或根本不在機甲階段（PetHotbar 上沒有機甲技能）就不掃。
        //
        // 🔑 錄製模式（MechaEventRecorder）開著時強制取樣：那兩個顯示開關預設是關的，
        //    不強制的話錄出來的 log 會缺掉「ICE 判定出什麼」這個最重要的部分，
        //    而且使用者要跑完一整場才會發現。
        //    ⚠️ 這只影響**取樣**，繪製仍然完全由 C.ShowMechaAoeOverlay 決定
        //    （MechaAoeOverlay.DrawInner 第一行就擋掉了），所以畫面上不會多出任何東西，
        //    也不會動到使用者的任何一個設定值。錄製關著時行為與先前完全相同。
        if (!MechaEventRecorder.ForceSampling && (!C.ShowMechaAoeOverlay || !C.ShowMechaTargets))
        {
            ClearTargets();
            return;
        }

        if (activeCandidates.Count == 0)
        {
            ClearTargets();
            return;
        }

        var self = Player.Object;
        if (self == null)
        {
            ClearTargets();
            return;
        }

        var selfId = self.GameObjectId;
        var origin = self.Position;

        // 60 是目前最長的機甲技能射程；上限拉到 200 只是防呆，不是建議值。
        var maxDist = Math.Clamp(C.MechaTargetRadius, 5f, 200f);
        var maxDistSq = maxDist * maxDist;

        // 「這是不是我當前選取的目標」——只比對 id，不保留 Svc.Targets.Target 這個物件。
        var currentTargetId = Svc.Targets.Target?.GameObjectId ?? 0UL;

        // 這一幀的「已確認任務目標」清單（由 MechaObjectiveTracker.ResolveFrame 剛剛算好的）。
        var confirmedIds = MechaObjectiveTracker.ConfirmedObjectIds;

        var targets = new List<MechaTarget>();
        foreach (var obj in Svc.Objects)
        {
            if (obj == null || obj.GameObjectId == selfId)
                continue;

            var kind = obj.ObjectKind;

            // 永遠不可能是攻擊目標的東西。這是唯一寫死的排除清單，刻意保持很短。
            if (kind is ObjectKind.MountType or ObjectKind.Companion
                or ObjectKind.Ornament or ObjectKind.Retainer)
                continue;

            // 玩家的過濾放在分級之前：其他玩家不是任務目標，而且名字有隱私考量，
            // 這一條無論如何都尊重使用者的設定。
            if (kind == ObjectKind.Player && !C.MechaTargetsIncludePlayers)
                continue;

            // ⚠️ 距離先篩：底下的名字解析會配置字串，而 ObjectTable 動輒好幾百格、
            //    這個方法**每幀**都跑。座標比對是純量運算，放前面才不會白白配置一堆字串。
            var pos = obj.Position;
            var dx = pos.X - origin.X;
            var dz = pos.Z - origin.Z;
            if (dx * dx + dz * dz > maxDistSq)
                continue;

            // 📌 Dalamud 已把 DataId 改名為 BaseId（同一個 Struct->BaseId，值完全一樣），
            //    舊名還在但標了 [Obsolete]。新碼一律用 BaseId。
            var baseId = obj.BaseId;

            var tier = confirmedIds.Contains(obj.GameObjectId) ? MechaTargetTier.Objective
                : MechaObjectiveTracker.IsObjectiveBaseId(baseId) ? MechaTargetTier.Likely
                // 🔑 第三條線索：家族內用 ObjectKind 補分級（CardStand＝協助員 per-player 目標、
                //    EventObj＝駕駛員目標，四個 DataId 兩場實機全吻合，見 RoleByObjectKind）。
                //    ⚠️ 這是**純加法**：只把 Other 提成 Likely，永遠不會把上面兩層判出來的降級。
                //    它要補的是「群組表沒把這個 DataId 分給我這個身份」的情況——
                //    日後新增事件、新的 DataId 還沒進群組表時就靠這條接住。
                : MechaObjectiveTracker.IsRoleTargetByKind(baseId, kind) ? MechaTargetTier.Likely
                : MechaTargetTier.Other;

            var rawName = GameTextUtil.StripGameIcons(obj.Name.ToString());

            // 🔑 任務目標（前兩層）一律列出，不受「只顯示可選取的物件」與雜訊過濾影響。
            //    這就是使用者回報那兩個症狀的正解：目標不必是可選取的，
            //    也不必為了看到它而把所有場景物件一起放進來。
            //
            // 🔴🔴 <b>2026-08-08 追加的第三道豁免：機甲事件物件家族（不分身份）。</b>
            //    前兩層都是「這一場我們已經有證據」才成立；在那之前（剛 SPAWN、遊戲還沒標、
            //    或身份還判不出來）目標會落到第 3 層，然後被下面兩條過濾靜默吃掉。
            //    實機證據（2026-08-08 協助員錄製）：<c>did=2014721</c> 的小型偏屬性水晶
            //    308 次出現裡有 <b>151 次</b> 被 <c>known-noise</c> 濾掉，而它正是使用者
            //    每一發宇宙鑽頭都打到的那個目標。
            //    ⚠️ 這條豁免<b>只認 DataId 家族</b>，不是把過濾整個放寬——
            //    同一份 log 裡還有 221 筆 EventNpc 之類的真雜訊，仍然要靠下面兩條擋著。
            //    兩條都豁免的理由分別是：
            //      ① <c>MechaTargetsTargetableOnly</c>：協助員的目標是 per-player 生成的實體，
            //         <c>IsTargetable</c> 讀到 False，但地面施放的宇宙工具打得到它
            //         ——這個旗標對機甲目標的語意根本不對。log 直證同一場裡連
            //         2014720 巨型水晶對協助員也是 <c>targetable=False</c>。
            //      ② <c>MechaTargetsHideSceneryAndNpcs</c>：見 IsKnownNoise 的註解。
            if (tier == MechaTargetTier.Other && !MechaObjectNames.IsKnownEventObjectId(baseId))
            {
                if (C.MechaTargetsTargetableOnly)
                {
                    if (!obj.IsTargetable)
                        continue;
                }
                else if (C.MechaTargetsHideSceneryAndNpcs && IsKnownNoise(kind, rawName, obj.IsTargetable))
                {
                    continue;
                }
            }

            // 名字空的時候再問資料表（EObjName）。⚠️ 台服「有害菌床」兩邊都是空的，
            // 所以顯示端還要有自己的後備標籤——這裡不硬塞一個假名字進去。
            var label = rawName.Length > 0 ? rawName : MechaObjectNames.FromSheet(baseId) ?? string.Empty;

            targets.Add(new MechaTarget(
                obj.GameObjectId,
                baseId,
                rawName,
                label,
                pos,
                obj.HitboxRadius,
                kind,
                currentTargetId != 0 && obj.GameObjectId == currentTargetId,
                tier));
        }

        // 換參考發布，發布後不再修改內容。
        activeTargets = targets;
    }

    /// <summary>
    /// 從「現在手上有哪些機甲技能」推出參與身份。
    ///
    /// 📌 六個技能的身份歸屬在 <see cref="MechaActionShapes"/> 的表裡（2026-08-02 離線驗證），
    /// 而且與 <c>WKSMechaEventData</c> 的協助員指示文字互相印證（那段文字直接點名
    /// 宇宙鑽頭／宇宙火焰噴射器，正是被標成協助員的 42150／42258）。
    ///
    /// 🔴 <b>駕駛員技能優先</b>：真的坐進機甲時，協助員的宇宙工具有沒有殘留在熱鍵上
    /// 我們並不知道；反過來協助員手上絕不可能出現駕駛員技能。所以「看到駕駛員技能就是駕駛員」
    /// 這個方向是安全的，倒過來則不是。
    ///
    /// ⚠️ 兩邊都沒看到（例如還沒上機甲、或日後新增了沒驗證過的技能）就回
    /// <see cref="MechaRole.Unknown"/>，呼叫端一律退化成「只信遊戲自己的標記」。
    /// </summary>
    private static MechaRole ResolveRole(List<MechaCandidate> candidates)
    {
        var sawGroundSupport = false;

        foreach (var c in candidates)
        {
            switch (MechaActionShapes.RoleOf(c.ActionId))
            {
                case MechaRole.Pilot:
                    return MechaRole.Pilot;
                case MechaRole.GroundSupport:
                    sawGroundSupport = true;
                    break;
            }
        }

        return sawGroundSupport ? MechaRole.GroundSupport : MechaRole.Unknown;
    }

    private static string lastRoleSignature = "";

    /// <summary>
    /// 身份／白名單狀態變化時輸出一行 Information。
    /// 📌 使用者跑 LogLevel 2，Debug 收不到；而「ICE 以為我是哪一種身份」正是
    /// 「目標對不對得上」唯一問得出答案的地方。
    /// </summary>
    private static void ReportRole()
    {
        var rowId = eventDetail?.DataRowId ?? 0u;
        var sig = $"{Role}/{rowId}/{EventFlags}";
        if (sig == lastRoleSignature)
            return;
        lastRoleSignature = sig;

        var roleText = Role switch
        {
            MechaRole.Pilot => "駕駛員",
            MechaRole.GroundSupport => "協助員",
            _ => "判不出來",
        };

        var whitelist = Role == MechaRole.Unknown
            ? "不套用資料表白名單（只信遊戲自己的標記）"
            : $"資料表白名單 {MechaObjectNames.RoleIdCount(rowId, Role)} 個 DataId"
              + $"（分群來源：{MechaObjectNames.RoleSplitSource}）";

        var objective = MechaObjectNames.EventObjectiveText(rowId, Role);

        // 📌 身份分流（畫不畫）的兩個輸入：開關 ＋ 這一場的歸屬表。
        //    使用者回報「協助員看到駕駛員的目標」時，答案就在這一行——
        //    歸屬表是空的代表分流根本沒生效，跟「生效但沒擋到東西」是兩回事。
        var roleGate = C.MechaShowOtherRoleTargets
            ? "身份分流：關（使用者選擇顯示其他身份的目標）"
            : $"身份分流：開（只畫自己身份的目標）；歸屬表 {MechaObjectNames.DescribeOwners(rowId)}";

        IceLogging.Info(
            $"機甲身份判定：{roleText}（依據＝PetHotbar 上的技能 "
            + $"[{string.Join(", ", activeCandidates.Select(c => c.ActionId + " " + c.Name))}]）"
            + $"；事件列 {rowId}；{whitelist}"
            + $"；標記學到 {MechaObjectiveTracker.LearnedBaseIdCount} 個 BaseId"
            + $"；模組旗標 0x{(uint)EventFlags:X}（僅供對照，不參與判定）"
            + $"\n  {roleGate}"
            + (objective != null ? "\n  你的指示：" + objective.Replace("\n", " ") : ""),
            "[MechaOps]");
    }

    /// <summary>
    /// 「這一筆是已知的雜訊嗎」——只在使用者已經把「只顯示可選取的物件」關掉時才會被問到，
    /// 而且任務目標（<see cref="MechaTargetTier.Objective"/>／<see cref="MechaTargetTier.Likely"/>）
    /// 根本不會走到這裡。
    ///
    /// 🔑 判準刻意寫成「**已知**是雜訊」而不是「不像目標」：前者漏掉的東西照樣顯示（安全的失敗方向），
    /// 後者漏判就會把真正的目標藏起來。所以清單只放兩類：
    ///  ① 機能型物件——NPC、以太之光、採集點、房屋、區域、過場、卡牌台；
    ///  ② <b>不可選取</b>且<b>沒有名字</b>的場景裝飾。
    ///
    /// ⚠️ ② 的兩個條件必須同時成立。少了「沒有名字」會連無名的任務目標一起濾掉——
    /// 那正是這次要修的 bug（不過任務目標在上一層就已經放行了，這裡是第二道保險）。
    ///
    /// 🔴🔴 <b>2026-08-08 實機定錨：這條規則曾經是誤殺協助員真目標的現行犯。</b>
    /// 使用者以協助員跑完一場「巨型偏屬性水晶破壞指令」，他實際在打的
    /// <c>did=2014721</c>（<c>kind=CardStand</c>、無名、<c>targetable=False</c>）
    /// 308 次出現裡有 <b>151 次</b> 的 <c>iceFilter</c> 是 <c>known-noise</c>——
    /// 正好命中 ② 的兩個條件。
    ///
    /// 📌 <b>處置是「在上游豁免」而不是「放寬這裡」</b>，兩個理由：
    /// <list type="number">
    ///   <item>同一份 log 裡還有 221 筆真雜訊（無名 EventNpc 之類）靠這條擋著，
    ///         放寬會把它們全放進來——那是使用者當初回報的另一個症狀。</item>
    ///   <item><c>name.Length == 0</c> 這個條件只會讓「被叫做雜訊」的東西<b>變少</b>。
    ///         拿掉它反而更危險（變成「不可選取就是雜訊」）。</item>
    /// </list>
    /// ⇒ 真正的修法是 <see cref="SampleTargets"/> 裡新增的
    /// <c>MechaObjectNames.IsKnownEventObjectId</c> 豁免：機甲事件家族的 <c>DataId</c>
    /// 根本走不到這裡。<b>所以這裡的 <c>name.Length</c> 不是「用名字做目標判定」</b>——
    /// 目標身分在上游就已經用 DataId 決定完了，這裡只剩「其餘東西怎麼排序雜訊」。
    /// ⚠️ 日後要動這條之前，先確認那道豁免還在。
    /// </summary>
    /// ⚠️ <c>internal</c> 而不是 <c>private</c>：<see cref="MechaEventRecorder"/> 要拿它算
    /// 「這一筆會被哪一條規則濾掉」寫進診斷。**共用同一份判準才不會漂移**——
    /// 抄一份到錄製端的話，之後改了這裡而忘了改那裡，log 就會開始說謊。
    internal static bool IsKnownNoise(ObjectKind kind, string name, bool targetable)
    {
        if (kind is ObjectKind.EventNpc or ObjectKind.Aetheryte or ObjectKind.GatheringPoint
            or ObjectKind.Housing or ObjectKind.Area or ObjectKind.Cutscene or ObjectKind.CardStand)
            return true;

        return !targetable && name.Length == 0;
    }

    private static void ClearTargets()
    {
        if (activeTargets.Count > 0)
            activeTargets = [];
    }

    /// <summary>
    /// 判定一個 proc 現在亮著沒有，並盡量取得剩餘秒數。
    ///
    /// 兩條路互為備援，都是標準／既有路徑：
    ///  (a) 本機玩家的 StatusList（純 Dalamud 受管理 API，不碰原生指標）——能拿到剩餘秒數；
    ///  (b) <c>ActionManager.IsActionHighlighted</c>——遊戲自己用來決定要不要在熱鍵上畫
    ///      「螞蟻框」的判定，只傳 ActionType + ActionId 兩個純量。
    ///
    /// 為什麼兩條都留：離線無法證明機甲階段的 status 一定掛在本機玩家身上
    /// （也可能掛在機甲那具 BattleChara 上）。(b) 不管載體掛在哪都會給出正確答案，
    /// 所以即使 (a) 的假設不成立，提示本身仍然正確，只是少了剩餘秒數而已。
    /// 反過來若 (b) 的特徵碼日後失準，(a) 仍能撐住基本功能。
    /// </summary>
    private static (bool Ready, float RemainingSeconds) ResolveProc(ActionManager* am, uint actionId, uint statusId)
    {
        var ready = false;
        var remaining = 0f;

        // (a) 受管理 API。⚠️ 只在這一幀內用，絕不把 status／IGameObject 存進欄位。
        var statusList = Player.Status;
        if (statusList != null)
        {
            foreach (var status in statusList)
            {
                if (status.StatusId != statusId)
                    continue;
                ready = true;
                remaining = Math.Max(0f, status.RemainingTime);
                break;
            }
        }

        // (b) 遊戲自己的判定。
        if (!ready && am != null)
            ready = am->IsActionHighlighted(ActionType.Action, actionId);

        return (ready, remaining);
    }

    /// <summary>
    /// 取樣 <c>WKSMechaEventModule.Flags</c>，以及（通過範圍驗證時）目前這場事件的進度純量。
    ///
    /// 存取路徑：
    ///   WKSManager.Instance()            → 靜態單例（同檔案的 MissionModule／ResearchModule
    ///                                      已經這樣用了，屬既有風險等級），null 檢查。
    ///   ->MechaEventModule               → WKSManager +0xE20 的指標欄位，null 檢查。
    ///   ->Flags                          → WKSMechaEventModule +0xA2A4 的 uint 位元欄位（純量）。
    ///   ->CurrentEvent                   → +0xA290 的 WKSMechaEvent*，見 <see cref="TryReadEventDetail"/>：
    ///                                      旗標把關 → null 檢查 → **範圍驗證** 三關都過才解參考。
    ///
    /// 🔴 永遠不碰的東西（就算之後有人覺得「順手多讀一點」也不行）：
    ///   - <c>WKSMechaEvent.MapMarkerPtrs</c>（+0x3910 的 StdVector&lt;Pointer&lt;…&gt;&gt;）
    ///   - <c>WKSMechaEvent.CurrentStateHandler</c>（+0x50C8）
    ///   - <c>WKSMechaEvent.WKSMechaEventDataRowPtr</c>（+0x50D0）
    ///   - <c>_mapMarkers</c> 與任何 WKSMechaEventStageHandlerBase
    /// 這些都是指標；台服沒有驗證過 WKSMechaEvent 的內部佈局，偏移一錯就是垃圾指標，
    /// 而 AccessViolationException 在 .NET Core 是 corrupted-state exception：
    /// try/catch 與 HookSafety.ExecuteSafe 都攔不到，會直接把使用者的遊戲帶走。
    ///
    /// 反過來說，<b>只讀純量</b>時偏移讀錯的後果只是「拿到錯的數字」——
    /// 前提是讀取位置一定落在已配置的記憶體內，這正是範圍驗證要保證的事。
    /// </summary>
    private static void ReadEventFlags()
    {
        var wks = WKSManager.Instance();
        if (wks == null || wks->MechaEventModule == null)
        {
            EventFlags = 0;
            EventFlagsValid = false;
            PilotTicketHeld = null;
            eventDetail = null;
            if (schedule.Count > 0)
                schedule = [];
            MechaObjectiveTracker.ClearMarkersOnly();
            return;
        }

        var mod = wks->MechaEventModule;
        EventFlags = mod->Flags;
        EventFlagsValid = true;
        PilotTicketHeld = ReadPilotTicket(mod);
        eventDetail = TryReadEventDetail(mod);
        schedule = ReadSchedule(mod);
    }

    /// <summary>
    /// 「這個模組的資料伺服器送過來了沒」的旗標 byte。封包處理器（0x14190B901）收到資料時
    /// 把它設成 1，離開內容時（0x14190BE53）清成 0。
    ///
    /// 🔑 <b>為什麼一定要問這一格</b>：沒設過的時候 <see cref="PilotTicketHeldOffset"/>
    /// 只是零初始化的殘值。把它當成「沒有申請書」就會在使用者其實有票的時候顯示「無」，
    /// 害他白跑一趟去換一張已經有的票 —— 那是「不知道」不是「沒有」。
    /// </summary>
    private const int PilotTicketReadyOffset = 0xA2AA;

    /// <summary>「持有駕駛申請書」的旗標 byte。語意證據見 <see cref="PilotTicketHeld"/>。</summary>
    private const int PilotTicketHeldOffset = 0xA2A9;

    /// <summary>
    /// 讀出駕駛申請書的持有狀態。只讀兩個 byte，不解任何指標。
    ///
    /// 🔴 <b>邊界</b>：兩個偏移都必須落在 CS 宣告的模組配置（<c>Size = 0xA2B0</c>）之內。
    /// ✅ 那個大小本身也離線證實過 —— <c>0x140D28E5B</c> 的 <c>mov ecx, 0A2B0h</c> 就是
    /// WKSManager 建這個模組時傳給配置器的位元組數，與 CS 宣告的完全相同。
    /// 這裡仍然實測一次：日後 CS 若縮小了這個結構，退化行為是<b>本列自動變成「不知道」</b>，
    /// 而不是開始越界讀取（同 <see cref="IsInsideEventArray"/> 的 (i) 那條的用意）。
    /// </summary>
    private static bool? ReadPilotTicket(WKSMechaEventModule* mod)
    {
        if (sizeof(WKSMechaEventModule) <= PilotTicketReadyOffset)
            return null;

        var raw = (byte*)mod;
        if (raw[PilotTicketReadyOffset] == 0)
            return null;

        return raw[PilotTicketHeldOffset] != 0;
    }

    /// <summary>
    /// 掃 <c>_events</c> 的每一格，把「有填開始時間戳」的抄成純值快照。
    ///
    /// 🔑 <b>這條路徑不解任何指標</b>：<c>_events</c> 是模組內嵌的
    /// <c>FixedSizeArray2&lt;WKSMechaEvent&gt;</c>（<c>module + 0x30</c>），
    /// 不是指標鏈，所以<b>不需要</b> <see cref="IsInsideEventArray"/> 那套驗證——
    /// 那套驗證要解決的問題是「<c>CurrentEvent</c> 這個指標指到哪」，這裡根本沒有那個指標。
    /// 唯一前提是 <paramref name="mod"/> 本身有效，而呼叫端已經檢查過。
    ///
    /// ⚠️ 仍然保留一次「陣列整塊落在模組配置內」的算術檢查（與
    /// <see cref="IsInsideEventArray"/> 的 (i) 同義）：日後 CS 若改了佈局讓算術不再成立，
    /// 退化行為是<b>本功能自動停用</b>，而不是開始越界讀取。
    /// </summary>
    private static List<MechaScheduleEntry> ReadSchedule(WKSMechaEventModule* mod)
    {
        var list = new List<MechaScheduleEntry>();

        var slots = mod->Events;
        if (slots.Length <= 0)
            return list;

        var slotSize = (nint)sizeof(WKSMechaEvent);
        if (slotSize <= 0)
            return list;

        var arrayBase = GetEventArrayBase(mod);
        var arrayBytes = slots.Length * slotSize;
        var modBase = (nint)mod;
        var modBytes = (nint)sizeof(WKSMechaEventModule);
        if (arrayBase < modBase || arrayBase + arrayBytes > modBase + modBytes)
            return list;

        // 伺服器時間一輪只取一次；取不到（0）時顯示端會退回顯示原始整數。
        var now = TryGetServerTime();
        var tick = Environment.TickCount64;

        for (var i = 0; i < slots.Length; i++)
        {
            ref var ev = ref slots[i];

            // 沒填開始時間戳的格子＝這一格現在沒有排程，直接跳過。
            // 🔑 這是唯一的「有沒有資料」判準，刻意不看旗標——
            //    實機證據顯示事件還沒開始（旗標還沒亮）時時間戳就已經填好了。
            var start = ev.EventStartTimestamp;
            if (start <= 0)
                continue;

            list.Add(new MechaScheduleEntry(
                i,
                ev.WKSMechaEventDataRowId,
                ev.Flags,
                start,
                ev.EventEndTimestamp,
                ev.PilotRegistrationStartTimestamp,
                ev.PilotRegistrationEndTimestamp,
                ev.TeleportStartTimestamp,
                now,
                tick));
        }

        return list;
    }

    /// <summary>
    /// 取樣緊急事件（紅色警報）狀態。
    ///
    /// 🔴🔴 <b>這是部署閘門後面的東西（<c>C.ShowMechaEmergency</c> 預設關）。</b>
    /// 理由與同 repo 的 <c>MechaObjectiveUseMarkerVector</c> 完全一樣：
    /// <c>AgentWKSAnnounce.Data</c> 是一個我們<b>沒有辦法驗證大小</b>的堆積配置，
    /// CS 宣告它 <c>Size = 0xA8</c> 是照國際服的佈局，台服沒有離線驗證過。
    /// 若台服的配置比較小，讀 <c>+0xA0</c> 的 <c>State</c> 就是越界——
    /// 而 AccessViolationException 是 corrupted-state exception，<c>try/catch</c> 攔不到。
    /// ⇒ 在實機證實之前，預設不開；開了也只影響有主動打開機甲總開關的人。
    ///
    /// 🔴 只讀純量（三個 byte ＋ 一個 uint），<b>不碰</b> <c>FormattedString</c>
    /// 那個 <c>Utf8String</c>——理由見 <see cref="MechaEmergencyState"/>。
    /// </summary>
    private static MechaEmergencyState? ReadEmergency()
    {
        if (!C.ShowMechaEmergency)
            return null;

        var agent = AgentWKSAnnounce.Instance();
        if (agent == null)
            return null;

        var data = agent->Data;
        if (data == null)
            return null;

        return new MechaEmergencyState(
            data->State,                    // +0xA0 byte
            data->EmergencyInfoRowId,       // +0x04 byte
            data->EmergencyInfoSubRowId,    // +0x08 byte
            data->EndTime,                  // +0x8C uint（unix 秒）
            TryGetServerTime(),
            Environment.TickCount64);
    }

    /// <summary>
    /// 取出目前這場事件的進度純量；任何一關沒過就回 <c>null</c>（顯示端什麼都不畫）。
    ///
    /// 四關依序是：
    ///  1. <see cref="WKSEventModuleFlag.HasCurrentEvent"/> 位元——沒設就連讀指標欄位都不讀。
    ///  2. 指標 null 檢查。
    ///  3. <see cref="IsInsideEventArray"/> 範圍驗證（本設計的核心，說明見該方法）。
    ///  4. 只讀純量欄位，且一個指標欄位都不碰。
    /// </summary>
    private static MechaEventDetail? TryReadEventDetail(WKSMechaEventModule* mod)
    {
        if ((mod->Flags & WKSEventModuleFlag.HasCurrentEvent) == 0)
        {
            MechaObjectiveTracker.ClearMarkersOnly();
            return null;
        }

        var ev = mod->CurrentEvent;
        if (ev == null)
        {
            MechaObjectiveTracker.ClearMarkersOnly();
            return null;
        }

        if (!IsInsideEventArray(mod, ev))
        {
            MechaObjectiveTracker.ClearMarkersOnly();
            // ⚠️ 這是「合格的失敗」：驗證不過就當作讀不到，絕不放寬條件。
            //    節流到 60 秒一次，不要每幀洗版。
            if (EzThrottler.Throttle("MechaEventPointerRejected", 60_000))
            {
                IceLogging.Info(
                    "CurrentEvent 未通過範圍驗證，本輪不顯示事件進度（這是設計上的安全退化，不是錯誤）："
                    + $" module=0x{(nint)mod:X} events=0x{GetEventArrayBase(mod):X}"
                    + $" moduleSize=0x{sizeof(WKSMechaEventModule):X} slotSize=0x{sizeof(WKSMechaEvent):X}"
                    + $" current=0x{(nint)ev:X}",
                    "[MechaOps]");
            }
            return null;
        }

        // 🔑 目的指示的取樣掛在這裡而不是別處，是因為安全論證的前提就是
        //    「ev 已經通過 IsInsideEventArray」——放在這一行以外的任何地方都失去那個保證。
        MechaObjectiveTracker.SampleMarkers(ev);

        // ---- 以下全部是純量讀取，位置一律落在 _events 這塊模組自有配置裡 ----
        return new MechaEventDetail(
            ev->Flags,                              // +0x50DC uint 旗標
            ev->WKSMechaEventDataRowId,             // +0x50D8 uint（不是那個 RowPtr）
            ev->EventProgress,                      // +0x5104 int
            ev->EventProgressMax,                   // +0x5108 int
            ev->Contribution,                       // +0x510C int
            ev->PersonalProgress,                   // +0x5114 int
            ev->PersonalProgressMax,                // +0x5118 int
            ev->EventStartTimestamp,                // +0x50E0 int
            ev->EventEndTimestamp,                  // +0x50E4 int
            ev->PilotRegistrationStartTimestamp,    // +0x50E8 int
            ev->PilotRegistrationEndTimestamp,      // +0x50EC int
            ev->TeleportStartTimestamp,             // +0x50F0 int
            TryGetServerTime(),
            Environment.TickCount64);
    }

    /// <summary>
    /// 取樣伺服器時間（秒）。取不到就回 0，顯示端會退回顯示原始整數。
    ///
    /// 為什麼是伺服器時間而不是 <c>DateTimeOffset.UtcNow</c>：遊戲自己判斷報名／傳送視窗
    /// 開不開時，比較的就是這個值（見 <see cref="MechaEventDetail"/> 的兩段反組譯）。
    /// 用本機時鐘的話，使用者系統時間偏個幾分鐘倒數就是錯的，而且完全沒有徵兆。
    ///
    /// ⚠️ <c>GetServerTime</c> 是 MemberFunction：特徵碼失準時 CS 產生的程式碼會丟
    /// <c>InvalidOperationException</c>（**受管理例外，不是 AVE**，見類別註解）。
    /// 這裡就地接住而不讓它往上冒，是因為它若冒到 <see cref="Tick"/> 的 try/catch，
    /// <c>eventDetail</c> 會停在上一次的值＝畫面凍住還一直顯示舊倒數；
    /// 就地接住則退化成「只顯示原始整數」，使用者看得出來。
    /// （台服 7.20 實測唯一命中 0x1400D9D30，與遊戲自己呼叫的是同一個函式。）
    /// </summary>
    private static long TryGetServerTime()
    {
        try
        {
            var t = CSFramework.GetServerTime();
            return t > 0 ? t : 0;
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaServerTimeFailed", 60_000))
                IceLogging.Info($"取不到伺服器時間，事件時間改顯示原始整數：{ex.Message}", "[MechaOps]");
            return 0;
        }
    }

    /// <summary>
    /// <c>_events</c>（<c>FixedSizeArray2&lt;WKSMechaEvent&gt;</c> @ +0x30）的起始位址。
    /// 位址是從 CS 產生的 <c>Events</c> span 取得的，<b>不是</b> 我們自己寫死 0x30——
    /// 這樣 CS 之後改佈局時這裡會自動跟著走，不會變成過期的常數。
    /// </summary>
    private static nint GetEventArrayBase(WKSMechaEventModule* mod)
    {
        fixed (WKSMechaEvent* p = mod->Events)
            return (nint)p;
    }

    /// <summary>
    /// 🔑 <b>本功能的放行條件</b>：確認 <c>CurrentEvent</c> 真的指向模組自己的
    /// <c>_events</c> 陣列裡的某一格開頭。
    ///
    /// ✅ <b>這個條件在台服 7.20 已經離線證明會成立</b>（2026-08-03，反組譯 ffxiv_dx11.exe）——
    /// 也就是說這個檢查不會把功能整個擋掉。證據有兩段：
    /// <code>
    /// // 模組建構式 0x14190AAA0：兩格 WKSMechaEvent 就地建構在 module+0x30，步長 0x5130
    ///   14190AADE  lea rbx, [rsi+30h]      ; &amp;_events[0]
    ///   14190AAE2  lea edi, [rbp+2]        ; 2 格（＝FixedSizeArray2）
    ///   14190AB00  mov rcx, rbx / call 1419A9FB0
    ///   14190AB08  add rbx, 5130h          ; ＝sizeof(WKSMechaEvent)
    ///   14190AB15  mov qword [rsi+0A290h], rbp   ; CurrentEvent = 0
    ///   14190AB4C  mov dword [rsi+0A2A4h], eax   ; Flags = 0
    ///
    /// // 事件建立 0x14190B70C：塞進 map 的值就是 &amp;_events[i]，而 CurrentEvent 取自 map
    ///   14190B757  imul rax, rcx, 5130h
    ///   14190B75E  lea r12, [rbp+30h]
    ///   14190B762  add r12, rax            ; r12 = &amp;_events[i]
    ///   14190B7F8  mov qword [rax+28h], r12      ; map node 的 value
    ///   14190DC8E  mov qword [rdi+0A290h], rcx   ; CurrentEvent ← 該 value
    /// </code>
    /// 所以 <c>CurrentEvent</c> 只可能是 0 或 <c>module + 0x30 + i * 0x5130</c>（i ∈ {0,1}），
    /// 正好就是下面三項檢查放行的集合。
    ///
    /// 三項檢查，全部要過：
    ///  (i)   <c>_events</c> 整塊必須落在模組宣告的配置（0xA2B0）之內。
    ///        算術上這是 CS 的編譯期不變量（0x30 + 2 * 0x5130 = 0xA290 &lt;= 0xA2B0，
    ///        而 0xA290 剛好就是 <c>CurrentEvent</c> 的偏移，內部一致），
    ///        但這裡仍然實測一次：日後 CS 若改了佈局讓算術不再成立，
    ///        結果是「本功能自動停用」而不是「開始越界讀取」。
    ///  (ii)  指標必須落在 <c>[base, base + Length * slotSize)</c> 之內。
    ///  (iii) 且必須對齊到某一格的開頭（位移是 slotSize 的整數倍）。
    ///
    /// 三關都過就證明：後面所有的純量讀取（最遠到 +0x511C）都落在
    /// <c>_events</c> 這塊、也就是模組自己的配置裡面。
    /// 即使台服的欄位語意跟國際服不同，最壞也只是數字不對，<b>不可能越界</b>。
    ///
    /// ⚠️ 若實機發現 <c>CurrentEvent</c> 指向別處（例如另外的堆積配置），
    ///    正確做法是「維持驗證失敗、不顯示」並回報，<b>不是</b>放寬這裡的條件。
    /// </summary>
    private static bool IsInsideEventArray(WKSMechaEventModule* mod, WKSMechaEvent* candidate)
    {
        var slots = mod->Events;
        if (slots.Length <= 0)
            return false;

        var slotSize = (nint)sizeof(WKSMechaEvent);
        if (slotSize <= 0)
            return false;

        var arrayBase = GetEventArrayBase(mod);
        var arrayBytes = slots.Length * slotSize;

        // (i) 陣列整塊在模組配置內。
        var modBase = (nint)mod;
        var modBytes = (nint)sizeof(WKSMechaEventModule);
        if (arrayBase < modBase || arrayBase + arrayBytes > modBase + modBytes)
            return false;

        // (ii) 指標在陣列範圍內。
        var delta = (nint)candidate - arrayBase;
        if (delta < 0 || delta >= arrayBytes)
            return false;

        // (iii) 對齊到格子開頭。
        return delta % slotSize == 0;
    }

    /// <summary>只清掉技能相關的快照，事件狀態維持上一次的取樣值。</summary>
    private static void DeactivateSkills()
    {
        if (activeCandidates.Count > 0)
            activeCandidates = [];
        if (activeProcs.Count > 0)
            activeProcs = [];
        // 目標點位跟技能是一組的：沒有技能就沒有「有沒有蓋到」可言。
        ClearTargets();

        // 🔑 身份是從技能清單推出來的，清單沒了就不能繼續宣稱身份——
        //    留著上一輪的值等於「事件結束後還記得你是駕駛員」，而那正是
        //    我們刻意不用中籤旗標的理由。
        Role = MechaRole.Unknown;

        if (wasActive)
        {
            wasActive = false;
            lastSignature = "";
        }
    }

    /// <summary>離開宇宙區域：技能與事件狀態全部清空。</summary>
    private static void Deactivate()
    {
        DeactivateSkills();
        EventFlags = 0;
        EventFlagsValid = false;
        PilotTicketHeld = null;
        eventDetail = null;
        // 排程與緊急事件都是「宇宙區域內才有意義」的東西，離開就一起丟掉，
        // 不要在別的地圖上留一行過期的「下次機甲事件」。
        if (schedule.Count > 0)
            schedule = [];
        emergency = null;
        // 目的指示連同繫結與手動釘選一起丟掉——換區之後物件 id 一律失效。
        MechaObjectiveTracker.Deactivate();
    }
}
