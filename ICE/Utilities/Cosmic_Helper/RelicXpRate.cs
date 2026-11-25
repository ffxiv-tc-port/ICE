namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 宇宙工具經驗模式「每分鐘期望經驗」的純計算：不碰遊戲狀態與設定物件，探針直接連結本檔。
/// </summary>
internal static class RelicXpRate
{
    /// <summary>沒有交件紀錄、任務也沒有時限時假設的每次分鐘數。</summary>
    internal const double DefaultEstimateMinutes = 10.0;

    /// <summary>
    /// 每次成功交件的期望分鐘＝（保留的交件耗時＋依生涯交件數縮放的放棄耗時）÷ 保留筆數；縮放與夾值和效益欄的
    /// <c>MissionStatsCalculator.EffectiveMinutes</c> 同式。沒有保留紀錄時用任務時限，時限是 0 用
    /// <see cref="DefaultEstimateMinutes"/>，這兩種 <paramref name="estimated"/>＝true。回傳值恆為正數。
    /// </summary>
    internal static double ExpectedMinutes(double recordSeconds, int recordCount, double abandonedSeconds, int totalCompletions,
                                           uint timeLimitSeconds, out bool estimated)
    {
        if (recordCount > 0)
        {
            var abandoned = abandonedSeconds > 0 ? abandonedSeconds : 0;
            if (totalCompletions > recordCount)
                abandoned *= (double)recordCount / totalCompletions;
            var minutes = (recordSeconds + abandoned) / 60.0 / recordCount;
            if (minutes > 0)
            {
                estimated = false;
                return minutes;
            }
        }

        estimated = true;
        return timeLimitSeconds > 0 ? timeLimitSeconds / 60.0 : DefaultEstimateMinutes;
    }

    /// <summary>經驗分數 ÷ 期望分鐘；分鐘不是正數時回 0，不讓壞資料變成無限大搶走挑選。</summary>
    internal static float PerMinute(float score, double minutes) => minutes > 0 ? (float)(score / minutes) : 0f;
}
