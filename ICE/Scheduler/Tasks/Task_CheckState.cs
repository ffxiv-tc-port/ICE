using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Sounds;
using ICE.Utilities.Cosmic;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using YamlDotNet.Core.Tokens;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;
using static ICE.Utilities.CosmicHelper;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_CheckState
    {
        public static void Enqueue()
        {
            P.TaskManager.Enqueue(() => CheckState(), "Checking to see what state we should be in");
        }

        private static unsafe bool? CheckState()
        {
            string tag = "Task: Check State";
            var currentMissionId = CosmicHelper.CurrentLunarMission;
            int maxStage = 14;

            if (AddonHelper.IsAddonActive("WKSLottery"))
            {
                IceLogging.Info("Setting State to gambling");
                SchedulerMain.State = IceState.Gambling;
                return true;
            }
            else
            {
                RelicInfo(out var allComplete, out var currentStage, out var XPTable);
                bool canTurnin = allComplete && currentStage != maxStage && C.TurninRelic;

                if (C.StopWhenLevel && Player.Level >= C.TargetLevel)
                {
                    SchedulerMain.State = IceState.Idle;
                    IceLogging.ChatInfo("Stop At Player Level is enabled. \nYour current level is: ?? and Goal: ??".Loc(Player.Level, C.TargetLevel), "[I.C.E.]");
                    if (C.PlaySoundAlert)
                    {
                        _ = SoundPlayer.PlaySoundAsync();
                    }

                    return true;
                }
                if (C.StopOnceHitCosmicScore)
                {
                    var scores = CosmicHelper.GetCosmicClassScores();

                    if (scores.classScore >= C.CosmicScoreCap)
                    {
                        IceLogging.ChatInfo("Stop At Cosmic Score is enabled. \nYour current level is: ?? and Goal: ??".Loc(scores.classScore, C.CosmicScoreCap), "[I.C.E.]");
                        SchedulerMain.State = IceState.Idle;
                        if (C.PlaySoundAlert)
                        {
                            _ = SoundPlayer.PlaySoundAsync();
                        }
                        return true;
                    }
                }
                if (C.StopOnceHitLunarCredits)
                {
                    uint[] currencies = [45691, 48146, 48147, 48148];
                    var manager = WKSManager.Instance();
                    var zoneId = *((byte*)manager + 0x5D);
                    var itemId = currencies[zoneId];

                    PlayerHelper.GetItemCount(itemId, out var credits);
                    if (credits >= C.LunarCreditsCap)
                    {
                        IceLogging.ChatInfo("You've either hit the Lunar Credit threshold, or gone above it.\nStopping I.C.E.".Loc(), "[I.C.E.]");
                        SchedulerMain.State = IceState.Idle;
                        if (C.PlaySoundAlert)
                        {
                            _ = SoundPlayer.PlaySoundAsync();
                        }
                        return true;
                    }
                }
                if (C.StopOnceHitCosmoCredits && !C.BuyItems)
                {
                    if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var hud) && hud.IsAddonReady && (hud.CosmoCredit >= C.CosmoCreditsCap))
                    {
                        IceLogging.ChatInfo("Stopping the plugin as you have ?? Cosmocredits.".Loc(hud.CosmoCredit), "[I.C.E.]");
                        SchedulerMain.State = IceState.Idle;
                        if (C.PlaySoundAlert)
                        {
                            _ = SoundPlayer.PlaySoundAsync();
                        }
                        return true;
                    }
                }
                if (C.StopOnceRelicFinished)
                {
                    if (allComplete && !canTurnin)
                    {
                        IceLogging.Debug("It says all have been completed. This is the current report");
                        for (int i = 0; i < XPTable.Count; i++)
                        {
                            var bar = XPTable[i + 1];
                            IceLogging.Debug($"Kind: [{i+1}] | Current: {bar.CurrentXP} | Needed: {bar.NeededXP}");
                        }
                        IceLogging.Debug("Config Status:\n" +
                                        $"Turnin Relic: {C.TurninRelic}\n" +
                                        $"Stop When Relic Finished: {C.StopOnceRelicFinished}");

                        IceLogging.Info("You have met all necessary relic xp, and you have \"Stop on Relic Completion\" enabled, so stopping for now");
                        SchedulerMain.State = IceState.Idle;
                        if (C.PlaySoundAlert)
                        {
                            _ = SoundPlayer.PlaySoundAsync();
                        }
                        return true;
                    }
                    else
                    {
                        IceLogging.Debug($"Check Relic XP has been concluded, and we still need some. So going to continue on because we don't need to stop.");
                    }



                    if (allComplete)
                    {

                        if (C.TurninRelic && currentStage != maxStage)
                        {
                            IceLogging.Info("We've hit a point where we can turnin the relic! Going to add a thing to check for that later");
                        }
                        else if (C.TurninRelic && currentStage == maxStage && C.StopOnceRelicFinished)
                        {
                            IceLogging.Info("You have met all necessary relic xp, and you have \"Stop on Relic Completion\" enabled, so stopping for now");
                            SchedulerMain.State = IceState.Idle;
                            if (C.PlaySoundAlert)
                            {
                                _ = SoundPlayer.PlaySoundAsync();
                            }
                            return true;
                        }
                    }
                }
                if (currentMissionId != 0)
                {
                    IceLogging.Debug($"Current mission id is not 0, which means we're in the middle of a mission");
                    if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) && missionInfo.IsAddonReady)
                    {
                        IceLogging.Debug($"Mission Infomation was active, checking if a mission is timed out.");
                        if (CosmicHandler.IsMissionTimedOut())
                        {
                            // Mission time has reached 0, checking the score/aborting if necessary
                            IceLogging.Info("Mission is currently timed out. Going to abandon the mission state", "[Task: Check State]");
                            SchedulerMain.State = IceState.AbandonMission;
                            P.TaskManager.Tasks.Clear();
                            return true;
                        }
                        else
                        {
                            IceLogging.Debug($"Mission isn't timed out... checking other states");
                            UpdateMissionState(currentMissionId);
                            C.MissionConfig.TryGetValue(currentMissionId, out var config);

                            var s = SchedulerMain.MissionState;
                            bool dualMission = (s.HasFlag(MissionAttributes.Craft) && (s.HasFlag(MissionAttributes.Gather) || s.HasFlag(MissionAttributes.Fish)));
                            // In the middle of a dual mission. 
                            // First, checking to see if you're in the middle of a gathering or crafting action
                            if (C.OnlyGrabMission || config.ManualMode || UnsupportedMissions.Ids.Contains(currentMissionId))
                            {
                                // TODO: Remove this once properly coded
                                // 這條分支就是「接了任務之後外掛完全不動」的最常見原因。原本只寫進 log，
                                // 遊戲裡沒有任何提示，使用者只會看到外掛啟用了卻不做事 —— 所以改成也印到聊天視窗。
                                var reason = UnsupportedMissions.Ids.Contains(currentMissionId)
                                    ? "這個任務在目前版本的 ICE 尚未支援（在 UnsupportedMissions 黑名單裡）"
                                    : C.OnlyGrabMission
                                        ? "你開了「只接任務」(Only Grab Mission)"
                                        : "這個任務的設定是手動模式 (Manual Mode)";

                                if (s.HasFlag(MissionAttributes.Fish))
                                {
                                    IceLogging.ChatInfo($"任務 {currentMissionId}：{reason}，所以切到手動模式，釣魚不會自動進行。", "[ICE]");
                                }
                                else
                                {
                                    IceLogging.ChatInfo($"任務 {currentMissionId}：{reason}，所以切到手動模式。", "[ICE]");
                                }
                                SchedulerMain.State = IceState.ManualMode;
                            }
                            else if (dualMission)
                            {
                                IceLogging.Info("We're in a dual craft mission, going to kick it over there", "[Task: Check State]");
                                Mission_Settings.ResetNodeCounter();
                                SchedulerMain.State = IceState.DualClass;
                            }
                            else if (Svc.Condition[ConditionFlag.Crafting] || P.Artisan.IsBusy())
                            {
                                IceLogging.Info("We are on a crafter, and either in the middle of crafting or need to start.", "[Task: Check State]");
                                SchedulerMain.State = IceState.Craft;
                            }
                            else if (Svc.Condition[ConditionFlag.Gathering])
                            {
                                Mission_Settings.ResetNodeCounter();
                                IceLogging.Info("On a gathering class, kicking over to the gathering action", "[Task: Check State]");
                                SchedulerMain.State = IceState.Gather;
                            }
                            else if (s.HasFlag(MissionAttributes.Fish))
                            {
                                IceLogging.Debug("We seem to be in the middle of a fishing mission. Going to reset/import all the presets");
                                Task_ExecuteMission.FishingTask(currentMissionId);
                                SchedulerMain.State = IceState.ScoreCheck;
                            }
                            else
                            {
                                // Not currently in the middle of an action, so time to check score and go from there.
                                IceLogging.Debug("Not in the middle of an action, swapping to score checking", "[Task_CheckState]");
                                SchedulerMain.State = IceState.ScoreCheck;
                            }

                            return true;
                        }
                    }
                    else
                    {
                        // The mission info (the one that contains the timer + current score while a mission is active) isn't loaded. Going to fix that.
                        if (EzThrottler.Throttle("Attempting to open the mission information window"))
                        {
                            IceLogging.Info("Opening the mission information window, you're in the middle of one!", "[Check State]");
                            CosmicHelper.OpenStellarMission();
                        }
                        return false;
                    }
                }
                else
                {
                    var currentJob = Player.JobId;

                    bool repairVendor = C.RepairAtVendor && PlayerHelper.NeedsRepair(C.RepairPercent);
                    bool selfRepairCraft = C.SelfRepairCrafter && PlayerHelper.NeedsRepair(C.RepairPercent) && CosmicHelper.CrafterJobList.Contains(currentJob);
                    bool selfRepairGather = C.SelfRepairGather && PlayerHelper.NeedsRepair(C.RepairPercent) && CosmicHelper.GatheringJobList.Contains(currentJob);
                    bool extractSpiritbond = C.SelfSpiritbondGather && Task_Spiritbond.IsSpiritbondReadyAny();
                    PlayerHelper.GetItemCount(45690, out var cosmoCreditAmount);
                    bool canBuyItems = C.BuyItems && Task_BuyCosmoItems.CanPurchaseAnyItem() && cosmoCreditAmount >= C.CosmoBuyAtAmount;
                    bool canGamba = false;

                    uint[] currencies = [45691, 48146, 48147, 48148];
                    var manager = WKSManager.Instance();
                    var zoneId = *((byte*)manager + 0x5D);
                    var itemId = currencies[zoneId];

                    if (C.GambaBetweenRuns)
                    {
                        if (PlayerHelper.GetItemCount(itemId, out var lunarCredits))
                        {
                            if (C.GambaAtAmount <= lunarCredits)
                                canGamba = true;
                            IceLogging.Debug($"Current Credit Setting: {C.GambaAtAmount} >= {lunarCredits} && AutoGamba: {C.GambaBetweenRuns}");

                        }
                    }

                    if (extractSpiritbond && CosmicHelper.GatheringJobList.Contains(currentJob))
                    {
                        IceLogging.Info("Extracting spiritbond is enabled. And you have some to extract. Going to go do so now", "[Task: Check State]");
                        SchedulerMain.State = IceState.Spiritbond;
                    }
                    else if (!C.RepairAtVendor && (selfRepairCraft || selfRepairGather))
                    {
                        IceLogging.Info("We need to repair! So going to go repair", "[Task: Check State]");
                        SchedulerMain.State = IceState.Repair;
                    }
                    else if (repairVendor || canTurnin || canBuyItems || canGamba)
                    {
                        SchedulerMain.State = IceState.HubReturn;
                        Task_HubActivities.RepairNpc = repairVendor;
                        Task_HubActivities.RelicTurnin = canTurnin;
                        Task_HubActivities.CosmoBuy = canBuyItems;
                        Task_HubActivities.CanGamba = canGamba;
                        IceLogging.Info("We have some reason to return back to the base so... we're doing so.\n" +
                                        $"Repairing at NPC: {repairVendor}\n" +
                                        $"Relic Turnin: {canTurnin}\n" +
                                        $"Buying Cosmocredit Items: {canBuyItems}\n" +
                                        $"Can Gamba: {canGamba}");
                    }
                    else
                    {
                        IceLogging.Info("Not in the middle of a mission, and don't need to repair/extract materia. So going to grab mission", "[Task: Check State]");
                        SchedulerMain.State = IceState.GrabMission;
                    }

                    IceLogging.Info($"There is no physical possible way for you to not be in a different state here. . . So reporting back the current state upon exiting here: {SchedulerMain.State}");
                    return true;
                }
            }
        }

        private static void UpdateMissionState(uint missionId)
        {
            // Clearing the current mission modifiers.
            SchedulerMain.MissionState = MissionAttributes.None;

            // Grabbing the mission info from the dictionary entry
            var missionDictInfo = CosmicHelper.SheetMissionDict[missionId];

            // Updating the Mission state to be the same as the current mission that's fired.
            SchedulerMain.MissionState = missionDictInfo.Attributes;
        }

        private static unsafe bool RelicInfo(out bool isComplete, out int currentStage, out Dictionary<int, XPType> XPTable)
        {
            var maxStage = CosmicHelper.MaxRelicLevel;

            string tag = "Relic Info Check";
            currentStage = 0; // Must initialize out parameters
            isComplete = false; // Must initialize out parameters
            XPTable = new();

            var wksManager = WKSManager.Instance();
            if (wksManager == null || wksManager->ResearchModule == null || !wksManager->ResearchModule->IsLoaded)
            {
                return false;
            }

            var job = Player.JobId;
            var toolClassId = (byte)(job - 7);
            var stage = wksManager->ResearchModule->CurrentStages[toolClassId - 1];
            var nextstate = wksManager->ResearchModule->UnlockedStages[toolClassId - 1];

            currentStage = stage;

            if (currentStage != maxStage)
            {
                for (byte type = 1; type < 6; type++)
                {
                    if (!wksManager->ResearchModule->IsTypeAvailable(toolClassId, type))
                    {
                        continue;
                    }

                    var neededXP = wksManager->ResearchModule->GetNeededAnalysis(toolClassId, type);
                    var currentXp = wksManager->ResearchModule->GetCurrentAnalysis(toolClassId, type);

                    if (!XPTable.ContainsKey(type))
                    {
                        XPTable[type] = new XPType()
                        {
                            CurrentXP = currentXp,
                            NeededXP = neededXP,
                        };
                    }
                }
            }
            else
            {
                // We're checking to make sure the max stage is completed
                for (byte type = 1; type < 6; type++)
                {
                    if (!wksManager->ResearchModule->IsTypeAvailable(toolClassId, type))
                    {
                        continue;
                    }

                    var maxXP = wksManager->ResearchModule->GetMaxAnalysis(toolClassId, type);
                    var currentXp = wksManager->ResearchModule->GetCurrentAnalysis(toolClassId, type);


                    if (!XPTable.ContainsKey(type))
                    {
                        XPTable[type] = new XPType()
                        {
                            CurrentXP = currentXp,
                            NeededXP = maxXP,
                        };
                    }
                }
            }

                for (int i = 0; i < XPTable.Count; i++)
                {
                    var bar = XPTable[i + 1];
                    IceLogging.Debug($"Checking: [{i+1}] Current: {bar.CurrentXP} | Needed: {bar.NeededXP}", tag);
                    if (bar.CurrentXP < bar.NeededXP)
                    {
                        IceLogging.Debug($"We're missing XP, so going to change this to false");
                        isComplete = false;
                        return false;
                    }
                }

            isComplete = true;
            return true;
        }
    }
}
