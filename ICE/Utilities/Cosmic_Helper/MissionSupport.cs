using ICE.Utilities.Cosmic;
using ICE.Utilities.GatheringHelper;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 「ICE 跑不跑得動這個任務」的<b>唯一判定入口</b>。
/// </summary>
/// <remarks>
/// 以前全艦隊只有一句 <c>UnsupportedMissions.Ids.Contains(id)</c> 散在 6 個地方，而「跑不動」
/// 其實有好幾種互不相干的成因（缺內建 AutoHook 釣魚設定、這個客戶端根本沒有這筆任務資料、
/// 上游手動列黑名單）。把判定收斂成一個函式之後：<br/>
/// ① 顯示端只要問一次就能拿到「未支援」＋<b>原因文字</b>；<br/>
/// ② 未來多一種成因只要改這裡，不必再去六個地方各加一次條件。<br/><br/>
///
/// 📌 <b>2026-08-07 更新</b>：<see cref="UnsupportedMissions.Ids"/> 的內容已經動過一次——
/// 495 與 543 解除停用、494 留下但換了成因。所以下面那段「16 筆／逐筆一致」的離線核對
/// <b>是當時的快照，不是現況</b>；要重新核對請以 <c>FishingPresets.cs</c> 為準重跑一次。
/// <br/>同時新增了 <see cref="UnsupportedReason.RequiresAutoHookFolderImport"/>：那一條
/// <b>不看清單</b>，而是看「內建 preset 是不是資料夾格式」＋「安裝的 AutoHook 支不支援」，
/// 所以它會隨使用者更新 AutoHook 而自動消失。<br/><br/>
///
/// 📌 離線核對（台服 7.20 當時的快照，逐筆重跑 <c>FishingPresets.cs</c> 與 <c>exd-tc/7.20/WKSMissionUnit.csv</c>）：
/// <list type="bullet">
/// <item>內建釣魚設定表 95 筆，其中 <b>16 筆的 preset 清單是空的</b>，而那 16 個 ID
///       <b>與黑名單完全相同</b>（479/481/482/484/486/487/489~495/510/511/543）。</item>
/// <item>台服有名字的任務共 544 筆（row 1..544），其中 <b>52 個是釣魚任務，全部都在設定表裡</b>
///       ——所以「釣魚任務但整筆不在設定表」目前是 0 筆，這條規則今天不會排除任何任務。</item>
/// <item>黑名單另外那 3 個（0／1003／1006）在台服<b>本來就不在任務表裡</b>，UI 與挑選流程都碰不到。</item>
/// </list>
/// 也就是說：這份「用真實成因推導」的判定，在台服今天的結果與寫死清單<b>逐筆一致</b>——
/// 它不改變任何現行行為，只是把原因說得出口，並且在上游補資料 / 台服開放第二顆星時自動跟上。
/// </remarks>
internal static class MissionSupport
{
    /// <summary>「ICE 跑不動這個任務」的成因。順序＝判定優先序，愈前面愈具體。</summary>
    internal enum UnsupportedReason
    {
        /// <summary>支援。</summary>
        None = 0,

        /// <summary>釣魚任務，但 ICE 內建的 AutoHook 設定是空的／沒有這一筆，而使用者也沒指定自訂 preset。</summary>
        MissingFishingPreset,

        /// <summary>這個客戶端查不到這筆任務的資料（台服 = 尚未開放的星球，整列是空的）。</summary>
        NotInMissionSheet,

        /// <summary>
        /// 內建設定是整包資料夾（<c>AHFOLDER_</c>），但目前安裝的 AutoHook 沒有資料夾匯入 IPC。
        /// </summary>
        /// <remarks>
        /// 🔴 這條是<b>出貨順序的相容性閘門</b>：ICE 與 AutoHook 是同一波出貨的，但使用者可能只更新其中一個。
        /// 沒有這條的話症狀會是「接了任務、站在釣點不動直到逾時」，而且完全沒有訊息。
        /// </remarks>
        RequiresAutoHookFolderImport,

        /// <summary>在 <see cref="UnsupportedMissions.Ids"/> 黑名單裡，但推導不出更具體的成因。</summary>
        Blacklisted,
    }

    /// <summary>任務名稱前面掛的標記。跟疊加層的任務類型標籤用同一種【】造型，一眼可辨。</summary>
    internal static string Marker => "【" + "Unsupported".Loc() + "】";

    /// <summary>
    /// 判定任務支不支援。<b>顯示端與挑選流程都只准問這一個函式</b>，不要再自己 <c>Contains</c>。
    /// </summary>
    internal static UnsupportedReason GetReason(uint missionId)
    {
        // 「沒有進行中的任務」用 0 表示。它同時也在黑名單裡（上游拿它當哨兵），
        // 但把「沒任務」講成「不支援」只會製造假訊息，所以在最前面擋掉。
        if (missionId == 0)
            return UnsupportedReason.None;

        // 缺內建釣魚設定 —— 黑名單那 16 個的**實際成因**，優先於籠統的「在黑名單裡」回報。
        if (MissingBuiltinFishingPreset(missionId))
            return UnsupportedReason.MissingFishingPreset;

        // 內建設定是整包資料夾，但這版 AutoHook 匯不進去 —— 放在黑名單判定之前，
        // 因為它是比「在黑名單裡」更具體的成因，而且**使用者更新 AutoHook 之後就會自己消失**。
        if (NeedsMissingFolderImport(missionId))
            return UnsupportedReason.RequiresAutoHookFolderImport;

        if (UnsupportedMissions.Ids.Contains(missionId))
            return UnsupportedReason.Blacklisted;

        // ⚠️ 這條放最後：SheetMissionDict 的鍵集合是「Name 不為空的 row」，台服 7.20 = 1..544。
        //    查不到就代表 UI 拿不到名字、流程也拿不到任何資料，一定跑不動。
        if (!CosmicHelper.SheetMissionDict.ContainsKey(missionId))
            return UnsupportedReason.NotInMissionSheet;

        return UnsupportedReason.None;
    }

    /// <summary>支援與否的簡答版。</summary>
    internal static bool IsSupported(uint missionId) => GetReason(missionId) == UnsupportedReason.None;

    /// <summary>不支援時回 true，並帶出原因，方便 <c>if (IsUnsupported(id, out var why))</c> 的寫法。</summary>
    internal static bool IsUnsupported(uint missionId, out UnsupportedReason reason)
    {
        reason = GetReason(missionId);
        return reason != UnsupportedReason.None;
    }

    /// <summary>不支援時回 true（不需要原因的呼叫端用）。</summary>
    internal static bool IsUnsupported(uint missionId) => GetReason(missionId) != UnsupportedReason.None;

    /// <summary>
    /// 釣魚任務缺內建 AutoHook 設定。
    /// <br/>⚠️ 使用者自己填了 <c>AutoHookPresetName</c> 就不算缺 —— 那條路徑
    /// （<c>Task_ExecuteMission</c> 的 <c>SetPreset</c>）根本不碰內建表。
    /// </summary>
    private static bool MissingBuiltinFishingPreset(uint missionId)
    {
        // 這個任務有沒有內建設定？沒有這一筆、或有但清單是空的，都算「內建設定缺」。
        var builtinMissing = !GatheringUtil.FishingPreset.TryGetValue(missionId, out var preset)
                             || preset.FishingPreset.Count == 0;
        if (!builtinMissing)
            return false;

        // 只有釣魚任務才需要 AutoHook 設定。查不到任務資料時交給 NotInMissionSheet 去回報，
        // 不要在這裡猜（猜錯會把「資料還沒開放」誤報成「preset 沒填」）。
        if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var info))
            return false;
        if (!info.Attributes.HasFlag(MissionAttributes.Fish))
            return false;

        // 使用者指定了自訂 preset 名稱 → 有得用，不算缺。
        if (C.MissionConfig.TryGetValue(missionId, out var config)
            && !string.IsNullOrWhiteSpace(config.AutoHookPresetName))
            return false;

        return true;
    }

    /// <summary>
    /// 這個任務的內建設定是整包資料夾匯出，而目前安裝的 AutoHook 沒有對應的匯入 IPC。
    /// </summary>
    /// <remarks>
    /// ⚠️ 判定<b>資料驅動</b>：看 preset 字串的前綴，不寫死任務 ID。
    /// 之後上游再補進來的資料夾型 preset 會自動吃到同一條規則。
    /// <br/>📌 <c>SupportsFolderImport()</c> 內部有快取，所以這裡每幀被問也不會反覆做 IPC 呼叫。
    /// </remarks>
    private static bool NeedsMissingFolderImport(uint missionId)
    {
        if (!GatheringUtil.FishingPreset.TryGetValue(missionId, out var preset))
            return false;

        var first = preset.FishingPreset.FirstOrDefault();
        if (first == null || !first.StartsWith("AHFOLDER", StringComparison.Ordinal))
            return false;

        // 使用者指定了自訂 preset 名稱 → 走 SetPreset，根本不碰內建表，不受這個限制。
        if (C.MissionConfig.TryGetValue(missionId, out var config)
            && !string.IsNullOrWhiteSpace(config.AutoHookPresetName))
            return false;

        return !P.AutoHook.SupportsFolderImport();
    }

    /// <summary>
    /// 原因的一句話說明（聊天視窗與 tooltip 共用）。語氣刻意寫成「這是預期行為」——
    /// 使用者原本看到的是一句像故障訊息的英文，這一條就是要修掉那個誤會。
    /// </summary>
    /// <remarks>⚠️ 字面是 <c>.Loc()</c> 的鍵，改動時要同步 <c>LanguageChineseTraditional.ini</c>。</remarks>
    internal static string ReasonText(UnsupportedReason reason) => reason switch
    {
        UnsupportedReason.MissingFishingPreset =>
            "ICE does not ship an AutoHook preset for this fishing mission, so it cannot be automated. This is expected, not a malfunction.".Loc(),
        UnsupportedReason.NotInMissionSheet =>
            "This client has no data for this mission (usually a planet that is not released yet), so it cannot be automated.".Loc(),
        UnsupportedReason.RequiresAutoHookFolderImport =>
            "This mission needs AutoHook's folder preset import, which the installed AutoHook does not provide. Update AutoHook to the version shipped alongside this ICE release.".Loc(),
        _ =>
            "This mission is on the ICE unsupported list and will not be automated. This is expected, not a malfunction.".Loc(),
    };

    /// <summary>任務不支援時在名稱前面加標記；支援就原樣回傳。字串內嵌用（tooltip、清單、log）。</summary>
    internal static string NameWithMarker(uint missionId, string name)
        => IsUnsupported(missionId) ? Marker + name : name;

    /// <summary>
    /// 「你接了一個 ICE 跑不動的任務」的統一告知點。
    /// </summary>
    /// <remarks>
    /// 🔑 <b>刻意讓所有呼叫端產生同一段文字</b>：<c>IceLogging.ChatInfo</c> 的節流鍵就是訊息全文
    /// （60 秒），所以偵測端（<c>PlayerHandlers.Tick</c>，ICE 沒在跑時也會執行）與排程器端
    /// （<c>Task_CheckState</c>／<c>Task_ExecuteMission</c>）同時觸發時，使用者只會看到一次，
    /// 不需要另外再造一套去重機制。
    /// </remarks>
    internal static void Notify(uint missionId, UnsupportedReason reason)
    {
        if (reason == UnsupportedReason.None)
            return;

        var name = CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var info) ? info.Name : "?";
        IceLogging.ChatInfo(
            $"[{missionId}] {Marker}{name} — {ReasonText(reason)} "
            + "You can still finish it by hand; ICE just will not drive it.".Loc(),
            "[ICE]");
    }
}
