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
            // 這三個跟 Task_AbandonMission 是同一組收尾任務，同樣可能卡在區域切換／視窗等待上，
            // 用 NeoTaskManager 的 30 秒預設逾時 + AbortOnTimeout 會把整個佇列清掉。
            P.TaskManager.Enqueue(() => TurninMission(), "Turning in the mission to the moon gods", Utils.TaskConfig);
            P.TaskManager.Enqueue(() => JobSwapCheck(), "Checking to see if you need to swap jobs", Utils.TaskConfig);
            P.TaskManager.Enqueue(() => GoldCheck(), "Checking if Gold Check Task needs to be completed", Utils.TaskConfig);
            P.TaskManager.Enqueue(() => CommandCheck(), "Checking for post mission commands", Utils.TaskConfig);
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
                // 跟 Task_AbandonMission 完全同形狀的雷：守了 MissionConfig，沒守 SheetMissionDict。
                // 兩個字典的鍵集合不同（MissionConfig 含 0，SheetMissionDict 沒有），
                // PreviousMissionId 為 0 時 TryGetValue 會過而 SheetMissionDict[0] 直接丟
                // KeyNotFoundException，例外在任務裡只會表現成「卡住不動」。
                if (C.MissionConfig.TryGetValue(PreviousMissionId, out var config) &&
                    CosmicHelper.SheetMissionDict.TryGetValue(PreviousMissionId, out var prevMission))
                {
                    if (config.BestTime != double.MaxValue)
                        IceLogging.Info($"Mission [{PreviousMissionId}] [{prevMission.Name}] completed in {duration:mm\\:ss\\.ff} | Best: {TimeSpan.FromSeconds(config.BestTime):mm\\:ss\\.ff} | Avg: {TimeSpan.FromSeconds(config.AverageTime):mm\\:ss\\.ff}", $"{tag} [Mission Timer]");
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
                // ✅ 曾經是零守衛的字典索引（跟同檔案下方那顆是同一個字典），已修：
                //    守衛＝下方那個 SheetMissionDict.TryGetValue。
                // 查不到就當成「不是限時任務」繼續走一般回報流程 —— 這比丟例外讓佇列卡死安全，
                // critical 只影響「要不要先走去收集點」這一步。
                var critical = CosmicHelper.SheetMissionDict.TryGetValue(id, out var turninMission)
                               && turninMission.Attributes.HasFlag(MissionAttributes.Critical);
                PreviousMissionId = id;

                if (EzThrottler.Throttle("Checking for previous score"))
                    PreviousScore = ScoreCheck();

                if (critical)
                {
                    // 這個任務的繳交點「應該」在哪。原本這個查表在下面 else 分支裡才做，
                    // 現在提上來給 TryGetObjectCollectionPoint 當篩選用的預期座標。
                    // 🔑 查不到（location == null 或原始座標是 Zero）就退回「不給預期座標」，
                    //    也就是舊行為 —— 挑最近的。不因為缺資料就整個挑不到。
                    Vector3? expectedLocation =
                        GatheringUtil.CriticalLocations.TryGetValue(id, out var location) && location.RawLocation != Vector3.Zero
                            ? location.RawLocation
                            : null;

                    // 允許半徑：任務自己的半徑再加 25，下限 100。
                    // 🔑 turninMission 來自上面那個已經有守衛的 TryGetValue（critical 為真就代表查到了），
                    //    不要改成 SheetMissionDict[id] —— 那正是這個檔修過兩次的無守衛索引形狀。
                    var collectionPointRadius = Math.Max(100f, turninMission.Radius + 25f);

                    var collectionPoint = expectedLocation is { } expectedForPick
                        ? Utils.TryGetObjectCollectionPoint(expectedForPick, collectionPointRadius)
                        : Utils.TryGetObjectCollectionPoint();
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
                            // （location 已在上面 critical 分支的開頭查過，這裡沿用。）
                            if (expectedLocation is { } expectedPos)
                            {
                                if (collectionPoint == null)
                                {
                                    // We still need to get within range of it. So just going to tell it to pathfind and moveto if it wasn't already.
                                    if (!P.Navmesh.IsRunning())
                                    {
                                        if (EzThrottler.Throttle("Telling navmesh to move to the spot"))
                                        {
                                            IceLogging.Debug("We're not close enough to the turnin point to find out where one's at. So going to the location where it might be at");
                                            IceLogging.DestinationLogs.Log(expectedPos);
                                            P.Navmesh.PathfindAndMoveTo(expectedPos, false);
                                        }
                                    }
                                    else
                                    {
                                        if (C.UseMountInMission && !Player.IsBusy && Player.DistanceTo(expectedPos) > C.MountRadius && !Svc.Condition[ConditionFlag.Mounted])
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

                                    if (Player.DistanceTo(expectedPos) > 75 && P.Navmesh.IsRunning())
                                    {
                                        if (EzThrottler.Throttle("Waiting to be in a better range", 1000))
                                        {
                                            IceLogging.Debug("Waiting to be within 50 yalms of the turnin point");
                                        }
                                    }
                                    else if (Player.DistanceTo(expectedPos) <= 75 || !P.Navmesh.IsRunning())
                                    {

                                        if (!PathfoundToRed)
                                        {
                                            P.Navmesh.Stop();
                                            // 📌 Information：這是「挑到哪一個繳交點」的唯一可回報證據。
                                            //    挑錯區的失敗形式是「跑很遠然後繳不掉」，事後從 log 只看得到
                                            //    導航指令，看不到當時有哪些候選、為什麼挑這個。
                                            //    「離任務中心多遠」與「允許半徑」一起印，才分得出
                                            //    「篩選器挑對了但人跑錯」和「篩選器本身挑到別區」。
                                            IceLogging.Info(
                                                $"已選定緊急任務繳交點 {collectionPoint.Position}；" +
                                                $"離玩家 {Player.DistanceTo(collectionPoint):F1}、" +
                                                $"離任務中心 {Vector3.Distance(collectionPoint.Position, expectedPos):F1}、" +
                                                $"允許半徑 {collectionPointRadius:F1}",
                                                tag);
                                            IceLogging.DestinationLogs.Log(collectionPoint.Position);
                                            P.Navmesh.PathfindAndMoveTo(collectionPoint.Position, false);
                                            PathfoundToRed = true;
                                        }
                                        else if (Player.DistanceTo(collectionPoint.Position) < C.DismountRadius && Svc.Condition[ConditionFlag.Mounted])
                                        {
                                            if (EzThrottler.Throttle("dismounting"))
                                                Utils.Dismount();
                                        }
                                        else if (C.UseMountInMission && !Player.IsBusy && Player.DistanceTo(expectedPos) > C.MountRadius && !Svc.Condition[ConditionFlag.Mounted])
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

            // ✅ 曾經是零守衛的字典索引 ×2，已修：守衛＝下方的 C.MissionConfig.TryGetValue。
            //    原因留存：PreviousMissionId 的初始值就是 0，而 MissionConfig 雖然
            // 通常含 0（MissionTimer 會補），GetOnlyPreviousMissionsRecursive 回來的前置任務
            // 卻不保證在 MissionConfig 裡。這一段跑在 GoldCheck 任務內，丟例外＝佇列卡住。
            if (C.RemoveAfterGold && isGold)
            {
                if (C.MissionConfig.TryGetValue(PreviousMissionId, out var goldConfig))
                {
                    // 🔴 連續任務的前置不能因為「自己拿到金星了」就停用：後續任務不是永久解鎖的，
                    //    要重跑前置才會再出現（同一個 GoldCheck 底下那段「沒金星就重新啟用所有前置」
                    //    就是上游對這個機制的認定）。停掉前置＝整條後續鏈再也接不到，
                    //    而且完全沒有提示，使用者只能自己回頭核對整張任務表。
                    //    台服 7.20 共有 88 條這種邊、最長三層（見 MissionChain 的資料統計）。
                    // 緊急任務的例外要排在鏈結檢查**之前**：兩者都是「不要停用」，
                    // 先問哪一個都不影響結果，但先問這一個時 log 講的理由才是使用者
                    // 自己勾的那個開關，而不是一個他沒設定過的鏈結規則。
                    if (MissionChain.ShouldKeepEnabledForEmergency(PreviousMissionId, out var emergencyReason))
                    {
                        IceLogging.Info(
                            $"保留任務 {MissionChain.DescribeMission(PreviousMissionId)}（不套用「取得金星後自動停用」）：{emergencyReason}。",
                            "[Gold Check Task]");
                    }
                    else if (MissionChain.ShouldKeepEnabledForChain(PreviousMissionId, out var keepReason))
                    {
                        IceLogging.Info(
                            $"保留任務 {MissionChain.DescribeMission(PreviousMissionId)}（不套用「取得金星後自動停用」）：{keepReason}。"
                            + "它是連續任務的前置，停用它會讓後續任務再也接不到。",
                            "[Gold Check Task]");
                    }
                    else
                    {
                        goldConfig.Enabled = false;
                    }
                }
                else
                {
                    IceLogging.Info($"任務 {PreviousMissionId} 在設定檔裡沒有對應的設定，跳過「達金後停用」。", "[Gold Check Task]");
                }
            }
            if (C.RemoveAfterGold && !isGold)
            {
                foreach (var prevMission in MainWindow.GetOnlyPreviousMissionsRecursive(PreviousMissionId))
                {
                    if (!C.MissionConfig.TryGetValue(prevMission, out var prevConfig))
                    {
                        IceLogging.Info($"前置任務 {prevMission} 在設定檔裡沒有對應的設定，跳過重新啟用。", "[Gold Check Task]");
                        continue;
                    }

                    prevConfig.Enabled = true;
                    C.Save();
                }
            }

            // 收拾先前版本已經造成的損害：前置被停用、鏈上卻還有使用者啟用中且尚未金星的任務。
            // 條件很緊（見 MissionChain.RepairSequentialPrerequisites 的說明），
            // 而且每一筆改動都會留一行 Information。
            MissionChain.RepairSequentialPrerequisites("[Gold Check Task]");

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
