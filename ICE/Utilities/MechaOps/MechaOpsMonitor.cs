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
    /// 取樣 <c>WKSMechaEventModule.Flags</c>。
    ///
    /// 存取路徑一共兩層，且刻意到此為止：
    ///   WKSManager.Instance()            → 靜態單例（同檔案的 MissionModule／ResearchModule
    ///                                      已經這樣用了，屬既有風險等級），null 檢查。
    ///   ->MechaEventModule               → WKSManager +0xE20 的指標欄位，null 檢查。
    ///   ->Flags                          → WKSMechaEventModule +0xA2A4 的 uint 位元欄位（純量）。
    ///
    /// 🔴 這裡永遠不會去碰以下東西，就算之後有人覺得「順手多讀一點」也不行：
    ///   - <c>CurrentEvent</c>（+0xA290 的 WKSMechaEvent*）
    ///   - <c>_events</c>（+0x30 的 FixedSizeArray2&lt;WKSMechaEvent&gt;）的內容
    ///   - WKSMechaEventMapMarker／MapMarkerPtrs 這類指標鏈
    /// WKSMechaEvent 是 0x5130 的大結構，台服完全沒有驗證過它的內部佈局。
    /// 讀錯偏移拿到的是垃圾指標，而 AccessViolationException 在 .NET Core 是
    /// corrupted-state exception：try/catch 與 HookSafety.ExecuteSafe 都攔不到，
    /// 會直接把使用者的遊戲帶走。
    ///
    /// 這也是本輪「只做事件狀態、不做事件進度」的原因——階段、剩餘時間、目標剩幾個
    /// 那些欄位全都在 WKSMechaEvent 裡面。要做的先決條件見 Ui/MechaOpsWindow 的註解。
    /// </summary>
    private static void ReadEventFlags()
    {
        var wks = WKSManager.Instance();
        if (wks != null && wks->MechaEventModule != null)
        {
            EventFlags = wks->MechaEventModule->Flags;
            EventFlagsValid = true;
            return;
        }

        EventFlags = 0;
        EventFlagsValid = false;
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
    }
}
