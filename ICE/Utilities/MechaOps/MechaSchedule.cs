using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 機甲事件排程的一格快照（<c>WKSMechaEventModule._events</c> 的其中一格）。
/// 🔑 <b>為什麼這一份跟 <see cref="MechaEventDetail"/> 是兩回事</b>：後者只看
/// <c>CurrentEvent</c>（現在進行中的那一場），而使用者要的是「<b>下一場</b>是什麼、幾點」。
/// <c>_events</c> 是模組內嵌的 <c>FixedSizeArray2</c>，兩格都可以掃，
/// 未來的 <c>EventStartTimestamp</c> 就在裡面。
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
/// 🔴🔴 <b>欄位名一定要用 <c>UnknownN</c>，不要照 <c>exd-tc</c> 的 CSV header 抄。</b>
/// 那份 dump 的 header 是另一版 schema（寫的是 <c>WKSEmergencyWarningText</c> 之類的具名欄），
/// 而我們出貨的那顆 Lumina 裡<b>根本沒有那些名字</b>，照抄會編不過。
/// </summary>
internal static class MechaEmergencyNames
{
    /// <summary>
    /// 查表結果的快取。⚠️ 顯示端**每幀**都會問一次，而查表要走 Lumina 的
    /// subrow sheet ＋ 再一次 <c>WKSEmergencyInfoText</c>；不快取就是每幀兩次查表。
    /// </summary>
    private static readonly Dictionary<ushort, (string? Short, string? Banner)> cache = [];

    /// <summary>
    /// 取這一場緊急事件的（短標題, 橫幅全文）。任何一關查不到就回 <c>null</c>——
    /// 顯示端要自己畫「？」，<b>不要</b>拿空字串當成「沒有緊急事件」。
    /// </summary>
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
