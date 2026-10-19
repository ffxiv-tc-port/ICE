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
using ICE.IPC;

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
                    TataruPraiseIPC.NotifyStopped("等級達到設定的上限");
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
                        TataruPraiseIPC.NotifyStopped("宇宙評分達到設定的上限");
                        if (C.PlaySoundAlert)
                        {
                            _ = SoundPlayer.PlaySoundAsync();
                        }
                        return true;
                    }
                }
                if (C.StopOnceHitLunarCredits)
                {
                    // 🔴 一定要走 Task_Gamba.TryGetCosmoCreditItemId：不在已知的宇宙探索區時回 false。
                    //    讀不到就**跳過這一項檢查繼續往下**（不是 return）——後面還有別的停手條件要判，
                    //    而「停手門檻讀不到」本身不該觸發停手。
                    if (Task_Gamba.TryGetCosmoCreditItemId(out var itemId))
                    {
                        PlayerHelper.GetItemCount(itemId, out var credits);
                        if (credits >= C.LunarCreditsCap)
                        {
                            IceLogging.ChatInfo("You've either hit the Lunar Credit threshold, or gone above it.\nStopping I.C.E.".Loc(), "[I.C.E.]");
                            SchedulerMain.State = IceState.Idle;
                            TataruPraiseIPC.NotifyStopped("月靈點達到設定的上限");
                            if (C.PlaySoundAlert)
                            {
                                _ = SoundPlayer.PlaySoundAsync();
                            }
                            return true;
                        }
                    }
                }
                if (C.StopOnceHitCosmoCredits && !C.BuyItems)
                {
                    if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var hud) && hud.IsAddonReady && (hud.CosmoCredit >= C.CosmoCreditsCap))
                    {
                        IceLogging.ChatInfo("Stopping the plugin as you have ?? Cosmocredits.".Loc(hud.CosmoCredit), "[I.C.E.]");
                        SchedulerMain.State = IceState.Idle;
                        TataruPraiseIPC.NotifyStopped("宇宙點達到設定的上限");
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
                        TataruPraiseIPC.NotifyStopped("靈器經驗值已經全滿");
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
                            TataruPraiseIPC.NotifyStopped("靈器已經做到最終階段");
                            if (C.PlaySoundAlert)
                            {
                                _ = SoundPlayer.PlaySoundAsync();
                            }
                            return true;
                        }
                    }
                }
                if (TryStopWhenStandardMissionsGolded())
                {
                    return true;
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

                            // MissionSettings 是 class，查不到時 config 是 null —— TryGetValue 的回傳值一定要看。
                            if (!C.MissionConfig.TryGetValue(currentMissionId, out var config))
                            {
                                IceLogging.ChatError($"任務 {currentMissionId} 在設定檔裡沒有對應的設定，" +
                                                     "無法判斷要走哪一種流程，回到領任務狀態。", "[ICE]");
                                SchedulerMain.State = IceState.GrabMission;
                                P.TaskManager.Tasks.Clear();
                                return true;
                            }

                            var s = SchedulerMain.MissionState;
                            bool dualMission = (s.HasFlag(MissionAttributes.Craft) && (s.HasFlag(MissionAttributes.Gather) || s.HasFlag(MissionAttributes.Fish)));
                            // In the middle of a dual mission. 
                            // First, checking to see if you're in the middle of a gathering or crafting action
                            var unsupported = MissionSupport.IsUnsupported(currentMissionId, out var unsupportedReason);
                            if (C.OnlyGrabMission || config.ManualMode || unsupported)
                            {
                                // TODO: Remove this once properly coded
                                // 這條分支就是「接了任務之後外掛完全不動」的最常見原因，所以除了 log 也印到聊天視窗。
                                if (unsupported)
                                {
                                    // 「不支援」走統一告知點：文字跟 PlayerHandlers 的接任務偵測完全相同，
                                    // 靠 ChatInfo 以訊息全文為鍵的節流自動去重，使用者只會看到一次。
                                    MissionSupport.Notify(currentMissionId, unsupportedReason);
                                }
                                else
                                {
                                    var reason = C.OnlyGrabMission
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
                    bool selfRepairCraft = C.SelfRepairCrafter && !Task_Repair.SelfRepairGaveUp && PlayerHelper.NeedsRepair(C.RepairPercent) && CosmicHelper.CrafterJobList.Contains(currentJob);
                    bool selfRepairGather = C.SelfRepairGather && !Task_Repair.SelfRepairGaveUp && PlayerHelper.NeedsRepair(C.RepairPercent) && CosmicHelper.GatheringJobList.Contains(currentJob);
                    bool extractSpiritbond = C.SelfSpiritbondGather && Task_Spiritbond.IsSpiritbondReadyAny();
                    PlayerHelper.GetItemCount(45690, out var cosmoCreditAmount);
                    bool canBuyItems = C.BuyItems && Task_BuyCosmoItems.CanPurchaseAnyItem() && cosmoCreditAmount >= C.CosmoBuyAtAmount;
                    bool canGamba = false;

                    // 🔴 同上面 StopOnceHitLunarCredits 那段：走 Task_Gamba 的共用版本，而且只在開了 GambaBetweenRuns 時才查。
                    //    讀不到就讓 canGamba 維持 false ——「不知道」不該變成「去賭」。
                    if (C.GambaBetweenRuns && Task_Gamba.TryGetCosmoCreditItemId(out var itemId))
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

        // 「本區、目前所選職業的普通任務全部金評就停」（只有停手邏輯，不含 Cosmic Agenda）。
        //   ① 金評旗標走 MissionStatusHelper.GetStatus（CustomCs.cs 的原生 IsMissionGolded）。
        //   ② 只有在宇宙探索區內才判——區外 WKSManager 為 null，且「本區任務」的概念不成立。
        //   ③ total == 0 一律不停（資料還沒載入或條件不成立；「不知道」不該觸發停手）。
        private static unsafe bool TryStopWhenStandardMissionsGolded()
        {
            if (!C.StopOnceStandardMissionsGolded || !PlayerHelper.IsInCosmicZone())
            {
                return false;
            }

            int golded = 0;
            int total = 0;

            foreach (var mission in CosmicHelper.SheetMissionDict)
            {
                var info = mission.Value;
                if (info.TerritoryId != Player.Territory || !info.Jobs.Contains(C.SelectedJob))
                {
                    continue;
                }

                var attributes = info.Attributes;
                if (attributes.HasFlag(MissionAttributes.Critical)
                    || attributes.HasFlag(MissionAttributes.ProvisionalTimed)
                    || attributes.HasFlag(MissionAttributes.ProvisionalWeather)
                    || attributes.HasFlag(MissionAttributes.ProvisionalSequential))
                {
                    continue;
                }

                total++;
                if (MissionStatusHelper.GetStatus(mission.Key).Gold)
                {
                    golded++;
                }
            }

            if (total == 0 || golded != total)
            {
                return false;
            }

            IceLogging.ChatInfo("All standard missions have been graded gold (??/??). Stopping I.C.E.".Loc(golded, total), "[I.C.E.]");
            SchedulerMain.State = IceState.Idle;
            TataruPraiseIPC.NotifyStopped("這個職業在這個區域的標準任務全部拿到金評");
            P.TaskManager.Tasks.Clear();
            if (C.PlaySoundAlert)
            {
                _ = SoundPlayer.PlaySoundAsync();
            }

            return true;
        }

        private static void UpdateMissionState(uint missionId)
        {
            // Clearing the current mission modifiers.
            SchedulerMain.MissionState = MissionAttributes.None;

            // Grabbing the mission info from the dictionary entry
            // SheetMissionDict 只有「Name 不為空的 row」（沒有 0），一定要 TryGetValue。
            // 查不到就維持在上面剛清乾淨的 None —— 呼叫端本來就是依 MissionState 分支的。
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var missionDictInfo))
            {
                IceLogging.Info($"任務 {missionId} 不在任務表裡，MissionState 維持 None。", "[Task: Check State]");
                return;
            }

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
