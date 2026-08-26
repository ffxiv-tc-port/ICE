using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Text;

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
/// ⚠️ <paramref name="EventStart"/> 等五個時間戳的**時間基準沒有離線證明**。
/// CS 只把型別標成 <c>int</c>、沒有註明 epoch；台服 7.20 的執行檔也沒有反編譯驗證過。
/// 所以顯示端不會無條件套用換算式——它會拿當下的 UTC Unix 秒做合理性檢查，
/// 通過才顯示倒數（並標示為推定值），不通過就只顯示原始整數。
/// </para>
///
/// <paramref name="SampledUnixSeconds"/> 只用於診斷（顯示快照有多舊），
/// 倒數一律用繪製當下的時間去算，這樣才不會被 250ms 的取樣節流卡住。
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
    long SampledUnixSeconds);

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
///  6. （P4）<c>WKSMechaEvent</c> 各欄位的**語意**（哪個 int 是進度、哪個是貢獻）沿用上游 CS，
///     台服沒有反編譯驗證過。不成立時是「數字對應錯了」——因為只讀純量、
///     而且讀取範圍已被 <see cref="IsInsideEventArray"/> 鎖在模組自有配置內，所以不會越界。
///  7. （P4）五個時間戳的 epoch 沒有離線證明。顯示端不無條件換算：先跟當下的 UTC Unix 秒
///     做合理性檢查，不通過就只顯示原始整數並標明。不成立時是「只看得到原始值」，不會崩。
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
    /// <c>WKSMechaEventModule.Flags</c> 的最新取樣值。只有 <see cref="EventFlagsValid"/>
    /// 為 true 時才有意義。
    /// </summary>
    public static WKSEventModuleFlag EventFlags { get; private set; }

    /// <summary>上一次取樣時 WKSManager 與 MechaEventModule 都拿得到。</summary>
    public static bool EventFlagsValid { get; private set; }

    /// <summary>
    /// 目前這場機甲事件的進度快照，<c>null</c> 代表「這一輪讀不到」——
    /// 沒有 <see cref="WKSEventModuleFlag.HasCurrentEvent"/>、指標是 null，
    /// 或指標沒有通過 <see cref="IsInsideEventArray"/> 的範圍驗證。
    /// 三種情況顯示端一律不畫（不做任何降級顯示）。
    /// 發布方式同 <see cref="ActiveCandidates"/>：整個換參考，發布後不再修改。
    /// </summary>
    public static MechaEventDetail? EventDetail => eventDetail;
    private static MechaEventDetail? eventDetail;

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
        if (!EzThrottler.Throttle("MechaOpsMonitorScan", 250))
            return;

        if (!PlayerHelper.IsInCosmicZone() || !Player.Available)
        {
            Deactivate();
            return;
        }

        // 事件狀態要在機甲階段之外也能顯示（報名 → 中籤 → 加入），所以在
        // PetHotbar 的檢查之前就取樣。
        ReadEventFlags();

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
        var targetText = target == null
            ? "無"
            : $"{target.Name} @ ({target.Position.X:F1}, {target.Position.Y:F1}, {target.Position.Z:F1})";

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
            eventDetail = null;
            return;
        }

        var mod = wks->MechaEventModule;
        EventFlags = mod->Flags;
        EventFlagsValid = true;
        eventDetail = TryReadEventDetail(mod);
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
            return null;

        var ev = mod->CurrentEvent;
        if (ev == null)
            return null;

        if (!IsInsideEventArray(mod, ev))
        {
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
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
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
        eventDetail = null;
    }
}
