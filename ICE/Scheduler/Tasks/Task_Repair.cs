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
            }

            // 🔴 這一行原本在 if (NeedsRepair) 裡面，所以「進來時已經不需要修理」＝什麼都沒排，
            //    而 Tick 只在佇列排空時才呼叫 Enqueue()，狀態又還停在 Repair
            //    → 下一個 tick 再進來、再什麼都沒排 → **永遠卡在 Repair 狀態，而且完全沒有 log**。
            //    觸發條件不只上面註解講的「自我修理逾時把佇列清掉」那一種：
            //    Task_CheckState 判定要修理、到這裡 Enqueue 之間只要有別的外掛
            //    （AutoRetainer／Deliveroo／使用者自己按修理）先把裝備修好，就會落進同一個死結。
            //    這個離開狀態的動作與「要不要修理」無關，所以必須在 if 外面無條件排。
            //    （移出來也不會多做事：狀態機本來就是「修理這一段結束 → 回去領任務」。）
            P.TaskManager.Enqueue(() => SchedulerMain.State = IceState.GrabMission);
        }
        public static unsafe bool? HubCheck()
        {
            // 各月面的據點中心（世界座標）。座標取自 cycleapple api13-tw `65a5806`。
            //
            // 舊寫法只在 Phaenna 設過中心點，Sinus Ardorum 那一邊 HubCenter 留在 Vector2.Zero，
            // 也就是「離世界原點 45 公尺內」。那在 1237 剛好會動——真正的據點中心是
            // (2.84, -0.06)，離原點不到 3 公尺，整個判定圈只偏了 3 公尺 ——
            // 但它是**碰巧成立**，不是有意的，而且對 1237/1291 以外的任何區域都是錯的。
            //
            // ⚠️ 舊寫法還有一行 `PlayerPos = new Vector2(Player.Position.Z, Player.Position.Z)`
            //    （Z 打了兩次，而且從頭到尾沒有被用到）——那是壞掉的死碼，一併移除。
            Vector3? hubCenter = Player.Territory switch
            {
                1237 => new Vector3(2.84f, 1.55f, -0.06f),   // 渴望灣 / Sinus Ardorum
                1291 => new Vector3(339.90f, 52.60f, -412.10f), // Phaenna（台服 7.20 尚未開放）
                _ => null,
            };

            // 🔴 刻意**不**照上游用 Vector3.Distance：那會把高度算進距離。
            //    這個判定要問的是「人在不在據點這一塊地上」，而玩家在據點上空飛行時
            //    高度差可以輕鬆超過 45 —— 用三維距離就會在人明明在據點正上方時判成
            //    「不在據點」，然後開始重複施放「返回月面基地」。
            //    ECommons 的 Player.DistanceTo(Vector2) 走的是 Position.ToVector2() = (X, Z)，
            //    也就是水平距離，正是舊寫法一直在用的語意，保持不變。
            if (hubCenter is not { } center)
            {
                // 不認得的區域＝算不出據點在哪。這裡**故意 fail-open**（當成已經在據點內）：
                // 判 false 會讓這個步驟一直重試並持續施放返回動作，而 ICE 在這個檔裡
                // 已經有兩次「狀態機無聲卡死」的前科。下游的 PathToRepair 自己有
                // TryGetMoonNpc 守衛，走到那裡會乾淨地 AbortToStateCheck。
                // 📌 這一行寫 Information：使用者跑 LogLevel 2，Debug 收不到，
                //    而「我人不在月面卻在跑據點流程」正是需要被回報的狀況。
                if (EzThrottler.Throttle("ICE: hub check unknown territory", 5000))
                    IceLogging.Info($"目前區域 {Player.Territory} 沒有登記據點中心座標（可能已經被傳送離開月面），據點範圍檢查直接放行。", "[Vendor Repair Check]");
                return true;
            }

            if (Player.DistanceTo(new Vector2(center.X, center.Z)) < 45)
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

                // 確認框還在＝還沒收乾淨。下面的收尾出口改成 return true 之後，
                // 這個分支就必須自己明確回 false，否則按下取消的同一幀就會宣告完成。
                return false;
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

                    return false;
                }
                else
                {
                    return true;
                }
            }

            // 🔴 兩個視窗都已經不在了＝收尾其實已經做完。這裡原本回 false，等於
            //    「永遠不完成」，而這三步掛的是 Utils.TaskConfig
            //    （timeLimitMS = 30 分鐘、abortOnTimeout = false）——所以症狀不是報錯，
            //    是**整個佇列卡在這一步 30 分鐘**，逾時之後才靠 abortOnTimeout = false
            //    放行下一步。上面兩個分支的 return false 就是為了配這個出口而補的。
            return true;
        }
    }
}
