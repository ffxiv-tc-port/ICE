using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using ICE.Config;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using Lumina.Excel.Sheets;
using System.Reflection;
using System.Text.RegularExpressions;
using YamlDotNet.Core.Tokens;
using static Dalamud.Interface.Utility.Raii.ImRaii;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;
using ICE.IPC;

namespace ICE.Scheduler.Tasks
{
    internal static partial class Task_CheckScore
    {
        /// <summary>
        /// 任務逾時後的統一收尾：已達交件門檻就交件、否則放棄。
        /// </summary>
        /// <remarks>
        /// 🔑 交件/放棄的<b>統計計數不在這裡</b>：那是 <see cref="Task_AbandonMission"/> 於任務真正結束
        /// （CurrentLunarMission==0）時依 <c>WasAbandoned</c> 記一次，這裡只切狀態，不會雙記。
        /// </remarks>
        private static void ApplyTimeoutDecision(bool canTurnin, CosmicHelper.CosmicInfo mission, uint currentScore, string tag)
        {
            SchedulerMain.State = canTurnin ? IceState.TurninMission : IceState.AbandonMission;
            if (canTurnin)
            {
                if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                    Mission_Settings.TurninState = TurninState.Critical;
                else
                    MedalChecker(mission, currentScore);
            }
            IceLogging.Info($"任務逾時：{(canTurnin ? "已達交件門檻，改為交件" : "未達交件門檻，放棄任務")}。", tag);
            P.TaskManager.Tasks.Clear();
        }

        /// <param name="logDetails">
        /// 交件當下要留下時間對照（預設）。<b>每幀會呼叫到的判定路徑請傳 <see langword="false"/></b>——
        /// 這個函式本身沒有節流，掛在 Tick 上會把 log 灌爆。
        /// </param>
        public static TurninState DetermineTurninState(bool logDetails = true)
        {
            string timerString = ActiveTimerAddon();
            var silverText = SilverTimerAddon();
            var goldText = GoldTimerAddon();
            TimeSpan silverRequirement = ParseRequirementTime(silverText);
            TimeSpan goldRequirement = ParseRequirementTime(goldText);
            var fromTable = false;
            if (CosmicHelper.TryGetCurrentMissionInfo(out var currentMission) && currentMission.IsTimeGraded)
            {
                fromTable |= FillRequirementFromTable(ref silverRequirement, currentMission.SilverScore);
                fromTable |= FillRequirementFromTable(ref goldRequirement, currentMission.GoldScore);
            }

            // Parse the timer string to get remaining time (left side of /)
            var remainingTime = ParseCurrentTime(timerString);

            if (logDetails)
                IceLogging.Info($"Timer Info:\n" +
                    $"Current Timer: {remainingTime}\n" +
                    $"Silver Requirement: {silverRequirement}\n" +
                    $"Gold Requirement: {goldRequirement}");

            // 🔴 門檻 TimeSpan.Zero 代表「沒讀到」，不是「零秒就達標」。
            //    ParseRequirementTime 解析失敗時回的就是 TimeSpan.Zero（節點讀不到、面板還沒畫好都會），
            //    沒有這個前置的話 remainingTime >= Zero 恆真 ⇒ 讀不到面板時一律記成金星。
            TurninState result;
            if (goldRequirement > TimeSpan.Zero && remainingTime >= goldRequirement)
                result = TurninState.Gold;
            else if (silverRequirement > TimeSpan.Zero && remainingTime >= silverRequirement)
                result = TurninState.Silver;
            else
                result = TurninState.Bronze;

            LogMedalCheckOnce(logDetails, timerString, remainingTime, silverText, silverRequirement, goldText, goldRequirement, fromTable, result);
            return result;
        }

        // 時間型任務的獎章判定診斷：每個任務每次執行只記一行，任務結束或排程啟動時放掉。
        // 只在 framework 執行緒上被呼叫（Task_CheckScore 的任務本體），所以不需要鎖。
        private static uint medalCheckLoggedMission;

        internal static void ResetMedalCheckLog() => medalCheckLoggedMission = 0;

        private static void LogMedalCheckOnce(bool decision, string timerText, TimeSpan remaining,
                                              string silverText, TimeSpan silver, string goldText, TimeSpan gold,
                                              bool fromTable, TurninState result)
        {
            var missionId = CosmicHelper.CurrentLunarMission;
            if (missionId == 0 || missionId == medalCheckLoggedMission)
                return;
            medalCheckLoggedMission = missionId;

            var table = CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var mission)
                ? $"銀 {SecondsText((int)(mission.SilverScore / 10))}、金 {SecondsText((int)(mission.GoldScore / 10))}"
                : "查不到任務資料";
            var remainingText = remaining > TimeSpan.Zero ? SecondsText((int)remaining.TotalSeconds) : "0（解析失敗或時間到）";
            IceLogging.Info(
                $"任務 {missionId}（{(decision ? "交件判定" : "門檻評估")}）｜"
                + $"時間列 [24]「{EscapeForMedalLog(timerText)}」→ 剩餘 {remainingText}｜"
                + $"銀條件 [15]「{EscapeForMedalLog(silverText)}」→ {RequirementText(silver)}｜"
                + $"金條件 [11]「{EscapeForMedalLog(goldText)}」→ {RequirementText(gold)}｜"
                + $"資料表門檻：{table}｜門檻來源＝{(fromTable ? "資料表（面板條件裡讀不到時間）" : "面板")}｜判定獎章＝{result}",
                "[Medal Check]");
        }

        private static string RequirementText(TimeSpan requirement)
            => requirement > TimeSpan.Zero ? SecondsText((int)requirement.TotalSeconds) : "解析失敗";

        private static string SecondsText(int seconds) => $"{seconds} 秒（{GameTextUtil.FormatDuration(seconds)}）";

        /// <summary>診斷用：私用區圖示、控制字元與非 ASCII 空白改寫成 &lt;U+XXXX&gt;，看得出解析時拿到的每一個字。</summary>
        private static string EscapeForMedalLog(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var sb = new System.Text.StringBuilder(text.Length);
            foreach (var c in text)
            {
                var privateUse = c >= (char)0xE000 && c <= (char)0xF8FF;
                if (privateUse || char.IsControl(c) || (char.IsWhiteSpace(c) && c != ' '))
                    sb.Append("<U+").Append(((int)c).ToString("X4")).Append('>');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static TimeSpan ParseCurrentTime(string timerString)
        {
            IceLogging.Verbose($"Raw timer string: '{timerString}'");

            // 與放棄流程的「時間到」判定共用同一份解析（MedalRules），兩邊對 0:00 的讀法不會分岔。
            if (!MedalRules.TryParseRemaining(timerString, out var remaining))
            {
                IceLogging.Verbose("Failed to parse timer string");
                return TimeSpan.Zero;
            }

            IceLogging.Verbose($"Successfully parsed - {remaining}");
            return remaining;
        }

        // 台服條件是「剩餘時間 24:30以上」，時間夾在字中間：在整段裡找半形「分:秒」（與 MissionObjectiveReader 同形狀）。
        private static readonly Regex RequirementTimePattern = new(@"(\d{1,2}):(\d{2})", RegexOptions.Compiled);

        private static TimeSpan ParseRequirementTime(string requirementString)
        {
            if (string.IsNullOrWhiteSpace(requirementString))
                return TimeSpan.Zero;

            var match = RequirementTimePattern.Match(requirementString);
            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, out var minutes)
                || !int.TryParse(match.Groups[2].Value, out var seconds))
                return TimeSpan.Zero;

            return new TimeSpan(0, minutes, seconds);
        }

        /// <summary>面板條件讀不到時間時，改用資料表門檻（時間型＝剩餘秒數 × 10）。回傳是否用了資料表。</summary>
        private static bool FillRequirementFromTable(ref TimeSpan requirement, uint tableValue)
        {
            if (requirement > TimeSpan.Zero || tableValue == 0)
                return false;
            requirement = TimeSpan.FromSeconds(tableValue / 10);
            return true;
        }

        private static unsafe string ActiveTimerAddon()
        {
            return AddonHelper.GetNodeText("WKSMissionInfomation", 24);
        }

        private static string SilverTimerAddon()
        {
            return AddonHelper.GetNodeText("WKSMissionInfomation", 15);
        }

        private static string GoldTimerAddon()
        {
            return AddonHelper.GetNodeText("WKSMissionInfomation", 11);
        }

        /// <summary>
        /// 交件門檻判定的<b>單一型別分岔點</b>：回答「金／銀的門檻達到了沒」。
        /// 四個呼叫端（<see cref="Fish"/>／<see cref="Crafts"/>／<see cref="Gather"/>／<see cref="DualClass"/>）
        /// 全部走這裡，<b>兩種單位才不會再相遇</b>。
        /// </summary>
        /// <remarks>
        /// 拿 <c>0</c> 去跟 <c>15500</c> 比大小，得到的不是「還沒達標」而是<b>問錯了問題</b>。
        /// </remarks>
        private static void EvaluateMedalGoals(uint missionId, CosmicHelper.CosmicInfo mission, uint currentScore,
                                               string handle, out bool goldGoal, out bool silverGoal)
        {
            if (mission.IsTimeGraded)
            {
                // 時間型走時間語意：讀面板的時間列換算出「現在交出去會是哪一級」，再拿獎章比獎章。
                // 用的是 MedalChecker 已經在用的同一個函式，兩邊不會分岔。
                // logDetails: false —— 這條路掛在 Tick 上，DetermineTurninState 自己沒有節流。
                var achieved = DetermineTurninState(logDetails: false);
                goldGoal = achieved >= TurninState.Gold;
                silverGoal = achieved >= TurninState.Silver;
                LogGoalDecisionOnce(missionId, handle, true, currentScore, mission, achieved, goldGoal, silverGoal);
                return;
            }

            goldGoal = mission.GoldScore <= currentScore;
            silverGoal = mission.SilverScore <= currentScore;
            LogGoalDecisionOnce(missionId, handle, false, currentScore, mission, TurninState.None, goldGoal, silverGoal);
        }

        /// <summary>
        /// 上一次印出來的交件門檻判定簽章。判定結果沒變就不重印 —— 這條路在 Tick 上，不能無節制地印。
        /// </summary>
        private static string _lastGoalLogSignature = "";

        /// <summary>
        /// 把交件門檻的判定過程寫進 log（要使用者回報的診斷）。
        /// 只在判定結果變動時印一次（分數會一直跳，但達標與否不會）。
        /// </summary>
        private static void LogGoalDecisionOnce(uint missionId, string handle, bool timeGraded, uint currentScore,
                                                CosmicHelper.CosmicInfo mission, TurninState achieved,
                                                bool goldGoal, bool silverGoal)
        {
            var signature = $"{missionId}|{timeGraded}|{achieved}|{goldGoal}|{silverGoal}";
            if (signature == _lastGoalLogSignature)
                return;
            _lastGoalLogSignature = signature;

            if (timeGraded)
                IceLogging.Info(
                    $"交件門檻判定〔時間型〕：任務 {missionId}｜門檻單位＝剩餘秒數×10"
                    + $"（銀 {mission.SilverScore} ＝ {mission.SilverScore / 10} 秒、金 {mission.GoldScore} ＝ {mission.GoldScore / 10} 秒）｜"
                    + $"依面板時間判到的獎章＝{achieved}｜金達標＝{goldGoal}、銀達標＝{silverGoal}。"
                    + "（這一型不比分數；面板的目前評價對這一型是 Undefined。）", handle);
            else
                IceLogging.Info(
                    $"交件門檻判定〔評價型〕：任務 {missionId}｜門檻單位＝分數"
                    + $"（銅 {mission.BronzeScore}、銀 {mission.SilverScore}、金 {mission.GoldScore}）｜"
                    + $"目前分數＝{currentScore}｜金達標＝{goldGoal}、銀達標＝{silverGoal}。"
                    + "（門檻 0 代表沒有這一級的要求，視為達標。）", handle);
        }

        /// <summary>
        /// 時間型釣魚任務、且 preset 沒填 <c>AmountRequired</c> 時的交件依據。
        /// </summary>
        /// <remarks>
        /// 取代原本的「分數保底」——那條在這一型上是<b>恆真</b>的（BronzeScore 表上全 0
        /// ＋ 面板評價 Undefined ⇒ <c>0 &gt;= 0</c>），會讓 ICE 一開面板就空手交件。
        /// </remarks>
        private static bool TimeGradedFishRequirementsMet(uint id, GatheringUtil.FishingTools fishingInfo,
                                                          CosmicHelper.CosmicInfo missionEntry, string tag)
        {
            // 🔴 傳送／換區途中 GetItemCount 一律回 0。這時判「還沒達標」是安全方向（不會誤交件），
            //    但要講清楚原因，否則 log 看起來會像「魚一直沒進帳」。
            if (!PlayerHelper.InventoryReadable())
            {
                if (EzThrottler.Throttle("ICE: timed fish requirement inventory unreadable", 5000))
                    IceLogging.Info($"任務 {id}〔時間型〕：玩家處於傳送／讀取中，道具數量讀出來會全是 0，暫停交件判定。", tag);
                return false;
            }

            if (missionEntry.Gathering_Min.Count > 0)
            {
                foreach (var item in missionEntry.Gathering_Min)
                {
                    if (!PlayerHelper.GetItemCount(item.Key, out var have) || have < item.Value)
                    {
                        IceLogging.Debug($"任務 {id}〔時間型〕：資料表要求道具 {item.Key} × {item.Value}，目前 {have}。繼續釣。", tag);
                        return false;
                    }
                }

                if (EzThrottler.Throttle($"ICE: timed fish sheet met {id}", 10000))
                    IceLogging.Info($"任務 {id}〔時間型〕：資料表的數量需求已全部滿足，判定可交件"
                                  + $"（依據＝WKSMissionToDo.RequiredItem，共 {missionEntry.Gathering_Min.Count} 項）。", tag);
                return true;
            }

            if (fishingInfo.RequiredFish.Count > 0)
            {
                foreach (var fishEntry in fishingInfo.RequiredFish)
                {
                    foreach (var fishId in fishEntry.Value)
                    {
                        if (PlayerHelper.GetItemCount(fishId, out var have) && have > 0)
                        {
                            if (EzThrottler.Throttle($"ICE: timed fish any met {id}", 10000))
                                IceLogging.Info($"任務 {id}〔時間型〕：沒有數量門檻可用，改以「至少一條目標魚」為交件依據，"
                                              + $"已符合（道具 {fishId} × {have}）。", tag);
                            return true;
                        }
                    }
                }

                IceLogging.Debug($"任務 {id}〔時間型〕：目標魚一條都還沒有，繼續釣。", tag);
                return false;
            }

            if (EzThrottler.Throttle($"ICE: timed fish no criteria {id}", 30000))
                IceLogging.Info($"任務 {id}〔時間型〕：preset 沒有 AmountRequired、也沒有 RequiredFish，資料表同樣沒有數量需求 —— "
                              + "沒有任何可用的交件依據，所以不判定為可交件，會一路釣到逾時再放棄。"
                              + "（原本的分數保底在這一型上是恆真的，會直接空手交件。）", tag);
            return false;
        }

        /// <summary>
        /// 判定這次交件要記成哪一種獎章，寫進 <see cref="Mission_Settings.TurninState"/>。
        /// </summary>
        /// <remarks>
        /// 📌 這個值**不決定要不要交件**——交不交件在呼叫端就已經由 shouldTurnin／canTurnin 決定完了。
        /// 它的下游只有：<c>Task_TurninMission.UpdateScoreInfo()</c> 的分數換算倍率
        /// （金 ×5、銀 ×4）、<c>MissionTimer</c> 的統計、以及任務表的顏色。
        /// </remarks>
        private static void MedalChecker(CosmicHelper.CosmicInfo mission, uint current)
        {
            // 遊戲宣告過的獎章（「評價已成功達到了金星！」等）是下限：ICE 晚一步讀面板也不會記低。
            var announced = MissionEndWatcher.Signals.AnnouncedMedal(CosmicHelper.CurrentLunarMission);
            if (mission.IsTimeGraded)
            {
                // 時間型走時間語意：讀面板的時間列來比。
                // 這正是既有 ScoreTimeRemaining 路徑本來就在用的函式，
                // 四個呼叫端統一走這裡之後兩邊不會再分岔。
                Mission_Settings.TurninState = MissionEndRules.Higher(DetermineTurninState(), announced);
                return;
            }

            Mission_Settings.TurninState = MissionEndRules.Higher(MedalRules.ScoreMedal(current, mission.SilverScore, mission.GoldScore), announced);
        }

        /// <summary>
        /// 遊戲已宣告「探索任務的評價已定」：不再看 ICE 自己數的條數或分數門檻設定，直接交件；
        /// 判到的獎章是失敗（時間用完、分數沒到銅星）就交給放棄流程按「回報結果」記成未達銅星。
        /// </summary>
        /// <returns>已切換狀態（呼叫端 return true）。</returns>
        private static bool TurninOnGameSignal(uint id, WKSMissionInfomation missionInfo, string handle)
        {
            LogExitButtonsOnce(id, missionInfo, handle);
            var signals = MissionEndWatcher.Signals;
            if (!signals.IsEvaluationFixed(id, DateTime.UtcNow) || !CosmicHelper.SheetMissionDict.TryGetValue(id, out var mission))
                return false;

            var critical = mission.Attributes.HasFlag(MissionAttributes.Critical);
            var announced = signals.AnnouncedMedal(id);
            var medal = TurninState.Critical;
            var basis = "緊急任務";
            if (!critical)
            {
                var timeGraded = mission.IsTimeGraded;
                var timerText = timeGraded ? ActiveTimerAddon() : "";
                var reportFailed = timeGraded && announced == TurninState.None
                                   && MedalRules.TimeGradedReportFailed(timerText, CosmicHandler.IsMissionTimedOut());
                var timerMedal = timeGraded ? DetermineTurninState() : TurninState.None;
                medal = MissionEndRules.FixedMedal(timeGraded, reportFailed, timerMedal, missionInfo.CurrentScore,
                                                   mission.BronzeScore, mission.SilverScore, mission.GoldScore, announced);
                basis = timeGraded
                    ? $"時間型，計時列「{EscapeForMedalLog(timerText)}」→ {timerMedal}{(reportFailed ? "（時間已到）" : "")}"
                    : $"評價型，目前評價 {missionInfo.CurrentScore?.ToString() ?? "讀不到"}（銅 {mission.BronzeScore}）";
            }

            var verdict = MissionEndRules.OnEvaluationFixed(true, critical, medal);
            if (verdict == MissionEndRules.SignalVerdict.None)
                return false;

            bool reportPressable, abandonPressable;
            unsafe
            {
                reportPressable = GenericHelpers.IsComponentEnabled(missionInfo.ReportResultsButton);
                abandonPressable = GenericHelpers.IsComponentEnabled(missionInfo.AbandonMissionButton);
            }

            IceLogging.Info($"依遊戲訊號交件：任務 {id}「{mission.Name}」｜遊戲已宣告評價已定｜{basis}｜遊戲宣告的獎章＝{announced}｜"
                            + $"記為＝{(verdict == MissionEndRules.SignalVerdict.Turnin ? medal.ToString() : "未達銅星（走放棄流程按回報結果）")}｜"
                            + $"回報鈕可按＝{reportPressable}、放棄鈕可按＝{abandonPressable}。", handle);

            if (verdict == MissionEndRules.SignalVerdict.Turnin)
            {
                Mission_Settings.TurninState = medal;
                SchedulerMain.State = IceState.TurninMission;
            }
            else
                SchedulerMain.State = IceState.AbandonMission;
            P.TaskManager.Tasks.Clear();
            return true;
        }

        private static uint exitButtonsLoggedMission;

        /// <summary>
        /// 要使用者回報的診斷：任務進行中回報／放棄鈕的狀態，與評價已定那一行對照，
        /// 才知道「回報鈕可按」能不能取代聊天訊息當完成訊號。每個任務 ID 換人時記一行。
        /// </summary>
        private static void LogExitButtonsOnce(uint id, WKSMissionInfomation missionInfo, string handle)
        {
            if (id == 0 || id == exitButtonsLoggedMission)
                return;
            exitButtonsLoggedMission = id;
            unsafe
            {
                IceLogging.Info($"任務 {id} 進行中：回報鈕可按＝{GenericHelpers.IsComponentEnabled(missionInfo.ReportResultsButton)}、"
                                + $"放棄鈕可按＝{GenericHelpers.IsComponentEnabled(missionInfo.AbandonMissionButton)}。", handle);
            }
        }

        /// <summary>
        /// 放棄流程按「回報結果」之前判定獎章：那條路不經過交件判定，不判就停在 None（分數換算倍率 1、完成統計不記）。
        /// 評價型讀不到目前評價時維持原值：寧可不記，也不要把銀星記成銅星。
        /// </summary>
        /// <returns>
        /// 遊戲判失敗、沒有報酬時回 true（評價型分數沒到銅星門檻；時間型計時列 0:00 且逾時標記亮著）：獎章設 None，呼叫端要當放棄記；
        /// 評價讀不到回 null＝沿用上次判定。
        /// </returns>
        internal static bool? JudgeMedalBeforeReport(uint missionId, uint? currentScore)
        {
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var mission))
                return false;

            if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                Mission_Settings.TurninState = TurninState.Critical;
            else if (mission.IsTimeGraded)
            {
                MedalChecker(mission, 0);
                // 遊戲已宣告過獎章＝目標在時間內完成了，計時列之後走到 0:00 也不是失敗。
                if (MissionEndWatcher.Signals.AnnouncedMedal(missionId) == TurninState.None
                    && MedalRules.TimeGradedReportFailed(ActiveTimerAddon(), CosmicHandler.IsMissionTimedOut()))
                {
                    Mission_Settings.TurninState = TurninState.None;
                    return true;
                }
            }
            else if (currentScore is not uint score)
                return null;
            else
            {
                Mission_Settings.TurninState = MissionEndRules.Higher(
                    MedalRules.ReportMedal(score, mission.BronzeScore, mission.SilverScore, mission.GoldScore),
                    MissionEndWatcher.Signals.AnnouncedMedal(missionId));
                return Mission_Settings.TurninState == TurninState.None;
            }
            return false;
        }
    }
}
