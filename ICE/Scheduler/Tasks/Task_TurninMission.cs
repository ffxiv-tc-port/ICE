using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Sounds;
using ICE.Ui;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_TurninMission
    {
        public static uint PreviousMissionId = 0;
        private static bool PathfoundToRed = false;
        private static int PreviousScore = 0;
        private static bool HasInteracted = false;
        private static int TickRate = 0;

        public static void Enqueue()
        {
            P.TaskManager.Enqueue(() => TurninMission(), "Turning in the mission to the moon gods", Utils.TaskConfig);
            P.TaskManager.Enqueue(() => JobSwapCheck(), "Checking to see if you need to swap jobs");
            P.TaskManager.Enqueue(() => GoldCheck(), "Checking if Gold Check Task needs to be completed");
            P.TaskManager.Enqueue(() => CommandCheck(), "Checking for post mission commands");
        }

        public static unsafe bool? TurninMission()
        {
            string tag = "[Turnin Mission]";
            var id = CosmicHelper.CurrentLunarMission;

            if (id == 0)
            {
                PathfoundToRed = false;
                HasInteracted = false;

                // Complete the timer and get duration
                var duration = P.MissionTimer.CompleteMission();

                // Log the results
                if (C.MissionConfig.TryGetValue(PreviousMissionId, out var config))
                {
                    if (config.BestTime != double.MaxValue)
                        IceLogging.Info($"Mission [{PreviousMissionId}] [{CosmicHelper.SheetMissionDict[PreviousMissionId].Name}] completed in {duration:mm\\:ss\\.ff} | Best: {TimeSpan.FromSeconds(config.BestTime):mm\\:ss\\.ff} | Avg: {TimeSpan.FromSeconds(config.AverageTime):mm\\:ss\\.ff}", $"{tag} [Mission Timer]");
                }

                if (P.AutoHook.Installed)
                {
                    P.AutoHook.DeleteAllAnonymousPresets();
                }

                UpdateScoreInfo();
                Mission_Settings.TurninState = TurninState.None;

                if (Mission_Settings.StopAfterCurrent)
                {
                    IceLogging.Debug($"Stop after current was enabled. Stopping now", "[Task Turnin]");
                    SchedulerMain.State = IceState.Idle;
                    return true;
                }
                else
                {
                    IceLogging.Debug($"Stop after current wasn't enabled. Grabbing another mission", "[Task Turnin]");
                    SchedulerMain.State = IceState.Start;
                    return true;
                }
            }
            else
            {
                var critical = CosmicHelper.SheetMissionDict[id].Attributes.HasFlag(MissionAttributes.Critical);
                PreviousMissionId = id;

                if (EzThrottler.Throttle("Checking for previous score"))
                    PreviousScore = ScoreCheck();

                if (critical)
                {
                    var collectionPoint = Utils.TryGetObjectCollectionPoint();
                    if (!PlayerHelper.CustomIsBusy)
                    {
                        if (collectionPoint != null && Player.DistanceTo(collectionPoint) <= 4)
                        {
                            if (EzThrottler.Throttle("Log Throttle", 1000))
                            {
                                IceLogging.Debug("Attempting to turnin/chekcing if we need to navmesh stop");
                            }

                            if (P.Navmesh.IsRunning())
                            {
                                if (EzThrottler.Throttle("Telling navmesh to stop"))
                                    P.Navmesh.Stop();

                                return false;
                            }

                            if (!HasInteracted)
                            {
                                if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent] || Svc.Condition[ConditionFlag.OccupiedInEvent])
                                {
                                    HasInteracted = true;
                                }
                                else
                                {
                                    if (EzThrottler.Throttle("Interacting with thing", 500))
                                    {
                                        Utils.TargetgameObject(collectionPoint);
                                        Utils.InteractWithObject(collectionPoint);
                                    }
                                }
                            }
                            else
                            {
                                if (EzThrottler.Throttle("Telling it to wait this much before turning it off", 6000))
                                {
                                    TickRate += 1;
                                }
                                if (TickRate > 1)
                                {
                                    TickRate = 0;
                                    HasInteracted = false;
                                }
                            }
                        }
                        else
                        {
                            if (EzThrottler.Throttle("Log Throttle"))
                            {
                                IceLogging.Debug("Need to move closer to this turnin");
                            }

                            // We need to path to the collection point, and get as *-close-* as we can. 
                            if (GatheringUtil.CriticalLocations.TryGetValue(id, out var location) && location.RawLocation != Vector3.Zero)
                            {
                                if (collectionPoint == null)
                                {
                                    // We still need to get within range of it. So just going to tell it to pathfind and moveto if it wasn't already.
                                    if (!P.Navmesh.IsRunning())
                                    {
                                        if (EzThrottler.Throttle("Telling navmesh to move to the spot"))
                                        {
                                            IceLogging.Debug("We're not close enough to the turnin point to find out where one's at. So going to the location where it might be at");
                                            IceLogging.DestinationLogs.Log(location.RawLocation);
                                            P.Navmesh.PathfindAndMoveTo(location.RawLocation, false);
                                        }
                                    }
                                    else
                                    {
                                        if (C.UseMountInMission && !Player.IsBusy && Player.DistanceTo(location.RawLocation) > C.MountRadius && !Svc.Condition[ConditionFlag.Mounted])
                                        {
                                            if (EzThrottler.Throttle("Mounting the mount"))
                                                Utils.MountAction();
                                        }
                                    }
                                }
                                else if (!C.DisablePathfindingToRedAlert)
                                {
                                    if (EzThrottler.Throttle("We're pathfinding wooo"))
                                        IceLogging.Debug("We're pathfinding to the turnin point!");

                                    if (Player.DistanceTo(location.RawLocation) > 75 && P.Navmesh.IsRunning())
                                    {
                                        if (EzThrottler.Throttle("Waiting to be in a better range", 1000))
                                        {
                                            IceLogging.Debug("Waiting to be within 50 yalms of the turnin point");
                                        }
                                    }
                                    else if (Player.DistanceTo(location.RawLocation) <= 75 || !P.Navmesh.IsRunning())
                                    {

                                        if (!PathfoundToRed)
                                        {
                                            P.Navmesh.Stop();
                                            IceLogging.DestinationLogs.Log(collectionPoint.Position);
                                            P.Navmesh.PathfindAndMoveTo(collectionPoint.Position, false);
                                            PathfoundToRed = true;
                                        }
                                        else if (Player.DistanceTo(collectionPoint.Position) < C.DismountRadius && Svc.Condition[ConditionFlag.Mounted])
                                        {
                                            if (EzThrottler.Throttle("dismounting"))
                                                Utils.Dismount();
                                        }
                                        else if (C.UseMountInMission && !Player.IsBusy && Player.DistanceTo(location.RawLocation) > C.MountRadius && !Svc.Condition[ConditionFlag.Mounted])
                                        {
                                            if (EzThrottler.Throttle("Mounting the mount"))
                                                Utils.MountAction();
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (EzThrottler.Throttle("Error message unfort"))
                                    IceLogging.Debug($"Failed to check for: {id}...");
                            }
                        }
                    }
                    else
                    {
                        if (EzThrottler.Throttle("Waiting for us to not be busy. . . ", 1000))
                            IceLogging.Debug("Waiting for player to not be in a busy state");
                    }
                }
                else if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) && missionInfo.IsAddonReady)
                {
                    if (Player.JobId == 18 && Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Gathering])
                    {
                        if (EzThrottler.Throttle("Stop fishing so we can turn in this mission!", 2000))
                            Task_DualClass.StopFishing();

                        return false;
                    }

                    if (EzThrottler.Throttle("Turning in mission"))
                        missionInfo.Report();
                }
                else if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud))
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

        public static bool? JobSwapCheck()
        {
            if (C.GrindProvisionals)
            {
                IceLogging.Info("We're currently grinding out provisionals, and that means swapping jobs constantly would be... hella bad LOL. So just continuing on like normal");
                return true;
            }

            if (Player.JobId != Mission_Settings.StartJob && Mission_Settings.StartJob != 0)
            {
                if (EzThrottler.Throttle("Swapping to crafter job", 1000))
                    GearsetHandler.TaskClassChange((Job)Mission_Settings.StartJob);

                return false;
            }
            else
            {
                return true;
            }
        }

        public static unsafe bool? GoldCheck()
        {
            var managerPtr = WKSManager.Instance();
            if (managerPtr == null) return false;

            var isGold = managerPtr->IsMissionGolded(PreviousMissionId);

            if (C.RemoveAfterGold && isGold)
            {
                C.MissionConfig[PreviousMissionId].Enabled = false;
            }
            if (C.RemoveAfterGold && !isGold)
            {
                if (MainWindow.GetOnlyPreviousMissionsRecursive(PreviousMissionId).Count > 0)
                {
                    foreach (var prevMission in MainWindow.GetOnlyPreviousMissionsRecursive(PreviousMissionId))
                    {
                        C.MissionConfig[prevMission].Enabled = true;
                        C.Save();
                    }
                }
            }

            IceLogging.Info("Gold Check is complete, and checking to see what state we need to be in post cleanup");
            if (Mission_Settings.StopAfterCurrent)
            {
                IceLogging.Info("We're stopping after this mission", "[Gold Check Task]");
                Mission_Settings.StopAfterCurrent = false;
                SchedulerMain.State = IceState.Idle;

                if (C.PlaySoundAlert)
                    _ = SoundPlayer.PlaySoundAsync();
            }
            else
            {
                IceLogging.Info("We're continuing after this mission", "[Gold Check Task]");
                SchedulerMain.State = IceState.Start;
            }

            return true;
        }

        public static unsafe bool? CommandCheck()
        {
            foreach (var task in C.PostMissionCommands)
            {
                P.TaskManager.Enqueue(() => ExecuteCommand(task.command));
                if (task.Delay > 0)
                    P.TaskManager.EnqueueDelay(task.Delay);
            }
            return true;
        }

        public static bool? ExecuteCommand(string command)
        {
            Svc.Commands.ProcessCommand(command);
            return true;
        }

        public static unsafe int ScoreCheck()
        {
            var wksManager = WKSManager.Instance();
            if (wksManager == null || wksManager->ResearchModule == null || !wksManager->ResearchModule->IsLoaded)
                return 0;

            var scores = wksManager->Scores;
            return scores[(int)Player.JobId - 8];
        }

        public static void UpdateScoreInfo()
        {
            var multiplier = 1;
            var turnin = Mission_Settings.TurninState;
            if (turnin == TurninState.Gold)
                multiplier = 5;
            else if (turnin == TurninState.Silver)
                multiplier = 4;

            var scoreDifference = (ScoreCheck() - PreviousScore);
            if (scoreDifference != 0)
            {
                scoreDifference = scoreDifference / multiplier;
                IceLogging.Debug($"Base Mission score is: {scoreDifference}");
                C.ScoreKeeper[PreviousMissionId] = (uint)scoreDifference;

                if (CosmicHelper.SheetMissionDict.TryGetValue(PreviousMissionId, out var missionInfo))
                {
                    missionInfo.ClassScore = (uint)scoreDifference;
                }
                C.Save();
            }
        }
    }
}
