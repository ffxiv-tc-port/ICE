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
    internal static class Task_CheckScore
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
                        var currentAmount = 0;
                        IceLogging.Debug($"Require'd amount of fish: {requiredAmount}");
                        foreach (var fishEntry in fishingInfo.RequiredFish)
                        {
                            foreach (var fishId in fishEntry.Value)
                            {
                                if (PlayerHelper.GetItemCount(fishId, out var fishAmount) && fishAmount > 0)
                                {
                                    currentAmount += fishAmount;
                                    IceLogging.Debug($"New Current Amount: {currentAmount} | Added via {fishId}");
                                }
                            }
                        }

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
                        if (missionEntry.Attributes.HasFlag(MissionAttributes.ScoreTimeRemaining))
                        {
                            if (MinRequirementsMet(id, missionInfo))
                            {
                                IceLogging.Info("We have enough unique fish to turnin for this mission. Proceeding to the mission turnin", handle);
                                SchedulerMain.State = IceState.TurninMission;
                                P.TaskManager.Tasks.Clear();

                                Mission_Settings.TurninState = DetermineTurninState();
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
                        SchedulerMain.State = IceState.Idle;
                        TataruPraiseIPC.NotifyNeedsHelp("遇到任務表裡不存在的任務 ID");
                        P.TaskManager.Tasks.Clear();
                        return true;
                    }
                }
            }
            else
            {
                // Addon wasn't visiable/ready. Opening it up.
                if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud))
                {
                    if (EzThrottler.Throttle("Opening the moon hud", 1000))
                    {
                        moonHud.Mission();
                        IceLogging.Info("Hud wasn't visible. Opening it", "[Score Check]");
                    }
                }
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
                                    IceLogging.Info($"任務逾時但高難進度讀不出來，本輪先不決定交件/放棄，等面板可讀。" +
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
                                IceLogging.Info($"高難任務進度讀不出來，本輪不交件、繼續製作。" +
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
                if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud))
                {
                    if (EzThrottler.Throttle("Opening the moon hud", 1000))
                    {
                        moonHud.Mission();
                        IceLogging.Info("Hud wasn't visible. Opening it", "[Score Check]");
                    }
                }
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
                                    IceLogging.Info($"任務逾時但高難進度讀不出來，本輪先不決定交件/放棄，等面板可讀。" +
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
                                IceLogging.Info($"高難任務進度讀不出來，本輪不交件、繼續採集。" +
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
                if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud))
                {
                    if (EzThrottler.Throttle("Opening the moon hud", 1000))
                    {
                        moonHud.Mission();
                        IceLogging.Info("Hud wasn't visible. Opening it", "[Score Check]");
                    }
                }
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
                                    IceLogging.Info($"任務逾時但高難進度讀不出來，本輪先不決定交件/放棄，等面板可讀。" +
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
                if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud))
                {
                    if (EzThrottler.Throttle("Opening the moon hud", 1000))
                    {
                        moonHud.Mission();
                        IceLogging.Info("Hud wasn't visible. Opening it", "[Score Check]");
                    }
                }
            }

            return false;
        }

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

            LogMedalCheckOnce(logDetails, timerString, remainingTime, silverText, silverRequirement, goldText, goldRequirement, result);
            return result;
        }

        // 時間型任務的獎章判定診斷：每個任務每次執行只記一行，任務結束或排程啟動時放掉。
        // 只在 framework 執行緒上被呼叫（Task_CheckScore 的任務本體），所以不需要鎖。
        private static uint medalCheckLoggedMission;

        internal static void ResetMedalCheckLog() => medalCheckLoggedMission = 0;

        private static void LogMedalCheckOnce(bool decision, string timerText, TimeSpan remaining,
                                              string silverText, TimeSpan silver, string goldText, TimeSpan gold,
                                              TurninState result)
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
                + $"資料表門檻：{table}｜判定獎章＝{result}",
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

            // Trim to remove the clock icon and any whitespace
            var currentTimeStr = timerString.Trim();

            // Remove any non-numeric characters except ':' (like the clock icon)
            currentTimeStr = new string(currentTimeStr.Where(c => char.IsDigit(c) || c == ':').ToArray());

            IceLogging.Verbose($"Cleaned time string: '{currentTimeStr}'");

            // Parse the time (format: M:SS or MM:SS)
            var timeParts = currentTimeStr.Split(':');
            IceLogging.Verbose($"Time parts count: {timeParts.Length}");

            if (timeParts.Length != 2)
            {
                IceLogging.Verbose($"Time split failed - got {timeParts.Length} parts");
                return new TimeSpan(0, 0, 0);
            }

            if (!int.TryParse(timeParts[0], out var minutes) ||
                !int.TryParse(timeParts[1], out var seconds))
            {
                IceLogging.Verbose($"Failed to parse time values");
                return new TimeSpan(0, 0, 0);
            }

            IceLogging.Verbose($"Successfully parsed - Minutes: {minutes}, Seconds: {seconds}");
            return new TimeSpan(0, minutes, seconds);
        }

        private static TimeSpan ParseRequirementTime(string requirementString)
        {
            if (string.IsNullOrWhiteSpace(requirementString))
                return TimeSpan.Zero;

            // Split on whitespace and take the first part (the time)
            var parts = requirementString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return TimeSpan.Zero;

            var timeStr = parts[0];

            // Parse the time (format: M:SS or MM:SS)
            var timeParts = timeStr.Split(':');
            if (timeParts.Length != 2)
                return TimeSpan.Zero;

            if (!int.TryParse(timeParts[0], out var minutes) ||
                !int.TryParse(timeParts[1], out var seconds))
                return TimeSpan.Zero;

            return new TimeSpan(0, minutes, seconds);
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
            if (mission.IsTimeGraded)
            {
                // 時間型走時間語意：讀面板的時間列來比。
                // 這正是既有 ScoreTimeRemaining 路徑本來就在用的函式，
                // 四個呼叫端統一走這裡之後兩邊不會再分岔。
                Mission_Settings.TurninState = DetermineTurninState();
                return;
            }

            Mission_Settings.TurninState = ScoreMedal(current, mission.SilverScore, mission.GoldScore);
        }

        /// <summary>
        /// 評價型任務的獎章判定。
        /// 🔴 <b>門檻 0 一律當成「沒有這個門檻」，不是「零分就達成」。</b>
        /// </summary>
        private static TurninState ScoreMedal(uint current, uint silver, uint gold)
        {
            if (gold > 0 && current >= gold)
                return TurninState.Gold;
            if (silver > 0 && current >= silver)
                return TurninState.Silver;
            return TurninState.Bronze;
        }
    }
}
