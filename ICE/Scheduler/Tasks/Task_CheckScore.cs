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

namespace ICE.Scheduler.Tasks
{
    internal static class Task_CheckScore
    {
        public static void Enqueue()
        {
            // ✅ 曾經是零守衛的字典索引，已修：守衛＝下一行的 SchedulerMain.CurrentMissionUnavailable。
            //    原因留存：這裡是 Enqueue，跑在 SchedulerMain.Tick 上，也被
            //    Task_Craft.Enqueue()/Task_DualClass.Enqueue()/Task_Gather.Enqueue() 轉呼叫，
            //    例外會冒到 Framework.Update 而且每個 tick 重來一次。
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
                // ⚠️ 同一個 bug class 的第三種變形：呼叫了 TryGetValue **但丟掉回傳值**。
                //    CosmicInfo 是 class，查不到時 mission 是 null，下一行就是 NullReferenceException ——
                //    看起來有守，其實完全沒守。
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

                                        // 守了 SheetMissionDict（上面的 TryGetValue）卻直接索引 MissionConfig ——
                                        // 兩個字典的鍵集合不一樣，那個守衛管不到這一行。
                                        if (SchedulerMain.CurrentMissionConfigUnavailable(id, handle, out var config))
                                            return true;

                                        bool AnyTurnin = config.AutoTurnin;
                                        bool GoldGoal = goldScore <= currentScore;
                                        bool SilverGoal = silverScore <= currentScore;
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
                        IceLogging.Debug("Mission is timed out, attempting to abandon", tag);
                        SchedulerMain.State = IceState.AbandonMission;
                        P.TaskManager.Tasks.Clear();
                        return true;
                    }

                    var Id = CosmicHelper.CurrentLunarMission;
                    // var mission = CosmicHelper.Dict_CosmicMissions[Id];  ← 原本的零守衛索引
                    // ✅ 已修：守衛＝下一行的 SchedulerMain.CurrentMissionUnavailable。
                    if (SchedulerMain.CurrentMissionUnavailable(tag, out var mission))
                        return true;

                    bool shouldTurnin = false;

                    if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                    {
                        if (missionInfo.CriticalScore == 1)
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

                            // 守了 SheetMissionDict 卻直接索引 MissionConfig —— 兩個字典的鍵集合不一樣。
                            if (SchedulerMain.CurrentMissionConfigUnavailable(Id, tag, out var config))
                                return true;

                            bool AnyTurnin = config.AutoTurnin;
                            bool GoldGoal = goldScore <= currentScore;
                            bool SilverGoal = silverScore <= currentScore;
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
                        SchedulerMain.State = IceState.AbandonMission;
                        P.TaskManager.Tasks.Clear();
                        return true;
                    }

                    IceLogging.Debug("Checking score for gathering. . .", "[Check Score: Gather]");
                    // Hud info should be available. Now time to check the mission status.
                    var id = CosmicHelper.CurrentLunarMission;
                    // ✅ 曾經是零守衛的字典索引，已修：守衛＝下一行的 SchedulerMain.CurrentMissionUnavailable。
                    if (SchedulerMain.CurrentMissionUnavailable("[Check Score: Gather]", out var mission))
                        return true;

                    if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                    {
                        if (missionInfo.CriticalScore == 1)
                        {
                            SchedulerMain.State = IceState.TurninMission;
                            P.TaskManager.Tasks.Clear();

                            Mission_Settings.TurninState = TurninState.Critical;

                            return true;
                        }
                        else
                        {
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
                            if (Mission_Settings.nodeTotal >= 7 && !Svc.Condition[ConditionFlag.Gathering])
                            {
                                // We've hit the node total, and can't gather anymore. Just going to try and turnin/abandon
                                SchedulerMain.State = IceState.AbandonMission;
                                Mission_Settings.nodeTotal = 0;
                                P.TaskManager.Tasks.Clear();
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

                            // 守了 SheetMissionDict 卻直接索引 MissionConfig —— 兩個字典的鍵集合不一樣。
                            if (SchedulerMain.CurrentMissionConfigUnavailable(id, "[Check Score: Gather]", out var config))
                                return true;

                            bool AnyTurnin = config.AutoTurnin;
                            bool GoldGoal = goldScore <= currentScore;
                            bool SilverGoal = silverScore <= currentScore;
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
                        SchedulerMain.State = IceState.AbandonMission;
                        P.TaskManager.Tasks.Clear();
                        return true;
                    }

                    var Id = CosmicHelper.CurrentLunarMission;
                    // var mission = CosmicHelper.Dict_CosmicMissions[Id];  ← 原本的零守衛索引
                    // ✅ 已修：守衛＝下一行的 SchedulerMain.CurrentMissionUnavailable。
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

                        // 守了 SheetMissionDict 卻直接索引 MissionConfig —— 兩個字典的鍵集合不一樣。
                        if (SchedulerMain.CurrentMissionConfigUnavailable(Id, tag, out var config))
                            return true;

                        bool AnyTurnin = config.AutoTurnin;
                        bool GoldGoal = goldScore <= currentScore;
                        bool SilverGoal = silverScore <= currentScore;
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
                        // 守了 SheetMissionDict 卻直接索引 MissionConfig —— 兩個字典的鍵集合不一樣。
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

        public static TurninState DetermineTurninState()
        {
            string timerString = ActiveTimerAddon();
            TimeSpan silverRequirement = ParseRequirementTime(SilverTimerAddon());
            TimeSpan goldRequirement = ParseRequirementTime(GoldTimerAddon());

            // Parse the timer string to get remaining time (left side of /)
            var remainingTime = ParseCurrentTime(timerString);

            IceLogging.Info($"Timer Info:\n" +
                $"Current Timer: {remainingTime}\n" +
                $"Silver Requirement: {silverRequirement}\n" +
                $"Gold Requirement: {goldRequirement}");

            // 🔴 門檻 TimeSpan.Zero 代表「沒讀到」，不是「零秒就達標」。
            //    ParseRequirementTime 解析失敗時回的就是 TimeSpan.Zero（節點讀不到、面板還沒畫好都會），
            //    沒有這個前置的話 remainingTime >= Zero 恆真 ⇒ 讀不到面板時一律記成金星。
            //    這條路徑現在還多了 MedalChecker 轉進來的時間型任務，所以這個洞一定要補。
            if (goldRequirement > TimeSpan.Zero && remainingTime >= goldRequirement)
                return TurninState.Gold;
            else if (silverRequirement > TimeSpan.Zero && remainingTime >= silverRequirement)
                return TurninState.Silver;
            else
                return TurninState.Bronze;
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
        /// 判定這次交件要記成哪一種獎章，寫進 <see cref="Mission_Settings.TurninState"/>。
        /// </summary>
        /// <remarks>
        /// 📌 這個值**不決定要不要交件**——交不交件在呼叫端就已經由 shouldTurnin／canTurnin 決定完了。
        /// 它的下游只有：<c>Task_TurninMission.UpdateScoreInfo()</c> 的分數換算倍率
        /// （金 ×5、銀 ×4）、<c>MissionTimer</c> 的統計、以及任務表的顏色。<br/><br/>
        ///
        /// 原本的寫法 <c>current &gt;= gold</c> 有兩個會靜默給錯答案的地方：<br/>
        /// ① <b>門檻 0 讓比較恆真</b>：<c>gold == 0</c> 時任何分數都 <c>&gt;= 0</c>，一律記成金星。<br/>
        /// ② <b>時間型任務的單位根本不是分數</b>：這一型的 <c>SilverScore</c>／<c>GoldScore</c> 是
        ///    「剩餘秒數 × 10」（見 <see cref="CosmicHelper.CosmicInfo.IsTimeGraded"/> 的離線核對），
        ///    而傳進來的 <c>current</c> 是面板的評價分數；更糟的是這一型的面板
        ///    <c>AtkValues[2]</c> 是 Undefined，<c>CurrentScore</c> 取不到值、<c>?? 0</c> 後恆為 0
        ///    ⇒ <b>時間型任務過去一律被記成銅星</b>，而且完全沒有徵兆。
        /// </remarks>
        private static void MedalChecker(CosmicHelper.CosmicInfo mission, uint current)
        {
            if (mission.IsTimeGraded)
            {
                // 時間型走時間語意：讀面板的時間列來比。
                // 這正是既有 ScoreTimeRemaining 路徑（本檔 :199、:574）本來就在用的函式，
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
