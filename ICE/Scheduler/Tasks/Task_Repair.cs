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

                if (!SelfRepairGaveUp && ((C.SelfRepairGather && CosmicHelper.GatheringJobList.Contains(currentJob)) || (C.SelfRepairCrafter && CosmicHelper.CrafterJobList.Contains(currentJob))))
                {
                    // 🔴 修一整套裝備要一件一件跑動畫，超過 30 秒很正常。用 NeoTaskManager 的
                    //    預設值（30 秒 + AbortOnTimeout）逾時就會把整個佇列清掉 ——
                    //    連同下面那一行「把狀態切回 GrabMission」也一起被清掉。
                    //    而狀態還停在 Repair，Tick 會再呼叫一次 Enqueue()，這時 NeedsRepair 已經
                    //    是 false，if 整段不執行 → 什麼都沒排 → **永遠停在 Repair 狀態**。
                    P.TaskManager.EnqueueMulti
                    (
                        new(OpenSelfRepair, "Opening the self repair window", Utils.TaskConfig),
                        new(SelfRepair, "Executing the self repair", Utils.TaskConfig),
                        new(CloseRepair, "Closing Self Repair", Utils.TaskConfig)
                    );
                }
            }

            // 🔴 這一行必須在 if (NeedsRepair) 外面無條件排：Tick 只在佇列排空時才呼叫 Enqueue()，「進來時已經不需要修理」
            //    （自我修理逾時把佇列清掉、或別的外掛／使用者先把裝備修好）又什麼都沒排的話，狀態就永遠卡在 Repair，
            //    而且完全沒有 log。這個離開狀態的動作與「要不要修理」無關。
            P.TaskManager.Enqueue(() => SchedulerMain.State = IceState.GrabMission);
        }

        /// <summary>這一輪自動化裡已放棄自行修理（缺暗物質、修理等級不夠等）。<see cref="ResetSelfRepairState"/> 歸零。</summary>
        internal static bool SelfRepairGaveUp;

        /// <summary>這次自行修理開始嘗試的時刻（<see cref="Environment.TickCount64"/>）；0＝沒在嘗試。跨佇列中止累計，
        /// 但兩次判定隔超過 <see cref="SelfRepairGapResetMs"/> 就當成新的一次重新計時。</summary>
        private static long selfRepairSince;

        /// <summary>上一次 <see cref="SelfRepairExpired"/> 被呼叫的時刻。</summary>
        private static long selfRepairLastCheck;

        private const long SelfRepairGiveUpMs = 120_000;

        // 嘗試中每幀都會判定；隔這麼久沒判定＝上一次已經結束（例如裝備在別處修好、佇列被清掉）。
        private const long SelfRepairGapResetMs = 60_000;

        internal static void ResetSelfRepairState()
        {
            SelfRepairGaveUp = false;
            selfRepairSince = 0;
        }

        /// <returns>true＝這次自行修理該放棄（第一次判定時寫一行 Information）。</returns>
        private static bool SelfRepairExpired(string step)
        {
            if (SelfRepairGaveUp)
                return true;

            var now = Environment.TickCount64;
            if (selfRepairSince != 0 && now - selfRepairLastCheck > SelfRepairGapResetMs)
                selfRepairSince = 0;
            selfRepairLastCheck = now;
            if (selfRepairSince == 0)
            {
                selfRepairSince = now;
                return false;
            }
            if (now - selfRepairSince < SelfRepairGiveUpMs)
                return false;

            SelfRepairGaveUp = true;
            IceLogging.Info($"自行修理嘗試了 {SelfRepairGiveUpMs / 1000} 秒仍未完成（停在「{step}」；常見原因：缺暗物質、修理等級不夠）。" +
                            "這一輪自動化不再自行修理；有開 NPC 修理的話改由 NPC 修理。重新開始自動化會再試一次。", "[Self Repair Task]");
            return true;
        }

        public static unsafe bool? HubCheck()
        {
            // 各月面的據點中心（世界座標）。不認得的區域回 null（見下面的 fail-open）。
            Vector3? hubCenter = Player.Territory switch
            {
                1237 => new Vector3(2.84f, 1.55f, -0.06f),   // 渴望灣 / Sinus Ardorum
                1291 => new Vector3(339.90f, 52.60f, -412.10f), // Phaenna（台服尚未開放）
                _ => null,
            };

            // 🔴 刻意**不**照上游用 Vector3.Distance：那會把高度算進距離。這個判定要問的是「人在不在據點這一塊地上」，
            //    玩家在據點上空飛行時高度差可以超過 45，三維距離會判成「不在據點」而重複施放「返回月面基地」。
            //    ECommons 的 Player.DistanceTo(Vector2) 走的是 Position.ToVector2() = (X, Z)，也就是水平距離。
            if (hubCenter is not { } center)
            {
                // 不認得的區域＝算不出據點在哪。這裡**故意 fail-open**（當成已經在據點內）：
                // 判 false 會讓這個步驟一直重試並持續施放返回動作。下游的 PathToRepair 自己有
                // TryGetMoonNpc 守衛，走到那裡會乾淨地 AbortToStateCheck。
                // 📌 要使用者回報的診斷：「人不在月面卻在跑據點流程」正是需要被回報的狀況。
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
            // MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是 Player.Territory —— 佇列排好之後玩家還是可能
            // 被傳送走（機甲行動抽中駕駛員就會），所以一定要走 NpcData.TryGetMoonNpc，不可以直接索引。
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
            // MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是 Player.Territory —— 佇列排好之後玩家還是可能
            // 被傳送走（機甲行動抽中駕駛員就會），所以一定要走 NpcData.TryGetMoonNpc，不可以直接索引。
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
                        // 🔴 YesnoPressGuard 一定要放在最後（它有副作用：記下這一次按壓）。
                        //    節流的 key 是「Saying yes to the gil」，與另外兩個按窗點各自獨立，
                        //    擋不住「同一扇窗被不同呼叫點接力重按」——見 YesnoPressGuard 的說明。
                        if (FrameThrottler.Throttle("Saying yes to the gil")
                            && YesnoGuard.ShouldConfirm(YesnoSituation.Repair)
                            && YesnoPressGuard.MayPress("委託修理：花費確認", (nint)Yesno.Base))
                            Yesno.Yes();
                    }
                    else if (EzThrottler.Throttle("Firing off repair request", 300)
                        && AddonPressGuard.TryBeginPress("委託修理：全部修理", "Repair", repair, "RepairAll", AddonPressGuard.RoutineRePressEscapeFrames))
                    {
                        // 修理視窗按「全部修理」後不關（開出 SelectYesno），屬多次互動窗：逃生口 15 幀。
                        IceLogging.Debug("Repair Callback", "[Self Repair Task]");
                        repair.RepairAll();
                    }
                }
            }
            else if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectIconString", out var iconString) && GenericHelpers.IsAddonReady(iconString))
            {
                // SelectIconString 一選即關：關閉中的幾幀 IsAddonReady 仍過而 Repair 還沒開起來，會再進這個分支；
                // 60 幀節流只擋本呼叫點自己，守衛認的是視窗位址＋參數組。擋下＝這一幀不選，照舊 return false。
                if (FrameThrottler.Throttle("Firing off repair string")
                    && PickRepairEntry(iconString) is var index and >= 0
                    && AddonPressGuard.TryBeginPress("委託修理：選擇修理選項", "SelectIconString", iconString, AddonPressGuard.BuildPressKey(true, index)))
                {
                    IceLogging.Debug($"Selecting repair from vendor (index {index})", "[Self Repair Task]");
                    ECommons.Automation.Callback.Fire(iconString, true, index);
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

        /// <summary>渴望灣修理 NPC 選單裡「修理」那一項的 CustomTalk 列（其 MainOption 就是選單文字）。</summary>
        private const uint RepairCustomTalkRow = 720915;

        /// <summary>舊版寫死的索引；選單文字對不到「修理」時沿用。</summary>
        private const int LegacyRepairEntryIndex = 6;

        /// <returns>要選的項目索引；-1＝文字正在變動（U+FFFD），這一幀不選。</returns>
        private static unsafe int PickRepairEntry(AtkUnitBase* addon)
        {
            var master = new SelectIconString(addon);
            var count = master.Entries.Length;
            var texts = new string[count];
            for (int i = 0; i < count; i++)
            {
                texts[i] = Task_BuyCosmoItems.SafeEntryText(master, i);
                if (AddonPressGuard.IsTextCorrupt("SelectIconString", texts[i]))
                    return -1;
            }

            var wanted = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.CustomTalk>().GetRowOrDefault(RepairCustomTalkRow)?.MainOption.ExtractText();
            var found = string.IsNullOrWhiteSpace(wanted) ? -1 : Array.FindIndex(texts, t => t == wanted);
            if (found >= 0)
                return found;

            if (EzThrottler.Throttle("ICE: repair entry text not found", 10000))
                IceLogging.Info($"修理 NPC 的選單裡找不到「{wanted}」（共 {count} 項：{string.Join(" / ", texts)}），沿用第 {LegacyRepairEntryIndex} 項。", "[Vendor Repair]");
            return LegacyRepairEntryIndex;
        }

        public unsafe static bool OpenSelfRepair()
        {
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Repair", out var x) && GenericHelpers.IsAddonReady(x))
            {
                return true;
            }

            if (SelfRepairExpired("開啟修理視窗"))
                return true;

            if (EzThrottler.Throttle("Opening Self Repair", 1000))
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 6);
            return false;
        }
        public unsafe static bool SelfRepair()
        {
            if (!PlayerHelper.NeedsRepair(C.RepairPercent))
            {
                selfRepairSince = 0;
                return true;
            }
            else if (SelfRepairExpired("全部修理"))
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
                // 這裡刻意保留原本的 Callback.Fire（不改成 SelectYesno.Yes()）—— 閘門只負責「准不准按」，按下去的方式維持原樣。
                // 🔴 300 幀的節流擋的只有「本呼叫點自己」重按：按下確定之後這一步就完成了，下一步 CloseRepair 用另一把
                //    從沒用過的 key，對同一扇還在關閉中的窗會立刻再送一次 callback —— 那就是原生 AccessViolation。
                //    跨呼叫點的重按只有 YesnoPressGuard 擋得住。
                if (FrameThrottler.Throttle("SelectYesnoThrottle", 300)
                    && YesnoGuard.ShouldConfirm(YesnoSituation.Repair)
                    && YesnoPressGuard.MayPress("自行修理：花費確認", (nint)addon))
                {
                    IceLogging.Debug("SelectYesno Callback", "Self Repair Task");
                    ECommons.Automation.Callback.Fire(addon, true, 0);
                }
            }
            else if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Repair", out var addon2) && GenericHelpers.IsAddonReady(addon2))
            {
                // 修理視窗送 0（全部修理）後不關（開出 SelectYesno），屬多次互動窗：逃生口 15 幀。
                if (FrameThrottler.Throttle("Firing off repair request", 300)
                    && AddonPressGuard.TryBeginPress("自行修理：全部修理", "Repair", addon2, AddonPressGuard.BuildPressKey(true, 0), AddonPressGuard.RoutineRePressEscapeFrames))
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
                // 🔴 這裡是那條崩潰路徑的下游端：上一步 SelfRepair 才剛按下確定，
                //    這把 key 是全新的（FrameThrottler 對沒見過的 key 第一次一律放行），
                //    所以會在同一扇窗「正在關閉中」的那幾幀立刻再送一次 callback。
                //    YesnoPressGuard 認的是視窗位址而不是 key，才擋得住這種接力重按。
                if (FrameThrottler.Throttle("Closing surprise repair window")
                    && YesnoPressGuard.MayPress("修理收尾：關閉殘留的確認框", (nint)Yesno.Base))
                {
                    ECommons.Automation.Callback.Fire(Yesno.Base, true, -1);
                }

                // 確認框還在＝還沒收乾淨。下面的收尾出口是 return true，
                // 所以這個分支必須自己明確回 false，否則按下取消的同一幀就會宣告完成。
                return false;
            }
            else if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Repair", out var repairWindow))
            {
                if (GenericHelpers.IsAddonReady(repairWindow))
                {
                    // -1 是關窗：關閉中的那幾幀 IsAddonReady 仍過、本步每幀重跑，300ms 節流不是防護。
                    // 擋下＝這一幀不送，照舊 return false；窗真的不見了由下面的 return true 收工。
                    if (EzThrottler.Throttle("Attempting to close out the repair window", 300)
                        && AddonPressGuard.TryBeginPress("修理收尾：關閉修理視窗", "Repair", repairWindow, AddonPressGuard.BuildPressKey(true, -1)))
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

            // 🔴 兩個視窗都已經不在了＝收尾已經做完，必須回 true：這三步掛的是 Utils.TaskConfig
            //    （timeLimitMS = 30 分鐘、abortOnTimeout = false），回 false 的症狀不是報錯，
            //    是**整個佇列卡在這一步 30 分鐘**。上面兩個分支的 return false 是配這個出口的。
            return true;
        }
    }
}
