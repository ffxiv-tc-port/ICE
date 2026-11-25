using ICE.Enums;

namespace ICE.Scheduler.Tasks
{
    /// <summary>獎章判定的純函式：只依門檻數字與面板文字，不碰遊戲狀態。</summary>
    /// <remarks>🔴 評價型門檻 0 一律當成「沒有這一級的門檻」，不是「零分就達成」。</remarks>
    internal static class MedalRules
    {
        /// <summary>交件流程用：呼叫端已確定過了銅星（或沒有銅星門檻），低於銀星一律是銅星。</summary>
        internal static TurninState ScoreMedal(uint current, uint silver, uint gold)
        {
            if (gold > 0 && current >= gold)
                return TurninState.Gold;
            if (silver > 0 && current >= silver)
                return TurninState.Silver;
            return TurninState.Bronze;
        }

        /// <summary>
        /// 放棄流程按「回報結果」用：分數可能沒到銅星。沒到銅星回 <see cref="TurninState.None"/>
        /// （遊戲判失敗、沒有報酬）；銅星門檻 0＝不看分數就算銅星。
        /// </summary>
        internal static TurninState ReportMedal(uint current, uint bronze, uint silver, uint gold)
        {
            var medal = ScoreMedal(current, silver, gold);
            if (medal == TurninState.Bronze && bronze > 0 && current < bronze)
                return TurninState.None;
            return medal;
        }

        /// <summary>
        /// 時間型任務按「回報結果」用：計時列確實讀成 0:00、而且遊戲的逾時標記亮著＝時間用完才回報，遊戲判失敗、沒有報酬。
        /// 計時列解析失敗（讀不到、面板沒畫好）一律回 false：寧可照舊記銅星，也不把真銅星記成失敗。
        /// </summary>
        internal static bool TimeGradedReportFailed(string? timerText, bool timedOutMarker)
            => timedOutMarker && TryParseRemaining(timerText, out var remaining) && remaining == TimeSpan.Zero;

        /// <summary>解析面板計時列（圖示＋「分:秒」）。只留數字與冒號後必須剛好兩段，否則回 false。</summary>
        internal static bool TryParseRemaining(string? timerText, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            if (timerText is null)
                return false;

            var cleaned = new string(timerText.Trim().Where(c => char.IsDigit(c) || c == ':').ToArray());
            var parts = cleaned.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var minutes) || !int.TryParse(parts[1], out var seconds))
                return false;

            remaining = new TimeSpan(0, minutes, seconds);
            return true;
        }
    }
}
