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
///
/// 🔴🔴 跟 <see cref="MechaTarget"/> 一樣，這裡只有純值：
/// <b>沒有 IGameObject、沒有 Address、沒有任何原生指標。</b>
/// <paramref name="ObjectId"/> 只是個 id，每一幀都重新去 ObjectTable 查。
/// </summary>
/// <param name="ObjectId">對上的物件 id；<c>0</c> ＝ 這一幀在 ObjectTable 裡找不到對應物件。</param>
/// <param name="BaseId">
/// 對上的物件的 <c>IGameObject.BaseId</c>（EventObj 的話就是 EObj 的列 id）。
/// 📌 <b>身分比對一律用 <c>BaseId</c> 而不是 <c>DataId</c></b>——後者在 Dalamud 已標
/// <c>[Obsolete]</c>，兩者取的是同一個欄位，新碼統一用 <c>BaseId</c>。
/// 這個值就是「同一種任務目標」的鍵：機甲事件的目標往往有二十幾個同型個體，
/// 而遊戲只給其中幾個標記。
/// </param>
/// <param name="Position">要畫在哪。對上物件時是**物件當下的位置**，否則是標記自己的座標。</param>
/// <param name="Label">已經過 <see cref="MechaPrivacy"/> 處理的顯示名稱。</param>
/// <param name="StaleRisk">
/// 這一筆的來源是「掃 30 格」而不是遊戲自己的有效標記清單，所以**可能是上一階段留下的舊標記**。
/// 🔑 這是 PalacePal 式兩態標示的第二態：繪製端一定要讓它在畫面上看得出來
/// （淡色外框 ＋ 一律附帶「?」），不能只寫在 tooltip 裡。
/// </param>
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
///
/// ─────────────────────────────────────────────────────────────────────
/// 📌 <b>離線已證明（台服 7.20 <c>ffxiv_dx11.exe</c>，2026-08-06，capstone 反組譯）</b>
///
/// 整條標記鏈的佈局在台服是**逐位元組吻合**上游 CS 的，證據是
/// <c>WKSMechaEvent</c> 的建構式 0x1419A9FB0：
/// <code>
///   1419AA06C  lea  rbx, [rsi+3928h]      ; &amp;_mapMarkers[0]
///   1419AA073  mov  [rsi+3910h], rax      ; MapMarkerPtrs.First = 0
///   1419AA07A  mov  [rsi+3918h], rax      ; MapMarkerPtrs.Last  = 0
///   1419AA081  mov  [rsi+3920h], rax      ; MapMarkerPtrs.End   = 0   → StdVector @ 0x3910
///   1419AA088  lea  edi, [rax+1Eh]        ; 30 格            → FixedSizeArray30
///   1419AA0A6  lea  rcx, [rbx+68h]        ; MapMarkerData     @ marker+0x68
///   1419AA0B6  mov  [rbx+0B8h], rax       ; DataRowId/LayoutId
///   1419AA0C3  mov  [rbx+0C4h], ax        ; Type / Flags
///   1419AA0D0  add  rbx, 0C8h             ; stride            = 0xC8
///   1419AA0DD  lea  rdi, [rsi+5098h]      ; 陣列尾 = 0x3928 + 30*0xC8 ✓
///   1419AA0FE  mov  r8d, 1770h            ; memset 長度 = 30*0xC8      ✓
/// </code>
/// 拆事件那段（0x1419AA790）還順帶證明了兩件事：
/// <code>
///   1419AA790  mov rax,[rcx] / add rcx,8 / or byte [rax+0B0h],1
///   1419AA79E  cmp rcx,[rsi+3918h] / jne
/// </code>
/// ①vector 的元素**確實是指向 marker 的指標**（解參考後 +0xB0 ＝ marker+0x68+0x48
///   ＝ <c>MapMarkerData.Flags</c>），②遊戲自己在拆事件時把 bit0 設起來當「隱藏」。
///
/// <c>MapMarkerData</c> 的內部佈局同樣在台服證明過，直接讀
/// <c>MapMarkerData::SetData</c>（0x140A12C90，CS 特徵碼在台服**唯一命中**）：
/// <code>
///   140A12CA6  mov   [rcx+10h], ebx       ; IconId   @ +0x10
///   140A12D0C  movss [rdi+1Ch], xmm0      ; Position @ +0x1C  ← 本功能要的東西
///   140A12D17  movss [rdi+20h], xmm1
///   140A12D25  movss [rdi+24h], xmm0
///   140A12D2A  movss [rdi+28h], xmm1      ; Radius   @ +0x28
///   140A12CDE  and   byte [rdi+48h], 0C0h ; Flags    @ +0x48
/// </code>
/// ─────────────────────────────────────────────────────────────────────
///
/// 🔴🔴 <b>部署閘門的處置（2026-08-06）</b>
///
/// 上面那些偏移雖然離線證明過，<b>但 <c>MapMarkerPtrs</c> 的後備儲存區在遊戲堆積上，
/// 我們沒有辦法對它做真正的範圍驗證</b>——只能做形狀啟發式。依艦隊紅線
/// 「未證實假設 ＋ 原生指標 ＝ 部署閘門，要先改成『假設不成立也不會崩』才准出貨」，
/// <b>預設路徑改成完全不解參考的「掃 30 格純量」</b>，vector 那條路移到
/// <c>C.MechaObjectiveUseMarkerVector</c>（預設 <c>false</c>）後面。
///
/// 為什麼不做「真正的驗證」：唯一能判斷那塊堆積還活不活的方法是去 probe 行程記憶體
/// （<c>VirtualQuery</c> 之類），那既踩到「不對執行中的遊戲做記憶體探測」的紅線，
/// 又有 TOCTOU（查完到讀之間仍可能被釋放），而且就算頁面可讀也證明不了那是同一個
/// vector。所以這個假設**離線與執行期都無法證實**，只能不預設走它。
///
/// 🔴 <b>安全設計</b>（預設路徑上完全沒有未驗證的解參考）：
///
///  1. 進到這裡的 <c>WKSMechaEvent*</c> 已經通過 <c>MechaOpsMonitor.IsInsideEventArray</c>
///     的範圍驗證，也就是它一定落在模組自有配置 <c>_events</c> 的某一格開頭。
///     <c>_mapMarkers</c> 跨越 ev+0x3928..ev+0x5098，整段都在那一格（0x5130）之內，
///     所以**讀它跟讀已經出貨的 +0x5104 進度欄位是同一個安全等級**。
///  2. <b>預設路徑</b>＝<see cref="ScanAllSlots"/>：掃那 30 格的純量，
///     <b>一個指標都不解</b>。代價是可能讀到上一階段留下的舊標記——這件事被記在
///     <see cref="MechaObjective.StaleRisk"/> 上，繪製端一定會標示出來。
///  3. 就算在預設路徑，<c>MapMarkerPtrs.First/Last</c> 仍然會被**讀**（那兩個欄位
///     在已驗證範圍內，讀是安全的）：兩者都是 null 或長度為 0 時，那是遊戲給的
///     **權威「現在沒有標記」**，直接發布空清單。這一步不解任何指標，卻消掉了
///     「事件結束後把舊標記整批復活」這個最主要的過期來源。
///  4. <b>選用路徑</b>（<c>MechaObjectiveUseMarkerVector</c>）才會解參考 <c>First[i]</c>，
///     而且解出來的每一個元素指標還要再過一次 <see cref="IsInsideMarkerArray"/>
///     （落在 <c>_mapMarkers</c> 內 ＋ 對齊到格位開頭）才准讀；任何一個沒過就整批放棄、
///     退回 (2) 並在 UI 標成異常。
///  5. 全程不碰三個帶指標的欄位：<c>WKSMechaEventMapMarker.Name</c>（Utf8String）、
///     <c>MapMarkerData.TooltipString</c>、<c>WKSMechaEvent.CurrentStateHandler</c>。
///
/// 🔑 <b>使用者裁決的前置</b>：「先確認目標在不在 ObjectTable」。
/// 這條被做成機械閘門而不是口頭約定——見 <see cref="ResolveFrame"/> 與
/// <c>C.MechaObjectiveRequireObjectTable</c>（預設 <c>true</c>）：
/// 沒有在 ObjectTable 對上實體物件的標記**預設不畫**。
/// 副作用是「上面那些偏移萬一在某次改版後失準」時，讀出來的垃圾座標對不上任何物件，
/// 於是**自動什麼都不畫**，而不是在地上灑一堆錯的圈。
///
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
    ///
    /// 🔑 這是「已確認」與「疑似」兩態標示的第一態，也是目標過濾器的白名單來源：
    /// 目的指示對上的東西<b>一定</b>要列出來，不能被「只顯示可選取的物件」擋掉——
    /// 使用者回報的正是「任務目標不可選取所以看不到，關掉過濾又整片場景灌進來」。
    /// </summary>
    public static IReadOnlySet<ulong> ConfirmedObjectIds => confirmedObjectIds;
    private static HashSet<ulong> confirmedObjectIds = [];

    /// <summary>
    /// 這一幀被標記對上的那些物件的 <c>BaseId</c> 集合（第二態＝「疑似任務目標」的判準）。
    ///
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
    /// 這個 <c>BaseId</c> 是不是「疑似任務目標」。兩條**互相獨立**的線索，但**不對等**：
    ///
    /// 🔴 <b>2026-08-06 修正</b>：舊版把兩條直接取聯集，而資料表白名單裡的
    /// 2014720／2014722 是<b>駕駛員</b>的巨型目標（離線證據見
    /// <see cref="MechaObjectNames.ObjectIdsForRole"/>），於是協助員會看到一批不是他要打的東西
    /// ——使用者實機回報的「協助員身份參加有害菌床驅除指令，目標不一樣」。
    ///
    /// 現在的優先順序：
    /// <list type="number">
    ///   <item>① 執行期學到的<b>永遠</b>有效。那是遊戲自己標出來的——它給誰標就是誰的目標，
    ///         比我們從資料表推出來的任何東西都可信，也天然跟著身份走。</item>
    ///   <item>② 資料表白名單只在<b>判得出身份</b>時才補充，而且只補這個身份該看的那一批。
    ///         判不出身份（<see cref="MechaRole.Unknown"/>）就完全不用它。</item>
    /// </list>
    /// 所以最壞情況是「只剩遊戲自己的標記」＝<b>少畫</b>，不會畫錯批。
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
        if (!C.ShowMechaAoeOverlay || !C.ShowMechaObjectives)
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
    ///
    /// 🔑 這就是使用者要求的前置。<c>C.MechaObjectiveRequireObjectTable</c> 為 true（預設）時，
    /// 沒對上的標記直接不進清單，繪製端連知道都不會知道有這一筆。
    ///
    /// 🔴 只讀 <c>IGameObject</c> 的受管理屬性，抄成純值後立刻丟掉物件本身；
    /// 一個 <c>Address</c> 都不留（理由見 <see cref="MechaTarget"/>）。
    /// </summary>
    public static void ResolveFrame()
    {
        if (!C.ShowMechaAoeOverlay || !C.ShowMechaObjectives)
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

        // ⚠️ ObjectTable **整幀只掃一次**，抄成純值。
        //    先前的寫法是「每個標記各掃一次」＝最壞 30 × 整張表，
        //    而且每個物件都會做一次 Name.ToString() 配置字串。
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

            // 🔴 沒有名字 ≠ 沒有東西。台服的「有害菌床」在遊戲資料裡就是空字串
            //    （離線證據見 MechaObjectNames），所以這裡一定要給得出後備標籤，
            //    不能讓圈上一片空白。
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
    /// 🔴 第 3 段是這次修補的重點：台服「有害菌床」在 <c>EObjName</c> 裡就是空字串，
    /// 前兩段必定落空。舊碼在這裡畫的是 <c>MechaPrivacy.Unknown</c>（一個「?」），
    /// 使用者看到的就是「目標沒名字」。
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
    ///
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
    /// 📌 刻意用 Information 而不是 Debug：使用者跑 LogLevel 2，Debug 收不到，
    /// 而這一行正是「目的指示為什麼沒出現」唯一問得出答案的地方。
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
