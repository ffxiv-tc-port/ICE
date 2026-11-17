using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using ICE.Sounds;
using ICE.Ui.MainUi.ModeSelect;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ICE.Utilities.Cosmic;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentWKSMission;
using static ICE.Utilities.CosmicHelper;
using ICE.IPC;

namespace ICE.Scheduler.Tasks
{
    internal static partial class Task_FindMission
    {
        private static Vector3 fishingHoleLoc = Vector3.Zero;
        public static unsafe bool? Navmesh_MoveToMission(uint missionId)
        {
            ThrottleMessage("Currently in a navmesh movement");

            // 兩個字典的鍵集合不同，所以分開守（SheetMissionDict 在這裡、C.MissionConfig 在下方）。
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var missionEntry))
            {
                IceLogging.ChatError($"任務 {missionId} 不在任務表裡，無法前往任務地點。", "[ICE]");
                SchedulerMain.State = IceState.GrabMission;
                P.TaskManager.Tasks.Clear();
                return true;
            }

            if (!C.MissionConfig.TryGetValue(missionId, out var missionConfig))
            {
                IceLogging.ChatError($"任務 {missionId} 在設定檔裡沒有對應的設定，無法前往任務地點。", "[ICE]");
                SchedulerMain.State = IceState.GrabMission;
                P.TaskManager.Tasks.Clear();
                return true;
            }
            var currentJob = Player.JobId;

            if (!missionEntry.Jobs.Contains(currentJob))
            {
                IceLogging.Error("Somehow, we've managed to get a job that isn't suppose to be an option for our jobs?? Resetting the whole process and going to try again.\n" +
                                 "If this continues on multiple times in a row, let me know.");
                SchedulerMain.State = IceState.GrabMission;
                P.TaskManager.Tasks.Clear();
                return true;
            }

            if (missionConfig.ManualMode || MissionSupport.IsUnsupported(missionId))
            {
                // TODO: Remove the extra 2 here until I can fix pathfinding thing
                // This is here to make sure that you don't need to be in the area for moving. Mainly cause nodes aren't mapped out yet and it's expecting to map to that area...
                IceLogging.Info("Found a mission that is either in Manual mode, or unsupported. Continuing on", "[FindMission: NavmeshMoveTo]");
                return true;
            }
            else if (!P.Navmesh.Installed)
            {
                IceLogging.Error("HEY. YOU DIDN'T READ THE INFO PAGE DID YOU HUH. Navmesh isn't installed.... sooo... yeah this is unfort. Read the info page on the main page. I ain't going to hold your hand on this one");
                return true;
            }
            else if (missionEntry.Attributes.HasFlag(MissionAttributes.Gather)) // TODO: Fix critical thingy
            {
                // Mission was found to be a gathering or critical mission, seeing if you're within range of it
                Vector2 PlayerPos = new Vector2(Player.Position.X, Player.Position.Z);
                Vector2 MapCenter = missionEntry.MapPosition;
                var missionTerritory = missionEntry.TerritoryId;
                var mapId = missionEntry.MapPosition;
                var gatherInfo = GatheringRouteLoader.GetRoute(missionTerritory, mapId);

                // ⚠️ GetRoute 查不到路線時回 null，不是空清單 —— 直接 .Count 是
                //    NullReferenceException，表現成「卡住不動而且沒有訊息」。
                if (gatherInfo == null || gatherInfo.Count == 0)
                {
                    IceLogging.Info("HEY. This gathering location hasn't been set to gather, and should honestly be set to a manual state. Cause things are about to bug out. If it's a new area please let me know o/");
                    return true;
                }

                // 🔴 第一次進場挑離玩家最近的點起跑，不是「路線檔的第一個點」：路線是環狀的
                //    （PathandCheckNode 會遞增並回繞），從哪一個點起跑都合法；用路線檔自己的座標算，
                //    不依賴採集點有沒有載進 ObjectTable。
                var entryNode = gatherInfo.OrderBy(x => Player.DistanceTo(x.LandZone)).First();
                Vector3 closestNode = entryNode.LandZone;

                // 套一層採集接近角：用角度資料（radius_start/end + max_distance）算出「從允許
                //     角度接近」的點；只有當它確實落在導航網格上（NearestPoint）才採用，否則維持原本的
                //     LandZone。🔴 fail-open 必須——角度資料是國際服作者標的、台服未驗，不讓它成為必經路徑。
                var approach = Task_Gather.GetGatherApproachPosition(entryNode, entryNode.Position);
                if (Task_Gather.TryResolveApproachOnMesh(approach, out var meshApproach))
                {
                    closestNode = meshApproach;
                    if (EzThrottler.Throttle("ICE: gather approach angle", 5000))
                        IceLogging.Info($"採集接近角：採集點 {entryNode.NodeId} 依角度資料算出接近點並吸附到導航網格，改用它當入口。",
                                        "[FindMission: NavmeshMoveTo]");
                }

                if (EzThrottler.Throttle("ICE: gather route entry node", 5000))
                    IceLogging.Info($"前往採集區域：路線共 {gatherInfo.Count} 個點，" +
                                    $"選最近的採集點 {entryNode.NodeId}（距離 {Player.DistanceTo(entryNode.LandZone):N1}）當入口。",
                                    "[FindMission: NavmeshMoveTo]");

                if (!P.Navmesh.IsRunning())
                {
                    foreach (var node in gatherInfo)
                    {
                        if (Player.DistanceTo(node.Position) < 5)
                        {
                            IceLogging.Debug("We're close to a gathering node. So continuing on.");
                            return true;
                        }
                    }
                }
                
                if (!Task_NavmeshMove.NavToDestination(closestNode))
                {
                    return false;
                }

                /*
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                    return false;
                }
                else if (!P.Navmesh.IsRunning())
                {
                    if (!Svc.Condition[ConditionFlag.Unknown101])
                    {
                        foreach (var node in gatherInfo)
                        {
                            if (Player.DistanceTo(node.Position) < 5)
                            {
                                IceLogging.Debug("We're close to a gathering node. So continuing on.");
                                return true;
                            }
                        }

                        // We ideally don't want to be trying to try and pathfind while on this. Need to wait for us to get off the hoverboard
                        if (EzThrottler.Throttle("Inializing movement for pathfinding"))
                        {
                            IceLogging.DestinationLogs.Log(closestNode);
                            P.Navmesh.PathfindAndMoveTo(closestNode, false);
                        }
                    }
                }
                else if (P.Navmesh.IsRunning())
                {
                    if (Svc.Condition[ConditionFlag.Unknown101])
                    {
                        // We're currently using the cosmoliners, telling it to stop the current navmesh in the mean time
                        if (EzThrottler.Throttle("Stopping navmesh temp"))
                        {
                            P.Navmesh.Stop();
                        }
                    }
                    if (C.UseMountOutsideMission && !Svc.Condition[ConditionFlag.Mounted] && !Player.IsBusy)
                    {
                        if (Player.DistanceTo(closestNode) > C.MountRadius)
                        {
                            if (EzThrottler.Throttle("Attemping to mount up for btn/min"))
                            {
                                Utils.MountAction();
                            }
                        }
                    }
                    else if (Svc.Condition[ConditionFlag.Mounted] && Player.DistanceTo(closestNode) < C.DismountRadius)
                    {
                        if (EzThrottler.Throttle("Dismounting from mount"))
                        {
                            Utils.Dismount();
                        }
                    }
                }
                */
            }
            else if (missionEntry.Attributes.HasFlag(MissionAttributes.Fish))
            {
                // Just a way to handle fishing missions specifically (that isn't under the critical unbrella).
                // Need to generate a path to the pre-set spot per fishing hole
                // Then need to add the last path to it once the base has been generated, to make sure that you're facing to the fishing hole properly.
                var location = missionEntry.MapPosition;
                var territory = missionEntry.TerritoryId;

                // 🔴 MoonFishingLocations 是**寫死在原始碼裡的釣點座標表**（上游照國際服建的）：外層鍵是區域 ID、
                //    內層鍵是任務的地圖旗標座標，而且是 Vector2 的**浮點數相等比對** —— 跟遊戲資料之間沒有任何共同保證，
                //    所以兩層都用 TryGetValue（裸索引找不到鍵是丟例外、不是回 null）。
                if (!GatheringUtil.MoonFishingLocations.TryGetValue(territory, out var territorySpots)
                    || !territorySpots.TryGetValue(location, out var fishingHole)
                    || fishingHole.Count == 0)
                {
                    if (EzThrottler.Throttle("ICE: fishing hole data missing", 5000))
                    {
                        IceLogging.Info(
                            $"找不到這個釣魚任務的釣點資料，略過自動前往（這個任務請改用手動模式）。" +
                            $"任務 {missionId}／區域 {territory}／地圖旗標 ({location.X}, {location.Y})。" +
                            "（釣點座標是寫死在 GatheringUtil.MoonFishingLocations 裡的，" +
                            "這個客戶端新開放的區域還沒有對應資料時就會走到這裡。）",
                            "[FindMission: 釣點]");
                    }
                    return true;
                }

                var navPos = Vector3.Zero;

                if (!P.Navmesh.IsRunning())
                {
                    foreach (var fishSpot in fishingHole)
                    {
                        if (Player.DistanceTo(fishSpot.FishingSpot) < 2)
                        {
                            IceLogging.Info("We're currently at a fishing spot! Continuing onto facing position -> grabbing the mission");
                            fishingHoleLoc = Vector3.Zero;
                            return true;
                        }
                    }
                }

                if (fishingHoleLoc == Vector3.Zero)
                {
                    var _random = new Random();
                    var randomIndex = _random.Next(fishingHole.Count);
                    if (EzThrottler.Throttle("Setting destination"))
                    {
                        IceLogging.Debug($"Random number generator said we're going to the following fishing hole #: {randomIndex}");
                        fishingHoleLoc = fishingHole[randomIndex].FishingSpot;
                    }
                }
                else
                {
                    if (!Task_NavmeshMove.NavToDestination(fishingHoleLoc))
                    {
                        if (EzThrottler.Throttle("Waitin for nav to finish"))
                        {
                            IceLogging.Debug($"Waiting for navmesh to get to: {fishingHoleLoc}");
                        }
                        return false;
                    }
                }

                /*
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                }
                if (!P.Navmesh.IsRunning())
                {
                    if (!Svc.Condition[ConditionFlag.Unknown101])
                    {
                        foreach (var fishSpot in fishingHole)
                        {
                            if (Player.DistanceTo(fishSpot.FishingSpot) < 2)
                            {
                                IceLogging.Info("We're currently at a fishing spot! Continuing onto facing position -> grabbing the mission");
                                return true;
                            }
                        }
                        if (EzThrottler.Throttle("Inializing movement for pathfinding"))
                        {
                            var _random = new Random();
                            var randomIndex = _random.Next(fishingHole.Count);
                            IceLogging.Debug($"Random number generator said we're going to the following fishing hole #: {randomIndex}");
                            if (randomIndex < fishingHole.Count)
                            {
                                Vector3 navPos = fishingHole[randomIndex].FishingSpot;
                                IceLogging.DestinationLogs.Log(navPos);
                                P.Navmesh.PathfindAndMoveTo(navPos, false);
                                fishingHoleLoc = navPos;
                                IceLogging.Debug($"Told navmesh to move to the following spot: {fishingHoleLoc}");
                            }
                        }
                    }
                }
                else if (P.Navmesh.IsRunning())
                {
                    if (Svc.Condition[ConditionFlag.Unknown101])
                    {
                        // We're currently using the cosmoliners, telling it to stop the current navmesh in the mean time
                        if (EzThrottler.Throttle("Stopping navmesh temp"))
                        {
                            IceLogging.Debug("Telling navmesh to stop cause on a cosmoliner");
                            P.Navmesh.Stop();
                            fishingPath.Clear();
                        }
                    }
                    if (C.UseMountOutsideMission && !Svc.Condition[ConditionFlag.Mounted] && !Player.IsBusy)
                    {
                        if (Player.DistanceTo(fishingHoleLoc) > C.MountRadius)
                        {
                            if (EzThrottler.Throttle("Attemping to mount up for btn/min"))
                            {
                                Utils.MountAction();
                            }
                        }
                    }
                    else if (Svc.Condition[ConditionFlag.Mounted] && Player.DistanceTo(fishingHoleLoc) < C.DismountRadius)
                    {
                        if (EzThrottler.Throttle("Dismounting from mount"))
                        {
                            Utils.Dismount();
                        }
                    }
                }
                */
            }
            else
            {
                IceLogging.Debug("Mission was not a gathering or critical mission. Navmesh moving was not necessary. Moving onto next step", "[Task_FindMission: Navmesh]");
                return true;
            }

            return false;
        }
    }
}
