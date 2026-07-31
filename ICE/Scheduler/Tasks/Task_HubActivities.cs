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
        private static Vector3 craftingSpot = Vector3.Zero;

        public static void Enqueue()
        {
            P.TaskManager.Enqueue(RegisterCraftingPosition, "Registering crafting position for later");
            P.TaskManager.Enqueue(Task_Repair.HubCheck, "Checking to see if we're in hub area");
            if (RepairNpc)
            {
                P.TaskManager.EnqueueMulti
                (
                    new(() => IceLogging.Info("Starting repair task at the npc", "Task_HubActivities")),
                    new(Task_Repair.PathToRepair, "Pathing to the repair NPC"),
                    new(Task_Repair.RepairAtNpc, "Repairing at the NPC Vendor"),
                    new(Task_Repair.CloseRepair, "Closing the repair window")
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
                // 原本這裡複製貼上寫成 "Starting Relic Turnin task"，害 log 裡分不出
                // 到底是宇宙工具回報還是購買在跑。
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
                new(() => SchedulerMain.State = IceState.GrabMission, "Swapping back to start")
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

            return true;
        }
    }
}
