using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Scheduler
{
    internal static class Task_HubActivities
    {
        public static bool RepairNpc = false;
        public static bool RelicTurnin = false;
        public static bool CosmoBuy = false;
        public static bool CanGamba = false;
        /// <summary>非 0＝這一趟包含「緊急任務結束後返回基地」，值是那個緊急任務的 ID。</summary>
        public static uint CriticalMissionId = 0;
        private static Vector3 craftingSpot = Vector3.Zero;

        public static void Enqueue()
        {
            var critical = CriticalMissionId;
            if (critical != 0)
            {
                var alsoDoing = string.Join("、", new[]
                {
                    RepairNpc ? "NPC 修理" : null,
                    RelicTurnin ? "繳交宇宙工具" : null,
                    CosmoBuy ? "宇宙信用點數購物" : null,
                    CanGamba ? "宇宙好運道" : null,
                }.Where(x => x != null));
                // 人在緊急任務的繳交點，不把那裡記成製作點走回去。旗標在這一步就清掉：
                // 後面的步驟逾時讓整趟重排時不再重試回基地。
                P.TaskManager.Enqueue(() =>
                {
                    craftingSpot = Vector3.Zero;
                    CriticalMissionId = 0;
                    return Task_CriticalReturn.Begin(critical, alsoDoing);
                }, "Starting the return to base after a critical mission");
                P.TaskManager.Enqueue(Task_CriticalReturn.Step, "Returning to base after a critical mission", Utils.TaskConfig);
            }
            else
            {
                P.TaskManager.Enqueue(RegisterCraftingPosition, "Registering crafting position for later");
            }

            // 只為緊急任務回來的那一趟不走 HubCheck：它沒有上限，回不去會卡住。
            if (critical == 0 || RepairNpc || RelicTurnin || CosmoBuy || CanGamba)
                P.TaskManager.Enqueue(Task_Repair.HubCheck, "Checking to see if we're in hub area");
            if (RepairNpc)
            {
                P.TaskManager.EnqueueMulti
                (
                    // 🔴 PathToRepair 是 vnav 走去修理 NPC（分鐘級），RepairAtNpc 要跑完整套修理動畫。
                    //    這一串用預設 30 秒 + AbortOnTimeout 逾時，會連同這個方法最後面那組
                    //    「ResetAll → 走回製作點 → 狀態切回 GrabMission」的收尾一起清掉。
                    //    收尾沒跑 → RepairNpc 還是 true、狀態還停在 HubReturn → Tick 再排一次
                    //    → 又走一半又逾時，變成無聲的無限迴圈。
                    new(() => IceLogging.Info("Starting repair task at the npc", "Task_HubActivities")),
                    new(Task_Repair.PathToRepair, "Pathing to the repair NPC", Utils.TaskConfig),
                    new(Task_Repair.RepairAtNpc, "Repairing at the NPC Vendor", Utils.TaskConfig),
                    new(Task_Repair.CloseRepair, "Closing the repair window", Utils.TaskConfig)
                );
            }
            if (RelicTurnin)
            {
                P.TaskManager.Enqueue(() => IceLogging.Info("Starting Relic Turnin task at the npc", "Task_HubActivities"));
                Task_RelicTurnin.Enqueue();
                P.TaskManager.Enqueue(() => IceLogging.Info("Task_Relic turnin is Complete"));
            }
            if (CosmoBuy)
            {
                P.TaskManager.Enqueue(() => IceLogging.Info("Starting Cosmo Buy task at the npc", "Task_HubActivities"));
                Task_BuyCosmoItems.Enqueue();
            }
            if (CanGamba)
            {
                P.TaskManager.Enqueue(() => IceLogging.Info("Starting Gamba task at the npc", "Task_HubActivities"));
                Task_Gamba.Enqueue();
            }
            P.TaskManager.EnqueueMulti
            (
                new(() => ResetAll(), "Setting all task to false"),
                new(() => IceLogging.Info("Checking to see if we need to path back to the spot")),
                new(PathBackToCraftingSpot, "Pathing back to our crafting spot", Utils.TaskConfig),
                // 緊急任務那一趟回 Start：檢查狀態排在精製／自行修理之前，回到 Start 才補得回來。
                new(() => SchedulerMain.State = critical != 0 ? IceState.Start : IceState.GrabMission, "Swapping back to start")
            );
        }

        public static bool? RegisterCraftingPosition()
        {
            if (CosmicHelper.CrafterJobList.Contains(Player.JobId))
                craftingSpot = Player.Position;

            return true;
        }

        public static bool? PathBackToCraftingSpot()
        {
            if (CosmicHelper.CrafterJobList.Contains(Player.JobId))
            {
                if (craftingSpot != Vector3.Zero)
                {
                    if (Player.Mounted && Player.DistanceTo(craftingSpot) < C.MountRadius && Player.Mounted)
                    {
                        if (EzThrottler.Throttle("Dismount if need be"))
                        {
                            IceLogging.Debug("Dismounting cause... we be mounted back to our crafting spot");
                            Utils.Dismount();
                        }
                        return false;
                    }
                    if (!P.Navmesh.IsReady())
                    {
                        Utils.VnavBuildInfo();
                        return false;
                    }
                    else if (!P.Navmesh.IsRunning() && Player.DistanceTo(craftingSpot) < 1)
                    {

                        craftingSpot = Vector3.Zero;
                        return true;
                    }
                    else
                    {
                        if (!P.Navmesh.IsRunning())
                        {
                            if (EzThrottler.Throttle("Telling navemesh to move to crafting spot"))
                            {
                                IceLogging.DestinationLogs.Log(craftingSpot);
                                P.Navmesh.PathfindAndMoveTo(craftingSpot, false);
                            }
                        }
                        else
                        {
                            if (C.UseMountOutsideMission && Player.DistanceTo(craftingSpot) >= C.MountRadius && !PlayerHelper.CustomIsBusy && !Player.Mounted)
                            {
                                if (EzThrottler.Throttle("Mounting the mount to head back to craft"))
                                {
                                    Utils.MountAction();
                                }
                            }
                        }

                        return false;
                    }
                }
                else
                {
                    return true;
                }
            }
            else
            {
                IceLogging.Info($"We're not on a crafting job. (Allegedly) which means that we don't need to path back | Player Job: {Player.JobId}");
                return true;
            }
        }

        private static bool? ResetAll()
        {
            IceLogging.Info("Resetting all hub task to false");

            RepairNpc = false;
            RelicTurnin = false;
            CosmoBuy = false;
            CanGamba = false;
            CriticalMissionId = 0;

            return true;
        }
    }
}
