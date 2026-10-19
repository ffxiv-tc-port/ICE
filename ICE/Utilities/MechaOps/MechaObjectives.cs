using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.Interop;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Text;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 一個機甲事件目的指示的**原始純值**快照，直接抄自
/// <c>WKSMechaEvent._mapMarkers[i]</c>（250ms 取樣一次）。
///
/// 🔴 這個型別裡沒有任何指標，也沒有從 <c>Utf8String Name</c>（+0x00）抄出來的字串——
/// 那個欄位內部帶指標，解參考它就等於押注 <c>Utf8String</c> 的佈局在台服也一樣。
/// 標籤一律改用 ObjectTable 對上的那個物件的名字（受管理 API，見 <see cref="MechaObjective"/>）。
/// </summary>
/// <param name="Slot">在 30 格 <c>_mapMarkers</c> 裡的索引。只拿來當繫結的備用鍵。</param>
/// <param name="LayoutId">
/// 佈局實例 id（+0xBC）。遊戲用它把標記綁到場景裡的物件；
/// 我們只拿它當「同一個標記」的穩定鍵，**不拿它去查任何表**。
/// </param>
/// <param name="MarkerType">
/// +0xC4。CS 註記 1..5 各自對應一個圖示 id。這裡不解讀語意，只用「非 0」當有效性訊號。
/// </param>
/// <param name="Position">
/// <c>MapMarkerData.Position</c>（marker +0x68 +0x1C）。世界座標，純量。
/// </param>
/// <param name="Hidden"><c>MapMarkerData.Flags</c> 的 bit0。遊戲拆事件時會把它設起來。</param>
internal readonly record struct MechaObjectiveMarker(
    int Slot,
    uint LayoutId,
    byte MarkerType,
    uint MarkerIcon,
    uint MapIconId,
    Vector3 Position,
    float Radius,
    bool Hidden)
{
    /// <summary>繫結用的穩定鍵：有 LayoutId 就用它，沒有就退回格位索引。</summary>
    public uint Key => LayoutId != 0 ? LayoutId : 0x8000_0000u | (uint)Slot;
}

/// <summary>
/// 一個目的指示在**這一幀**的可繪製狀態：原始標記 ＋（可能有的）ObjectTable 對應物件。
/// </summary>
/// <param name="ObjectId">對上的物件 id；<c>0</c> ＝ 這一幀在 ObjectTable 裡找不到對應物件。</param>
internal readonly record struct MechaObjective(
    MechaObjectiveMarker Marker,
    ulong ObjectId,
    uint BaseId,
    Vector3 Position,
    float Radius,
    string Label,
    ObjectKind Kind,
    float MatchDistance,
    bool StaleRisk)
{
    /// <summary>ObjectTable 有對上。這是「已確認」與「只是個座標」的分界。</summary>
    public bool Confirmed => ObjectId != 0;

    /// <summary>這一筆是使用者從右鍵選單釘的，不是遊戲給的標記（<c>Slot &lt; 0</c>）。</summary>
    public bool IsPin => Marker.Slot < 0;
}

/// <summary>
/// 這一輪的標記是從哪裡來的。⚠️ 三個值代表**三種不同的可信度**，不要合併。
/// </summary>
internal enum MechaMarkerSource
{
    /// <summary>
    /// 掃 30 格 <c>_mapMarkers</c> 的純量（**預設**）。一個指標都不解，所以絕不會崩，
    /// 但清單裡可能混有上一階段留下、遊戲還沒清掉的舊標記。
    /// </summary>
    Scan,

    /// <summary>
    /// 走 <c>MapMarkerPtrs</c>，也就是遊戲自己維護的有效標記清單。清單是準的，
    /// 但需要解參考堆積上的後備儲存區（見 <c>C.MechaObjectiveUseMarkerVector</c>）。
    /// </summary>
    Vector,

    /// <summary>
    /// 使用者開了 vector 模式，但這一輪的形狀檢查沒過，已自動退回掃描。
    /// ⚠️ 這是**異常**，跟預設的 <see cref="Scan"/> 不是同一件事，UI 要分開標示。
    /// </summary>
    VectorRejected,
}

/// <summary>
/// 機甲行動「目的指示標示」的取樣與解析。
/// ─────────────────────────────────────────────────────────────────────
/// 🔑 <b>使用者裁決的前置</b>：「先確認目標在不在 ObjectTable」。
/// 🔴 零自動化：這裡只算座標與距離，不選目標、不走位、不施放、不報名。
/// </summary>
internal static unsafe class MechaObjectiveTracker
{
    /// <summary>本輪（250ms）取樣到的原始標記。Framework 執行緒寫、繪製執行緒讀，整份換參考。</summary>
    public static IReadOnlyList<MechaObjectiveMarker> Markers => markers;
    private static List<MechaObjectiveMarker> markers = [];

    /// <summary>這一幀解析完的目的指示（含 ObjectTable 對應結果）。每幀重算。</summary>
    public static IReadOnlyList<MechaObjective> Active => active;
    private static List<MechaObjective> active = [];

    /// <summary>
    /// 使用者在右鍵選單釘選的物件 id。**只存 id、只活在這個 session**——
    /// 不寫進設定檔：物件 id 換場景就沒意義了，存起來只會累積垃圾。
    /// </summary>
    private static readonly HashSet<ulong> pinnedObjectIds = [];

    /// <summary>標記鍵 → 已確認的物件 id。同樣只存 id，每幀重查。</summary>
    private static readonly Dictionary<uint, ulong> bindings = [];

    /// <summary>
    /// 這一幀真的被標記對上的物件 id。
    /// 🔑 這是「已確認」與「疑似」兩態標示的第一態，也是目標過濾器的白名單來源：
    /// 目的指示對上的東西<b>一定</b>要列出來，不能被「只顯示可選取的物件」擋掉
    /// （任務目標常常不可選取）。
    /// </summary>
    public static IReadOnlySet<ulong> ConfirmedObjectIds => confirmedObjectIds;
    private static HashSet<ulong> confirmedObjectIds = [];

    /// <summary>
    /// 這一幀被標記對上的那些物件的 <c>BaseId</c> 集合（第二態＝「疑似任務目標」的判準）。
    /// 🔑 <b>這條線索不依賴任何遊戲資料表，而且天然跟著身份走</b>：遊戲只標了二十幾個菌床裡的
    /// 幾個，但被標到的那個的 <c>BaseId</c> 就等於告訴我們「長這樣的都是目標」；
    /// 而遊戲是<b>依你的身份</b>決定要標什麼給你看的。
    /// 資料表那條線索是另一條獨立的路，但它必須先依身份篩過才能用——見
    /// <see cref="IsObjectiveBaseId"/>。
    /// </summary>
    public static IReadOnlySet<uint> ConfirmedBaseIds => confirmedBaseIds;
    private static HashSet<uint> confirmedBaseIds = [];

    /// <summary>
    /// 這一場事件裡「曾經」被標記對上過的 <c>BaseId</c>。
    /// ⚠️ 跟 <see cref="ConfirmedBaseIds"/> 的差別是**生命週期**：階段切換時遊戲會把標記整批清掉，
    /// 但「長這樣的東西是這場事件的目標」在同一場事件裡不會變。事件結束／離開區域才丟。
    /// </summary>
    private static readonly HashSet<uint> learnedBaseIds = [];

    /// <summary>診斷用：這一場事件從遊戲的標記學到幾個 BaseId。</summary>
    public static int LearnedBaseIdCount => learnedBaseIds.Count;

    /// <summary>
    /// 同上，但給得出實際的 id。<see cref="MechaEventRecorder"/> 要拿它分辨
    /// 「這個分級是遊戲自己標的（可信）還是我們從資料表推的（要靠身份才篩得對）」。
    /// ⚠️ 唯讀視圖，呼叫端不得修改。
    /// </summary>
    public static IReadOnlySet<uint> LearnedBaseIds => learnedBaseIds;

    /// <summary>這個 <c>BaseId</c> 算不算（疑似）任務目標。兩條線索依序判斷：
    /// <list type="number">
    ///   <item>① 執行期學到的<b>永遠</b>有效。那是遊戲自己標出來的——它給誰標就是誰的目標，
    ///         比我們從資料表推出來的任何東西都可信，也天然跟著身份走。</item>
    ///   <item>② 資料表白名單只在<b>判得出身份</b>時才補充，而且只補這個身份該看的那一批。
    ///         判不出身份（<see cref="MechaRole.Unknown"/>）就完全不用它。</item>
    /// </list>
    /// </summary>
    public static bool IsObjectiveBaseId(uint baseId)
    {
        if (baseId == 0)
            return false;

        // ① 遊戲自己的答案，優先且無條件。
        if (learnedBaseIds.Contains(baseId))
            return true;

        // ② 資料表推論，依身份收窄。ObjectIdsForRole 對 Unknown 一律回空集合，
        //    這裡仍然先擋一次，省掉不必要的建表。
        var role = MechaOpsMonitor.Role;
        if (role == MechaRole.Unknown)
            return false;

        var rowId = MechaOpsMonitor.EventDetail?.DataRowId ?? 0u;
        return MechaObjectNames.ObjectIdsForRole(rowId, role).Contains(baseId);
    }

    /// <summary>
    /// 家族內靠 <c>ObjectKind</c> 判這是不是這個身份的目標。⚠️ 兩道前置缺一不可：判不出身份就不用它（<see cref="MechaRole.Unknown"/>），
    /// 不在家族白名單裡的 <c>DataId</c> 也不用它——否則場景裡每一個 <c>EventObj</c>
    /// 都會被判成駕駛員目標。
    /// </summary>
    public static bool IsRoleTargetByKind(uint baseId, ObjectKind kind)
    {
        if (baseId == 0)
            return false;

        var role = MechaOpsMonitor.Role;
        if (role == MechaRole.Unknown)
            return false;

        if (!MechaObjectNames.IsKnownEventObjectId(baseId))
            return false;

        return MechaObjectNames.RoleByObjectKind(kind) == role;
    }

    // ---- 顯示層的身份分流 ----

    /// <summary>
    /// 「這個東西是**誰的**目標」。
    /// 🔴 第 ① 層給出 <see cref="MechaTargetOwner.Shared"/> 時<b>絕不</b>往下問第 ② 層：
    /// 野外探測器是兩種身份共用的，但它在 ObjectTable 裡很可能是 <c>EventObj</c>，
    /// 往下問就會被判成駕駛員的東西，協助員就看不到自己該投放資源的地方。
    /// </summary>
    public static MechaTargetOwner OwnerOf(uint baseId, ObjectKind kind)
    {
        if (baseId == 0)
            return MechaTargetOwner.Unknown;

        var rowId = MechaOpsMonitor.EventDetail?.DataRowId ?? 0u;

        // ① 群組表。Unknown ＝這一場的群組沒提到它，才輪得到第二層。
        var fromGroups = MechaObjectNames.OwnerFromGroups(rowId, baseId);
        if (fromGroups != MechaTargetOwner.Unknown)
            return fromGroups;

        // ② ObjectKind。⚠️ 前置：一定要先確認它是機甲事件家族的 DataId，
        //    否則場景裡每一個 EventObj 都會被判成「駕駛員的東西」而被藏起來。
        if (!MechaObjectNames.IsKnownEventObjectId(baseId))
            return MechaTargetOwner.Unknown;

        return MechaObjectNames.RoleByObjectKind(kind) switch
        {
            MechaRole.Pilot => MechaTargetOwner.Pilot,
            MechaRole.GroundSupport => MechaTargetOwner.GroundSupport,
            _ => MechaTargetOwner.Unknown,
        };
    }

    /// <summary>
    /// 這一筆屬於「另一個身份」。
    /// ⚠️ <b>刻意不看設定</b>：開關由 <see cref="HiddenByRoleGate"/> 問。分成兩個方法是為了讓
    /// <see cref="MechaEventRecorder"/> 記得出「本來會被擋掉、但使用者把開關打開了」
    /// 這個狀態——只有一個布林的話，log 會把「沒被擋」與「開關開著」混成同一件事。
    /// </summary>
    public static bool IsOtherRoleTarget(uint baseId, ObjectKind kind)
    {
        var role = MechaOpsMonitor.Role;
        if (role == MechaRole.Unknown)
            return false;

        return OwnerOf(baseId, kind) switch
        {
            MechaTargetOwner.Pilot => role != MechaRole.Pilot,
            MechaTargetOwner.GroundSupport => role != MechaRole.GroundSupport,
            _ => false,
        };
    }

    /// <summary>
    /// 顯示層真正的閘門：<c>true</c> ＝這一筆這一幀<b>不要畫</b>。
    /// 使用者可以用 <c>C.MechaShowOtherRoleTargets</c> 把分流整個關掉（預設是開著分流的）。
    /// </summary>
    public static bool HiddenByRoleGate(uint baseId, ObjectKind kind)
        => !C.MechaShowOtherRoleTargets && IsOtherRoleTarget(baseId, kind);

    // ---- 診斷（給狀態視窗與 Information 級 log 用）----

    /// <summary>上一輪取樣讀到幾個有效標記。<c>-1</c> ＝ 這一輪根本沒讀到（不是 0 個）。</summary>
    public static int MarkerCount { get; private set; } = -1;

    /// <summary>其中有幾個在 ObjectTable 裡對上了實體物件。<c>-1</c> ＝ 未知。</summary>
    public static int ConfirmedCount { get; private set; } = -1;

    /// <summary>
    /// 這一輪的標記是從哪裡來的。預設是 <see cref="MechaMarkerSource.Scan"/>——
    /// ⚠️ 那**不是**故障，是部署閘門下的正常路徑；只有
    /// <see cref="MechaMarkerSource.VectorRejected"/> 才是異常。
    /// </summary>
    public static MechaMarkerSource Source { get; private set; } = MechaMarkerSource.Scan;

    /// <summary>掃描來源的標記可能是上一階段留下的。繪製端據此決定兩態樣式。</summary>
    private static bool SourceHasStaleRisk => Source != MechaMarkerSource.Vector;

    private static string lastDiagSignature = "";

    /// <summary>
    /// 250ms 一次的原始標記取樣。呼叫端必須**先**通過 <c>IsInsideEventArray</c>，
    /// 這是本類別所有安全論證的前提。
    /// </summary>
    public static void SampleMarkers(WKSMechaEvent* ev)
    {
        // 🔑 錄製模式開著時強制取樣，理由與 MechaOpsMonitor.SampleTargets 同一條：
        //    不強制的話（顯示開關預設關）錄出來的 log 缺掉目的指示這一整塊。
        //    ⚠️ 只影響取樣，不影響繪製。
        if (!MechaEventRecorder.ForceSampling && (!C.ShowMechaAoeOverlay || !C.ShowMechaObjectives))
        {
            ResetMarkers();
            return;
        }

        fixed (WKSMechaEventMapMarker* arrayBase = ev->MapMarkers)
        {
            var slotSize = (nint)sizeof(WKSMechaEventMapMarker);
            var slotCount = ev->MapMarkers.Length;
            if (slotSize <= 0 || slotCount <= 0)
            {
                ResetMarkers();
                return;
            }

            var found = new List<MechaObjectiveMarker>();

            // 🔑 形狀判定一律先做，**因為它只讀 First/Last 這兩個純量、不解任何指標**。
            //    在預設路徑上它唯一的用途就是下面那個「權威的空」判斷。
            var shape = ReadVectorShape(ev, out var first, out var count);

            if (shape == VectorShape.Empty)
            {
                // 🔑 空 vector 是**權威答案**：這場事件現在沒有目的指示。
                //    這一步在兩條路徑上都做，而且不解任何指標——
                //    遊戲拆標記時只清 vector，30 格 _mapMarkers 裡的舊資料是留著的，
                //    少了這一關，掃描路徑會在事件結束後把上一階段的標記整批復活。
                Source = MechaMarkerSource.Vector;   // 「沒有」這個答案本身是權威的，不帶過期風險
            }
            else if (C.MechaObjectiveUseMarkerVector
                     && shape == VectorShape.Ok
                     && TryReadViaVector(first, count, arrayBase, slotSize, slotCount, found))
            {
                // 🔴 只有使用者明確開啟時才會走到這裡（唯一會解參考堆積指標的路徑）。
                Source = MechaMarkerSource.Vector;
            }
            else
            {
                // 預設路徑：不解任何指標，直接掃已經被範圍驗證罩住的 30 格。
                found.Clear();
                ScanAllSlots(arrayBase, slotSize, slotCount, found);

                // ⚠️ 要分清楚「這是預設路徑」與「使用者開了 vector 但它壞了」。
                //    後者是異常，UI 上要跟前者長得不一樣。
                Source = C.MechaObjectiveUseMarkerVector
                    ? MechaMarkerSource.VectorRejected
                    : MechaMarkerSource.Scan;
            }

            markers = found;
            MarkerCount = found.Count;
        }
    }

    /// <summary>
    /// <c>MapMarkerPtrs</c> 的形狀判定結果。
    /// ⚠️ <see cref="Empty"/> 與 <see cref="Malformed"/> 是**完全不同**的答案：
    /// 前者是「真的沒有標記」，後者是「我們讀到的東西不像那個 vector」。
    /// 把兩者混為一談，就會在事件結束後把舊標記重新畫出來。
    /// </summary>
    private enum VectorShape
    {
        /// <summary>形狀合格，可以安全解參考。</summary>
        Ok,

        /// <summary>合格但長度為 0 ＝ 現在沒有任何目的指示。</summary>
        Empty,

        /// <summary>形狀不合格，不准解參考。</summary>
        Malformed,
    }

    /// <summary>
    /// 讀 <c>MapMarkerPtrs</c> 的形狀並判斷能不能安全解參考。
    /// 這三個欄位本身在驗證過的範圍內，**讀**沒有風險；這一關把關的是後面的解參考。
    /// </summary>
    private static VectorShape ReadVectorShape(WKSMechaEvent* ev, out Pointer<WKSMechaEventMapMarker>* first, out int count)
    {
        first = null;
        count = 0;

        var f = ev->MapMarkerPtrs.First;
        var l = ev->MapMarkerPtrs.Last;

        // 建構式把 First/Last/End 一起清 0，拆事件時也是。兩個都是 null ＝ 空。
        if (f == null && l == null)
            return VectorShape.Empty;

        // 只有一邊是 null ＝ 讀到的不是一組合法的 vector 欄位。
        if (f == null || l == null)
            return VectorShape.Malformed;

        var bytes = (nint)l - (nint)f;
        if (bytes < 0)
            return VectorShape.Malformed;

        var step = (nint)sizeof(Pointer<WKSMechaEventMapMarker>);
        if (step <= 0 || bytes % step != 0)
            return VectorShape.Malformed;

        // 指標本身要對齊。沒對齊等於讀到的不是指標欄位。
        if (((nint)f & (step - 1)) != 0)
            return VectorShape.Malformed;

        var n = bytes / step;
        if (n == 0)
            return VectorShape.Empty;   // 已配置但目前沒有元素，同樣是權威的「沒有」。

        // 上限就是 _mapMarkers 的格數：vector 裡裝的是指向那 30 格的指標，
        // 不可能比 30 多。超過就代表我們讀到的根本不是這個 vector。
        if (n > ev->MapMarkers.Length)
            return VectorShape.Malformed;

        first = f;
        count = (int)n;
        return VectorShape.Ok;
    }

    /// <summary>
    /// 走 vector：每一個解出來的元素指標都必須落在 <c>_mapMarkers</c> 裡、且對齊到格位開頭。
    /// 任何一個沒過就回 <c>false</c>，由呼叫端整批改走掃描路徑。
    /// </summary>
    private static bool TryReadViaVector(
        Pointer<WKSMechaEventMapMarker>* first,
        int count,
        WKSMechaEventMapMarker* arrayBase,
        nint slotSize,
        int slotCount,
        List<MechaObjectiveMarker> into)
    {
        for (var i = 0; i < count; i++)
        {
            var p = first[i].Value;
            if (p == null)
                continue;

            if (!IsInsideMarkerArray(p, arrayBase, slotSize, slotCount, out var slot))
            {
                if (EzThrottler.Throttle("MechaObjectiveVectorRejected", 60_000))
                {
                    IceLogging.Info(
                        "目的指示：MapMarkerPtrs 第 " + i + " 個元素沒通過範圍驗證，本輪改走純量掃描"
                        + "（這是設計上的安全退化，不是錯誤）："
                        + $" element=0x{(nint)p:X} arrayBase=0x{(nint)arrayBase:X}"
                        + $" slotSize=0x{slotSize:X} slots={slotCount}",
                        "[MechaOps]");
                }
                return false;
            }

            if (TryReadSlot(arrayBase, slotSize, slot, out var marker))
                into.Add(marker);
        }

        return true;
    }

    /// <summary>
    /// 退化路徑：掃全部 30 格，用純量判斷哪些像是「現在真的有效」。
    /// 這裡刻意寬鬆——過度篩選的失敗形式是「該顯示的沒顯示」，
    /// 而且後面還有 ObjectTable 閘門會再收一次。
    /// </summary>
    private static void ScanAllSlots(
        WKSMechaEventMapMarker* arrayBase,
        nint slotSize,
        int slotCount,
        List<MechaObjectiveMarker> into)
    {
        for (var slot = 0; slot < slotCount; slot++)
        {
            if (TryReadSlot(arrayBase, slotSize, slot, out var marker))
                into.Add(marker);
        }
    }

    /// <summary>
    /// 讀一格的純量並做有效性判斷。**只讀純量**，一個指標欄位都不碰。
    /// </summary>
    private static bool TryReadSlot(
        WKSMechaEventMapMarker* arrayBase,
        nint slotSize,
        int slot,
        out MechaObjectiveMarker marker)
    {
        marker = default;

        var m = (WKSMechaEventMapMarker*)((byte*)arrayBase + slot * slotSize);

        var type = m->Type;
        var icon = m->Icon;
        var layoutId = m->LayoutId;

        // 建構式把整塊 memset 成 0，所以「type 與 icon 與 layoutId 全 0」＝這格沒被用過。
        if (type == 0 && icon == 0 && layoutId == 0)
            return false;

        var data = m->MapMarkerData;
        var pos = data.Position;

        // 座標要是真的數字。NaN/Inf 進到繪製端會變成畫面上的垃圾。
        if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
            return false;

        // 全 0 座標＝沒填。宇宙區域的原點附近不會是任務目的地。
        if (pos == Vector3.Zero)
            return false;

        var radius = float.IsFinite(data.Radius) ? Math.Clamp(data.Radius, 0f, 500f) : 0f;

        marker = new MechaObjectiveMarker(
            slot,
            layoutId,
            type,
            icon,
            data.IconId,
            pos,
            radius,
            (data.Flags & 1) != 0);
        return true;
    }

    /// <summary>
    /// 元素指標必須落在 <c>_mapMarkers</c> 之內**且**對齊到某一格的開頭。
    /// 跟 <c>MechaOpsMonitor.IsInsideEventArray</c> 是同一套做法，理由也一樣：
    /// 通過之後，後面所有純量讀取都保證落在模組自有配置裡。
    /// </summary>
    private static bool IsInsideMarkerArray(
        WKSMechaEventMapMarker* candidate,
        WKSMechaEventMapMarker* arrayBase,
        nint slotSize,
        int slotCount,
        out int slot)
    {
        slot = 0;

        var delta = (nint)candidate - (nint)arrayBase;
        if (delta < 0 || delta >= slotCount * slotSize)
            return false;
        if (delta % slotSize != 0)
            return false;

        slot = (int)(delta / slotSize);
        return true;
    }

    /// <summary>
    /// 每幀執行的解析：把 250ms 前抄下來的標記，對上**這一幀**的 ObjectTable。
    /// 🔑 這就是使用者要求的前置。<c>C.MechaObjectiveRequireObjectTable</c> 為 true（預設）時，
    /// 沒對上的標記直接不進清單，繪製端連知道都不會知道有這一筆。
    /// 🔴 只讀 <c>IGameObject</c> 的受管理屬性，抄成純值後立刻丟掉物件本身；
    /// 一個 <c>Address</c> 都不留（理由見 <see cref="MechaTarget"/>）。
    /// </summary>
    public static void ResolveFrame()
    {
        // 🔑 錄製模式強制解析（只影響取樣，不影響繪製），理由同 SampleMarkers。
        if (!MechaEventRecorder.ForceSampling && (!C.ShowMechaAoeOverlay || !C.ShowMechaObjectives))
        {
            ClearActive();
            return;
        }

        // 🔑 絕大多數時間會走這一條（沒在機甲事件裡就沒有標記），
        //    所以下面那次 ObjectTable 掃描根本不會發生。
        if (markers.Count == 0 && pinnedObjectIds.Count == 0)
        {
            ClearActive();
            ConfirmedCount = MarkerCount < 0 ? -1 : 0;
            ReportDiagnostics();
            return;
        }

        var self = Player.Object;
        if (self == null)
        {
            ClearActive();
            return;
        }

        var selfId = self.GameObjectId;

        // ⚠️ ObjectTable **整幀只掃一次**，抄成純值（每個標記各掃一次的話最壞是 30 × 整張表）。
        //    這裡只抄 id/座標/半徑/種類，名字留到真的配對成功時才取。
        candidatesScratch.Clear();
        foreach (var obj in Svc.Objects)
        {
            if (obj == null)
                continue;

            var kind = obj.ObjectKind;

            // 永遠不會是任務目的地的東西。清單刻意保持很短。
            if (kind is ObjectKind.MountType or ObjectKind.Companion
                or ObjectKind.Ornament or ObjectKind.Retainer)
                continue;

            candidatesScratch.Add(new ObjectSnapshot(
                // 📌 身分比對用 BaseId：Dalamud 已把 DataId 標成 [Obsolete]，兩者同值。
                obj.GameObjectId, obj.BaseId, obj.Position, obj.HitboxRadius, kind));
        }

        var matchRadius = Math.Clamp(C.MechaObjectiveMatchRadius, 1f, 50f);
        var matchRadiusSq = matchRadius * matchRadius;

        // 已經繫結的物件放寬到 4 倍距離才解除，否則目標一移動就會一直重新配對。
        var keepRadiusSq = matchRadiusSq * 16f;

        var resolved = new List<MechaObjective>();
        var confirmed = 0;
        seenKeysScratch.Clear();

        // 這一輪要發布的兩個白名單。整份換參考發布，讀取端不會看到半成品。
        var confirmedIds = new HashSet<ulong>();
        var confirmedBases = new HashSet<uint>();

        // 這一輪的標記是掃描來的還是遊戲的有效清單來的——整批同一個值。
        var staleRisk = SourceHasStaleRisk;

        // 「目標 N」的 N。⚠️ 只有在**真的沒有名字**時才會用到，所以有名字的目標
        //   不會佔號碼；編號依標記格位順序遞增，同一組標記下每幀都是同一個號。
        var unnamedOrdinal = 0;

        foreach (var marker in markers)
        {
            if (marker.Hidden)
                continue;

            seenKeysScratch.Add(marker.Key);

            bindings.TryGetValue(marker.Key, out var bound);
            var match = FindMatch(marker.Position, bound, matchRadiusSq, keepRadiusSq);

            if (match.ObjectId != 0)
            {
                bindings[marker.Key] = match.ObjectId;
                confirmed++;

                // 🔑 這兩行就是目標過濾器的資料來源：被標記對上的那個物件本身（已確認），
                //    以及「長得跟它一樣的東西」（疑似）。詳見 ConfirmedBaseIds 的註解。
                confirmedIds.Add(match.ObjectId);
                if (match.BaseId != 0)
                {
                    confirmedBases.Add(match.BaseId);
                    // 事件中途標記可能整批消失（階段切換），但「這個 BaseId 是目標」
                    // 這件事在同一場事件裡不會變，所以另外記一份到事件結束才丟。
                    learnedBaseIds.Add(match.BaseId);
                }
            }
            else
            {
                bindings.Remove(marker.Key);
                if (C.MechaObjectiveRequireObjectTable)
                    continue;   // 🔑 前置沒過 → 不畫。這就是那道機械閘門。
            }

            // 🔴 沒有名字 ≠ 沒有東西。台服的「有害菌床」在遊戲資料裡就是空字串，
            //    所以這裡一定要給得出後備標籤，不能讓圈上一片空白。
            var label = match.ObjectId != 0
                ? ResolveLabel(match.ObjectId, match.BaseId, match.Kind, selfId, ref unnamedOrdinal)
                : FallbackLabel(ref unnamedOrdinal);

            var drawRadius = match.ObjectId != 0
                ? MathF.Max(MathF.Max(match.HitboxRadius, marker.Radius), 1.5f)
                : MathF.Max(marker.Radius, 2f);

            resolved.Add(new MechaObjective(
                marker,
                match.ObjectId,
                match.BaseId,
                match.ObjectId != 0 ? match.Position : marker.Position,
                drawRadius,
                label,
                match.Kind,
                match.Distance,
                staleRisk));
        }

        // 使用者手動釘選的物件（右鍵選單）。用同一份快照，不再掃一次表。
        foreach (var snap in candidatesScratch)
        {
            if (!pinnedObjectIds.Contains(snap.ObjectId))
                continue;

            resolved.Add(new MechaObjective(
                new MechaObjectiveMarker(-1, 0, 0, 0, 0, snap.Position, 0f, false),
                snap.ObjectId,
                snap.BaseId,
                snap.Position,
                MathF.Max(snap.HitboxRadius, 1.5f),
                ResolveLabel(snap.ObjectId, snap.BaseId, snap.Kind, selfId, ref unnamedOrdinal),
                snap.Kind,
                0f,
                // 釘選是使用者自己剛按的、而且每幀都在 ObjectTable 重查，
                // 沒有「可能是舊資料」可言。
                false));
        }

        // 清掉已經不存在的標記留下的繫結，免得字典無限成長。
        if (bindings.Count > 0)
        {
            List<uint>? stale = null;
            foreach (var key in bindings.Keys)
            {
                if (!seenKeysScratch.Contains(key))
                    (stale ??= []).Add(key);
            }
            if (stale != null)
            {
                foreach (var key in stale)
                    bindings.Remove(key);
            }
        }

        active = resolved;
        ConfirmedCount = confirmed;

        // 整份換參考發布（讀取端是同一條 Framework 執行緒的 SampleTargets，
        // 以及繪製執行緒的疊加層；兩邊都只讀）。
        confirmedObjectIds = confirmedIds;
        confirmedBaseIds = confirmedBases;

        ReportDiagnostics();
    }

    /// <summary>ObjectTable 的一格純值快照。**沒有名字**——名字只在配對成功時才取。</summary>
    private readonly record struct ObjectSnapshot(
        ulong ObjectId,
        uint BaseId,
        Vector3 Position,
        float HitboxRadius,
        ObjectKind Kind);

    /// <summary>每幀重用的暫存，避免每幀配置新的 List/HashSet。</summary>
    private static readonly List<ObjectSnapshot> candidatesScratch = [];
    private static readonly HashSet<uint> seenKeysScratch = [];

    /// <summary>
    /// 物件 id → 已遮蔽的顯示名稱的快取。
    /// 目的指示的繫結很穩定，所以不必每幀都 <c>Name.ToString()</c> 配置一次字串。
    /// ⚠️ 快取的是**遮蔽後**的結果，所以設定一改就要清掉。
    /// 📌 空字串 ＝「查過了，這個物件真的沒有名字」——不要因此每幀重查。
    /// </summary>
    private static readonly Dictionary<ulong, string> labelCache = [];
    private static bool labelCacheFullNames;

    /// <summary>
    /// 目的指示的顯示標籤，三段後備：
    /// <list type="number">
    ///   <item>ObjectTable 的 <c>Name</c>（過 <see cref="MechaPrivacy"/>）；</item>
    ///   <item>資料表 <c>EObjName[BaseId]</c>（例如「野外探測器」「巨型偏屬性水晶」）；</item>
    ///   <item>都沒有 → 「目標 N」。</item>
    /// </list>
    /// </summary>
    private static string ResolveLabel(ulong objectId, uint baseId, ObjectKind kind, ulong selfId, ref int unnamedOrdinal)
    {
        // 設定切換時整份作廢——否則關掉遮蔽之後畫面上還是舊的縮寫。
        if (labelCacheFullNames != C.MechaShowFullPlayerNames)
        {
            labelCacheFullNames = C.MechaShowFullPlayerNames;
            labelCache.Clear();
        }

        if (labelCache.TryGetValue(objectId, out var cached))
            return cached.Length > 0 ? cached : FallbackLabel(ref unnamedOrdinal);

        string real;
        var obj = Svc.Objects.SearchById(objectId);
        if (obj == null)
        {
            real = string.Empty;
        }
        else
        {
            var raw = GameTextUtil.StripGameIcons(obj.Name.ToString());
            if (raw.Length == 0)
                raw = MechaObjectNames.FromSheet(baseId) ?? string.Empty;

            real = raw.Length == 0
                ? string.Empty
                : MechaPrivacy.Sanitize(raw, kind, objectId == selfId);
        }

        // 上限只是防呆：一場機甲行動不可能繫結到幾百個不同物件。
        if (labelCache.Count < 256)
            labelCache[objectId] = real;

        return real.Length > 0 ? real : FallbackLabel(ref unnamedOrdinal);
    }

    /// <summary>
    /// 沒有任何名字可用時的標籤：「目標 1」「目標 2」…
    /// 🔑 刻意<b>不</b>畫成空白或單一個「?」——「不知道叫什麼」跟「這裡有一個目標」是兩件事，
    /// 後者必須看得見，而且多個目標要分得開（號碼依標記格位順序，同一組標記下每幀相同）。
    /// </summary>
    private static string FallbackLabel(ref int unnamedOrdinal)
        => "Objective ??".Loc(++unnamedOrdinal);

    private readonly record struct ObjectMatch(
        ulong ObjectId,
        uint BaseId,
        Vector3 Position,
        float HitboxRadius,
        ObjectKind Kind,
        float Distance);

    /// <summary>
    /// 在這一幀的 ObjectTable 快照裡找對應的物件。
    /// 先試已繫結的那一個（放寬距離），沒有才用最近距離重新配對。
    /// ⚠️ 距離一律在 XZ 平面上算：標記的 Y 可能是地圖平面高度而不是物件的實際高度，
    /// 拿 3D 距離配對會在有高低差的地形上**靜默**配不到。
    /// </summary>
    private static ObjectMatch FindMatch(Vector3 markerPos, ulong bound, float matchRadiusSq, float keepRadiusSq)
    {
        var best = default(ObjectMatch);
        var bestSq = float.MaxValue;

        foreach (var snap in candidatesScratch)
        {
            var dx = snap.Position.X - markerPos.X;
            var dz = snap.Position.Z - markerPos.Z;
            var dSq = dx * dx + dz * dz;

            if (bound != 0 && snap.ObjectId == bound)
            {
                // 繫結的那一個：只要還在放寬範圍內就繼續用它，不管有沒有更近的。
                if (dSq <= keepRadiusSq)
                    return new ObjectMatch(snap.ObjectId, snap.BaseId, snap.Position, snap.HitboxRadius, snap.Kind, MathF.Sqrt(dSq));
                continue;
            }

            if (dSq > matchRadiusSq || dSq >= bestSq)
                continue;

            bestSq = dSq;
            best = new ObjectMatch(snap.ObjectId, snap.BaseId, snap.Position, snap.HitboxRadius, snap.Kind, MathF.Sqrt(dSq));
        }

        return best;
    }

    // ---- 右鍵選單用的釘選 API（純顯示，見 MechaContextMenu）----

    public static bool IsPinned(ulong objectId) => objectId != 0 && pinnedObjectIds.Contains(objectId);

    public static void Pin(ulong objectId)
    {
        if (objectId != 0)
            pinnedObjectIds.Add(objectId);
    }

    public static void Unpin(ulong objectId) => pinnedObjectIds.Remove(objectId);

    public static int PinnedCount => pinnedObjectIds.Count;

    public static void ClearPins() => pinnedObjectIds.Clear();

    /// <summary>
    /// 狀態變化時輸出一行 Information。
    /// 📌 要使用者回報的診斷：
    /// 這一行正是「目的指示為什麼沒出現」唯一問得出答案的地方。
    /// </summary>
    private static void ReportDiagnostics()
    {
        // ⚠️ 這個方法**每幀**都會被呼叫，所以簽章不能含每幀都在抖的數字。
        //    ConfirmedCount 會隨物件進出而跳動，這裡只分「0 vs 非 0」——
        //    因為要回答的問題是「為什麼什麼都沒畫」，不是「現在剛好幾個」。
        var sig = $"{MarkerCount}/{(ConfirmedCount > 0 ? "+" : ConfirmedCount)}/{Source}/{pinnedObjectIds.Count}";
        if (sig == lastDiagSignature)
            return;
        if (!EzThrottler.Throttle("MechaObjectiveDiag", 10_000))
            return;   // 這一輪先不印也不更新 signature，下一輪補印最終狀態
        lastDiagSignature = sig;

        var sb = new StringBuilder();
        sb.Append("目的指示：讀到 ").Append(MarkerCount < 0 ? "?" : MarkerCount.ToString()).Append(" 個標記，")
          .Append(ConfirmedCount < 0 ? "?" : ConfirmedCount.ToString()).Append(" 個在 ObjectTable 對上實體物件");
        sb.Append(Source switch
        {
            // ⚠️ 預設就是 Scan，所以這裡的措辭刻意不是「錯誤」——但仍然要講清楚代價。
            MechaMarkerSource.Scan =>
                "（來源：逐格掃描＝預設的安全路徑，不解任何指標；清單可能含上一階段的舊標記）",
            MechaMarkerSource.VectorRejected =>
                "（⚠️ 已開啟 MapMarkerPtrs 模式，但這一輪形狀檢查沒過，已自動退回逐格掃描）",
            _ => "（來源：MapMarkerPtrs，遊戲自己的有效標記清單）",
        });
        if (pinnedObjectIds.Count > 0)
            sb.Append("；手動釘選 ").Append(pinnedObjectIds.Count).Append(" 個");
        if (MarkerCount > 0 && ConfirmedCount == 0)
            sb.Append("。⚠️ 一個都沒對上：預設不會畫任何東西（前置閘門）。"
                    + "配對半徑目前 ").Append(C.MechaObjectiveMatchRadius.ToString("F0")).Append(" 公尺。");

        // 目標識別用的兩條線索，以及身份把白名單收窄到多少。
        // ⚠️ 兩個數字要分開看：學到的是遊戲自己標的（跟著身份走），
        //    白名單是我們從資料表推的（要靠身份才篩得對）。
        var role = MechaOpsMonitor.Role;
        var rowId = MechaOpsMonitor.EventDetail?.DataRowId ?? 0u;
        sb.Append("\n  目標識別：身份=").Append(role switch
          {
              MechaRole.Pilot => "駕駛員",
              MechaRole.GroundSupport => "協助員",
              _ => "判不出來",
          })
          .Append("；標記學到 ").Append(learnedBaseIds.Count).Append(" 個 BaseId")
          .Append(learnedBaseIds.Count > 0 ? "（" + string.Join(",", learnedBaseIds.OrderBy(x => x)) + "）" : "")
          .Append("；資料表白名單本身 ").Append(MechaObjectNames.KnownEventObjectIds.Count)
          .Append(" 個，依身份篩後 ")
          .Append(role == MechaRole.Unknown ? "不套用" : MechaObjectNames.RoleIdCount(rowId, role) + " 個");

        // 校準用的原始值。⚠️ 這裡只印座標與 id，**不印任何角色名**。
        var layoutMap = MechaObjectNames.LayoutToBaseId;
        foreach (var m in markers)
        {
            sb.Append($"\n  slot{m.Slot:00} layout={m.LayoutId} type={m.MarkerType} icon={m.MarkerIcon}")
              .Append($" mapIcon={m.MapIconId} pos=({m.Position.X:F1}, {m.Position.Y:F1}, {m.Position.Z:F1})")
              .Append($" r={m.Radius:F1} hidden={m.Hidden}");

            // ⚠️ 純診斷：假設 marker 的 LayoutId 與 WKSMechaEventObject 第 0 欄是同一套編號。
            //    這個假設**沒有離線證明**，所以只印出來給人看，不參與任何顯示判斷。
            //    對得上代表假設成立（可以拿去做更強的識別），對不上就只是少一段字。
            if (m.LayoutId != 0 && layoutMap.TryGetValue(m.LayoutId, out var mappedBase))
                sb.Append($" sheetBaseId={mappedBase}");
        }

        IceLogging.Info(sb.ToString(), "[MechaOps]");
    }

    private static void ResetMarkers()
    {
        if (markers.Count > 0)
            markers = [];
        MarkerCount = -1;
        Source = MechaMarkerSource.Scan;
    }

    private static void ClearActive()
    {
        if (active.Count > 0)
            active = [];
        ConfirmedCount = -1;

        // ⚠️ 這兩份是「這一幀」的狀態，跟著 active 一起清。
        //    「這場事件的目標長什麼樣」記在 learnedBaseIds，那一份留到事件結束。
        if (confirmedObjectIds.Count > 0)
            confirmedObjectIds = [];
        if (confirmedBaseIds.Count > 0)
            confirmedBaseIds = [];
    }

    /// <summary>離開宇宙區域／事件消失：全部清空，連繫結與釘選一起丟掉。</summary>
    public static void Deactivate()
    {
        ResetMarkers();
        ClearActive();
        bindings.Clear();
        pinnedObjectIds.Clear();
        labelCache.Clear();
        candidatesScratch.Clear();
        learnedBaseIds.Clear();
        lastDiagSignature = "";
    }

    /// <summary>事件讀不到，但還在宇宙區域：標記清掉，釘選留著（那是使用者自己按的）。</summary>
    public static void ClearMarkersOnly()
    {
        ResetMarkers();
        bindings.Clear();

        // 沒有進行中的事件了 → 上一場學到的「目標長什麼樣」也失效。
        // （階段切換時走的不是這一條，所以事件進行中不會被清掉。）
        learnedBaseIds.Clear();
    }
}
