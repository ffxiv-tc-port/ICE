using System.Collections.Generic;
using System.Linq;
using ICE.Enums;

namespace ICE.Scheduler.Tasks
{
    /// <summary>
    /// 一次任務執行中，遊戲用系統訊息宣告的結束狀態（評價已定、宣告過的獎章、拒絕垂釣、最終回報的獎章）。
    /// </summary>
    /// <remarks>
    /// 契約：純邏輯，不引用 Dalamud／Lumina（離線探針直接連結本檔）；只在框架執行緒呼叫，不加鎖。
    /// 「這次執行」以任務 ID＋開始／結束訊息界定：同一個任務連續再接一次，上一次的「評價已定」不得帶過去。
    /// 訊息列號＝台服 LogMessage 表列號，比對句型由呼叫端負責。
    /// </remarks>
    internal sealed class MissionEndSignals
    {
        internal const uint FishingRefusedMessage = 10760;   // 目前該釣場無法進行垂釣。
        internal const uint SilverReachedMessage = 10770;    // 探索任務的評價已更新為銀星！
        internal const uint GoldReachedMessage = 10771;      // 評價已成功達到了金星！
        internal const uint ReportedBronzeMessage = 10777;   // 以銅星評價完成了探索任務「」！
        internal const uint ReportedSilverMessage = 10778;
        internal const uint ReportedGoldMessage = 10779;
        internal const uint EvaluationFixedMessage = 10828;  // 探索任務的評價已定。
        internal const uint MissionStartedMessage = 11154;   // 探索任務「」已開始。

        /// <summary>10780 完成（不帶獎章）、10781 放棄、10782 離開區域放棄、10783 結束：都只用來收掉這次執行。</summary>
        internal static readonly uint[] RunEndedMessages = [10780, 10781, 10782, 10783];

        internal static readonly uint[] WatchedMessages =
        [
            FishingRefusedMessage, SilverReachedMessage, GoldReachedMessage,
            ReportedBronzeMessage, ReportedSilverMessage, ReportedGoldMessage,
            EvaluationFixedMessage, MissionStartedMessage, .. RunEndedMessages,
        ];

        // 遊戲宣告之後 ICE 幾秒內就會交件；超過這個時間仍沒交掉，就不再單靠這則訊息判斷。
        internal static readonly TimeSpan FixedSignalLifetime = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan CastHoldAfterRefusal = TimeSpan.FromSeconds(5);

        private uint run;
        private DateTime fixedAt = DateTime.MinValue;
        private TurninState announced = TurninState.None;
        private int refusals;
        private int refusalsJudged;
        private DateTime lastRefusal = DateTime.MinValue;

        internal uint Run => run;
        internal uint ReportedMission { get; private set; }
        internal TurninState ReportedMedal { get; private set; } = TurninState.None;

        /// <summary>新的一次執行開始（ICE 接下任務、或收到「探索任務已開始」）。同一個 ID 也要清，含上一次沒取走的回報獎章。</summary>
        internal void BeginRun(uint missionId)
        {
            ResetRun(missionId);
            ReportedMission = 0;
            ReportedMedal = TurninState.None;
        }

        private void ResetRun(uint missionId)
        {
            run = missionId;
            fixedAt = DateTime.MinValue;
            announced = TurninState.None;
            refusals = 0;
            refusalsJudged = 0;
            lastRefusal = DateTime.MinValue;
        }

        /// <param name="currentMission">收到訊息當下遊戲回報的任務 ID；0＝已經沒有任務（回報訊息常落在這時）。</param>
        internal void Observe(uint messageId, uint currentMission, DateTime now)
        {
            if (messageId == MissionStartedMessage)
            {
                BeginRun(currentMission);
                return;
            }

            // 沒看到開始訊息就換了任務：清掉這次執行的狀態，但留著上一個任務還沒取走的回報獎章。
            if (currentMission != 0 && currentMission != run)
                ResetRun(currentMission);

            switch (messageId)
            {
                case EvaluationFixedMessage:
                    if (run != 0)
                        fixedAt = now;
                    return;
                case GoldReachedMessage:
                    announced = MissionEndRules.Higher(announced, TurninState.Gold);
                    return;
                case SilverReachedMessage:
                    announced = MissionEndRules.Higher(announced, TurninState.Silver);
                    return;
                case FishingRefusedMessage:
                    if (run != 0)
                    {
                        refusals++;
                        lastRefusal = now;
                    }
                    return;
                case ReportedBronzeMessage:
                case ReportedSilverMessage:
                case ReportedGoldMessage:
                    ReportedMission = run;
                    ReportedMedal = messageId switch
                    {
                        ReportedGoldMessage => TurninState.Gold,
                        ReportedSilverMessage => TurninState.Silver,
                        _ => TurninState.Bronze,
                    };
                    EndRun();
                    return;
            }

            if (Array.IndexOf(RunEndedMessages, messageId) >= 0)
                EndRun();
        }

        private void EndRun() => ResetRun(0);

        internal bool IsEvaluationFixed(uint currentMission, DateTime now)
            => currentMission != 0 && currentMission == run && fixedAt != DateTime.MinValue
               && now - fixedAt < FixedSignalLifetime;

        internal TurninState AnnouncedMedal(uint currentMission)
            => currentMission != 0 && currentMission == run ? announced : TurninState.None;

        internal int Refusals(uint currentMission) => currentMission != 0 && currentMission == run ? refusals : 0;

        /// <summary>遊戲剛拒絕垂釣：這段時間內不再送開始釣魚。</summary>
        internal bool HoldCast(uint currentMission, DateTime now)
            => Refusals(currentMission) > 0 && now - lastRefusal < CastHoldAfterRefusal;

        /// <summary>每一則新的拒絕只回一次 true：呼叫端拿來回去重判狀態，不在拒絕後每幀重判。</summary>
        internal bool TakeUnjudgedRefusal(uint currentMission)
        {
            if (Refusals(currentMission) <= refusalsJudged)
                return false;
            refusalsJudged = refusals;
            return true;
        }

        /// <summary>取走遊戲最終回報的獎章（只認同一個任務、只取一次）。</summary>
        internal bool TryTakeReportedMedal(uint missionId, out TurninState medal)
        {
            medal = ReportedMedal;
            if (missionId == 0 || ReportedMission != missionId || medal == TurninState.None)
                return false;
            ReportedMission = 0;
            ReportedMedal = TurninState.None;
            return true;
        }
    }

    /// <summary>釣魚任務結束判定的純函式。</summary>
    internal static class MissionEndRules
    {
        /// <summary>WKSMissionText 115「盡快釣到指定數量的任意水產品」：任何一條魚都算一條。</summary>
        internal const uint AnyFishCountText = 115;

        internal enum SignalVerdict
        {
            None,
            Turnin,
            ReportAsFailed,
        }

        internal static TurninState Higher(TurninState a, TurninState b)
            => (int)a >= (int)b ? a : b;

        /// <summary>
        /// 遊戲已宣告評價已定時怎麼收尾。緊急任務（Critical）有自己的進度判定與繳交點，不走這裡。
        /// 宣告失敗的情況（時間型時間用完、評價型分數沒到銅星）交給放棄流程按「回報結果」，記成未達銅星。
        /// </summary>
        internal static SignalVerdict OnEvaluationFixed(bool evaluationFixed, bool critical, TurninState medal)
        {
            if (!evaluationFixed || critical)
                return SignalVerdict.None;
            return medal == TurninState.None ? SignalVerdict.ReportAsFailed : SignalVerdict.Turnin;
        }

        /// <summary>
        /// 評價已定那一刻的獎章：時間型看計時列、評價型看分數，再和遊戲宣告過的獎章取高。
        /// 評價型分數讀不到時以銅星計（遊戲已宣告完成，不當成失敗）。
        /// </summary>
        internal static TurninState FixedMedal(bool timeGraded, bool timeGradedReportFailed, TurninState timerMedal,
                                               uint? score, uint bronze, uint silver, uint gold, TurninState announced)
        {
            TurninState computed;
            if (timeGraded)
                computed = timeGradedReportFailed ? TurninState.None : timerMedal;
            else if (score is uint s)
                computed = MedalRules.ReportMedal(s, bronze, silver, gold);
            else
                computed = TurninState.Bronze;
            return Higher(computed, announced);
        }

        /// <summary>
        /// 任意類任務的計數魚種：與任務同一地名（PlaceName）的所有釣場的全部魚，再併入手寫清單（只增不減）。
        /// </summary>
        internal static HashSet<uint> AnyFishItems(uint placeName,
                                                   IEnumerable<(uint PlaceName, IEnumerable<uint> Items)> spots,
                                                   IEnumerable<uint> handWritten)
        {
            var set = new HashSet<uint>(handWritten.Where(id => id != 0));
            if (placeName == 0)
                return set;
            foreach (var (place, items) in spots)
                if (place == placeName)
                    foreach (var id in items)
                        if (id != 0)
                            set.Add(id);
            return set;
        }

        /// <summary>總條數：每個道具 ID 只算一次（清單裡重複的 ID 不會重複計）。讀不到的道具算 0。</summary>
        internal static int CountFish(IEnumerable<uint> items, Func<uint, int> count)
        {
            var total = 0;
            foreach (var id in items.Distinct())
                total += Math.Max(0, count(id));
            return total;
        }
    }
}
