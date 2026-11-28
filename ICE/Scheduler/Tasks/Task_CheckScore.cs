using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using ICE.Config;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using Lumina.Excel.Sheets;
using System.Reflection;
using YamlDotNet.Core.Tokens;
using static Dalamud.Interface.Utility.Raii.ImRaii;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;
using ICE.IPC;

namespace ICE.Scheduler.Tasks
{
    internal static partial class Task_CheckScore
    {
        public static void Enqueue()
        {
            // 這裡是 Enqueue，跑在 SchedulerMain.Tick 上（也被 Task_Craft／Task_DualClass／Task_Gather 的 Enqueue 轉呼叫），
            // 例外會冒到 Framework.Update 而且每個 tick 重來一次，所以先過 CurrentMissionUnavailable。
            if (SchedulerMain.CurrentMissionUnavailable("[Task: Score Check]", out var mission))
                return;

            var jobs = mission.Jobs;

            if (CosmicHelper.CrafterJobList.Overlaps(jobs) && CosmicHelper.GatheringJobList.Overlaps(jobs))
            {
                P.TaskManager.Enqueue(() => DualClass(), "Checking dual class score");
                P.TaskManager.Enqueue(() => SchedulerMain.State = IceState.DualClass, "Setting state to dual class");
            }
            else if (CosmicHelper.CrafterJobList.Overlaps(jobs))
            {
                IceLogging.Info("Currently on a crafting job, checking for crafting scoring", "Task: Score Check");
                P.TaskManager.Enqueue(() => SchedulerMain.State = IceState.Craft);
                P.TaskManager.Enqueue(() => Crafts(), "Checking for crafting score mission");
            }
            else if (CosmicHelper.GatheringJobList.Overlaps(jobs))
            {
                var jobId = Player.JobId;

                IceLogging.Info($"Currently on a gathering job {jobId}");
                if (jobId == 18)
                {
                    P.TaskManager.Enqueue(() => SchedulerMain.State = IceState.Fish);
                    P.TaskManager.Enqueue(() => Fish(), "Checking fishing missions for score");
                }
                else
                {
                    P.TaskManager.Enqueue(() => SchedulerMain.State = IceState.Gather);
                    P.TaskManager.Enqueue(() => Gather(), "Checking the gathering score");
                }
            }
        }

        public static unsafe bool? Fish()
        {
            string handle = "[Score Check: Fish]";

            static bool MinRequirementsMet(uint id, WKSMissionInfomation missionInfo)
            {
                string tag = "[Fishing Score | Minimum Fish Caught]";
                if (GatheringUtil.FishingPreset.TryGetValue(id, out var fishingInfo) && CosmicHelper.SheetMissionDict.TryGetValue(id, out var missionEntry))
                {
                    // 🔴 時間型任務**不能**走下面那條「分數保底」—— 那條保底在這一型上是**恆真**的：
                    //    時間型任務的 BronzeScore 全都是 0，面板的目前評價（AtkValues[2]）對這一型是 Undefined ⇒ CurrentScore `?? 0`，
                    //    於是 `0 >= 0` 永遠成立 ⇒ **任務面板一開就判「已達標」直接交件，身上一條魚都沒有。**
                    //    改走數量語意，見 TimeGradedFishRequirementsMet。
                    if (fishingInfo.AmountRequired == 0
                        && !missionEntry.Attributes.HasFlag(MissionAttributes.Critical)
                        && missionEntry.IsTimeGraded)
                    {
                        return TimeGradedFishRequirementsMet(id, fishingInfo, missionEntry, tag);
                    }

                    if (fishingInfo.AmountRequired == 0 && !missionEntry.Attributes.HasFlag(MissionAttributes.Critical))
                    {
                        IceLogging.Debug("We're in a mission where score is the only importants. Checking to see if we meet the minimum score thresh", tag);
                        var currentScore = (missionInfo.CurrentScore ?? 0);
                        if (currentScore >= missionEntry.BronzeScore)
                        {
                            IceLogging.Info($"We've met the bronze scoring threshold. Current Score: {currentScore} | Bronze Score Requirement: {missionEntry.BronzeScore}", tag);
                            return true;
                        }
                        else
                        {
                            IceLogging.Info($"We're missing score currently to be able to turn in. Current Score: {currentScore} | Bronze Score Requirement: {missionEntry.BronzeScore}", tag);
                            return false;
                        }
                    }
                    else if (fishingInfo.UniqueFish)
                    {
                        IceLogging.Debug("We're in a mission where we need to gather a certain amount of *-unique-* fish", tag);
                        var requiredAmount = fishingInfo.AmountRequired;
                        var currentAmount = 0;
                        IceLogging.Debug($"Require'd amount of unique fish: {requiredAmount}");
                        foreach (var fishEntry in fishingInfo.RequiredFish)
                        {
                            foreach (var fishId in fishEntry.Value)
                            {
                                if (PlayerHelper.GetItemCount(fishId, out var fishAmount) && fishAmount > 0)
                                {
                                    currentAmount += 1;
                                    break;
                                }
                            }
                        }

                        if (currentAmount >= requiredAmount)
                        {
                            IceLogging.Info("We've met the minimum threshold to complete the mission!", tag);
                            return true;
                        }
                        else
                        {
                            IceLogging.Info($"We're still missing fish. Current count: {currentAmount} | Need: {requiredAmount}");
                            return false;
                        }
                    }
                    else
                    {
                        IceLogging.Debug("We're in a mission where we need a certain amount of fish... so we're going to be checking that");
                        var requiredAmount = fishingInfo.AmountRequired;
                        IceLogging.Debug($"Require'd amount of fish: {requiredAmount}{(fishingInfo.AnyFishItems != null ? "（任意類：同地名釣場的魚都算）" : "")}");
                        // 「New Current Amount … Added via」的格式有離線工具在解析（逐竿重建），不要改字。
                        var runningAmount = 0;
                        var currentAmount = MissionEndRules.CountFish(fishingInfo.CountedFishItems, fishId =>
                        {
                            if (!PlayerHelper.GetItemCount(fishId, out var fishAmount) || fishAmount <= 0)
                                return 0;
                            runningAmount += fishAmount;
                            IceLogging.Debug($"New Current Amount: {runningAmount} | Added via {fishId}");
                            return fishAmount;
                        });

                        if (currentAmount >= requiredAmount)
                        {
                            IceLogging.Info($"We've met the minimum fish to get for completion. Current Amount: {currentAmount} | Required Amount: {requiredAmount}", tag);
                            return true;
                        }
                        else
                        {
                            IceLogging.Info($"We're still missing fish. Current Amount: {currentAmount} | Required Amount: {requiredAmount}", tag);
                            return false;
                        }
                    }
                }
                else
                {
                    IceLogging.Info("This... isn't a valid fishing mission? Please give the ID of it/what planet you're currently getting this error on.");
                    return false;
                }
            }
            static void CheckMedalStatus(uint id, WKSMissionInfomation missionInfo)
            {
                // CosmicInfo 是 class，查不到時 mission 是 null —— TryGetValue 的回傳值一定要看。
                if (!CosmicHelper.SheetMissionDict.TryGetValue(id, out var mission))
                {
                    IceLogging.Info($"任務 {id} 不在任務表裡，無法判斷獎章狀態。", "[Score Check: Fish]");
                    return;
                }

                if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                {
                    IceLogging.Debug("WE'RE IN A CRITICAL MISSION");
                    Mission_Settings.TurninState = TurninState.Critical;
                }
                else
                {
                    IceLogging.Debug("WE'RE NOT IN A CRITICAL MISSION");

                    var currentScore = (missionInfo.CurrentScore ?? 0);

                    MedalChecker(mission, currentScore);
                }
            }

            if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) && missionInfo.IsAddonReady)
            {
                if (missionInfo.Addon->AtkValuesCount > 4) // Really just here to make sure that the addon atkValues are fully loaded...
                {
                    var id = CosmicHelper.CurrentLunarMission;

                    // 遊戲已宣告評價已定就以遊戲為準，排在逾時判斷前面：同一刻兩個都成立時要交件，不走放棄。
                    if (TurninOnGameSignal(id, missionInfo, handle))
                        return true;

                    if (CosmicHandler.IsMissionTimedOut())
                    {
                        IceLogging.Info("We've met a timeout state, so we're just going to see if we can turnin... and if not then just abandon");
                        if (MinRequirementsMet(id, missionInfo))
                        {
                            CheckMedalStatus(id, missionInfo);
                            SchedulerMain.State = IceState.TurninMission;
                            P.TaskManager.Tasks.Clear();
                            return true;
                        }
                        else
                        {
                            SchedulerMain.State = IceState.AbandonMission;
                            P.TaskManager.Tasks.Clear();
                            return true;
                        }
                    }

                    if (CosmicHelper.SheetMissionDict.TryGetValue(id, out var missionEntry))
                    {
                        // 查分在每次釣魚狀態結束、下一次 /ahstart 之前，換 preset 不會換在竿子還在水裡的時候。
                        ScoreStagedPresets.Apply(id, missionInfo.CurrentScore, missionEntry.SilverScore,
                            static name => P.AutoHook.SetPreset(name), handle);

                        if (missionEntry.Attributes.HasFlag(MissionAttributes.ScoreTimeRemaining))
                        {
                            if (MinRequirementsMet(id, missionInfo))
                            {
                                IceLogging.Info("We have enough unique fish to turnin for this mission. Proceeding to the mission turnin", handle);
                                SchedulerMain.State = IceState.TurninMission;
                                P.TaskManager.Tasks.Clear();

                                Mission_Settings.TurninState = MissionEndRules.Higher(DetermineTurninState(), MissionEndWatcher.Signals.AnnouncedMedal(id));
                                return true;
                            }
                            else
                            {
                                IceLogging.Info("We don't have enough fish for turning in, continuing on with fishing");
                                return true;
                            }
                        }
                        else
                        {
                            IceLogging.Debug("We're not in a mission where it's scored based off of time, so going to check to see if we meet the bronze threshold instead");
                            if (CosmicHelper.SheetMissionDict.TryGetValue(id, out var mission))
                            {
                                var bronzeTurnin = MinRequirementsMet(id, missionInfo);
                                var shouldTurnin = false;

                                if (bronzeTurnin)
                                {
                                    if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                                    {
                                        IceLogging.Debug("We're in a critical mission, and we have met the minimul requirements for it. So going to continue on", handle);
                                        shouldTurnin = true;
                                    }
                                    else
                                    {
                                        IceLogging.Debug("We've met the minimum bronze threshold, so checking the rest now", handle);
                                        var currentScore = (missionInfo.CurrentScore ?? 0);
                                        var bronzeScore = mission.BronzeScore;
                                        var silverScore = mission.SilverScore;
                                        var goldScore = mission.GoldScore;

                                        // MissionConfig 的鍵集合與 SheetMissionDict 不一樣，要分開守。
                                        if (SchedulerMain.CurrentMissionConfigUnavailable(id, handle, out var config))
                                            return true;

                                        bool AnyTurnin = config.AutoTurnin;
                                        // 型別分岔統一走 EvaluateMedalGoals：時間型比時間、評價型比分數，
                                        // 兩種單位不再相遇。這條路今天只走得到評價型（上面 ScoreTimeRemaining
                                        // 已經把時間型分出去了），但判別留在這裡才不會因為上游分支改動而靜默錯配。
                                        EvaluateMedalGoals(id, mission, currentScore, handle, out var GoldGoal, out var SilverGoal);
                                        bool TurninBronze = config.TurninBronze;

                                        if (config.AutoTurnin)
                                        {
                                            // AutoTurnin enabled, going to check for gold only since we have materials/time still
                                            if (GoldGoal)
                                            {
                                                IceLogging.Info("Auto turnin was enabled, and hit the max score.", handle);
                                                shouldTurnin = true;
                                            }
                                        }
                                        else
                                        {
                                            if (GoldGoal && config.TurninGold)
                                            {
                                                IceLogging.Info("Gold Turnin was enabled, and hit the max score.", handle);
                                                shouldTurnin = true;
                                            }
                                            else if (SilverGoal && config.TurninSilver)
                                            {
                                                if (!config.TurninGold) // Check is here, just to make sure we shouldn't still be aiming for gold
                                                {
                                                    IceLogging.Info("Silver Turnin was enabled, and you didn't have gold enabled.", handle);
                                                    shouldTurnin = true;
                                                }
                                            }
                                            else if (config.TurninBronze)
                                            {
                                                if (!config.TurninSilver && !config.TurninGold) // Checking to make sure that silver and gold scores both aren't true
                                                {
                                                    IceLogging.Info("Silver Turnin was enabled, and you didn't have gold or silver enabled.", handle);
                                                    shouldTurnin = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    IceLogging.Info("We have not met the minimum requirements for turning in in general... so we shall continue");
                                    return true;
                                }
                                if (shouldTurnin)
                                {
                                    CheckMedalStatus(id, missionInfo);
                                    IceLogging.Info("The threshold for scoring was met. Time to turnin", handle);
                                    SchedulerMain.State = IceState.TurninMission;
                                    P.TaskManager.Tasks.Clear();

                                    return true;
                                }
                                else
                                {
                                    IceLogging.Info("Minimum scoring isn't met for your current preset. Continuing on", handle);
                                    return true;
                                }
                            }
                        }
                    }
                    else
                    {
                        IceLogging.Error("We're homehow here, which means you've found a mission that doesn't exist?? Please let me know.\n" +
                                        $"MissionID (allegedly) {id}");
                        SchedulerMain.StopWithNotice("遇到任務表裡不存在的任務 ID", StopReasons.Error, needsHelp: true);
                        P.TaskManager.Tasks.Clear();
                        return true;
                    }
                }
            }
            else
            {
                // Addon wasn't visiable/ready. Opening it up.
                CosmicHelper.ReopenMissionHudForScore();
            }

            return false;
        }

        public static unsafe bool? Crafts()
        {
            string tag = "Check Score: Crafts";
            IceLogging.Verbose("Checking score progress with crafts", tag);
            if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) && missionInfo.IsAddonReady)
            {
                if (missionInfo.Addon->AtkValuesCount > 4) // Really just here to make sure that the addon atkValues are fully loaded...
                {
                    if (CosmicHandler.IsMissionTimedOut())
                    {
                        // 逾時先驗分再決定交件/放棄，
                        //    不要無條件放棄把已經做好、分數也夠的成品一起丟掉。
                        if (SchedulerMain.CurrentMissionUnavailable(tag, out var timeoutMission))
                            return true;

                        bool canTurnin;
                        if (timeoutMission.Attributes.HasFlag(MissionAttributes.Critical))
                        {
                            // 判空沿用我方 uint? CriticalScore：null＝讀不到，絕不當達標；逾時但讀不到就先不決定，等面板可讀。
                            var criticalScore = missionInfo.CriticalScore;
                            if (criticalScore == null)
                            {
                                if (EzThrottler.Throttle("ICE: timeout critical unreadable (craft)", 10000))
                                    IceLogging.Info($"任務逾時但緊急任務進度讀不出來，本輪先不決定交件/放棄，等面板可讀。" +
                                                    $"（面板原字串：「{missionInfo.CriticalScoreRaw ?? "<面板尚未載入>"}」）", tag);
                                return false;
                            }
                            canTurnin = criticalScore == 1;
                        }
                        else if (timeoutMission.BronzeScore == 0)
                        {
                            // BronzeScore==0＝沒有分數門檻，改看成品數量是否備齊。
                            // 🔴 傳送/換區途中 GetItemCount 一律回 0，會把「其實做好了」誤判成「沒做」而放棄。讀不到就先等。
                            if (!PlayerHelper.InventoryReadable())
                            {
                                if (EzThrottler.Throttle("ICE: timeout inventory unreadable (craft)", 5000))
                                    IceLogging.Info("任務逾時，但玩家處於傳送/讀取中，道具數量讀出來會全是 0，暫緩交件/放棄判定。", tag);
                                return false;
                            }
                            canTurnin = timeoutMission.Crafts_Main.All(item =>
                                PlayerHelper.GetItemCount(item.Value.ItemId, out var count)
                                && count >= item.Value.RequiredAmount);
                        }
                        else
                        {
                            canTurnin = (missionInfo.CurrentScore ?? 0) >= timeoutMission.BronzeScore;
                        }

                        ApplyTimeoutDecision(canTurnin, timeoutMission, missionInfo.CurrentScore ?? 0, tag);
                        return true;
                    }

                    var Id = CosmicHelper.CurrentLunarMission;
                    // var mission = CosmicHelper.Dict_CosmicMissions[Id];  ← 原本的零守衛索引
                    if (SchedulerMain.CurrentMissionUnavailable(tag, out var mission))
                        return true;

                    // 每一件成品實際貢獻了多少評價 —— 這是「還有沒有機會拿到金星」唯一的地面真值，
                    // 也是使用者事後判斷「剛剛那次收手是不是誤判」的依據。只在數字變動時印一行。
                    CraftGoldFeasibility.Observe(Id, mission, missionInfo.CurrentScore, tag);

                    // 「剩餘材料已經追不上金星」的處置（預設關閉）。判定不出來時一律當成還有機會，
                    // 詳見 CraftGoldFeasibility 的說明。
                    if (CraftGoldFeasibility.HandleUnreachableGold(Id, mission, missionInfo.CurrentScore, tag))
                        return true;

                    bool shouldTurnin = false;

                    if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                    {
                        // 🔴 CriticalScore 是 uint?：null＝「這格讀不出可信的數字」，不是 0、更不是達標。
                        //    ECommons 端已把「把面板上所有數字黏成一個假數」的舊解析拿掉，讀不出來就回 null。
                        //    這裡明確走三態，未知一律走保守路徑（不交件、繼續做），並留下可回報的診斷 ——
                        //    否則使用者只會看到「ICE 卡著不交件」而完全沒有線索。
                        var criticalScore = missionInfo.CriticalScore;
                        if (criticalScore == null)
                        {
                            if (EzThrottler.Throttle("ICE: critical score unreadable (craft)", 10000))
                                IceLogging.Info($"緊急任務進度讀不出來，本輪不交件、繼續製作。" +
                                                $"（面板原字串：「{missionInfo.CriticalScoreRaw ?? "<面板尚未載入>"}」）", tag);
                        }
                        else if (criticalScore == 1)
                        {
                            IceLogging.Verbose("We've completed the critical!", tag);
                            shouldTurnin = true;
                        }
                    }
                    else
                    {
                        // First things first, have to check to see if you have enough of the initial crafts/meet the score threshold
                        // 🔴 傳送／換區途中 GetItemCount 一律回 0，這裡會把「成品其實做好了」誤判成
                        //    「還沒做」而把狀態切回 Craft，白做一輪。等讀得到再判斷。
                        if (!PlayerHelper.InventoryReadable())
                        {
                            if (EzThrottler.Throttle("ICE: craftscore inventory unreadable log", 5000))
                                IceLogging.Info("玩家目前處於傳送／讀取中，暫停製作成果檢查" +
                                                "（此時道具數量讀出來會全是 0）。", tag);
                            return false;
                        }

                        foreach (var item in mission.Crafts_Main)
                        {
                            var itemId = item.Value.ItemId;
                            var recipeEntry = item.Value;

                            if (!PlayerHelper.GetItemCount(itemId, out var count) || count < recipeEntry.RequiredAmount)
                            {
                                IceLogging.Debug("Found an item that you didn't have the minumim amount of. Continuing on with our task", "[Task_CheckScore: Craft]");
                                IceLogging.Debug($"RecipeId: {item.Key} | Have: {count} | Expected amount: {recipeEntry.RequiredAmount}");
                                SchedulerMain.State = IceState.Craft;
                                return true;
                            }
                        }

                        // Next, need to check to see if there is a bronze threshold that is required, and make sure we're hitting it (if there is any)

                        if (mission.BronzeScore != 0 && ((missionInfo.CurrentScore ?? 0) <= mission.BronzeScore))
                        {
                            IceLogging.Info("Bronze score is recorded at not 0. Which means that it needs a minimum score. \n" +
                                            $"Current Score: {(missionInfo.CurrentScore ?? 0)}\n" +
                                            $"Minimum Score: {mission.BronzeScore}\n" +
                                            $"Continuing on with the crafting process");
                            return true;
                        }
                        else
                        {
                            var currentScore = (missionInfo.CurrentScore ?? 0);
                            var bronzeScore = mission.BronzeScore;
                            var silverScore = mission.SilverScore;
                            var goldScore = mission.GoldScore;

                            // MissionConfig 的鍵集合與 SheetMissionDict 不一樣，要分開守。
                            if (SchedulerMain.CurrentMissionConfigUnavailable(Id, tag, out var config))
                                return true;

                            bool AnyTurnin = config.AutoTurnin;
                            // 型別分岔統一走 EvaluateMedalGoals：純製作任務目前全是評價型，但碼上不假設這件事。
                            EvaluateMedalGoals(Id, mission, currentScore, tag, out var GoldGoal, out var SilverGoal);
                            bool TurninBronze = config.TurninBronze;

                            if (config.AutoTurnin)
                            {
                                // AutoTurnin enabled, going to check for gold only since we have materials/time still
                                if (GoldGoal)
                                {
                                    IceLogging.Info("Auto turnin was enabled, and hit the max score.", tag);
                                    shouldTurnin = true;
                                }
                            }
                            else
                            {
                                if (GoldGoal && config.TurninGold)
                                {
                                    IceLogging.Info("Gold Turnin was enabled, and hit the max score.", tag);
                                    shouldTurnin = true;
                                }
                                else if (SilverGoal && config.TurninSilver)
                                {
                                    if (!config.TurninGold) // Check is here, just to make sure we shouldn't still be aiming for gold
                                    {
                                        IceLogging.Info("Silver Turnin was enabled, and you didn't have gold enabled.", "[Craft Scoring]");
                                        shouldTurnin = true;
                                    }
                                }
                                else if (config.TurninBronze)
                                {
                                    if (!config.TurninSilver && !config.TurninGold) // Checking to make sure that silver and gold scores both aren't true
                                    {
                                        IceLogging.Info("Silver Turnin was enabled, and you didn't have gold or silver enabled.", "[Craft Scoring]");
                                        shouldTurnin = true;
                                    }
                                }
                            }
                        }
                    }

                    if (shouldTurnin)
                    {
                        IceLogging.Debug("The threshold for scoring was met. Time to turnin", tag);

                        SchedulerMain.State = IceState.TurninMission;
                        P.TaskManager.Tasks.Clear();

                        if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                            Mission_Settings.TurninState = TurninState.Critical;
                        else
                        {
                            var currentScore = (missionInfo.CurrentScore ?? 0);

                            MedalChecker(mission, currentScore);
                        }

                        return true;
                    }
                    else
                    {
                        IceLogging.Debug("Minimum scoring isn't met for your current preset. Continuing on", tag);
                        if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                            IceLogging.Debug("Critical score is still 0", tag);
                        else
                        {
                            var currentScore = (missionInfo.CurrentScore ?? 0);
                            var silverScore = mission.SilverScore;
                            var goldScore = mission.GoldScore;

                            IceLogging.Debug("Still missing score/items...", tag);
                            foreach (var item in mission.Crafts_Main)
                            {
                                var itemId = item.Value.ItemId;
                                var recipeEntry = item.Value;

                                PlayerHelper.GetItemCount(itemId, out var count);
                                IceLogging.Debug($"ItemID: {itemId}, current amount: {count}");
                            }
                            IceLogging.Debug("Score board: \n" +
                                             $"Current score: {currentScore}\n" +
                                             $"Bronze goal: {mission.BronzeScore}" +
                                             $"Silver goal: {silverScore}\n" +
                                             $"Gold goal: {goldScore}", tag);
                        }

                        return true;
                    }
                }
            }
            else
            {
                // Addon wasn't visiable/ready. Opening it up.
                CosmicHelper.ReopenMissionHudForScore();
            }

            return false;
        }

        public static unsafe bool? Gather()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) && missionInfo.IsAddonReady)
            {
                if (missionInfo.Addon ->AtkValuesCount > 4) // Really just here to make sure that the addon atkValues are fully loaded...
                {
                    if (CosmicHandler.IsMissionTimedOut())
                    {
                        // 逾時先驗分再決定交件/放棄。
                        if (SchedulerMain.CurrentMissionUnavailable("[Check Score: Gather]", out var timeoutMission))
                            return true;

                        bool canTurnin;
                        if (timeoutMission.Attributes.HasFlag(MissionAttributes.Critical))
                        {
                            var criticalScore = missionInfo.CriticalScore;
                            if (criticalScore == null)
                            {
                                if (EzThrottler.Throttle("ICE: timeout critical unreadable (gather)", 10000))
                                    IceLogging.Info($"任務逾時但緊急任務進度讀不出來，本輪先不決定交件/放棄，等面板可讀。" +
                                                    $"（面板原字串：「{missionInfo.CriticalScoreRaw ?? "<面板尚未載入>"}」）", "[Check Score: Gather]");
                                return false;
                            }
                            canTurnin = criticalScore == 1;
                        }
                        else if (timeoutMission.Attributes.HasFlag(MissionAttributes.ScoreTimeRemaining) || timeoutMission.BronzeScore == 0)
                        {
                            // 時間型或無分數門檻：改看採集道具數量是否備齊。
                            if (!PlayerHelper.InventoryReadable())
                            {
                                if (EzThrottler.Throttle("ICE: timeout inventory unreadable (gather)", 5000))
                                    IceLogging.Info("任務逾時，但玩家處於傳送/讀取中，道具數量讀出來會全是 0，暫緩交件/放棄判定。", "[Check Score: Gather]");
                                return false;
                            }
                            canTurnin = timeoutMission.Gathering_Min.All(item =>
                                PlayerHelper.GetItemCount(item.Key, out var count)
                                && count >= item.Value);
                        }
                        else
                        {
                            canTurnin = (missionInfo.CurrentScore ?? 0) >= timeoutMission.BronzeScore;
                        }

                        ApplyTimeoutDecision(canTurnin, timeoutMission, missionInfo.CurrentScore ?? 0, "[Check Score: Gather]");
                        return true;
                    }

                    IceLogging.Debug("Checking score for gathering. . .", "[Check Score: Gather]");
                    // Hud info should be available. Now time to check the mission status.
                    var id = CosmicHelper.CurrentLunarMission;
                    if (SchedulerMain.CurrentMissionUnavailable("[Check Score: Gather]", out var mission))
                        return true;

                    if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                    {
                        // 🔴 三態，理由同 Crafts()：null＝未知，絕不能被當成達標。
                        //    未知與「還沒到 1」走同一條保守路徑（繼續採集），差別只在有沒有留診斷。
                        var criticalScore = missionInfo.CriticalScore;
                        if (criticalScore == 1)
                        {
                            SchedulerMain.State = IceState.TurninMission;
                            P.TaskManager.Tasks.Clear();

                            Mission_Settings.TurninState = TurninState.Critical;

                            return true;
                        }
                        else
                        {
                            if (criticalScore == null && EzThrottler.Throttle("ICE: critical score unreadable (gather)", 10000))
                                IceLogging.Info($"緊急任務進度讀不出來，本輪不交件、繼續採集。" +
                                                $"（面板原字串：「{missionInfo.CriticalScoreRaw ?? "<面板尚未載入>"}」）",
                                                "[Check Score: Gather]");

                            // Still waiting for it to hit 1. So just returning true
                            return true;
                        }
                    }
                    else if (mission.Attributes.HasFlag(MissionAttributes.ScoreTimeRemaining))
                    {
                        // We're just checking to see if we have all the items for missions that have a score time remaining. 
                        // These are typically missions that have 6 gather points, and also require a certain amount of items.
                        foreach (var item in mission.Gathering_Min)
                        {
                            if (PlayerHelper.GetItemCount(item.Key, out var count) && count < item.Value)
                            {
                                // found an item that is less than what we need. Going back to continue on gathering.
                                return true;
                            }
                        }

                        // if we've gotten here, that means that we actually have all the items. Proceeding to turnin item
                        SchedulerMain.State = IceState.TurninMission;
                        P.TaskManager.Tasks.Clear();

                        Mission_Settings.TurninState = DetermineTurninState();

                        return true;
                    }
                    else
                    {
                        if (mission.Attributes.HasFlag(MissionAttributes.Limited))
                        {
                            // 整條路線已採光（GatheringNodesDepleted）就收手：不無條件放棄，
                            //     走逾時同一套驗分決策（達標交件、否則放棄）。
                            if (Mission_Settings.GatheringNodesDepleted && !Svc.Condition[ConditionFlag.Gathering])
                            {
                                bool depletedCanTurnin;
                                if (mission.BronzeScore == 0)
                                {
                                    // 無分數門檻＝看採集道具數量是否備齊。傳送/換區途中 GetItemCount 全回 0，讀不到就先等。
                                    if (!PlayerHelper.InventoryReadable())
                                    {
                                        if (EzThrottler.Throttle("ICE: depleted inventory unreadable (gather)", 5000))
                                            IceLogging.Info("限量採集點已採光，但玩家處於傳送/讀取中，道具數量讀出來會全是 0，暫緩交件/放棄判定。", "[Check Score: Gather]");
                                        return true;
                                    }
                                    depletedCanTurnin = mission.Gathering_Min.All(item =>
                                        PlayerHelper.GetItemCount(item.Key, out var count) && count >= item.Value);
                                }
                                else
                                {
                                    depletedCanTurnin = (missionInfo.CurrentScore ?? 0) >= mission.BronzeScore;
                                }

                                ApplyTimeoutDecision(depletedCanTurnin, mission, missionInfo.CurrentScore ?? 0, "[Check Score: Gather]");
                                return true;
                            }
                        }

                        var canTurnin = false;

                        // Not retricted by time, but by either score or item's gathered.
                        if (mission.BronzeScore == 0)
                        {
                            // This is a mission that requires a certain amount of each item. Checking that first.
                            foreach (var item in mission.Gathering_Min)
                            {
                                if (PlayerHelper.GetItemCount(item.Key, out var count) && count < item.Value)
                                {
                                    // you don't have enough items to meet the turnin here. 
                                    return true;
                                }
                            }

                            // If we've gotten this far, than that means we've met the bronze threshold!
                            canTurnin = true;
                        }
                        else
                        {
                            // a minimum threshold of bronze scoring is required. Time to check that.
                            var currentScore = (missionInfo.CurrentScore ?? 0);
                            if (currentScore >= mission.BronzeScore)
                                canTurnin = true;
                        }

                        if (canTurnin)
                        {
                            // Turnin threshold has been met. Time to check to see if we're at the point where we want to turn in minimumly
                            var currentScore = (missionInfo.CurrentScore ?? 0);
                            var bronzeScore = mission.BronzeScore;
                            var silverScore = mission.SilverScore;
                            var goldScore = mission.GoldScore;

                            // MissionConfig 的鍵集合與 SheetMissionDict 不一樣，要分開守。
                            if (SchedulerMain.CurrentMissionConfigUnavailable(id, "[Check Score: Gather]", out var config))
                                return true;

                            bool AnyTurnin = config.AutoTurnin;
                            // 同 Fish()：時間型在上面的 ScoreTimeRemaining 分支就分出去了，
                            // 這裡實際上只會是評價型；判別留著是為了不讓上游分支的改動靜默錯配。
                            EvaluateMedalGoals(id, mission, currentScore, "[Check Score: Gather]", out var GoldGoal, out var SilverGoal);
                            bool TurninBronze = config.TurninBronze;

                            bool shouldTurnin = false;

                            if (config.AutoTurnin)
                            {
                                // AutoTurnin enabled, going to check for gold only since we have materials/time still
                                if (GoldGoal)
                                {
                                    IceLogging.Info("Auto turnin was enabled, and hit the max score.", "[Craft Scoring]");
                                    shouldTurnin = true;
                                }
                            }
                            else
                            {
                                if (GoldGoal && config.TurninGold)
                                {
                                    IceLogging.Info("Gold Turnin was enabled, and hit the max score.", "[Craft Scoring]");
                                    shouldTurnin = true;
                                }
                                else if (SilverGoal && config.TurninSilver)
                                {
                                    if (!config.TurninGold) // Check is here, just to make sure we shouldn't still be aiming for gold
                                    {
                                        IceLogging.Info("Silver Turnin was enabled, and you didn't have gold enabled.", "[Craft Scoring]");
                                        shouldTurnin = true;
                                    }
                                }
                                else if (config.TurninBronze)
                                {
                                    if (!config.TurninSilver && !config.TurninGold) // Checking to make sure that silver and gold scores both aren't true
                                    {
                                        IceLogging.Info("Silver Turnin was enabled, and you didn't have gold or silver enabled.", "[Craft Scoring]");
                                        shouldTurnin = true;
                                    }
                                }
                            }

                            if (shouldTurnin)
                            {
                                IceLogging.Debug("The threshold for scoring was met. Time to turnin", "[Gathering Scoring]");

                                SchedulerMain.State = IceState.TurninMission;
                                P.TaskManager.Tasks.Clear();

                                MedalChecker(mission, currentScore);

                                return true;
                            }
                            else
                            {
                                IceLogging.Debug("Minimum scoring isn't met for your current preset. Continuing on", "[Gathering Scoring]");

                                return true;
                            }
                        }
                        else
                        {
                            IceLogging.Debug($"Minimum turnin hasn't been met yet. Continuing onto gathering", "[Score Check: Gather]");
                            return true;
                        }
                    }
                }

            }
            else
            {
                // Addon wasn't visiable/ready. Opening it up.
                CosmicHelper.ReopenMissionHudForScore();
            }

            return false;
        }

        public static unsafe bool? DualClass()
        {
            string tag = "[Check Score: Dual Class]";
            if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) && missionInfo.IsAddonReady)
            {
                if (missionInfo.Addon->AtkValuesCount > 4) // Really just here to make sure that the addon atkValues are fully loaded...
                {
                    if (CosmicHandler.IsMissionTimedOut())
                    {
                        // 逾時先驗分再決定交件/放棄。
                        //    雙職任務只看評價/銅門檻（與其正常計分一致，不另查 Crafts_Main/Gathering_Min）。
                        if (SchedulerMain.CurrentMissionUnavailable(tag, out var timeoutMission))
                            return true;

                        bool canTurnin;
                        if (timeoutMission.Attributes.HasFlag(MissionAttributes.Critical))
                        {
                            var criticalScore = missionInfo.CriticalScore;
                            if (criticalScore == null)
                            {
                                if (EzThrottler.Throttle("ICE: timeout critical unreadable (dual)", 10000))
                                    IceLogging.Info($"任務逾時但緊急任務進度讀不出來，本輪先不決定交件/放棄，等面板可讀。" +
                                                    $"（面板原字串：「{missionInfo.CriticalScoreRaw ?? "<面板尚未載入>"}」）", tag);
                                return false;
                            }
                            canTurnin = criticalScore == 1;
                        }
                        else
                        {
                            canTurnin = timeoutMission.BronzeScore == 0 || (missionInfo.CurrentScore ?? 0) >= timeoutMission.BronzeScore;
                        }

                        ApplyTimeoutDecision(canTurnin, timeoutMission, missionInfo.CurrentScore ?? 0, tag);
                        return true;
                    }

                    var Id = CosmicHelper.CurrentLunarMission;
                    // var mission = CosmicHelper.Dict_CosmicMissions[Id];  ← 原本的零守衛索引
                    if (SchedulerMain.CurrentMissionUnavailable(tag, out var mission))
                        return true;

                    bool shouldTurnin = false;

                    if (mission.BronzeScore != 0 && ((missionInfo.CurrentScore ?? 0) <= mission.BronzeScore))
                    {
                        IceLogging.Info("Bronze score is recorded at not 0. Which means that it needs a minimum score. \n" +
                                        $"Current Score: {(missionInfo.CurrentScore ?? 0)}\n" +
                                        $"Minimum Score: {mission.BronzeScore}\n" +
                                        $"Continuing on with the crafting process");
                        return true;
                    }
                    else
                    {
                        var currentScore = (missionInfo.CurrentScore ?? 0);
                        var bronzeScore = mission.BronzeScore;
                        var silverScore = mission.SilverScore;
                        var goldScore = mission.GoldScore;

                        // MissionConfig 的鍵集合與 SheetMissionDict 不一樣，要分開守。
                        if (SchedulerMain.CurrentMissionConfigUnavailable(Id, tag, out var config))
                            return true;

                        bool AnyTurnin = config.AutoTurnin;
                        // 型別分岔統一走 EvaluateMedalGoals（理由同 Crafts()）。
                        EvaluateMedalGoals(Id, mission, currentScore, tag, out var GoldGoal, out var SilverGoal);
                        bool TurninBronze = config.TurninBronze;

                        if (config.AutoTurnin)
                        {
                            // AutoTurnin enabled, going to check for gold only since we have materials/time still
                            if (GoldGoal)
                            {
                                IceLogging.Info("Auto turnin was enabled, and hit the max score.", "[Craft Scoring]");
                                shouldTurnin = true;
                            }
                        }
                        else
                        {
                            if (GoldGoal && config.TurninGold)
                            {
                                IceLogging.Info("Gold Turnin was enabled, and hit the max score.", "[Craft Scoring]");
                                shouldTurnin = true;
                            }
                            else if (SilverGoal && config.TurninSilver)
                            {
                                if (!config.TurninGold) // Check is here, just to make sure we shouldn't still be aiming for gold
                                {
                                    IceLogging.Info("Silver Turnin was enabled, and you didn't have gold enabled.", "[Craft Scoring]");
                                    shouldTurnin = true;
                                }
                            }
                            else if (config.TurninBronze)
                            {
                                if (!config.TurninSilver && !config.TurninGold) // Checking to make sure that silver and gold scores both aren't true
                                {
                                    IceLogging.Info("Silver Turnin was enabled, and you didn't have gold or silver enabled.", "[Craft Scoring]");
                                    shouldTurnin = true;
                                }
                            }
                        }
                    }

                    if (shouldTurnin)
                    {
                        IceLogging.Debug("The threshold for scoring was met. Time to turnin", "[Craft Scoring]");

                        if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                            Mission_Settings.TurninState = TurninState.Gold;
                        else
                        {
                            var currentScore = (missionInfo.CurrentScore ?? 0);
                            MedalChecker(mission, currentScore);
                        }

                        SchedulerMain.State = IceState.TurninMission;
                        P.TaskManager.Tasks.Clear();

                        return true;
                    }
                    else
                    {
                        // MissionConfig 的鍵集合與 SheetMissionDict 不一樣，要分開守。
                        if (SchedulerMain.CurrentMissionConfigUnavailable(Id, tag, out var config))
                            return true;

                        var currentScore = (missionInfo.CurrentScore ?? 0);
                        var bronzeScore = mission.BronzeScore;
                        var silverScore = mission.SilverScore;
                        var goldScore = mission.GoldScore;

                        IceLogging.Debug("Minimum scoring isn't met for your current preset. Continuing on", "[Craft Scoring]");
                        IceLogging.Info("Currently Enabled:\n" +
                                        $"Bronze Enable: {config.TurninBronze} | Score: {bronzeScore}" +
                                        $"Silver Enable: {config.TurninSilver} | Score: {silverScore}" +
                                        $"Gold Enabled: {config.TurninGold} | Score: {goldScore}" +
                                        $"Any Turnin Enabled: {config.AutoTurnin}" +
                                        $"Current Score: {(missionInfo.CurrentScore ?? 0)}");

                        return true;
                    }
                }
            }
            else
            {
                // Addon wasn't visiable/ready. Opening it up.
                CosmicHelper.ReopenMissionHudForScore();
            }

            return false;
        }
    }
}
