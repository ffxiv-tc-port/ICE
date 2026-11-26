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

        /// <summary>
        /// 解析面板計時列。任務進行中多半是「圖示＋剩餘/時限」（例「 29:58/30:00」），交件當下常只剩「圖示＋剩餘」；
        /// 一律只看斜線前面那段的「分:秒」。全形數字、全形冒號與斜線視同半形，圖示、空白與不可見字元不計；
        /// 那段裡冒號不是剛好一個、分鐘沒有數字、秒不是兩位或 ≥60，都回 false（呼叫端當成讀不到）。
        /// </summary>
        internal static bool TryParseRemaining(string? timerText, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            if (string.IsNullOrEmpty(timerText))
                return false;

            var text = timerText.AsSpan();
            var slash = text.IndexOfAny('/', '／', '∕');
            var head = slash >= 0 ? text[..slash] : text;

            int colonCount = 0, minuteLen = 0, secondLen = 0, minutes = 0, seconds = 0;
            foreach (var ch in head)
            {
                if (ch is ':' or '：' or '∶')
                {
                    colonCount++;
                    continue;
                }

                int digit;
                if (ch is >= '0' and <= '9')
                    digit = ch - '0';
                else if (ch is >= '０' and <= '９')
                    digit = ch - '０';
                else
                    continue;

                if (colonCount == 0)
                {
                    minutes = minutes * 10 + digit;
                    minuteLen++;
                }
                else
                {
                    seconds = seconds * 10 + digit;
                    secondLen++;
                }
            }

            if (colonCount != 1 || minuteLen is 0 or > 3 || secondLen != 2 || seconds >= 60)
                return false;

            remaining = new TimeSpan(0, minutes, seconds);
            return true;
        }
    }
}
