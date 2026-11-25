using ICE.Enums;

namespace ICE.Scheduler.Tasks
{
    /// <summary>評價型（比分數）任務的獎章判定，只依門檻數字，不碰遊戲狀態。</summary>
    /// <remarks>🔴 門檻 0 一律當成「沒有這一級的門檻」，不是「零分就達成」。</remarks>
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
    }
}
