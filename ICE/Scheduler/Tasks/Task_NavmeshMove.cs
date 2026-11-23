using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Toast;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.Scheduler.Tasks
{
    internal class Task_NavmeshMove
    {
        /// <summary>ICE 導航期間要求 vnavmesh 使用的路徑容許值。刻意＝vnavmesh 自己的預設值。</summary>
        /// <remarks>
        /// ⚠️ 這個值<b>不是</b>多餘的：別的外掛（例：AutoDuty 拿不到租約時）會把 vnavmesh 的同一個全域欄位
        /// 改成別的數字，所以 ICE 開路前重設它是有作用的。
        /// </remarks>
        private const float NavigationTolerance = 0.25f;

        public static bool? Task_NavTo(Vector3 pos, bool waitForBusy = true, float distance = 2.0f, bool stayMounted = false)
        {
            bool usingCosmoliner = Svc.Condition[ConditionFlag.Unknown101];
            bool mounted = Player.Mounted;
            // 🔴 CurrentLunarMission **0 代表沒有任務**（SchedulerMain 與 Task_AbandonMission 都拿 `== 0` 當「任務已被取消」的判準），
            //    所以「任務進行中」是 `!= 0`；寫反的話底下 UseMountInMission／UseMountOutsideMission 兩個設定的行為
            //    會互相對調，而且沒有任何錯誤訊息。
            //    ⚠️ 同一份判定在下面的 NavToDestination 還有一份，兩處必須一起改。
            bool inMission = CosmicHelper.CurrentLunarMission != 0;
            float minMountDistance = C.MountRadius;
            float dismountDistance = C.DismountRadius;

            if (!P.Navmesh.Installed)
            {
                IceLogging.Info("We seem to be missing navmesh... so we're just going to exit here");
                return true;
            }
            else if (P.Navmesh.IsRunning())
            {
                if (C.JumpIfStuck)
                {
                    if (CheckAndHandleStuck(pos))
                    {
                        return false; // Let the stuck handler take control
                    }
                }

                if (!mounted && Player.DistanceTo(pos) > minMountDistance)
                {
                    if (inMission && C.UseMountInMission)
                    {
                        if (EzThrottler.Throttle("Using mount"))
                            Utils.MountAction();
                    }
                    else if (!inMission && C.UseMountOutsideMission)
                    {
                        if (EzThrottler.Throttle("Using mount"))
                            Utils.MountAction();
                    }
                }

                if (Player.DistanceTo(pos) <= dismountDistance && !stayMounted)
                {
                    if (EzThrottler.Throttle("Dismounting the mount"))
                    {
                        Utils.Dismount();
                    }
                }

                if (Player.IsMoving && waitForBusy)
                {
                    if (EzThrottler.Throttle("Throttle message tehe"))
                        IceLogging.Verbose("We're currently moving, and we were told to wait for us to NOT be moving so... yeah, we waiting");

                    return false;
                }
                else if (!waitForBusy && Player.DistanceTo(pos) <= distance)
                {
                    if (EzThrottler.Throttle("Telling navmesh to stop"))
                    {
                        IceLogging.Debug("We're within stopping distance, so stopping navmesh");
                        P.Navmesh.Stop();
                    }
                }

                if (usingCosmoliner)
                {
                    if (EzThrottler.Throttle("Telling navmesh to stop"))
                        P.Navmesh.Stop();
                }
            }
            else if (!P.Navmesh.IsReady())
            {
                if (EzThrottler.Throttle("Waiting on navmesh", 1000))
                {
                    var navProgress = P.Navmesh.BuildProgress();
                    IceLogging.Debug($"Waiting for navmesh to finish building. Currently at: {navProgress:N2}");
                }
            }
            else if (!P.Navmesh.IsRunning())
            {
                // We're here, which means it's time to start fresh for navmesh
                if (usingCosmoliner)
                {
                    // We don't want navmesh/any checks to be running while using the cosmoliner, so just exiting out
                    return false;
                }

                if (Player.DistanceTo(pos) < distance)
                {
                    if (mounted && !stayMounted)
                    {
                        if (EzThrottler.Throttle("Dismounting the mount"))
                        {
                            Utils.Dismount();
                        }
                        return false;
                    }
                    else if (Player.IsJumping)
                    {
                        return false;
                    }
                    else
                    {
                        IceLogging.Debug("We've met the distance threshold, continuing on");
                        ResetInfo();
                        // 導航結束：把容許值租約還回去，vnavmesh 立刻恢復使用者自己的設定。
                        // 📌 沒持有租約時是無操作；就算這一步沒被走到，租約也會自己逾時還原。
                        P.Navmesh.ReleaseToleranceLease("已抵達目的地");
                        return true;
                    }
                }
                else
                {
                    if (EzThrottler.Throttle("Telling navmesh to start"))
                    {
                        // 🔴 走租約（ApplyTolerance）而不是直接 SetTolerance：後者是對 vnavmesh **全域**欄位的單向寫入，
                        //    沒有任何人會還原。租約在 ICE 結束導航、被卸載或當掉時由 vnavmesh 自己還原成使用者的值；
                        //    舊版 vnavmesh 沒有租約端點時會自動退回直接寫入。
                        P.Navmesh.ApplyTolerance(NavigationTolerance);
                        IceLogging.Debug($"We're setting the tolerance to {NavigationTolerance}f here");
                        IceLogging.DestinationLogs.Log(pos);
                        P.Navmesh.PathfindAndMoveTo(pos, false);
                    }
                }
            }

            return false;
        }

        public static bool NavToDestination(Vector3 pos, bool waitForBusy = true, float distance = 2.0f, bool stayMounted = false)
        {
            bool usingCosmoliner = Svc.Condition[ConditionFlag.Unknown101];
            bool mounted = Player.Mounted;
            // 與 Task_NavTo 同一份判定（`!= 0` 才是任務進行中），兩處必須一起改。
            bool inMission = CosmicHelper.CurrentLunarMission != 0;
            float minMountDistance = C.MountRadius;
            float dismountDistance = C.DismountRadius;

            if (!P.Navmesh.Installed)
            {
                IceLogging.Info("We seem to be missing navmesh... so we're just going to exit here");
                return true;
            }
            else if (P.Navmesh.IsRunning())
            {
                if (C.JumpIfStuck)
                {
                    if (CheckAndHandleStuck(pos))
                    {
                        return false; // Let the stuck handler take control
                    }
                }

                if (!mounted && Player.DistanceTo(pos) > minMountDistance)
                {
                    if (inMission && C.UseMountInMission)
                    {
                        if (EzThrottler.Throttle("Using mount"))
                            Utils.MountAction();
                    }
                    else if (!inMission && C.UseMountOutsideMission)
                    {
                        if (EzThrottler.Throttle("Using mount"))
                            Utils.MountAction();
                    }
                }

                if (Player.DistanceTo(pos) <= dismountDistance && !stayMounted)
                {
                    if (EzThrottler.Throttle("Dismounting the mount"))
                    {
                        Utils.Dismount();
                    }
                }

                if (Player.IsMoving && waitForBusy)
                {
                    if (EzThrottler.Throttle("Throttle message tehe", 2000))
                        IceLogging.Verbose("We're currently moving, and we were told to wait for us to NOT be moving so... yeah, we waiting");

                    return false;
                }
                else if (!waitForBusy && Player.DistanceTo(pos) <= distance)
                {
                    if (EzThrottler.Throttle("Telling navmesh to stop"))
                    {
                        IceLogging.Debug("We're within stopping distance, so stopping navmesh");
                        P.Navmesh.Stop();
                    }
                }

                if (usingCosmoliner)
                {
                    if (EzThrottler.Throttle("Telling navmesh to stop"))
                        P.Navmesh.Stop();
                }
            }
            else if (!P.Navmesh.IsReady())
            {
                if (EzThrottler.Throttle("Waiting on navmesh", 1000))
                {
                    var navProgress = P.Navmesh.BuildProgress();
                    IceLogging.Debug($"Waiting for navmesh to finish building. Currently at: {navProgress:N2}");
                }
            }
            else if (!P.Navmesh.IsRunning())
            {
                // We're here, which means it's time to start fresh for navmesh
                if (usingCosmoliner || Player.IsJumping)
                {
                    // We don't want navmesh/any checks to be running while using the cosmoliner, so just exiting out
                    return false;
                }

                if (Player.DistanceTo(pos) < distance)
                {
                    if (mounted && !stayMounted)
                    {
                        if (EzThrottler.Throttle("Dismounting the mount"))
                        {
                            Utils.Dismount();
                        }
                        return false;
                    }
                    else
                    {
                        IceLogging.Debug("We've met the distance threshold, continuing on");
                        ResetInfo();
                        return true;
                    }
                }
                else
                {
                    if (EzThrottler.Throttle("Telling navmesh to start"))
                    {
                        IceLogging.DestinationLogs.Log(pos);
                        P.Navmesh.PathfindAndMoveTo(pos, false);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 卡住偵測：錨點＝最後一次「移動超過 <see cref="StuckDistance"/>」的位置與時間。
        /// 停在原地超過 <see cref="StuckMs"/> 先跳一次，再停住仍沒動就停掉 vnavmesh 讓它重算路；
        /// 同一個目的地最多脫困 <see cref="MaxRecoveries"/> 次，之後只記一次 log、不再插手。只在框架執行緒讀寫。
        /// 中間有一段沒在走（算路中、宇宙航線）就重設錨點，算路花的時間不算卡住。
        /// </summary>
        private static Vector3 _target;
        private static long _lastCheckMs;
        private static Vector3 _anchorPos;
        private static bool _anchorValid;
        private static long _anchorMs;
        private static bool _jumped;
        private static int _recoveries;
        private static bool _gaveUpLogged;
        private const float StuckDistance = 1.0f;
        private const long StuckMs = 3000;
        private const int MaxRecoveries = 3;

        private static unsafe bool CheckAndHandleStuck(Vector3 target)
        {
            var currentPos = Player.Position;
            var nowMs = Environment.TickCount64;

            if (Vector3.Distance(target, _target) > StuckDistance)
            {
                ResetInfo();
                _target = target;
            }

            var gap = nowMs - _lastCheckMs > 500;
            _lastCheckMs = nowMs;
            if (gap || !_anchorValid || Vector3.Distance(currentPos, _anchorPos) > StuckDistance)
            {
                _anchorPos = currentPos;
                _anchorValid = true;
                _anchorMs = nowMs;
                _jumped = false;
                return false;
            }

            if (nowMs - _anchorMs <= StuckMs)
                return false;

            if (_recoveries >= MaxRecoveries)
            {
                if (!_gaveUpLogged)
                {
                    _gaveUpLogged = true;
                    IceLogging.Info($"卡住自動脫困已試 {MaxRecoveries} 次仍沒離開原地（{currentPos.X:F1}, {currentPos.Y:F1}, {currentPos.Z:F1}），這段導航不再跳躍或重算路，交給 vnavmesh 自己處理。", "[Nav Stuck]");
                }
                return false;
            }

            _anchorMs = nowMs;
            if (!_jumped)
            {
                _jumped = true;
                IceLogging.Warning($"Player appears stuck, attempting to jump（{StuckMs / 1000} 秒內移動不到 {StuckDistance} 米）");
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
            }
            else
            {
                _jumped = false;
                _recoveries++;
                IceLogging.Warning($"Player still stuck after jump attempt, stopping navmesh（第 {_recoveries}/{MaxRecoveries} 次，停掉讓 vnavmesh 重算路）");
                P.Navmesh.Stop();
            }
            return true;
        }

        public static void ResetInfo()
        {
            _anchorValid = false;
            _jumped = false;
            _recoveries = 0;
            _gaveUpLogged = false;
        }
    }
}
