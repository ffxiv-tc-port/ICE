using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ICE.Ui.DebugWindowTabs;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;
using static ICE.Utilities.GatheringHelper.GatheringUtil;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_Fishing
    {
        // Something to note. 42, 43, 85 are the conditions that you get while you're fishing
        // 43 and 85 are active while you're fishing
        // 42 is active when reeling in a fish
        // Something to consider, start fishing... (that's condition 42 when you start)
        // Whenever all the conditions are cleared, check the inventory for the frame, see if you have enough/meet the score

        private static FishingDebug _fishingDebug = null;

        public static void Enqueue()
        {
            // think the process should be:
            // check score
            // if score not complete, check if can craft
            // if craft not required, fish
            // wait for fishing to be done

            LogFishingEntryState();

            // 這兩個都必須帶 Utils.TaskConfig。NeoTaskManager 的預設是 TimeLimitMS = 30000
            // 且 AbortOnTimeout = true，而 FishingCheck 在「走去釣點 / 等餌裝上 / 等咬鉤」時
            // 本來就會連續回傳 false 好幾分鐘 —— 用預設值會每 30 秒把整個佇列 Abort 一次，
            // 表現出來就是「動一下又重來」而完全沒有錯誤訊息。
            // （同樣的坑在 Task_Craft.Enqueue 已經修過一次，見該處註解。）
            P.TaskManager.Enqueue(() => Task_CheckScore.Fish(), "Checking fishing score", Utils.TaskConfig);
            P.TaskManager.Enqueue(() => FishingCheck(), "Standard fishing check", Utils.TaskConfig);
        }

        /// <summary>
        /// 進入釣魚流程時印一次現況。釣魚卡住的回報幾乎都缺這幾個欄位，
        /// 沒有它們就只能靠猜「停在哪一步」，所以刻意寫 Information 等級
        /// （使用者的記錄等級會濾掉 Debug/Verbose）。
        /// </summary>
        private static void LogFishingEntryState()
        {
            if (!EzThrottler.Throttle("ICE: fishing entry state log", 10000))
                return;

            var missionId = CosmicHelper.CurrentLunarMission;
            var missionName = CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var sheetInfo) ? sheetInfo.Name : "(不在任務表裡)";
            var presetCount = GatheringUtil.FishingPreset.TryGetValue(missionId, out var preset) ? preset.FishingPreset.Count : -1;
            var baitCount = GatheringUtil.MoonBaits.Sum(x => x.Value.Count);

            IceLogging.Info($"進入釣魚流程。任務 {missionId}「{missionName}」｜AutoHook 已安裝: {P.AutoHook.Installed}" +
                            $"｜內建 preset 筆數: {(presetCount < 0 ? "任務不在 preset 表裡" : presetCount.ToString())}" +
                            $"｜已知月面餌種類: {baitCount}｜目前掛的餌: {CosmicHelper.CurrentBait?.ToString() ?? "null(不在任務中)"}" +
                            $"｜目前職業: {Player.JobId}", "[Task_Fishing]");
        }

        private static int BaitCounter = 0;

        private static unsafe bool? FishingCheck()
        {
            if (_fishingDebug == null)
            {
                _fishingDebug = new FishingDebug();
            }

            if (Player.Mounted)
            {
                Utils.Dismount();
                return false;
            }

            string handle = "[Standard Fishing: Fishing Check]";
            if (EzThrottler.Throttle("Throttling intro message", 1000))
            {
                IceLogging.Debug("Checking to see where we need to be here", handle);
            }
            bool hasBait = false;

            if (CosmicHelper.CurrentBait == 0)
            {
                if (EzThrottler.Throttle("Equipping bait"))
                {
                    foreach (var bait in GatheringUtil.MoonBaits)
                    {
                        foreach (var baitId in bait.Value)
                        {
                            if (PlayerHelper.GetItemCount(baitId, out var count) && count > 0)
                            {
                                // TrySwapBait 會在 IPC 不可用時退回 /ahbait 指令，並且把結果寫進 log。
                                // 直接呼叫 P.AutoHook.SwapBaitById 會在台服的 AutoHook 上靜默失敗。
                                P.AutoHook.TrySwapBait(baitId);
                                if (EzThrottler.Throttle("ICE: fishing bait equip log", 5000))
                                    IceLogging.Info($"目前沒有掛餌，要求裝上餌 ID {baitId}（{bait.Key}）。", handle);
                                return false;
                            }
                        }
                    }

                    IceLogging.Info("If we've gotten here, that means we're out of bait. Proceeding to turnin/abandon the mission");
                    SchedulerMain.State = IceState.AbandonMission;
                    P.TaskManager.Tasks.Clear();
                    return true;
                }
                return false;
            }

            // little check here for seeing if we have any baits
            foreach (var bait in GatheringUtil.MoonBaits)
            {
                foreach (var baitId in bait.Value)
                {
                    if (PlayerHelper.GetItemCount(baitId, out var count) && count > 0)
                    {
                        if (EzThrottler.Throttle("Throttling bait message", 1000))
                            IceLogging.Debug("We have the bait! Continuing onwards");
                        hasBait = true;
                        break;
                    }
                }
            }

            if (!hasBait)
            {
                IceLogging.Info("If we've gotten here, that means we're out of bait. Proceeding to turnin/abandon the mission");
                SchedulerMain.State = IceState.AbandonMission;
                P.TaskManager.Tasks.Clear();
                return true;
            }
            else if (CosmicHelper.CurrentMissionInfo.Attributes.HasFlag(MissionAttributes.Collectables) && !PlayerHelper.HasStatusId(805))
            {
                if (EzThrottler.Throttle("Log Throttle for fishing"))
                    IceLogging.Debug("We need to apply collector's glove", "Task_Start Fishing");

                if (!Player.IsBusy)
                {
                    if (EzThrottler.Throttle("Attempting to turn on collectability"))
                        ActionManager.Instance()->UseAction(ActionType.Action, 4101);
                }
                return false;
            }
            else if (!Svc.Condition[ConditionFlag.Gathering])
            {
                if (!_fishingDebug.IsFishable())
                {
                    if (_fishingDebug.FindFishableLocation(out var fishablePos, searchSteps:64))
                    {
                        IceLogging.Info("We're not in a fishable angle, so going to face one", handle);
                        P.TaskManager.Tasks.Clear();
                        P.TaskManager.Enqueue(() => FacePosition(fishablePos.Value));
                        return true;
                    }
                    else
                    {
                        IceLogging.Debug("Our current fishing position isn't viable. So going to move to the next fishing spot");
                        var mission = CosmicHelper.CurrentMissionInfo;
                        var flag = mission.MapPosition;
                        var territoryId = mission.TerritoryId;

                        var nextFishingSpot = GetNextFishingSpot(territoryId, flag, Player.Position);
                        if (nextFishingSpot != null)
                        {
                            IceLogging.Info($"We found another fishing spot to move to! {nextFishingSpot.FishingSpot} | moving to it");
                            P.TaskManager.Tasks.Clear();
                            P.TaskManager.Enqueue(() => InitiateMoving(nextFishingSpot.FishingSpot), "Vnav moving to fishing");
                            return true;
                        }

                        // 這裡是原本會「靜默永遠不動」的死路：站的位置不能釣、又查不到任何備用釣點，
                        // 就一路 return false 下去，而唯一的線索是上面那行 Debug（使用者的記錄等級看不到）。
                        if (EzThrottler.Throttle("ICE: no fishing spot data log", 10000))
                        {
                            var hasZone = MoonFishingLocations.ContainsKey(territoryId);
                            var spotCount = hasZone && MoonFishingLocations[territoryId].TryGetValue(flag, out var zoneSpots) ? zoneSpots.Count : 0;
                            IceLogging.Info($"目前位置不能釣魚，而且找不到可以移動過去的釣點：" +
                                            $"地區 {territoryId} 是否有釣點資料 = {hasZone}，任務標記 {flag} 下的釣點筆數 = {spotCount}。" +
                                            $"（釣點資料是寫死在 GatheringUtil.MoonFishingLocations 裡的，這筆缺了就只能手動釣。）", handle);
                        }
                    }
                }
                else if (EzThrottler.Throttle("Starting to fish", 1000))
                {
                    IceLogging.Info("已站在可釣位置，送出 /ahstart 開始釣魚。", handle);
                    // ActionManager.Instance()->UseAction(ActionType.Action, 289);
                    Svc.Commands.ProcessCommand("/ahstart");
                }
                else if (EzThrottler.Throttle("Adding counter for bait not equipped"))
                {
                    BaitCounter++;
                    IceLogging.Debug($"Adding 1 to the counter. Counter is at: {BaitCounter}");
                    if (BaitCounter >= 2)
                    {
                        foreach (var bait in GatheringUtil.MoonBaits)
                        {
                            foreach (var baitId in bait.Value)
                            {
                                if (PlayerHelper.GetItemCount(baitId, out var count) && count > 0)
                                {
                                    P.AutoHook.TrySwapBait(baitId);
                                    if (EzThrottler.Throttle("ICE: fishing bait recheck log", 5000))
                                        IceLogging.Info($"已重試 {BaitCounter} 次仍未開始釣魚，重新要求裝上餌 ID {baitId}（{bait.Key}）。", handle);
                                    return false;
                                }
                            }
                        }
                    }
                }
                return false;
            }
            else
            {
                // Means we are fishing, all we need to do is enable autohook then wait for us to get the amount of fish we need
                P.AutoHook.SetPluginState(true);
                IceLogging.Info("We're starting to fish. So kicking it over to checking the fish items", handle);
                P.TaskManager.Insert(() => WaitToStartFishing(), "Waiting till we actually start fishing", Utils.TaskConfig);
                BaitCounter = 0;
                return true;
            }
        }

        private static unsafe bool? WaitToStartFishing()
        {
            if (Svc.Condition[ConditionFlag.Fishing])
            {
                IceLogging.Info("We've started fishing, just going to wait for it to finish", "[Fishing: Waiting]");
                P.TaskManager.Insert(() => FinishFishing(), "Waiting for fishing to finish", Utils.TaskConfig);
                return true;
            }
            if (!Svc.Condition[ConditionFlag.Gathering])
            {
                IceLogging.Info("Somehow, we've gotten here and we're not fishing?? Going to check the score for a timer check", "[Fishing: Waiting]");
                P.TaskManager.Tasks.Clear();
                return true;
            }

            return false;
        }

        private static int collectableCounter = 0;
        private static unsafe bool? FinishFishing()
        {
            if (!Svc.Condition[ConditionFlag.Fishing])
            {
                IceLogging.Info("We're done fishing, time to go back to the score check", "[Fishing: Finished]");
                return true;
            }
            else
            {
                if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var yesNo) && yesNo.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Adding +1 to counter", 250))
                    {
                        collectableCounter += 1;
                    }
                    if (collectableCounter >= 2)
                    {
                        if (EzThrottler.Throttle("Selecting yes to collectables"))
                        {
                            yesNo.Yes();
                        }
                        return false;
                    }
                    return false;
                }
            }
            if (collectableCounter != 0)
                collectableCounter = 0;

            return false;
        }

        public static unsafe bool? FacePosition(Vector3 pos, float tolerance = 0.1f)
        {
            float currentRotation = Player.Rotation;

            // If rotation is still changing, wait for it to stabilize
            if (Math.Abs(Player.Rotation - currentRotation) > tolerance)
            {
                return false;
            }

            Vector3 direction = pos - Player.Position;
            float targetRotation = (float)Math.Atan2(direction.X, direction.Z);

            float angleDifference = GetShortestAngleDifference(Player.Rotation, targetRotation);

            if (Math.Abs(angleDifference) < tolerance)
            {
                return true;
            }

            if (EzThrottler.Throttle("Facing toward the fishing hole"))
            {
                var fwk = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();

                var autoRotateConfig = fwk->SystemConfig.GetConfigOption((uint)ConfigOption.AutoFaceTargetOnAction);
                var autoRotateOriginal = autoRotateConfig->Value.UInt;

                autoRotateConfig->Value.UInt = 1;

                IceLogging.Debug($"Telling the game to face you to: {pos}");
                Vector3 temp = pos;
                ActionManager.Instance()->AutoFaceTargetPosition(&temp);

                autoRotateConfig->Value.UInt = autoRotateOriginal;
            }

            return false;
        }
        public static float GetShortestAngleDifference(float currentAngle, float targetAngle)
        {
            float difference = targetAngle - currentAngle;

            // Normalize to [-π, π] for shortest path
            while (difference > Math.PI) difference -= (float)(2 * Math.PI);
            while (difference < -Math.PI) difference += (float)(2 * Math.PI);

            return difference;
        }
        public static FisherSpotInfo? GetNextFishingSpot(uint zone, Vector2 flag, Vector3 playerPosition)
        {
            // Check if the zone and flag exist
            if (!MoonFishingLocations.TryGetValue(zone, out var zoneData) ||
                !zoneData.TryGetValue(flag, out var spots) ||
                spots.Count == 0)
            {
                return null;
            }

            // Find the index of the spot close to the player (within distance of 2)
            int currentIndex = -1;
            for (int i = 0; i < spots.Count; i++)
            {
                float distance = Vector3.Distance(playerPosition, spots[i].FacePosition);
                if (distance < 2f)
                {
                    currentIndex = i;
                    break;
                }
            }

            // If a close spot was found, return the next one (cycling back to 0 if at the end)
            if (currentIndex != -1)
            {
                int nextIndex = (currentIndex + 1) % spots.Count;
                return spots[nextIndex];
            }

            // If no close spot found, return the first entry
            return spots[0];
        }
        public static bool? InitiateMoving(Vector3 fishingPos)
        {
            if (!P.Navmesh.IsReady())
            {
                Utils.VnavBuildInfo();
                return false;
            }
            else if (P.Navmesh.IsRunning())
            {
                P.TaskManager.Enqueue(() => !P.Navmesh.IsRunning());
                return true;
            }
            else
            {
                if (EzThrottler.Throttle("Navmesh movement"))
                {
                    IceLogging.DestinationLogs.Log(fishingPos);
                    P.Navmesh.PathfindAndMoveTo(fishingPos, false);
                }
                return false;
            }
        }
    }
}
