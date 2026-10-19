using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.NeoTaskManager.Tasks;
using ICE.Utilities.Cosmic_Helper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_Craft
    {
        public static void Enqueue()
        {
            if (P.Artisan.IsBusy())
            {
                // 等待 Artisan 完成製作必須帶 Utils.TaskConfig。NeoTaskManager 的預設是
                // TimeLimitMS = 30000 且 AbortOnTimeout = true —— 一個宇宙製作(尤其第二階段
                // 還要跑 Raphael 解算)實測要 77 秒,用預設值必定逾時並把整個佇列中止。
                P.TaskManager.Enqueue(() => WaitingForArtisan(), "Waiting for artisan to finish crafting", Utils.TaskConfig);
                P.TaskManager.Enqueue(() => Task_CheckScore.Crafts(), "Checking score");
            }
            else
            {
                P.TaskManager.Enqueue(() => Task_CheckScore.Enqueue(), "Checking Score");
                P.TaskManager.Enqueue(() => CheckMaterials(), "Checking materials", Utils.TaskConfig);
            }
        }

        private static bool? WaitingForArtisan()
        {
            if (!P.Artisan.IsBusy())
            {
                IceLogging.Info("Artisan is no longer running, continuing the process");

                // 🔴 「Artisan 不忙了」不等於「做出東西來了」：用背包實際數量判斷有沒有進展，
                //    連續失敗就退避，達上限就停下來並說明原因。
                CraftProgressGuard.OnArtisanStopped("[Task Craft: Waiting For Artisan]");
                if (CraftProgressGuard.LimitReached)
                {
                    CraftProgressGuard.ReportAndStop("[Task Craft: Waiting For Artisan]");
                    return true;
                }
                return true;
            }
            else
            {
                if (Svc.Condition[ConditionFlag.ExecutingCraftingAction])
                {
                    // Need to add a timer check here. Make it configuarable maybe... 10s?
                    // If the timer exceeds 10 seconds, then that means we're stuck in an animation lock
                    // then need to cancel them all and just force abandon lock failsafe
                }
                if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud))
                {
                    if (!AddonHelper.IsAddonActive("WKSMissionInfomation"))
                    {
                        if (EzThrottler.Throttle("Opening mission scoring info"))
                        {
                            moonHud.Mission();
                        }
                    }
                }
            }
            return false;
        }

        private static uint throttleCounter = 0;

        private static void InsertArtisanWait(ushort craftId, int amount, uint resultItemId,
                                              IReadOnlyDictionary<uint, int>? requiredItems = null)
        {
            // 記下「要求製作前的成品持有數量」，WaitingForArtisan 才有辦法判斷有沒有進展。
            CraftProgressGuard.Arm(craftId, resultItemId, requiredItems);

            // 這兩個任務要帶 Utils.TaskConfig（30 分鐘 + abortOnTimeout: false）：NeoTaskManager 的預設值是
            // TimeLimitMS = 30000、AbortOnTimeout = true，等 Artisan 製作必定逾時。
            P.TaskManager.InsertMulti(
                new(() => ThrottleArtisanTask(craftId, amount), "Telling artisan to craft", Utils.TaskConfig),
                new(() => WaitingForArtisan(), "Waiting for artisan", Utils.TaskConfig)
            );
        }

        private static bool? ThrottleArtisanTask(ushort craftId, int amount)
        {
            int delay = C.DelayCraft ? C.DelayCraftIncrease : 25;


            if (EzThrottler.Throttle("Waiting X Amount of seconds for artisan", delay))
            {
                throttleCounter += 1;
            }
            if (throttleCounter >= 2)
            {
                if (EzThrottler.Throttle("Artisan Crafting Task"))
                {
                    // 要使用者回報的診斷：這是「IPC 真的送出去了」的唯一證據，
                    // 上面那行 CheckMaterials 的「Telling artisan to craft」只是宣告要做，不代表真的呼叫了。
                    IceLogging.Info($"Artisan IPC CraftItem sent: recipe {craftId} x{amount}");
                    // 🔴 一定要排在 CraftItem 之前：Artisan 是在收到製作指令、
                    //    建立 CraftState 的那一刻才決定要用哪個求解器。
                    //    這一支是冪等的，而且任何失敗都只是「照原樣製作」。
                    CosmicSolverOverride.ApplyBeforeCraft(craftId, "[Task Craft: Throttle Artisan]");
                    P.Artisan.CraftItem(craftId, amount);
                }

                if (P.TaskManager.IsBusy)
                {
                    throttleCounter = 0;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 確認「再做 craftAmount 個」時每一種材料都夠。
        /// </summary>
        /// <remarks>
        /// RequiredItems 在 ICEDictornaryCreation 建表時就已經存了 AmountIngredient[k]
        /// (= 每做一個要幾個),所以這裡不必再查一次 Excel。
        /// </remarks>
        private static bool HasMaterialsFor(CosmicHelper.CraftingInfo info, int craftAmount, out string shortage)
        {
            shortage = string.Empty;
            if (info == null)
                return false;

            foreach (var material in info.RequiredItems)
            {
                if (material.Key == 0 || material.Value <= 0)
                    continue;

                var needed = material.Value * craftAmount;
                PlayerHelper.GetItemCount(material.Key, out var held);
                // ⚠️ 這裡讀出來的 held 只有在 PlayerHelper.InventoryReadable() 為真時才有意義；
                //    傳送／換區途中 InventoryManager 一律回 0。呼叫端 CheckMaterials() 已經在
                //    最上面擋掉那個狀態，所以這裡不重複檢查（重複檢查會讓「材料真的不夠」
                //    跟「現在讀不到」兩種情況混在同一個回傳值裡，更難查）。
                if (held < needed)
                {
                    shortage = $"item {material.Key} held {held}, need {needed} ({material.Value} per craft x{craftAmount})";
                    return false;
                }
            }

            return true;
        }

        private static bool? CheckMaterials()
        {
            const string handle = "[Task Craft: Check Materials]";

            // 🔴 這一整個方法有三條「材料不夠 → AbandonMission + Tasks.Clear()」的破壞性出口
            //    （moonCrate 那條 else、foreach 裡的 HasMaterialsFor 失敗、以及最後的 moreCraft）。
            //    傳送／換區途中 InventoryManager.GetInventoryItemCount 一律回 0，
            //    也就是說「身上有材料」跟「現在讀不到材料」在這裡會得到一模一樣的結論：放棄任務。
            if (!PlayerHelper.InventoryReadable())
            {
                if (EzThrottler.Throttle("ICE: craft inventory unreadable log", 5000))
                    IceLogging.Info("玩家目前處於傳送／讀取中，暫停材料檢查（此時道具數量讀出來會全是 0，" +
                                    "會被誤判成「材料不夠」而放棄任務）。", handle);
                return false;
            }

            // 前一次要求製作完全沒有進展時的退避期。不加這個的話，重試節奏是實機量到的
            // 每 1.35 秒一輪 —— 對一個已經確定不會成功的動作猛敲，只會把 log 灌爆。
            if (!CraftProgressGuard.MayAttemptNow(out var waitSeconds))
            {
                if (EzThrottler.Throttle("ICE: craft backoff log", 5000))
                    IceLogging.Info(
                        $"上一次要求 Artisan 製作沒有任何進展（連續第 {CraftProgressGuard.ConsecutiveFailures} 次），"
                        + $"退避中，還要等 {waitSeconds:0.0} 秒。", handle);
                return false;
            }

            // SheetMissionDict 沒有 key 0，而遊戲端取消任務時 CurrentLunarMission 就是 0 ——
            // 直接索引的例外在任務裡只會表現成「卡住不動」。
            if (SchedulerMain.CurrentMissionUnavailable(handle, out var mission))
                return true;

            var id = CosmicHelper.CurrentLunarMission;

            if (!P.Artisan.IsBusy())
            {
                if (mission.Crafts_Pre.Count > 0)
                {
                    // Mission has pre-crafts that are required. 
                    // Checking to see if you have enough pre-crafts first
                    var preCraft = mission.Crafts_Pre.FirstOrDefault();
                    var mainCraft = mission.Crafts_Main.FirstOrDefault();

                    var preItemId = preCraft.Value.ItemId;
                    var mainItemId = mainCraft.Value.ItemId;

                    PlayerHelper.GetItemCount(preCraft.Value.ItemId, out var preItemAmount);
                    PlayerHelper.GetItemCount(preCraft.Value.RequiredItems.FirstOrDefault().Key, out var moonCrateCount);
                    PlayerHelper.GetItemCount(mainCraft.Value.ItemId, out var mainItemCount);

                    if (preItemAmount >= mainCraft.Value.RequiredItems[preItemId])
                    {
                        IceLogging.Info($"Required pre-Item count: {mainCraft.Value.RequiredItems[preItemId]} | amount necessary: {preItemAmount}");

                        // There's enough items to craft the mainhand. Telling it to craft it instead. 
                        if (mainItemCount < mainCraft.Value.RequiredAmount)
                        {
                            // you don't have enough of the pre-crafts to craft the main item. 
                            // going to tell artisan to just kick it into gear
                            var craftAmount = mainCraft.Value.RequiredAmount - mainItemCount;
                            InsertArtisanWait(mainCraft.Key, craftAmount, mainCraft.Value.ItemId, mainCraft.Value.RequiredItems);
                            IceLogging.Info($"Telling artisan to craft: {mainCraft.Value.ItemId} -> {craftAmount}", "[Task Craft: Check Materials]");
                            return true;
                        }
                        else
                        {
                            // you have enough of the main hand item. But you still are crafting. So time to just craft 1 more
                            // 不要在這裡另外呼叫 P.Artisan.CraftItem：InsertArtisanWait 排的 ThrottleArtisanTask 本身就會送 CraftItem IPC。
                            InsertArtisanWait(mainCraft.Key, 1, mainCraft.Value.ItemId, mainCraft.Value.RequiredItems);
                            IceLogging.Info($"Current item count of: {mainCraft.Value.ItemId} | {mainItemCount}");
                            IceLogging.Info($"Telling artisan to craft: {mainCraft.Value.ItemId} -> 1", "[Task Craft: Check Materials]");
                            return true;
                        }

                    }
                    else if (moonCrateCount >= preCraft.Value.RequiredAmount)
                    {
                        // You should have enough to make this pre-craft. Initiating the thing now.
                        var craftAmount = preCraft.Value.RequiredAmount - preItemAmount;

                        if (mainCraft.Value.RequiredAmount > 1 && mainItemCount == 0)
                        {
                            craftAmount = mainCraft.Value.RequiredAmount * (preCraft.Value.RequiredAmount - preItemAmount);
                        }
                        if (craftAmount < 1)
                            craftAmount = 1;

                        // InsertArtisanWait 已經會送 CraftItem IPC，不要在這裡再送一次。
                        InsertArtisanWait(preCraft.Key, craftAmount, preCraft.Value.ItemId, preCraft.Value.RequiredItems);
                        IceLogging.Info($"Found a material that still needed to be crafted", "[Task Craft: Check Materials]");
                        return true;
                    }
                    else
                    {
                        IceLogging.Info($"Somehow, out of mats. Need to exit. And either attempt to turnin, or just straight up abandon.", "[Task Craft: Check Materials]");
                        SchedulerMain.State = IceState.AbandonMission;
                        P.TaskManager.Tasks.Clear();
                        return true;
                    }
                }
                else
                {
                    // This is the case when you need multiple items, or even just a single item.

                    // 每次做決定時把「所有目標」的狀態印出來（要使用者回報的診斷）：只印被選中的那一個時，
                    // 「為什麼跳過前面那些」在 log 裡完全看不出來。連 recipe id 一起印，才對得上 Artisan 那邊印的 recipe 編號。
                    IceLogging.Info(
                        $"Mission {id} craft objectives -> " + string.Join(" | ", mission.Crafts_Main.Select(c =>
                        {
                            PlayerHelper.GetItemCount(c.Value.ItemId, out var held);
                            return $"recipe {c.Key} item {c.Value.ItemId} held {held}/{c.Value.RequiredAmount}";
                        })), "[Craft: No Pre-Mats]");

                    foreach (var craft in mission.Crafts_Main)
                    {
                        PlayerHelper.GetItemCount(craft.Value.ItemId, out var reqAmount);
                        if (reqAmount < craft.Value.RequiredAmount)
                        {
                            // If you need less than what is necessary, this should change the count to be proper
                            reqAmount = craft.Value.RequiredAmount - reqAmount;

                            // Found an item that needs to be crafted. Time to check if you have enough of the material
                            if (HasMaterialsFor(craft.Value, reqAmount, out var shortage))
                            {
                                InsertArtisanWait(craft.Key, reqAmount, craft.Value.ItemId, craft.Value.RequiredItems);
                                IceLogging.Info($"Telling artisan to craft: recipe {craft.Key} (item {craft.Value.ItemId}) -> {reqAmount}", "[Craft: No Pre-Mats]");
                                return true;
                            }
                            else
                            {
                                // You don't have enough to craft this for the mission. Exiting out and checking for score/force abandon
                                IceLogging.Info($"Out of materials for recipe {craft.Key} (item {craft.Value.ItemId}): {shortage}. Going to abandon the mission now", "[Crafts: No Pre-Mats]");
                                SchedulerMain.State = IceState.AbandonMission;
                                P.TaskManager.Tasks.Clear();
                                return true;
                            }
                        }
                    }

                    var moreCraft = mission.Crafts_Main.FirstOrDefault();
                    // If you've gotten this far, that means you still need scoring. Just going to queue up the first mission (if possible)
                    // If you need less than what is necessary, this should change the count to be proper
                    var AdditionalItem = 1;

                    // Found an item that needs to be crafted. Time to check if you have enough of the material
                    if (HasMaterialsFor(moreCraft.Value, AdditionalItem, out var moreShortage))
                    {
                        InsertArtisanWait(moreCraft.Key, AdditionalItem, moreCraft.Value.ItemId, moreCraft.Value.RequiredItems);
                        IceLogging.Info($"Telling artisan to craft: recipe {moreCraft.Key} (item {moreCraft.Value.ItemId}) -> {AdditionalItem}", "[Craft: No Pre-Mats]");
                        return true;
                    }
                    else
                    {
                        // You don't have enough to craft this for the mission. Exiting out and checking for score/force abandon
                        SchedulerMain.State = IceState.AbandonMission;
                        IceLogging.Info($"Out of materials for recipe {moreCraft.Key} (item {moreCraft.Value.ItemId}): {moreShortage}. Going to abandon the mission now", "[Crafts: No Pre-Mats]");
                        P.TaskManager.Tasks.Clear();
                        return true;
                    }

                }
            }
            else
            {
                if (EzThrottler.Throttle("Artisan Busy Log", 3000))
                {
                    IceLogging.Debug("Artisan is currently busy... so we're properly waiting for it to finish");
                }
            }

            return false;
        }
    }
}
