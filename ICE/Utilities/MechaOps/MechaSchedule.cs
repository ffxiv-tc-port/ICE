using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 機甲事件排程的一格快照（<c>WKSMechaEventModule._events</c> 的其中一格）。
///
/// 🔑 <b>為什麼這一份跟 <see cref="MechaEventDetail"/> 是兩回事</b>：後者只看
/// <c>CurrentEvent</c>（現在進行中的那一場），而使用者要的是「<b>下一場</b>是什麼、幾點」。
/// <c>_events</c> 是模組內嵌的 <c>FixedSizeArray2</c>，兩格都可以掃，
/// 未來的 <c>EventStartTimestamp</c> 就在裡面。
///
/// ✅ <b>提前讀得到，已由實機錄製證實</b>（2026-08-08，三場 <c>[MechaRec]</c>）：
/// 第一場 SNAP #79（20:01）就已經是 <c>evRow=5; evStart=1786191600</c>，
/// 而該場事件 20:20 才開始 —— <b>提前 19 分鐘</b>就讀得到，且開始時刻分秒吻合。
///
/// 🔴🔴 <b>安全性：這條路徑一個指標都不用解。</b>
/// <c>_events</c> 是內嵌陣列（<c>module + 0x30</c>），不是指標鏈，
/// 所以它<b>不需要</b> <c>CurrentEvent</c> 那套範圍驗證——唯一前提是模組指標本身有效，
/// 而那在呼叫端已經檢查過。讀的也全部是純量（int／uint／旗標位元），
/// 一個指標欄位都不碰（<c>MapMarkerPtrs</c>／<c>CurrentStateHandler</c>／<c>WKSMechaEventDataRowPtr</c>）。
/// ⇒ 就算台服的欄位語意跟國際服不同，最壞情況只是「數字不對」，不可能越界。
///
/// <paramref name="ServerTimeAtSample"/>／<paramref name="SampledTick"/> 的用法與
/// <see cref="MechaEventDetail"/> 相同：顯示端把它外推到繪製當下，倒數才不會被
/// 250ms 的取樣節流卡成一格一格跳。
/// </summary>
internal sealed record MechaScheduleEntry(
    int Slot,
    uint DataRowId,
    WKSMechaEventFlag Flags,
    int EventStart,
    int EventEnd,
    int RegistrationStart,
    int RegistrationEnd,
    int TeleportStart,
    long ServerTimeAtSample,
    long SampledTick)
{
    /// <summary>取樣時的伺服器秒數＋從那時起經過的牆鐘秒數。<c>0</c>＝這一輪拿不到。</summary>
    public long ServerTimeNow => ServerTimeAtSample <= 0
        ? 0
        : ServerTimeAtSample + (Environment.TickCount64 - SampledTick) / 1000L;
}

/// <summary>
/// 緊急事件（遊戲內叫「紅色警報」）的狀態快照。
///
/// 📌 資料源是 <c>AgentWKSAnnounce.AnnounceData</c>（CS 具名結構），
/// <b>不是</b> <c>WKSManager+0xE38</c> 的 <c>EmergencyInfoModule</c>——後者在 CS 裡是
/// 無型別的 <c>private void*</c>，沒有任何欄位定義，離線定位不了，硬讀等於猜偏移。
///
/// 🔴 <b>刻意不讀 <c>FormattedString</c></b>（+0x18 的 <c>Utf8String</c>）：
/// 它的 <c>ToString()</c> 會解 <c>StringPtr</c> 去讀那塊記憶體，而我們沒有任何辦法
/// 驗證那個指標是活的；讀壞就是 AccessViolationException，<c>try/catch</c> 攔不到。
/// 事件名稱改走純資料表查詢（見 <see cref="MechaEmergencyNames"/>），查不到就顯示「？」。
/// </summary>
internal sealed record MechaEmergencyState(
    byte State,
    byte InfoRowId,
    byte InfoSubRowId,
    long EndTime,
    long ServerTimeAtSample,
    long SampledTick)
{
    /// <summary>取樣時的伺服器秒數＋從那時起經過的牆鐘秒數。<c>0</c>＝這一輪拿不到。</summary>
    public long ServerTimeNow => ServerTimeAtSample <= 0
        ? 0
        : ServerTimeAtSample + (Environment.TickCount64 - SampledTick) / 1000L;

    /// <summary>
    /// 紅色警報是不是正在跑。<c>State</c> 的語意照 CS 的註解：
    /// <c>1 = Red Alert Incoming</c>（預警中）、<c>2 = Red Alert Progressing</c>（進行中）。
    /// 其餘值都是機甲行動或開發階段的狀態，不屬於緊急事件。
    /// </summary>
    public bool IsRedAlert => State is 1 or 2;

    /// <summary>預警中（還沒開始）＝ <c>true</c>；進行中 ＝ <c>false</c>。</summary>
    public bool IsIncoming => State == 1;
}

/// <summary>
/// 緊急事件的名稱／橫幅文字查表（純 Lumina，不碰任何原生結構）。
///
/// 🔬 <b>資料鏈（2026-08-08 離線核對 <c>exd-tc/7.20</c>，兩張表互證）</b>：
/// <code>
/// AgentWKSAnnounce.Data.EmergencyInfoRowId / EmergencyInfoSubRowId
///   → WKSEmergencyInfo[row][subrow]
///       .Unknown3 → WKSEmergencyInfoText 列（短標題）
///       .Unknown4 → WKSEmergencyInfoText 列（橫幅全文）
/// </code>
/// 台服 7.20 只有 <c>WKSEmergencyInfo</c> row 1 的 6 個 subrow 有內容，對應三種緊急事件：
/// <list type="table">
///   <item><term>subrow 0/1</term><description>短標題列 9 →「緊急情況：磁暴」／橫幅列 3</description></item>
///   <item><term>subrow 2/3</term><description>短標題列 10 →「緊急情況：流星雨」／橫幅列 4</description></item>
///   <item><term>subrow 4/5</term><description>短標題列 11 →「緊急情況：孢子霧」／橫幅列 5</description></item>
/// </list>
/// 橫幅列 3 的全文就是使用者截圖的那一句：
/// 「觀測到即將發生大規模磁暴天氣的徵兆。\n預報時間內使用設備時請務必注意安全。」
///
/// 🔴🔴 <b>欄位名一定要用 <c>UnknownN</c>，不要照 <c>exd-tc</c> 的 CSV header 抄。</b>
/// 那份 dump 的 header 是另一版 schema（寫的是 <c>WKSEmergencyWarningText</c> 之類的具名欄），
/// 而我們出貨的那顆 Lumina 裡<b>根本沒有那些名字</b>，照抄會編不過。
/// （2026-08-08 用 <c>~/.claude/tools/fleet/sheetapi --members WKSEmergency</c> 直接對
///  <c>Dalamud/bin/Release/Lumina.Excel.dll</c> 問出來的實際成員。）
/// </summary>
internal static class MechaEmergencyNames
{
    /// <summary>
    /// 取這一場緊急事件的（短標題, 橫幅全文）。任何一關查不到就回 <c>null</c>——
    /// 顯示端要自己畫「？」，<b>不要</b>拿空字串當成「沒有緊急事件」。
    /// </summary>
    /// <summary>
    /// 查表結果的快取。⚠️ 顯示端**每幀**都會問一次，而查表要走 Lumina 的
    /// subrow sheet ＋ 再一次 <c>WKSEmergencyInfoText</c>；不快取就是每幀兩次查表。
    /// 資料是靜態的（同一版遊戲不會變），所以永久快取即可，連失敗也快取
    /// （否則查不到的情況會變成每幀重試一次）。
    /// </summary>
    private static readonly Dictionary<ushort, (string? Short, string? Banner)> cache = [];

    public static (string? Short, string? Banner) Lookup(byte rowId, byte subRowId)
    {
        var key = (ushort)((rowId << 8) | subRowId);
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var result = LookupUncached(rowId, subRowId);
        cache[key] = result;
        return result;
    }

    private static (string? Short, string? Banner) LookupUncached(byte rowId, byte subRowId)
    {
        try
        {
            var infoSheet = Svc.Data.GetSubrowExcelSheet<WKSEmergencyInfo>();
            if (infoSheet == null)
                return (null, null);

            var subrows = infoSheet.GetRowOrDefault(rowId);
            if (subrows == null || subRowId >= subrows.Value.Count)
                return (null, null);

            var row = subrows.Value[subRowId];
            return (TextOf(row.Unknown3), TextOf(row.Unknown4));
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaEmergencyNameFailed", 60_000))
                IceLogging.Info($"查緊急事件名稱失敗，改顯示未知：{ex.Message}", "[MechaOps]");
            return (null, null);
        }
    }

    /// <summary>
    /// <c>WKSEmergencyInfoText</c> 的單欄查表。列 0 與空字串都當作「沒有」回 <c>null</c>，
    /// 這樣顯示端才分得出「查不到」與「查到一個空的」。
    /// </summary>
    private static string? TextOf(byte textRowId)
    {
        if (textRowId == 0)
            return null;

        var row = Svc.Data.GetExcelSheet<WKSEmergencyInfoText>()?.GetRowOrDefault(textRowId);
        if (row == null)
            return null;

        var s = row.Value.Unknown0.ExtractText();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
