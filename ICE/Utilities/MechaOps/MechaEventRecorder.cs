using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Text;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 機甲事件錄製（debug 模式，<c>/ice d</c> →「機甲事件錄製」）。
///
/// 🔑 <b>它要回答的問題</b>（2026-08-08 使用者：「ICE 對機甲活動的支援還不夠，目標判定很奇怪」）：
/// 目標判定要修，得先有實機證據。這個模式把「一場事件從開始到結束、ICE 現行目標判定的
/// <b>全部輸入</b>」錄下來，讓「奇怪」可以離線重放：
/// <list type="number">
///   <item><b>預測側</b>：ICE 算出來的範圍參數（中心／朝向／形狀／半徑）＋範圍內的實體清單，
///         <b>每一筆都帶距離</b>——將來要改半徑就是拿這些距離來訂。</item>
///   <item><b>實際側</b>：計分欄位的變化（<c>EventProgress</c>／<c>PersonalProgress</c>／
///         <c>Contribution</c>）與實體從 ObjectTable 消失的時刻。</item>
///   <item><b>互動側</b>：玩家自己鎖定／互動過哪些 object（＝「正確答案」的真值來源），
///         而且 <b>分得出「人選的」與「外掛選的」</b>（<c>src=manual</c> / <c>src=ice</c>）。</item>
///   <item><b>對照</b>：事件結束時輸出「預測有但實際無」與「實際有但預測無」兩份差集。</item>
/// </list>
///
/// 🔴 <b>安全</b>（這個檔案一個原生指標都不碰）：
///  - 只讀 <see cref="MechaOpsMonitor"/>／<see cref="MechaObjectiveTracker"/> 已經發布的<b>不可變純值快照</b>
///    （那兩個類別自己有指標範圍驗證），加上 Dalamud 的受管理 API（<c>Svc.Objects</c>／<c>Svc.Targets</c>／<c>Player</c>）。
///  - 實體一律<b>只存 ObjectId 這個值</b>，每次取樣重新去 ObjectTable 查；
///    <c>IGameObject</c> 與它的 <c>Address</c> 一個都不跨幀保留
///    （<c>Address</c> 建構時凍結、<c>IsValid()</c> 只檢查有沒有登入，兩者都不是防護；
///     物件回收後解參考就是攔不住的 AccessViolationException）。
///  - 每一層都判空，讀不到就記 <c>?</c>／空字串，<b>不擲例外</b>。
///  - 整個 <see cref="Tick"/> 被 try/catch 包住，連續失敗 10 次就自動關掉錄製——
///    ⚠️ 那個 catch 攔得到的只有受管理例外（AVE 攔不到），所以真正的防護是「不碰指標」，
///    catch 只是讓錄製自己的邏輯錯誤不會拖垮 ICE 本體。
///
/// 🔴 <b>零自動化</b>：純讀取。不施放、不走位、不報名、不改任何遊戲狀態、不改任何使用者設定。
///
/// 📌 <b>輸出</b>：一律 <c>Information</c> 級（使用者跑 LogLevel 2，Debug/Verbose 收不到），
/// 統一前綴 <c>[MechaRec]</c>，一筆一行、<c>欄位=值</c> 以分號串接，方便 grep 與 diff。
/// ⚠️ 逐實體的大量行（<c>OBJ</c>／<c>MARK</c>／<c>OBJV</c>／<c>PRED</c>）只寫 <c>dalamud.log</c>，
/// <b>刻意不進 ICE 自己的 <c>LogSystem</c></b>——那是一份只有 3000 筆的環形紀錄，
/// 灌進去會把 ICE 其他的診斷全部擠掉。低量高價值的行（START／SNAP／CAST／VANISH／
/// target-change／interact／SCORE／END／SUMMARY）兩邊都寫。
/// </summary>
internal static class MechaEventRecorder
{
    private const string Prefix = "[MechaRec]";

    /// <summary>兩次完整快照之間的最短間隔。變化驅動之外的節流閘，避免每幀洪水。</summary>
    private const long MinIntervalMs = 1000;

    /// <summary>就算什麼都沒變也要記一筆的間隔（位置與計時仍然在動）。</summary>
    private const long HeartbeatMs = 15_000;

    /// <summary>事件訊號消失多久之後才收工。留一段餘裕才不會在階段切換的空檔把紀錄切成兩半。</summary>
    private const long IdleStopMs = 20_000;

    /// <summary>冷卻剩餘時間往上跳超過這個秒數就當成「這個技能剛剛被使用」。</summary>
    private const float CastDetectDelta = 0.5f;

    /// <summary>單一實體清單最多列幾筆（一行不要長到無法閱讀）。超過會標 <c>+N more</c>。</summary>
    private const int MaxListItems = 24;

    /// <summary>一次 OBJ 傾印最多幾個實體。⚠️ 被截掉的數量一定要印出來——「不知道」要看得見。</summary>
    private const int MaxObjectDump = 60;

    /// <summary>追蹤字典的上限，純防呆。一場機甲行動不會出現幾百個不同的相關實體。</summary>
    private const int MaxTracks = 512;

    // ---- 使用者可調（debug 用，刻意**不寫進設定檔**）----

    /// <summary>
    /// 總開關。預設關、<b>不持久化</b>：這是 debug 用的高流量診斷，
    /// 寫進設定檔的話會在使用者忘記關掉之後永遠灌 log。
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// 不等事件旗標，直接強制開一段錄製（讓使用者在事件外也能確認這個功能真的會動）。
    /// </summary>
    public static bool ForceSession { get; set; }

    /// <summary>ObjectTable 掃描半徑（公尺，XZ 平面）。刻意比 ICE 自己的列舉半徑寬。</summary>
    public static float SweepRadius { get; set; } = 100f;

    /// <summary>要不要逐筆傾印 ObjectTable。關掉只留統計與差集，log 會小很多。</summary>
    public static bool DumpObjects { get; set; } = true;

    /// <summary>
    /// 錄製期間要不要強制 <see cref="MechaOpsMonitor"/> 與 <see cref="MechaObjectiveTracker"/> 取樣。
    ///
    /// 🔑 <b>為什麼需要</b>：那兩個取樣路徑本來掛在 <c>C.ShowMechaAoeOverlay</c> 底下（預設關），
    /// 疊加層沒開就什麼都不算——那樣錄出來的 log 會缺掉「ICE 判定出什麼」這個最重要的部分，
    /// 而且使用者要跑完一整場才會發現。
    ///
    /// ⚠️ 這<b>只影響取樣</b>，不影響繪製：疊加層要不要畫仍然完全由 <c>C.ShowMechaAoeOverlay</c> 決定
    /// （見 <see cref="MechaAoeOverlay"/> 開頭那道閘門）。也就是說開錄不會讓畫面上多出任何東西，
    /// 也不會動到使用者的任何一個設定值。
    /// </summary>
    public static bool ForceSampling => Enabled;

    // ---- 狀態（給 UI 顯示用）----

    public static bool SessionActive => sessionActive;
    public static int SnapshotCount => snapshotSeq;
    public static int CastCount => castCount;
    public static int VanishCount => vanishCount;
    public static int InteractionCount => interactionCount;
    public static int TrackedCount => tracks.Count;
    public static double SessionSeconds => sessionActive ? (Environment.TickCount64 - sessionStartTick) / 1000.0 : 0;
    public static string LastStopReason { get; private set; } = "";

    private static bool sessionActive;
    private static long sessionStartTick;
    private static int snapshotSeq;
    private static int castCount;
    private static int vanishCount;
    private static int interactionCount;
    private static int consecutiveFailures;

    private static long lastSnapshotTick;
    private static long idleSinceTick;
    private static string lastSignature = "";

    private static int lastProgress, lastPersonal, lastContribution;
    private static bool progressBaselineSet;

    /// <summary>
    /// 上一次掃描裡有幾格的 <c>ObjectId</c> 跟前面某一格重複。
    /// 🔑 <c>&gt;0</c> 代表這一輪所有以 id 為鍵的統計都是合計值，見 <see cref="UpdateTracks"/>。
    /// </summary>
    private static int lastDuplicateOidCount;

    private static ulong lastHardTargetId, lastSoftTargetId, lastFocusTargetId;

    /// <summary>ActionId → 上一次看到的冷卻剩餘秒數。用來判「剛剛施放了」。</summary>
    private static readonly Dictionary<uint, float> lastRecast = [];

    /// <summary>ICE 自己剛剛指定過的物件 id → 時刻。用來把後續的 target 變化標成 <c>src=ice</c>。</summary>
    private static readonly Dictionary<ulong, long> iceInitiated = [];

    /// <summary>這一場事件見過的實體。🔴 只有值，沒有任何 <c>IGameObject</c>。</summary>
    private static readonly Dictionary<ulong, RecordedEntity> tracks = [];

    /// <summary>去重後的互動清單（玩家與 ICE 分開計數）。</summary>
    private static readonly Dictionary<ulong, InteractionTally> interactions = [];

    /// <summary>
    /// 一個實體在這一場事件裡的完整生命週期。<b>全部是純值</b>。
    /// </summary>
    private sealed class RecordedEntity
    {
        public ulong ObjectId;
        public uint BaseId;
        public ObjectKind Kind;
        public string Name = "";
        public Vector3 FirstPos;
        public Vector3 LastPos;
        public float LastDistance;
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public int SeenSnapshots;

        /// <summary>被任一啟用中的技能「預測會蓋到」的快照數。</summary>
        public int PredictedSnapshots;

        /// <summary>被預測蓋到時，離預測中心最近的一次距離。<c>MaxValue</c>＝從沒被預測到。</summary>
        public float MinPredictedDistance = float.MaxValue;

        /// <summary>曾經出現在 ICE 自己的目標清單（<c>MechaOpsMonitor.ActiveTargets</c>）裡。</summary>
        public bool EverIceListed;

        /// <summary>看過的最高分級。</summary>
        public MechaTargetTier BestTier;

        /// <summary>
        /// 🔴 <b>這個 ObjectId 在同一次掃描裡出現過不只一格</b>（＝好幾個實體共用同一個
        /// <c>GameObjectId</c>）。詳見 <see cref="UpdateTracks"/> 的說明——
        /// 為 true 時這一筆的所有計數都是<b>好幾個實體的合計</b>，不是單一實體的。
        /// </summary>
        public bool DuplicateId;

        /// <summary>從 ObjectTable 消失（＝「實際命中／被消滅」的<b>代理訊號</b>，侷限見 <see cref="EmitSummary"/>）。</summary>
        public bool Vanished;
        public DateTime VanishedAt;

        /// <summary>消失前後那一段的計分變化，用來判「這一次消失有沒有算在我頭上」。</summary>
        public int ProgressDeltaAtVanish;
        public int PersonalDeltaAtVanish;
    }

    private readonly record struct InteractionTally(int Manual, int Ice, uint BaseId, string Name, ObjectKind Kind);

    /// <summary>ObjectTable 的一格純值快照。⚠️ <b>不做任何名字／種類過濾</b>——見 <see cref="Sweep"/>。</summary>
    private readonly record struct SweepEntry(
        ulong ObjectId,
        uint BaseId,
        ObjectKind Kind,
        string Name,
        Vector3 Position,
        float HitboxRadius,
        bool Targetable,
        float Distance,
        int Hp,
        int MaxHp);

    // ────────────────────────────────────────────────────────────────────
    //  進入點
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 掛在 <c>ICE.Tick</c>（Framework 執行緒），<b>排在 <see cref="MechaOpsMonitor.Tick"/> 之後</b>——
    /// 這樣讀到的一定是這一幀最新的快照。
    /// 🔑 刻意<b>不</b>寫進 MechaOpsMonitor 的內部流程：那裡有好幾條提前 return，
    /// 插進去會改到既有的控制流，而錄製功能沒有任何理由承擔那個風險。
    /// </summary>
    public static void Tick()
    {
        if (!Enabled)
        {
            if (sessionActive)
                StopSession("錄製開關已關閉");
            return;
        }

        try
        {
            TickInner();
            consecutiveFailures = 0;
        }
        catch (Exception ex)
        {
            consecutiveFailures++;
            if (EzThrottler.Throttle("MechaRecorderError", 10_000))
                IceLogging.Error($"錄製這一輪失敗（第 {consecutiveFailures} 次，ICE 其他功能不受影響）：{ex}", Prefix);

            if (consecutiveFailures >= 10)
            {
                Enabled = false;
                sessionActive = false;
                LastStopReason = "連續失敗自動停止";
                IceLogging.Info("連續失敗 10 次，已自動關閉機甲事件錄製（ICE 其他功能不受影響）", Prefix);
            }
        }
    }

    private static void TickInner()
    {
        if (!PlayerHelper.IsInCosmicZone() || !Player.Available)
        {
            if (sessionActive)
                StopSession("離開宇宙區域或登出");
            return;
        }

        // 互動序列每幀輪詢：目標切換是瞬間的事，1 秒節流會漏掉。
        // ⚠️ 只讀受管理 API 並立刻抄成純值，不留任何物件。
        PollTargets();

        var hasSignal = HasEventSignal();

        if (!sessionActive)
        {
            if (!hasSignal && !ForceSession)
                return;
            StartSession(hasSignal);
        }
        else if (!hasSignal && !ForceSession)
        {
            // 階段切換時旗標可能短暫歸零，所以要有一段餘裕才收工。
            if (idleSinceTick == 0)
                idleSinceTick = Environment.TickCount64;
            else if (Environment.TickCount64 - idleSinceTick >= IdleStopMs)
            {
                StopSession("事件訊號消失");
                return;
            }
        }
        else
        {
            idleSinceTick = 0;
        }

        // 施放偵測同樣每幀跑（只讀已發布的快照，成本是走一遍最多 16 筆的清單）。
        DetectCasts();

        var now = Environment.TickCount64;
        if (now - lastSnapshotTick < MinIntervalMs)
            return;

        var heartbeat = now - lastSnapshotTick >= HeartbeatMs;
        var sig = BuildSignature();
        if (!heartbeat && sig == lastSignature)
            return;

        lastSnapshotTick = now;
        lastSignature = sig;
        EmitSnapshot(heartbeat);
    }

    /// <summary>
    /// 「現在有沒有機甲事件相關的訊號」。四條線索取聯集，任何一條成立就開錄——
    /// 🔑 刻意寬鬆：漏錄一場的代價是使用者白跑一場，多錄一段的代價只是 log 長一點。
    /// </summary>
    private static bool HasEventSignal()
        => MechaOpsMonitor.EventDetail != null
        || (MechaOpsMonitor.EventFlagsValid && MechaOpsMonitor.EventFlags != 0)
        || MechaOpsMonitor.ActiveCandidates.Count > 0
        || MechaObjectiveTracker.MarkerCount > 0;

    // ────────────────────────────────────────────────────────────────────
    //  一段錄製的開始與結束
    // ────────────────────────────────────────────────────────────────────

    private static void StartSession(bool bySignal)
    {
        sessionActive = true;
        sessionStartTick = Environment.TickCount64;
        snapshotSeq = 0;
        castCount = 0;
        vanishCount = 0;
        interactionCount = 0;
        idleSinceTick = 0;
        lastSnapshotTick = 0;
        lastSignature = "";
        lastSweepIdSignature = "";
        lastDuplicateOidCount = 0;
        progressBaselineSet = false;
        tracks.Clear();
        interactions.Clear();
        lastRecast.Clear();
        iceInitiated.Clear();
        LastStopReason = "";

        var d = MechaOpsMonitor.EventDetail;
        var rowId = d?.DataRowId ?? 0u;

        EmitRing(
            $"START;t={DateTime.Now:yyyy-MM-dd HH:mm:ss};trigger={(bySignal ? "event-signal" : "manual")}"
            + $";zone={Player.Territory};role={RoleText(MechaOpsMonitor.Role)}"
            + $";evRow={rowId};evName={Sanitize(MechaObjectNames.EventName(rowId) ?? "")}"
            + $";sweepRadius={SweepRadius:F0};dumpObjects={DumpObjects}");

        // 設定快照：判定的每一個輸入參數都要留底，否則事後看 log 分不出
        // 「ICE 判錯了」與「使用者把某個開關關掉了」。
        EmitRing(
            "START-CFG"
            + $";overlay={C.ShowMechaAoeOverlay};targets={C.ShowMechaTargets};objectives={C.ShowMechaObjectives}"
            + $";iceRadius={C.MechaTargetRadius:F0};coneDeg={C.MechaConeAngleDeg:F0};useHitbox={C.MechaCoverageUseHitbox}"
            // ⚠️ drillLen 是可校準的形狀參數（見 MechaActionShapes.ApplyCalibration）——
            //    下一輪拿 log 訂參數時，沒有這一欄就分不出「預測形狀不準」與「使用者調過滑桿」。
            + $";drillLen={C.MechaDrillLength:F1}"
            + $";targetableOnly={C.MechaTargetsTargetableOnly};hideNoise={C.MechaTargetsHideSceneryAndNpcs}"
            + $";includePlayers={C.MechaTargetsIncludePlayers};requireObjectTable={C.MechaObjectiveRequireObjectTable}"
            + $";matchRadius={C.MechaObjectiveMatchRadius:F1};useMarkerVector={C.MechaObjectiveUseMarkerVector}"
            + $";fullPlayerNames={C.MechaShowFullPlayerNames}"
            + $";skillToggles={(C.MechaAoeSkillToggles.Count == 0 ? "全開(預設)" : string.Join(",", C.MechaAoeSkillToggles.Select(kv => kv.Key + "=" + kv.Value)))}"
            // 🔑 per-skill 形狀覆蓋。**有覆蓋的印出值、沒覆蓋的印 default**——
            //    下一輪拿 log 訂參數時，這一欄是唯一能分出
            //    「我們預測的形狀不準」與「使用者自己把滑桿拉過」的東西。
            //    （drillLen/coneDeg 兩個舊鍵仍照印，它們是沒有覆蓋時的實際來源。）
            + $";shapeOverrides={DescribeShapeOverrides()}");
    }

    /// <summary>
    /// 六個已驗證機甲技能的形狀覆蓋摘要，形如
    /// <c>42150[p=3.5,hw=default],42258[p=default,ang=200]</c>。
    /// 沒有任何覆蓋時回 <c>全部default</c>——刻意不印空字串，
    /// 免得事後看 log 分不出「沒有覆蓋」與「這一版還沒有這一欄」。
    /// </summary>
    private static string DescribeShapeOverrides()
    {
        if (C.MechaShapeOverrides.Count == 0)
            return "全部default";

        var parts = new List<string>();
        foreach (var id in MechaActionShapes.BaselineActionIds)
        {
            if (!C.MechaShapeOverrides.TryGetValue(id, out var ov) || ov == null || ov.IsEmpty)
                continue;
            parts.Add($"{id}[p={Fmt(ov.Primary)},hw={Fmt(ov.HalfWidth)},ang={Fmt(ov.AngleDeg)}]");
        }

        // 白名單以外的技能（日後新增的）也要印，不然它們的覆蓋在 log 上是隱形的。
        foreach (var kv in C.MechaShapeOverrides)
        {
            if (Array.IndexOf(MechaActionShapes.BaselineActionIds, kv.Key) >= 0)
                continue;
            if (kv.Value == null || kv.Value.IsEmpty)
                continue;
            parts.Add($"{kv.Key}[p={Fmt(kv.Value.Primary)},hw={Fmt(kv.Value.HalfWidth)},ang={Fmt(kv.Value.AngleDeg)}]");
        }

        return parts.Count == 0 ? "全部default" : string.Join(",", parts);

        static string Fmt(float? v) => v is { } f ? f.ToString("F1") : "default";
    }

    private static void StopSession(string reason)
    {
        if (!sessionActive)
            return;

        var seconds = (Environment.TickCount64 - sessionStartTick) / 1000.0;
        sessionActive = false;
        LastStopReason = reason;

        EmitRing(
            $"END;t={DateTime.Now:yyyy-MM-dd HH:mm:ss};reason={reason};duration={seconds:F1}s"
            + $";snapshots={snapshotSeq};casts={castCount};vanished={vanishCount}"
            + $";tracked={tracks.Count};interactions={interactionCount}");

        EmitSummary();
    }

    /// <summary>
    /// 事件結束的對照輸出：<b>預測側 vs 實際側的兩份差集</b>。這是使用者最終要看的那幾行。
    ///
    /// 🔴 <b>「實際命中」是代理訊號，不是直接觀測</b>，讀這幾行的時候必須知道它的三個侷限：
    /// <list type="number">
    ///   <item>遊戲<b>沒有</b>把「這一招打到了誰」暴露成任何可讀的欄位（要拿到就得攔封包，
    ///         那是艦隊紅線）。這裡用的是「實體從 ObjectTable 消失」＋「計分欄位跳動」。</item>
    ///   <item>機甲行動是<b>多人內容</b>：別人打掉的目標一樣會消失。所以每一筆 VANISH 都附上
    ///         同時段的 <c>PersonalProgress</c> 變化——有跳才比較可能是你打的。</item>
    ///   <item>「消失」也可能只是<b>離開串流範圍</b>。所以附上消失前最後的距離
    ///         （<c>lastDist</c>），靠近的才有參考價值。</item>
    /// </list>
    ///
    /// 🔑 兩個方向都要列，而且<b>反方向（實際有但預測無）才是「預測範圍太小」的直接證據</b>。
    /// </summary>
    private static void EmitSummary()
    {
        var predicted = new List<RecordedEntity>();
        var vanished = new List<RecordedEntity>();
        var predictedOnly = new List<RecordedEntity>();
        var vanishedOnly = new List<RecordedEntity>();
        var both = 0;

        foreach (var t in tracks.Values)
        {
            var wasPredicted = t.PredictedSnapshots > 0;
            if (wasPredicted)
                predicted.Add(t);
            if (t.Vanished)
                vanished.Add(t);

            if (wasPredicted && t.Vanished)
                both++;
            else if (wasPredicted)
                predictedOnly.Add(t);
            else if (t.Vanished)
                vanishedOnly.Add(t);
        }

        EmitRing(
            $"SUMMARY;predicted={predicted.Count};vanished={vanished.Count};bothSides={both}"
            + $";predictedNotVanished={predictedOnly.Count};vanishedNotPredicted={vanishedOnly.Count}"
            + $";tracked={tracks.Count};interacted={interactions.Count}");

        EmitRing(
            "SUMMARY-NOTE;「實際命中」是代理訊號不是直接觀測："
            + "遊戲沒有可讀的命中欄位（要拿到就得攔封包＝紅線），這裡用的是"
            + "「實體從 ObjectTable 消失」＋「同時段計分有沒有跳」。"
            + "多人內容裡別人打掉的也會消失，離開串流範圍同樣會消失 ⇒ 請一併看 persDelta 與 lastDist。"
            + "⚠️ 另外：dupId=True 的那幾筆是**好幾個實體共用同一個 ObjectId**的合計值"
            + "（機甲事件的 per-player 目標會這樣），seenSnaps 可能大於快照總數，"
            + "而且個別消失偵測不到——那幾筆不要拿來算命中率。");

        // 預測有但實際無：預測範圍可能太寬，或那一發根本沒放出去。
        foreach (var t in predictedOnly.OrderBy(x => x.MinPredictedDistance))
        {
            EmitRing(
                $"SUMMARY-PRED-ONLY;oid=0x{t.ObjectId:X};did={t.BaseId};kind={t.Kind};name={t.Name}"
                + $";predictedSnaps={t.PredictedSnapshots};minPredDist={t.MinPredictedDistance:F2}"
                + $";lastDist={t.LastDistance:F2};lastPos={Fmt(t.LastPos)};tier={t.BestTier};iceListed={t.EverIceListed}"
                + $";dupId={t.DuplicateId}");
        }

        // 實際有但預測無：🔑 這一份才是「預測範圍太小／判定漏掉它」的直接證據。
        foreach (var t in vanishedOnly.OrderBy(x => x.LastDistance))
        {
            EmitRing(
                $"SUMMARY-VANISH-ONLY;oid=0x{t.ObjectId:X};did={t.BaseId};kind={t.Kind};name={t.Name}"
                + $";lastDist={t.LastDistance:F2};lastPos={Fmt(t.LastPos)};seenSnaps={t.SeenSnapshots}"
                + $";tier={t.BestTier};iceListed={t.EverIceListed};dupId={t.DuplicateId}"
                + $";persDelta={t.PersonalDeltaAtVanish};progDelta={t.ProgressDeltaAtVanish}");
        }

        // 互動過的 object 去重清單＝「正確目標」的人工真值。
        foreach (var kv in interactions.OrderByDescending(x => x.Value.Manual + x.Value.Ice))
        {
            EmitRing(
                $"SUMMARY-INTERACT;oid=0x{kv.Key:X};did={kv.Value.BaseId};kind={kv.Value.Kind}"
                + $";name={kv.Value.Name};manual={kv.Value.Manual};ice={kv.Value.Ice}");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  互動序列（玩家 vs ICE）
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 輪詢三個目標槽。值變了就記一筆。
    /// 🔑 <b>來源標記</b>：ICE 自己呼叫 <c>Utils.TargetgameObject</c>／<c>Utils.InteractWithObject</c> 時
    /// 會先呼叫 <see cref="NoteIceAction"/> 把物件 id 登記進 <see cref="iceInitiated"/>，
    /// 所以這裡看到的變化只要對得上就標 <c>src=ice</c>，其餘一律 <c>src=manual</c>。
    /// 沒有這個區分的話，log 上分不出「使用者選的」與「外掛決定的」——而這次要找的正是那個差別。
    /// </summary>
    private static void PollTargets()
    {
        PollSlot("hard", Svc.Targets.Target, ref lastHardTargetId);
        PollSlot("soft", Svc.Targets.SoftTarget, ref lastSoftTargetId);
        PollSlot("focus", Svc.Targets.FocusTarget, ref lastFocusTargetId);
    }

    private static void PollSlot(string slot, IGameObject? obj, ref ulong last)
    {
        // ⚠️ 只在這個方法裡碰 obj，抄完純值就丟；一個參考都不留。
        var id = obj?.GameObjectId ?? 0UL;
        if (id == last)
            return;
        last = id;

        // 沒在錄製時仍然要更新 last，否則開錄那一刻會憑空多出一筆假的「變化」。
        if (!sessionActive)
            return;

        if (id == 0 || obj == null)
        {
            EmitRing($"target-change;{TimeFields()};slot={slot};oid=0;did=0;name=;pos=;src=manual;note=取消選取");
            return;
        }

        var kind = obj.ObjectKind;
        var name = SafeName(obj.Name.ToString(), kind, obj.GameObjectId == (Player.Object?.GameObjectId ?? 0UL));
        var pos = obj.Position;
        var src = ConsumeIceSource(id);

        EmitRing(
            $"target-change;{TimeFields()};slot={slot};src={src};oid=0x{id:X};did={obj.BaseId};kind={kind}"
            + $";name={name};pos={Fmt(pos)};dist={FlatDistanceToPlayer(pos):F2};targetable={obj.IsTargetable}");

        interactionCount++;
        TallyInteraction(id, obj.BaseId, kind, name, src == "ice");
    }

    /// <summary>
    /// ICE 自己發起的目標／互動。由 <c>Utils.TargetgameObject</c> 與 <c>Utils.InteractWithObject</c> 呼叫。
    ///
    /// 🔴 <b>這個方法絕對不能影響呼叫端</b>：整段被 try/catch 包住，錄製壞掉也不會讓
    /// ICE 的互動流程掉一步。錄製沒開的話第一行就 return，成本是一次布林判斷。
    /// </summary>
    /// <param name="what">呼叫點的語意（<c>target</c>／<c>interact</c>）。</param>
    public static void NoteIceAction(string what, IGameObject? obj)
    {
        if (!Enabled || obj == null)
            return;

        try
        {
            var id = obj.GameObjectId;
            if (id == 0)
                return;

            // 登記「這是 ICE 弄的」，讓隨後的 target 輪詢標得出來源。
            // 上限只是防呆：正常情況這份表最多幾筆。
            if (iceInitiated.Count > 256)
                iceInitiated.Clear();
            iceInitiated[id] = Environment.TickCount64;

            if (!sessionActive)
                return;

            var kind = obj.ObjectKind;
            var name = SafeName(obj.Name.ToString(), kind, false);
            var pos = obj.Position;

            EmitRing(
                $"interact;{TimeFields()};src=ice;what={what};oid=0x{id:X};did={obj.BaseId};kind={kind}"
                + $";name={name};pos={Fmt(pos)};dist={FlatDistanceToPlayer(pos):F2};targetable={obj.IsTargetable}");

            interactionCount++;
            TallyInteraction(id, obj.BaseId, kind, name, true);
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaRecorderNoteFailed", 60_000))
                IceLogging.Info($"記錄 ICE 互動失敗（不影響互動本身）：{ex.Message}", Prefix);
        }
    }

    /// <summary>這個 id 是不是 ICE 剛剛（3 秒內）指定的。</summary>
    private static string ConsumeIceSource(ulong id)
    {
        if (!iceInitiated.TryGetValue(id, out var tick))
            return "manual";
        return Environment.TickCount64 - tick <= 3000 ? "ice" : "manual";
    }

    private static void TallyInteraction(ulong id, uint baseId, ObjectKind kind, string name, bool byIce)
    {
        interactions.TryGetValue(id, out var tally);
        interactions[id] = new InteractionTally(
            tally.Manual + (byIce ? 0 : 1),
            tally.Ice + (byIce ? 1 : 0),
            baseId,
            // 名字之後可能才拿得到（空名的實體查表要時間），所以有值就更新。
            string.IsNullOrEmpty(tally.Name) ? name : tally.Name,
            kind);
    }

    // ────────────────────────────────────────────────────────────────────
    //  施放偵測
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 從冷卻剩餘時間「往上跳」推出「這個技能剛剛被使用」。
    ///
    /// 📌 為什麼是這個訊號：它完全是唯讀的（<see cref="MechaOpsMonitor"/> 已經在讀 <c>GetRecastTime</c>／
    /// <c>GetRecastTimeElapsed</c>），不需要新增任何 hook，也不碰封包。
    ///
    /// ⚠️ 三個已知侷限（讀 log 時要記得）：
    ///  ① 解析度只有 250ms（候選快照的取樣週期），所以 <c>CAST</c> 那一行的座標最多晚 250ms；
    ///  ② 機甲六技共用重置群組 75／76，<b>同群組的別的動作</b>也會讓剩餘時間跳起來 ⇒ 可能誤報；
    ///  ③ 沒有冷卻（<c>RecastTotal &lt;= 0</c>）的技能偵測不到，一律不報。
    /// </summary>
    private static void DetectCasts()
    {
        var candidates = MechaOpsMonitor.ActiveCandidates;
        if (candidates.Count == 0)
        {
            if (lastRecast.Count > 0)
                lastRecast.Clear();
            return;
        }

        foreach (var c in candidates)
        {
            var hadPrev = lastRecast.TryGetValue(c.ActionId, out var prev);
            lastRecast[c.ActionId] = c.RecastRemaining;

            if (!hadPrev || c.RecastTotal <= 0f)
                continue;
            if (c.RecastRemaining <= prev + CastDetectDelta)
                continue;

            castCount++;
            EmitCast(c, prev);
        }
    }

    private static void EmitCast(MechaCandidate c, float prevRemaining)
    {
        var self = Player.Object;
        if (self == null)
            return;

        var origin = self.Position;
        var rotation = self.Rotation;
        var casterHitbox = self.HitboxRadius;

        // 施放當下重新掃一次，不用上一輪的快照——移動中的駕駛員差 1 秒座標就差很多。
        var sweep = Sweep(origin);
        var d = MechaOpsMonitor.EventDetail;

        EmitRing(
            $"CAST;{TimeFields()};action={c.ActionId};name={Sanitize(c.Name)}"
            + $";recastPrev={prevRemaining:F1};recastNow={c.RecastRemaining:F1};recastTotal={c.RecastTotal:F1}"
            + $";origin={Fmt(origin)};rot={rotation:F3};casterHitbox={casterHitbox:F2}"
            + $";{ShapeFields(c)}"
            + $";prog={d?.Progress.ToString() ?? "?"};pers={d?.PersonalProgress.ToString() ?? "?"}"
            + $";contrib={d?.Contribution.ToString() ?? "?"}");

        // 預測側的兩份清單：ICE 自己認的目標 vs 純幾何（不套 ICE 過濾）。
        // 🔑 兩者的差就是「ICE 的過濾把誰擋掉了」——正是「目標判定很奇怪」最可能的成因。
        EmitCoverage("CAST-PRED-ICE", c, origin, rotation, casterHitbox, IceTargetsAsSweep());
        EmitCoverage("CAST-PRED-RAW", c, origin, rotation, casterHitbox, sweep);
    }

    // ────────────────────────────────────────────────────────────────────
    //  快照
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 變化偵測用的簽章。🔑 刻意<b>不含連續變動的座標</b>——含了的話每一輪都算「有變化」，
    /// 10 分鐘的事件會產生 600 筆完整快照，把 log 灌爆而且沒有新資訊。
    /// 位置的變化由心跳（<see cref="HeartbeatMs"/>）與 <c>CAST</c> 事件負責取樣。
    /// </summary>
    private static string BuildSignature()
    {
        var d = MechaOpsMonitor.EventDetail;
        var sb = new StringBuilder(192);

        sb.Append((uint)MechaOpsMonitor.EventFlags).Append('|')
          .Append(MechaOpsMonitor.Role).Append('|')
          .Append(d?.DataRowId ?? 0).Append('|')
          .Append((uint?)d?.Flags ?? 0).Append('|')
          .Append(d?.Progress ?? -1).Append('|')
          .Append(d?.PersonalProgress ?? -1).Append('|')
          .Append(d?.Contribution ?? -1).Append('|')
          .Append(MechaObjectiveTracker.MarkerCount).Append('|')
          .Append(MechaObjectiveTracker.ConfirmedCount).Append('|')
          .Append(MechaObjectiveTracker.Source).Append('|')
          .Append(MechaObjectiveTracker.LearnedBaseIdCount).Append('|');

        foreach (var m in MechaObjectiveTracker.Markers)
            sb.Append(m.Slot).Append(':').Append(m.LayoutId).Append(':').Append(m.Hidden ? 1 : 0).Append(',');
        sb.Append('|');

        foreach (var o in MechaObjectiveTracker.Active)
            sb.Append(o.ObjectId).Append(':').Append(o.BaseId).Append(',');
        sb.Append('|');

        foreach (var t in MechaOpsMonitor.ActiveTargets)
            sb.Append(t.GameObjectId).Append(':').Append((int)t.Tier).Append(',');
        sb.Append('|');

        foreach (var c in MechaOpsMonitor.ActiveCandidates)
            sb.Append(c.ActionId).Append(',');

        return sb.ToString();
    }

    private static void EmitSnapshot(bool heartbeat)
    {
        var self = Player.Object;
        if (self == null)
            return;

        var seq = ++snapshotSeq;
        var origin = self.Position;
        var rotation = self.Rotation;
        var casterHitbox = self.HitboxRadius;

        var sweep = Sweep(origin);
        var d = MechaOpsMonitor.EventDetail;
        var iceTargets = MechaOpsMonitor.ActiveTargets;

        // ---- 計分變化（實際側的第一個訊號）----
        var progDelta = 0;
        var persDelta = 0;
        if (d != null)
        {
            if (progressBaselineSet)
            {
                progDelta = d.Progress - lastProgress;
                persDelta = d.PersonalProgress - lastPersonal;
                var contribDelta = d.Contribution - lastContribution;
                if (progDelta != 0 || persDelta != 0 || contribDelta != 0)
                {
                    EmitRing(
                        $"SCORE;{TimeFields()};#{seq}"
                        + $";prog={progDelta:+#;-#;0}({d.Progress}/{d.ProgressMax})"
                        + $";pers={persDelta:+#;-#;0}({d.PersonalProgress}/{d.PersonalProgressMax})"
                        + $";contrib={contribDelta:+#;-#;0}({d.Contribution})");
                }
            }
            lastProgress = d.Progress;
            lastPersonal = d.PersonalProgress;
            lastContribution = d.Contribution;
            progressBaselineSet = true;
        }

        // ---- 追蹤更新 ＋ 消失偵測 ----
        UpdateTracks(sweep, iceTargets, progDelta, persDelta);

        // ---- 預測側：ICE 的判定 vs 純幾何 ----
        var predictedNow = new HashSet<ulong>();
        var enabledSkills = 0;
        foreach (var c in MechaOpsMonitor.ActiveCandidates)
        {
            if (!MechaAoeOverlay.IsSkillEnabled(c.ActionId))
                continue;
            enabledSkills++;

            EmitCoverage($"PRED #{seq}", c, origin, rotation, casterHitbox, IceTargetsAsSweep(), predictedNow);
            EmitCoverage($"PRED-RAW #{seq}", c, origin, rotation, casterHitbox, sweep);
        }

        // 被預測蓋到的計數只算一次（多個技能同時蓋到不重複累加）。
        foreach (var id in predictedNow)
        {
            if (tracks.TryGetValue(id, out var t))
                t.PredictedSnapshots++;
        }

        // ---- 主快照行 ----
        var srvNow = d?.ServerTimeNow ?? 0;
        var rowId = d?.DataRowId ?? 0u;
        var role = MechaOpsMonitor.Role;

        EmitRing(
            $"SNAP #{seq};{TimeFields()};{(heartbeat ? "why=heartbeat" : "why=change")}"
            + $";role={RoleText(role)};roleSrc={string.Join(",", MechaOpsMonitor.ActiveCandidates.Select(c => c.ActionId))}"
            + $";modFlags=0x{(uint)MechaOpsMonitor.EventFlags:X}({(MechaOpsMonitor.EventFlagsValid ? MechaOpsMonitor.EventFlags.ToString() : "invalid")})"
            + $";evRow={rowId};evFlags={(d == null ? "?" : "0x" + ((uint)d.Flags).ToString("X") + "(" + d.Flags + ")")}"
            + $";prog={(d == null ? "?" : d.Progress + "/" + d.ProgressMax)}"
            + $";pers={(d == null ? "?" : d.PersonalProgress + "/" + d.PersonalProgressMax)}"
            + $";contrib={d?.Contribution.ToString() ?? "?"}"
            + $";evStart={d?.EventStart.ToString() ?? "?"};evEnd={d?.EventEnd.ToString() ?? "?"}"
            + $";regStart={d?.RegistrationStart.ToString() ?? "?"};regEnd={d?.RegistrationEnd.ToString() ?? "?"}"
            + $";tpStart={d?.TeleportStart.ToString() ?? "?"};srvNow={(srvNow > 0 ? srvNow.ToString() : "?")}"
            + $";regOpen={BoolText(d?.IsRegistrationOpen(srvNow))};tpOpen={BoolText(d?.IsTeleportOpen(srvNow))}"
            + $";markerSrc={MechaObjectiveTracker.Source}"
            + $";markers={CountText(MechaObjectiveTracker.MarkerCount)};confirmed={CountText(MechaObjectiveTracker.ConfirmedCount)}"
            + $";objectives={MechaObjectiveTracker.Active.Count};pins={MechaObjectiveTracker.PinnedCount}"
            + $";learned={(MechaObjectiveTracker.LearnedBaseIds.Count == 0 ? "-" : string.Join("/", MechaObjectiveTracker.LearnedBaseIds.OrderBy(x => x)))}"
            + $";wlAll={MechaObjectNames.KnownEventObjectIds.Count}"
            + $";wlRole={(role == MechaRole.Unknown ? "不套用" : MechaObjectNames.RoleIdCount(rowId, role).ToString())}"
            + $";roleSplit={MechaObjectNames.RoleSplitSource}"
            + $";iceTargets={iceTargets.Count};skillsEnabled={enabledSkills};predicted={predictedNow.Count}"
            // 🔑 >0 代表這一輪有好幾個實體共用同一個 ObjectId，所有 id 為鍵的統計都是合計值。
            + $";sweep={sweep.Count};dupOids={lastDuplicateOidCount};pPos={Fmt(origin)};pRot={rotation:F3};pHitbox={casterHitbox:F2}"
            + $";hardTarget=0x{lastHardTargetId:X};softTarget=0x{lastSoftTargetId:X}");

        // ---- 逐筆明細（只寫 dalamud.log）----
        foreach (var m in MechaObjectiveTracker.Markers)
        {
            var sheetBase = MechaObjectNames.LayoutToBaseId.TryGetValue(m.LayoutId, out var mapped)
                ? mapped.ToString()
                : "-";
            EmitBulk(
                $"MARK #{seq};slot={m.Slot:00};layout={m.LayoutId};type={m.MarkerType};icon={m.MarkerIcon}"
                + $";mapIcon={m.MapIconId};pos={Fmt(m.Position)};r={m.Radius:F1};hidden={m.Hidden};sheetBase={sheetBase}");
        }

        foreach (var o in MechaObjectiveTracker.Active)
        {
            EmitBulk(
                $"OBJV #{seq};oid=0x{o.ObjectId:X};did={o.BaseId};kind={o.Kind};name={Sanitize(o.Label)}"
                + $";pos={Fmt(o.Position)};r={o.Radius:F1};matchDist={o.MatchDistance:F2}"
                + $";confirmed={o.Confirmed};stale={o.StaleRisk};pin={o.IsPin};markerSlot={o.Marker.Slot}");
        }

        // ⚠️ 先算再判斷，不要塞進 && 的短路裡：短路會讓心跳那幾輪不更新簽章，
        //    下一輪就拿過期的簽章去比，變成無謂的重印。
        var idsChanged = SweepIdsChanged(sweep);
        if (DumpObjects && (heartbeat || seq == 1 || idsChanged))
            DumpSweep(seq, sweep, iceTargets);
    }

    // ────────────────────────────────────────────────────────────────────
    //  ObjectTable 掃描
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 掃 ObjectTable，抄成純值。
    ///
    /// 🔑 <b>刻意不做任何名字／種類的先入為主過濾</b>（2026-08-08 使用者：「沒名字的目標的判定也有嗎，
    /// 應該要錄 objectid」）：機甲事件的目標常常是<b>無名的 EventObj</b>，
    /// 只要在這裡濾掉就永遠查不出「沒錄到」是因為它不存在、還是因為被我濾掉了。
    /// 唯一的界線是距離（<see cref="SweepRadius"/>），而那個半徑本身會寫進 log。
    /// ICE 自己的過濾條件則<b>另外算成 <c>iceFilter</c> 欄位記下來</b>，一筆都不會少。
    ///
    /// 🔴 抄走的只有 <c>(id, BaseId, 種類, 名字, 座標, hitbox, 可選取, HP)</c> 這些純值；
    /// <c>IGameObject</c> 與 <c>Address</c> 一個都不留。
    /// </summary>
    private static List<SweepEntry> Sweep(Vector3 origin)
    {
        var result = new List<SweepEntry>();
        var radius = Math.Clamp(SweepRadius, 10f, 300f);
        var radiusSq = radius * radius;
        var selfId = Player.Object?.GameObjectId ?? 0UL;

        foreach (var obj in Svc.Objects)
        {
            if (obj == null)
                continue;

            var pos = obj.Position;
            var dx = pos.X - origin.X;
            var dz = pos.Z - origin.Z;
            var distSq = dx * dx + dz * dz;
            if (distSq > radiusSq)
                continue;

            var kind = obj.ObjectKind;
            var id = obj.GameObjectId;

            int hp = -1, maxHp = -1;
            if (obj is IBattleChara bc)
            {
                hp = (int)bc.CurrentHp;
                maxHp = (int)bc.MaxHp;
            }

            result.Add(new SweepEntry(
                id,
                // 📌 身分比對一律用 BaseId：Dalamud 已把 DataId 標成 [Obsolete]，兩者同值。
                obj.BaseId,
                kind,
                SafeName(obj.Name.ToString(), kind, id == selfId),
                pos,
                obj.HitboxRadius,
                obj.IsTargetable,
                MathF.Sqrt(distSq),
                hp,
                maxHp));
        }

        return result;
    }

    private static string lastSweepIdSignature = "";

    /// <summary>掃描結果的 id 集合有沒有變（有生有滅才值得重印一次完整傾印）。</summary>
    private static bool SweepIdsChanged(List<SweepEntry> sweep)
    {
        var sb = new StringBuilder(sweep.Count * 8);
        foreach (var e in sweep.OrderBy(x => x.ObjectId))
            sb.Append(e.ObjectId).Append(',');
        var sig = sb.ToString();
        if (sig == lastSweepIdSignature)
            return false;
        lastSweepIdSignature = sig;
        return true;
    }

    private static void DumpSweep(int seq, List<SweepEntry> sweep, IReadOnlyList<MechaTarget> iceTargets)
    {
        var listed = new HashSet<ulong>();
        foreach (var t in iceTargets)
            listed.Add(t.GameObjectId);

        // 排序：任務目標優先，其次由近到遠。截斷時保留最有價值的那一批。
        var ordered = sweep
            .Select(e => (Entry: e, Tier: TierOf(e.ObjectId, e.BaseId, e.Kind)))
            .OrderByDescending(x => (int)x.Tier)
            .ThenBy(x => x.Entry.Distance)
            .ToList();

        var shown = 0;
        foreach (var (e, tier) in ordered)
        {
            if (shown >= MaxObjectDump)
                break;
            shown++;

            EmitBulk(
                $"OBJ #{seq};oid=0x{e.ObjectId:X};did={e.BaseId};kind={e.Kind};name={e.Name}"
                + $";pos={Fmt(e.Position)};dist={e.Distance:F2};hitbox={e.HitboxRadius:F2}"
                + $";targetable={e.Targetable};hp={(e.Hp < 0 ? "-" : e.Hp + "/" + e.MaxHp)}"
                + $";tier={tier};tierWhy={TierWhy(e.ObjectId, e.BaseId, e.Kind)}"
                + $";iceListed={listed.Contains(e.ObjectId)};iceFilter={IceFilterReason(e, tier)}");
        }

        if (ordered.Count > shown)
        {
            // ⚠️ 被截掉這件事一定要看得見，否則事後會把「沒印出來」讀成「不存在」。
            EmitBulk($"OBJ #{seq};truncated={ordered.Count - shown};total={ordered.Count};shown={shown};sweepRadius={SweepRadius:F0}");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  預測範圍
    // ────────────────────────────────────────────────────────────────────

    /// <summary>把 ICE 自己的目標清單轉成掃描格式，好跟純幾何那一份用同一段程式碼算。</summary>
    private static List<SweepEntry> IceTargetsAsSweep()
    {
        var origin = Player.Object?.Position ?? Vector3.Zero;
        var result = new List<SweepEntry>();
        foreach (var t in MechaOpsMonitor.ActiveTargets)
        {
            result.Add(new SweepEntry(
                t.GameObjectId, t.DataId, t.Kind, t.Label, t.Position, t.HitboxRadius,
                true, FlatDistance(origin, t.Position), -1, -1));
        }
        return result;
    }

    /// <summary>
    /// 一個技能的預測範圍：<b>參數全記，範圍內的實體逐筆帶距離</b>。
    ///
    /// 🔑 距離是重點：將來要改半徑／角度就是拿這些數字來訂
    /// （「預測包含但實際沒打到的那幾個，距離都在 6.5 以上」這種結論才訂得出參數）。
    ///
    /// 判定完全走 <see cref="MechaCoverage"/>——跟疊加層畫圖、跟狀態視窗算 n/m 是同一套程式碼，
    /// 所以 log 裡的數字就是使用者畫面上看到的數字。
    /// ⚠️ 唯一的差別是<b>錨點取樣的時刻</b>：疊加層用繪製那一幀的座標，這裡用 Framework 那一幀的，
    /// 兩者差不到一幀。
    /// </summary>
    private static void EmitCoverage(
        string tag,
        MechaCandidate c,
        Vector3 origin,
        float rotation,
        float casterHitbox,
        List<SweepEntry> entries,
        HashSet<ulong>? predictedInto = null)
    {
        // per-skill 角度：這個方法本來就是逐技能呼叫的，直接問這一技的值。
        var coneRad = MechaActionShapes.ConeAngleFor(c.ActionId) * MathF.PI / 180f;
        var useHitbox = C.MechaCoverageUseHitbox;

        var covered = new List<(SweepEntry E, float D)>();
        var reach = new List<(SweepEntry E, float D)>();

        foreach (var e in entries)
        {
            // MechaCoverage 只讀 Position 與 HitboxRadius，其餘欄位給預設值即可。
            var t = new MechaTarget(e.ObjectId, e.BaseId, e.Name, e.Name, e.Position,
                e.HitboxRadius, e.Kind, false, MechaTargetTier.Other);

            var d = FlatDistance(origin, e.Position);

            if (MechaCoverage.IsInReach(c.Shape, origin, casterHitbox, t, useHitbox))
                reach.Add((e, d));

            if (MechaCoverage.IsCovered(c.Shape, coneRad, origin, rotation, casterHitbox, t, useHitbox))
            {
                covered.Add((e, d));
                if (predictedInto != null)
                {
                    predictedInto.Add(e.ObjectId);
                    if (tracks.TryGetValue(e.ObjectId, out var track) && d < track.MinPredictedDistance)
                        track.MinPredictedDistance = d;
                }
            }
        }

        EmitBulk(
            $"{tag};action={c.ActionId};name={Sanitize(c.Name)};{ShapeFields(c)}"
            + $";origin={Fmt(origin)};rot={rotation:F3};casterHitbox={casterHitbox:F2};useHitbox={useHitbox}"
            + $";pool={entries.Count};covered={covered.Count};inReach={reach.Count}"
            + $";coveredIds={IdDistList(covered)};reachIds={IdDistList(reach)}");
    }

    /// <summary>形狀參數。矩形／扇形／自身圓／單體射程圈的語意見 <see cref="MechaAoeShape"/>。</summary>
    private static string ShapeFields(MechaCandidate c)
        => $"shape={c.Shape.Kind};primary={c.Shape.Primary:F1};halfWidth={c.Shape.HalfWidth:F1}"
         + $";coneDeg={(c.Shape.Kind == MechaAoeKind.Cone ? MechaActionShapes.ConeAngleFor(c.ActionId).ToString("F0") : "-")}";

    private static string IdDistList(List<(SweepEntry E, float D)> items)
    {
        if (items.Count == 0)
            return "-";

        var sb = new StringBuilder(items.Count * 20);
        var n = 0;
        foreach (var (e, d) in items.OrderBy(x => x.D))
        {
            if (n >= MaxListItems)
            {
                sb.Append("+").Append(items.Count - n).Append("more");
                break;
            }
            if (n > 0)
                sb.Append(',');
            sb.Append("0x").Append(e.ObjectId.ToString("X")).Append('@').Append(d.ToString("F2"));
            n++;
        }
        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────────────
    //  實體追蹤與「實際命中」的代理訊號
    // ────────────────────────────────────────────────────────────────────

    private static void UpdateTracks(List<SweepEntry> sweep, IReadOnlyList<MechaTarget> iceTargets, int progDelta, int persDelta)
    {
        var listed = new HashSet<ulong>();
        foreach (var t in iceTargets)
            listed.Add(t.GameObjectId);

        var seenNow = new HashSet<ulong>();
        lastDuplicateOidCount = 0;

        foreach (var e in sweep)
        {
            // 🔴🔴 <b>GameObjectId 在機甲事件裡不是唯一鍵</b>（2026-08-08 實機直證）：
            //    同一次掃描裡 <c>oid=0x100B2413A</c> 同時出現 2~4 格，座標各差十幾二十公尺，
            //    <c>did</c> 全是 2014721（協助員的小型目標，per-player 生成）。
            //    50 次傾印裡有 <b>33 次</b> 含這種重複。
            //    ⇒ 這不是傾印迭代的 bug，是 ObjectTable 真的有好幾格共用一個 id。
            //
            //    ⚠️ 後果一定要看得見，否則 log 會自相矛盾而讀的人查錯方向：
            //    本檔所有以 id 為鍵的統計（<c>seenSnaps</c>／<c>predictedSnaps</c>／消失偵測）
            //    都會把那幾個實體<b>併成一筆</b>——實機就出現過 <c>seenSnaps=367</c>
            //    而全場只有 79 次快照。消失偵測更是只要還有任何一個同 id 實體在，
            //    <c>Svc.Objects.SearchById</c> 就查得到 ⇒ <b>個別消失偵測不到</b>。
            //
            //    這裡刻意<b>不</b>改識別模型（換鍵要嘛不穩定要嘛得靠座標猜，兩種都更糟），
            //    只把「這一筆是合計值」標出來。
            if (!seenNow.Add(e.ObjectId))
            {
                lastDuplicateOidCount++;
                if (tracks.TryGetValue(e.ObjectId, out var dup))
                    dup.DuplicateId = true;
            }

            if (!tracks.TryGetValue(e.ObjectId, out var track))
            {
                if (tracks.Count >= MaxTracks)
                    continue;   // 防呆上限；超過就不再新增，既有的照常更新

                track = new RecordedEntity
                {
                    ObjectId = e.ObjectId,
                    BaseId = e.BaseId,
                    Kind = e.Kind,
                    Name = e.Name,
                    FirstPos = e.Position,
                    FirstSeen = DateTime.Now,
                };
                tracks[e.ObjectId] = track;

                var tier0 = TierOf(e.ObjectId, e.BaseId, e.Kind);
                EmitBulk(
                    $"SPAWN;{TimeFields()};oid=0x{e.ObjectId:X};did={e.BaseId};kind={e.Kind};name={e.Name}"
                    + $";pos={Fmt(e.Position)};dist={e.Distance:F2};tier={tier0};tierWhy={TierWhy(e.ObjectId, e.BaseId, e.Kind)}");
            }

            // 名字可能一開始是空的、之後才查得到（EObjName 是延遲建表的）。
            if (track.Name.Length == 0 && e.Name.Length > 0)
                track.Name = e.Name;

            track.LastPos = e.Position;
            track.LastDistance = e.Distance;
            track.LastSeen = DateTime.Now;
            track.SeenSnapshots++;
            track.EverIceListed |= listed.Contains(e.ObjectId);

            var tier = TierOf(e.ObjectId, e.BaseId, e.Kind);
            if (tier > track.BestTier)
                track.BestTier = tier;
        }

        // ---- 消失偵測 ----
        // 🔑 用 SearchById 而不是「不在這次掃描結果裡」：後者會把「走出掃描半徑」
        //    誤判成「被消滅」。SearchById 查的是整張表，與半徑無關。
        foreach (var track in tracks.Values)
        {
            if (track.Vanished || seenNow.Contains(track.ObjectId))
                continue;

            if (Svc.Objects.SearchById(track.ObjectId) != null)
                continue;   // 還在，只是超出掃描半徑

            track.Vanished = true;
            track.VanishedAt = DateTime.Now;
            track.ProgressDeltaAtVanish = progDelta;
            track.PersonalDeltaAtVanish = persDelta;
            vanishCount++;

            EmitRing(
                $"VANISH;{TimeFields()};oid=0x{track.ObjectId:X};did={track.BaseId};kind={track.Kind}"
                + $";name={track.Name};lastPos={Fmt(track.LastPos)};lastDist={track.LastDistance:F2}"
                + $";seenSnaps={track.SeenSnapshots};predictedSnaps={track.PredictedSnapshots}"
                + $";minPredDist={(track.MinPredictedDistance == float.MaxValue ? "-" : track.MinPredictedDistance.ToString("F2"))}"
                + $";tier={track.BestTier};iceListed={track.EverIceListed};dupId={track.DuplicateId}"
                + $";progDelta={progDelta:+#;-#;0};persDelta={persDelta:+#;-#;0}"
                // 距離遠的「消失」很可能只是離開串流範圍，不是被打掉。
                + $";streamRisk={(track.LastDistance > Math.Clamp(SweepRadius, 10f, 300f) * 0.8f)}");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  ICE 現行判定的重算（只寫進 log，不影響任何顯示）
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ 這裡的三層順序必須跟 <see cref="MechaOpsMonitor.SampleTargets"/> <b>逐字對應</b>——
    /// 這是「重算」不是攔截，兩邊漂掉 log 就會說謊。
    /// </summary>
    private static MechaTargetTier TierOf(ulong objectId, uint baseId, ObjectKind kind)
        => MechaObjectiveTracker.ConfirmedObjectIds.Contains(objectId) ? MechaTargetTier.Objective
        : MechaObjectiveTracker.IsObjectiveBaseId(baseId) ? MechaTargetTier.Likely
        : MechaObjectiveTracker.IsRoleTargetByKind(baseId, kind) ? MechaTargetTier.Likely
        : MechaTargetTier.Other;

    /// <summary>分級是<b>怎麼</b>來的。同樣一個 <c>Likely</c>，來自遊戲標記與來自資料表推論的可信度差很多。</summary>
    private static string TierWhy(ulong objectId, uint baseId, ObjectKind kind)
    {
        if (MechaObjectiveTracker.ConfirmedObjectIds.Contains(objectId))
            return "confirmed-marker";
        if (MechaObjectiveTracker.LearnedBaseIds.Contains(baseId))
            return "learned-baseid";

        var role = MechaOpsMonitor.Role;
        if (role != MechaRole.Unknown)
        {
            var rowId = MechaOpsMonitor.EventDetail?.DataRowId ?? 0u;
            if (MechaObjectNames.ObjectIdsForRole(rowId, role).Contains(baseId))
                return "sheet-whitelist";
        }

        // 家族內靠 ObjectKind 補上的那一層（見 MechaObjectiveTracker.IsRoleTargetByKind）。
        if (MechaObjectiveTracker.IsRoleTargetByKind(baseId, kind))
            return "kind-role";

        // 🔑 家族成員但不屬於我這個身份（例如協助員看到的巨型目標）。
        //    它仍然會被列出來（雜訊過濾豁免），但分級就是 Other——這兩件事不一樣，要分得出來。
        if (MechaObjectNames.IsKnownEventObjectId(baseId))
            return "family-other-role";

        return "-";
    }

    /// <summary>
    /// 「ICE 現行的目標列舉會不會把這一筆濾掉，濾掉的話是被哪一條濾掉的」。
    ///
    /// 🔑 <b>過濾條件本身要記進 log</b>（2026-08-08 使用者要求）：不記的話，事後看到某個實體
    /// 沒出現在 ICE 的清單裡，分不出是「它不存在」還是「它被某條規則靜默排除了」。
    /// 這裡逐條重算 <see cref="MechaOpsMonitor.SampleTargets"/> 的判準，順序也一致。
    ///
    /// ⚠️ 這是<b>重算</b>不是攔截：真值仍以 <c>iceListed</c>（實際出現在 <c>ActiveTargets</c> 裡）為準。
    /// 兩者不一致本身就是值得追的線索。
    /// </summary>
    private static string IceFilterReason(SweepEntry e, MechaTargetTier tier)
    {
        if (!C.ShowMechaAoeOverlay || !C.ShowMechaTargets)
            return "sampling-off";

        if (e.Kind is ObjectKind.MountType or ObjectKind.Companion
            or ObjectKind.Ornament or ObjectKind.Retainer)
            return "excluded-kind";

        if (e.Kind == ObjectKind.Player && !C.MechaTargetsIncludePlayers)
            return "players-off";

        if (e.Distance > Math.Clamp(C.MechaTargetRadius, 5f, 200f))
            return "out-of-ice-radius";

        if (tier != MechaTargetTier.Other)
            return "-";   // 任務目標一律放行，不受下面兩條影響

        // 🔑 機甲事件物件家族（不分身份）同樣放行。⚠️ 這一行必須跟
        //    MechaOpsMonitor.SampleTargets 的豁免條件<b>逐字對應</b>——
        //    這裡是「重算」不是攔截，兩邊漂掉的話 log 會開始說謊。
        if (MechaObjectNames.IsKnownEventObjectId(e.BaseId))
            return "-";

        if (C.MechaTargetsTargetableOnly)
            return e.Targetable ? "-" : "targetable-only";

        if (C.MechaTargetsHideSceneryAndNpcs && MechaOpsMonitor.IsKnownNoise(e.Kind, e.Name, e.Targetable))
            return "known-noise";

        return "-";
    }

    // ────────────────────────────────────────────────────────────────────
    //  小工具
    // ────────────────────────────────────────────────────────────────────

    private static void EmitRing(string line) => IceLogging.Info(line, Prefix);

    /// <summary>
    /// 高流量的逐筆明細。**只寫 dalamud.log**，不進 ICE 的 <c>LogSystem</c>——
    /// 那是一份只有 3000 筆的環形紀錄，灌進去會把 ICE 其他診斷全部擠掉。
    /// 📌 仍然是 <c>Information</c> 級，使用者的 LogLevel 2 收得到。
    /// </summary>
    private static void EmitBulk(string line) => PluginLog.Information($"{Prefix} {line}");

    private static string TimeFields()
        => $"t={DateTime.Now:HH:mm:ss.fff};dt={(sessionActive ? (Environment.TickCount64 - sessionStartTick) / 1000.0 : 0):F1}";

    private static string Fmt(Vector3 v) => $"({v.X:F2},{v.Y:F2},{v.Z:F2})";

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static float FlatDistanceToPlayer(Vector3 pos)
    {
        var self = Player.Object;
        return self == null ? -1f : FlatDistance(self.Position, pos);
    }

    private static string RoleText(MechaRole role) => role switch
    {
        MechaRole.Pilot => "Pilot(駕駛員)",
        MechaRole.GroundSupport => "GroundSupport(協助員)",
        _ => "Unknown(判不出來)",
    };

    private static string BoolText(bool? v) => v == null ? "?" : v.Value ? "True" : "False";

    /// <summary><c>-1</c> 是「這一輪根本沒讀到」，跟「0 個」不是同一件事，所以印成 <c>?</c>。</summary>
    private static string CountText(int v) => v < 0 ? "?" : v.ToString();

    /// <summary>
    /// 名字的處理。兩件事：
    ///  ① <b>空名照實記成空字串</b>——機甲事件的目標常常本來就沒有名字
    ///     （台服「有害菌床」在 <c>EObjName</c> 裡就是空的），把它換成「?」會讓事後分不出
    ///     「沒有名字」與「查不到」；
    ///  ② 其他玩家的角色名預設縮寫（<see cref="MechaPrivacy"/>）——這份 log 是要拿出去給人看的。
    /// </summary>
    private static string SafeName(string? raw, ObjectKind kind, bool isSelf)
    {
        if (string.IsNullOrEmpty(raw))
            return "";

        var text = GameTextUtil.StripGameIcons(raw);
        if (text.Length == 0)
            return "";

        if (kind == ObjectKind.Player && !isSelf && !C.MechaShowFullPlayerNames)
            text = MechaPrivacy.Abbreviate(text);

        return Sanitize(text);
    }

    /// <summary>把會破壞「分號串」格式的字元換掉，讓每一行都還原得回欄位。</summary>
    private static string Sanitize(string s)
        => s.Replace(';', '．').Replace('\n', ' ').Replace('\r', ' ');
}
