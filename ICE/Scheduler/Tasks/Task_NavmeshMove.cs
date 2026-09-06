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
        /// ⚠️ 這個值<b>不是</b>多餘的：AutoDuty 會把 vnavmesh 的同一個全域欄位改成別的數字
        /// （<c>AutoDuty/Helpers/MovementHelper.cs</c>），所以 ICE 開路前重設它是有作用的。
        /// </remarks>
        private const float NavigationTolerance = 0.25f;

        public static bool? Task_NavTo(Vector3 pos, bool waitForBusy = true, float distance = 2.0f, bool stayMounted = false)
        {
            bool usingCosmoliner = Svc.Condition[ConditionFlag.Unknown101];
            bool mounted = Player.Mounted;
            // 🔴 這一行原本寫成 `== 0`，語意整個是反的：CurrentLunarMission **0 代表沒有任務**
            //    （SchedulerMain 與 Task_AbandonMission 都拿 `== 0` 當「任務已被取消」的判準）。
            //    後果是底下兩個設定的行為**互相對調**：任務進行中時吃 UseMountOutsideMission、
            //    在據點閒晃時反而吃 UseMountInMission。兩個設定都在、都有作用，所以使用者看到的
            //    只是「這個勾選好像沒照我想的走」，沒有任何錯誤訊息。
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
                    if (CheckAndHandleStuck())
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
                        // 🔴 這裡原本是 P.Navmesh.SetTolerance(0.25f) —— 對 vnavmesh 的**全域**欄位
                        //    FollowPath.Tolerance 單向寫入，寫進去就一直停在那裡，沒有任何人會還原。
                        // 🔑 改成租約之後語意不變（ICE 導航期間就是 0.25），差別在於 ICE 結束導航、
                        //    被卸載或當掉時 vnavmesh 會自己還原成使用者的值。
                        //    舊版 vnavmesh 沒有租約端點時會自動退回原本那個直接寫入的行為。
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
            // 與 Task_NavTo 同一顆反向判定，理由見該處註解。
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
                    if (CheckAndHandleStuck())
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

        private static Vector3 _lastPosition = Vector3.Zero;
        private static DateTime _lastPositionChange = DateTime.Now;
        private static int _stuckAttempts = 0;
        private const float STUCK_DISTANCE_THRESHOLD = 1.0f; // Consider stuck if moved less than this
        private const int STUCK_TIME_THRESHOLD = 3000; // Time in ms before considering stuck

        private static unsafe bool CheckAndHandleStuck()
        {
            var currentPos = Player.Position;
            var timeSinceLastChange = (DateTime.Now - _lastPositionChange).TotalMilliseconds;

            // Check if we've moved significantly
            if (Vector3.Distance(currentPos, _lastPosition) > STUCK_DISTANCE_THRESHOLD)
            {
                // We moved, reset tracking
                ResetInfo();
                return false;
            }

            // We haven't moved much, check if we've been stuck long enough
            if (timeSinceLastChange > STUCK_TIME_THRESHOLD)
            {
                _stuckAttempts++;

                if (_stuckAttempts == 1)
                {
                    // First attempt: try jumping
                    if (EzThrottler.Throttle("Stuck - attempting jump", 1000))
                    {
                        IceLogging.Warning("Player appears stuck, attempting to jump");
                        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
                        _lastPositionChange = DateTime.Now; // Give it time to work
                    }
                    return true;
                }
                else if (_stuckAttempts >= 2)
                {
                    // Second attempt: stop navmesh after jump had time to execute
                    if (EzThrottler.Throttle("Stuck - stopping navmesh", 1000))
                    {
                        IceLogging.Warning("Player still stuck after jump attempt, stopping navmesh");
                        P.Navmesh.Stop();
                        _stuckAttempts = 0; // Reset for next time
                        _lastPositionChange = DateTime.Now;
                    }
                    return true;
                }
            }

            return false;
        }

        public static void ResetInfo()
        {
            _lastPosition = Vector3.Zero;
            _lastPositionChange = DateTime.Now;
            _stuckAttempts = 0;
        }
    }
}
