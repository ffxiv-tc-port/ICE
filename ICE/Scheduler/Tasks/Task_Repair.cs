using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ICE.Utilities.Cosmic_Helper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_Repair
    {
        public static void Enqueue()
        {
            if (PlayerHelper.NeedsRepair(C.RepairPercent))
            {
                var currentJob = Player.JobId;

                if ((C.SelfRepairGather && CosmicHelper.GatheringJobList.Contains(currentJob)) || (C.SelfRepairCrafter && CosmicHelper.CrafterJobList.Contains(currentJob)))
                {
                    // 🔴 修一整套裝備要一件一件跑動畫，超過 30 秒很正常。用 NeoTaskManager 的
                    //    預設值（30 秒 + AbortOnTimeout）逾時就會把整個佇列清掉 ——
                    //    連同下面那一行「把狀態切回 GrabMission」也一起被清掉。
                    //    而狀態還停在 Repair，Tick 會再呼叫一次 Enqueue()，這時 NeedsRepair 已經
                    //    是 false，if 整段不執行 → 什麼都沒排 → **永遠停在 Repair 狀態**。
                    //    這正是「佇列尾端有收尾步驟、前面逾時會讓它靜默不執行」的教科書案例。
                    P.TaskManager.EnqueueMulti
                    (
                        new(OpenSelfRepair, "Opening the self repair window", Utils.TaskConfig),
                        new(SelfRepair, "Executing the self repair", Utils.TaskConfig),
                        new(CloseRepair, "Closing Self Repair", Utils.TaskConfig)
                    );
                }
                P.TaskManager.Enqueue(() =>  SchedulerMain.State = IceState.GrabMission);
            }
        }
        public static unsafe bool? HubCheck()
        {
            Vector2 HubCenter = Vector2.Zero;
            if (PlayerHelper.IsInPhaenna())
            {
                HubCenter = new Vector2(340.0f, -420.0f);
            }

            Vector2 PlayerPos = new Vector2(Player.Position.Z, Player.Position.Z);

            if (Player.DistanceTo(HubCenter) < 45)
            {
                IceLogging.Info("Player is in the range of the main hub area right now", "[Vendor Repair Check]");
                return true;
            }
            else
            {
                //Not within the vicinity of the hub area, time to return
                if (!Player.IsBusy)
                {
                    if (EzThrottler.Throttle("Returning back to the moon base"))
                        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 26);
                }
            }

            return false;
        }
        public static unsafe bool? PathToRepair()
        {
            var zoneId = Player.Territory;
            // ✅ 這裡曾經是零守衛的字典索引，已修：守衛＝下一行的 NpcData.TryGetMoonNpc。
            // 原因留存：MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是
            // Player.Territory —— 佇列排好之後玩家還是可能被傳送走（機甲行動抽中駕駛員就會），
            // 下一個 tick 讀到的區域就不是月面了。修之前的 .First()/.FirstOrDefault() 兩種寫法
            // 都沒處理「找不到」，一個丟 InvalidOperationException、一個回 null 再 NRE。
            if (!NpcData.TryGetMoonNpc(zoneId, NpcData.NpcType.Repair, out var npcEntry))
            {
                if (EzThrottler.Throttle("ICE: moon npc missing Repair", 5000))
                    IceLogging.Info($"目前區域 {zoneId} 沒有登記修理 NPC 的資料（可能已經被傳送離開月面），中止這一步。", "[ICE]");
                SchedulerMain.AbortToStateCheck();
                return true;
            }

            if (EzThrottler.Throttle("Log Throttle for repair", 2000))
            {
                IceLogging.Debug($"NPC: {npcEntry.Name} | {npcEntry.NpcId} | Zone: {zoneId}");
            }

            if (Player.DistanceTo(npcEntry.NpcLocation) <= 6.75f)
            {
                if (P.Navmesh.IsRunning())
                {
                    if (Player.DistanceTo(npcEntry.NpcLocation) < 5)
                    {
                        IceLogging.Info("Pathing to NPC has reached the distance thresh, stopping");
                        P.Navmesh.Stop();
                        return true;
                    }
                }
                else
                {
                    IceLogging.Debug($"Distance to the npc is correct, commending repair");
                    return true;
                }
            }
            else
            {
                if (!P.Navmesh.IsRunning())
                {
                    if (EzThrottler.Throttle("Pathing to repair NPC"))
                    {
                        IceLogging.Debug($"Pathing to: {npcEntry.Name}");

                        Vector3 randomPoint = RandomUtil.GetRandomPointInBounds(npcEntry.Corner1, npcEntry.Corner2, npcEntry.Corner3, npcEntry.Corner4, npcEntry.NpcLocation.Y);
                        IceLogging.DestinationLogs.Log(randomPoint);
                        P.Navmesh.PathfindAndMoveTo(randomPoint, false);
                    }
                }
            }

            return false;
        }
        public static unsafe bool? RepairAtNpc()
        {
            var zoneId = Player.Territory;
            // ✅ 這裡曾經是零守衛的字典索引，已修：守衛＝下一行的 NpcData.TryGetMoonNpc。
            // 原因留存：MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是
            // Player.Territory —— 佇列排好之後玩家還是可能被傳送走（機甲行動抽中駕駛員就會），
            // 下一個 tick 讀到的區域就不是月面了。修之前的 .First()/.FirstOrDefault() 兩種寫法
            // 都沒處理「找不到」，一個丟 InvalidOperationException、一個回 null 再 NRE。
            if (!NpcData.TryGetMoonNpc(zoneId, NpcData.NpcType.Repair, out var npcEntry))
            {
                if (EzThrottler.Throttle("ICE: moon npc missing Repair", 5000))
                    IceLogging.Info($"目前區域 {zoneId} 沒有登記修理 NPC 的資料（可能已經被傳送離開月面），中止這一步。", "[ICE]");
                SchedulerMain.AbortToStateCheck();
                return true;
            }

            Utils.TryGetNpcObject(npcEntry, out var gameObject);
            var currentTarget = Svc.Targets.Target;
            var repairAmount = C.RepairPercent;

            if (!PlayerHelper.NeedsRepair(99.9f))
            {
                IceLogging.Debug("Repair Complete! Finishing task and closing window");
                return true;
            }
            else if (GenericHelpers.TryGetAddonMaster<Repair>("Repair", out var repair) && repair.IsAddonReady)
            {
                if (PlayerHelper.NeedsRepair(repairAmount))
                {
                    if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var Yesno) && Yesno.IsAddonReady)
                    {
                        // 閘門預設是「一律按下確定」，那條路徑連確認框的文字都不會讀（見 YesnoGuard）。
                        if (FrameThrottler.Throttle("Saying yes to the gil") && YesnoGuard.ShouldConfirm(YesnoSituation.Repair))
                            Yesno.Yes();
                    }
                    else if (EzThrottler.Throttle("Firing off repair request", 300))
                    {
                        IceLogging.Debug("Repair Callback", "[Self Repair Task]");
                        repair.RepairAll();
                    }
                }
            }
            else if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectIconString", out var iconString) && GenericHelpers.IsAddonReady(iconString))
            {
                if (FrameThrottler.Throttle("Firing off repair string"))
                {
                    IceLogging.Debug("Selecting repair from vendor", "[Self Repair Task]");
                    ECommons.Automation.Callback.Fire(iconString, true, 6);
                }
            }
            else
            {
                if (EzThrottler.Throttle("Attempting to target the repair NPC + Interact"))
                {
                    Utils.TargetgameObject(gameObject);
                    Utils.InteractWithObject(gameObject);
                }
            }

            return false;
        }
        public unsafe static bool OpenSelfRepair()
        {
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Repair", out var x) && GenericHelpers.IsAddonReady(x))
            {
                return true;
            }

            if (EzThrottler.Throttle("Opening Self Repair", 1000))
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 6);
            return false;
        }
        public unsafe static bool SelfRepair()
        {
            if (!PlayerHelper.NeedsRepair(C.RepairPercent))
            {
                return true;
            }
            else if (Svc.Condition[ConditionFlag.Mounted])
            {
                if (EzThrottler.Throttle("Attempting to dismount for repairing"))
                {
                    IceLogging.Debug("Dismounting for self repair", "[Self Repair Task]");
                    ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
                }
            }
            else if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectYesno", out var addon) && GenericHelpers.IsAddonReady(addon))
            {
                // 這裡刻意保留原本的 Callback.Fire（不改成 SelectYesno.Yes()）—— 閘門只負責「准不准按」，
                // 真正按下去的方式維持原樣，免得順手換掉一條已經在出貨中驗過的路徑。
                if (FrameThrottler.Throttle("SelectYesnoThrottle", 300) && YesnoGuard.ShouldConfirm(YesnoSituation.Repair))
                {
                    IceLogging.Debug("SelectYesno Callback", "Self Repair Task");
                    ECommons.Automation.Callback.Fire(addon, true, 0);
                }
            }
            else if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Repair", out var addon2) && GenericHelpers.IsAddonReady(addon2))
            {
                if (FrameThrottler.Throttle("Firing off repair request", 300))
                {
                    IceLogging.Debug("Repair Callback", "[Self Repair Task]");
                    ECommons.Automation.Callback.Fire(addon2, true, 0);
                }
            }
            return false;
        }
        public unsafe static bool CloseRepair()
        {
            if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var Yesno) && Yesno.IsAddonReady)
            {
                if (FrameThrottler.Throttle("Closing surprise repair window"))
                {
                    ECommons.Automation.Callback.Fire(Yesno.Base, true, -1);
                }
            }
            else if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Repair", out var repairWindow))
            {
                if (GenericHelpers.IsAddonReady(repairWindow))
                {
                    if (EzThrottler.Throttle("Attempting to close out the repair window", 300))
                    {
                        IceLogging.Debug("Closing the repair window", "[Repair Task]");
                        ECommons.Automation.Callback.Fire(repairWindow, true, -1);
                    }
                }
                else
                {
                    return true;
                }
            }

            return false;
        }
    }
}
