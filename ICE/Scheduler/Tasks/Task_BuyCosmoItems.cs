using ECommons.GameHelpers;
using ICE.Utilities.AddonMasters;
using ICE.Config;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_BuyCosmoItems
    {
        public static void Enqueue()
        {
            P.TaskManager.EnqueueMulti
                (
                    new(PathToCreditVendor, "Pathing to the credit vendor"),
                    new(TalkToCreditNPC, "Talking to the credit NPC to start the buying process"),
                    new(SelectShop, "Selecting the shop entry we want to go to"),
                    new(BuyItems, "Buying items from the vendor", Utils.TaskConfig),
                    new(CloseShop, "Closing the shop menu")
                );
        }

        private static bool? PathToCreditVendor()
        {
            var zoneId = Player.Territory;
            // ✅ 這裡曾經是零守衛的字典索引，已修：守衛＝下一行的 NpcData.TryGetMoonNpc。
            // 原因留存：MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是
            // Player.Territory —— 佇列排好之後玩家還是可能被傳送走（機甲行動抽中駕駛員就會），
            // 下一個 tick 讀到的區域就不是月面了。修之前的 .First()/.FirstOrDefault() 兩種寫法
            // 都沒處理「找不到」，一個丟 InvalidOperationException、一個回 null 再 NRE。
            if (!NpcData.TryGetMoonNpc(zoneId, NpcData.NpcType.Credit, out var npcEntry))
            {
                if (EzThrottler.Throttle("ICE: moon npc missing Credit", 5000))
                    IceLogging.Info($"目前區域 {zoneId} 沒有登記兌換 NPC 的資料（可能已經被傳送離開月面），中止這一步。", "[ICE]");
                SchedulerMain.AbortToStateCheck();
                return true;
            }

            if (Player.DistanceTo(npcEntry.NpcLocation) <= 6.75f)
            {
                if (P.Navmesh.Installed)
                {
                    if (P.Navmesh.IsReady())
                    {
                        if (P.Navmesh.IsRunning())
                        {
                            if (Player.DistanceTo(npcEntry.NpcLocation) < 5)
                            {
                                IceLogging.Debug("Pathing to NPC has reached the distance thresh, stopping");
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
                        Utils.VnavBuildInfo();
                    }
                }
            }
            else
            {
                if (P.Navmesh.Installed)
                {
                    if (P.Navmesh.IsReady())
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
                    else
                    {
                        Utils.VnavBuildInfo();
                    }
                }
            }

            return false;
        }
        private static bool? TalkToCreditNPC()
        {
            if (GenericHelpers.TryGetAddonMaster<SelectIconString>("SelectIconString", out var iconString) && iconString.IsAddonReady)
            {
                IceLogging.Info("Icon string is visible! Time to shop");
                return true;
            }
            else
            {
                // ✅ 這裡曾經是零守衛的字典索引，已修：守衛＝下一行的 NpcData.TryGetMoonNpc。
                // 原因留存：MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是
                // Player.Territory —— 佇列排好之後玩家還是可能被傳送走（機甲行動抽中駕駛員就會），
                // 下一個 tick 讀到的區域就不是月面了。修之前的 .First()/.FirstOrDefault() 兩種寫法
                // 都沒處理「找不到」，一個丟 InvalidOperationException、一個回 null 再 NRE。
                if (!NpcData.TryGetMoonNpc(Player.Territory, NpcData.NpcType.Credit, out var npcEntry))
                {
                    if (EzThrottler.Throttle("ICE: moon npc missing Credit", 5000))
                        IceLogging.Info($"目前區域 {Player.Territory} 沒有登記兌換 NPC 的資料（可能已經被傳送離開月面），中止這一步。", "[ICE]");
                    SchedulerMain.AbortToStateCheck();
                    return true;
                }
                Utils.TryGetNpcObject(npcEntry, out var researchNpc);
                if (EzThrottler.Throttle("Interacting with researchingway"))
                {
                    Utils.TargetgameObject(researchNpc);
                    Utils.InteractWithObject(researchNpc);
                }
            }

            return false;
        }
        private static bool? SelectShop()
        {
            // Something to consider here... there's 2 different shops. Probably going to need to add a check to see which one we're going to select because I *-know-* people are going to ask about it >.>
            // For now, just going to support the one shop
            if (GenericHelpers.TryGetAddonMaster<SelectIconString>("SelectIconString", out var iconString) && iconString.IsAddonReady)
            {
                if (EzThrottler.Throttle("Selecting Materia Selection"))
                {
                    var select = iconString.Entries[1];
                    IceLogging.Debug($"Selecting: {select.Text}");
                    select.Select();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<ShopExchangeCurrency>("ShopExchangeCurrency", out var shopExchange) && shopExchange.IsAddonReady)
            {
                return true;
            }

            return false;
        }
        private static bool? CloseShop()
        {
            if (GenericHelpers.TryGetAddonMaster<ShopExchangeCurrency>("ShopExchangeCurrency", out var shopExchange) && shopExchange.IsAddonReady)
            {
                if (EzThrottler.Throttle("Close Shop"))
                    GenericHandlers.FireCallback("ShopExchangeCurrency", true, -1);
                return false;
            }
            else
                return true;
        }

        private static int BuyAmount = 0;
        private static uint ItemId = 0;
        private static int KeepAmount = 0;

        private static bool? BuyItems()
        {
            if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var YesNo) && YesNo.IsAddonReady)
            {
                if (EzThrottler.Throttle("Buy Item", 500))
                {
                    YesNo.Yes();
                    if (BuyAmount != 0)
                    {
                        if (C.CosmoShopping.TryGetValue(ItemId, out var config))
                        {
                            config.BuyAmount -= BuyAmount;
                            if (config.BuyAmount <= 0)
                                config.BuyAmount = 0;
                            C.Save();
                        }
                        BuyAmount = 0;
                        KeepAmount = 0;
                    }
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<ShopExchangeCurrency>("ShopExchangeCurrency", out var shopExchange) && shopExchange.IsAddonReady)
            {
                var currencyAmount = shopExchange.CurrencyAmount;


                // Try BuyAmount first
                if (TryPurchaseItem(shopExchange, currencyAmount,
                    (item, itemId) => item.BuyAmount,
                    (amount) => BuyAmount = amount))
                    return false;

                // Then try KeepAmount (accounting for what player already has)
                if (TryPurchaseItem(shopExchange, currencyAmount, 
                    (item, itemId) =>
                    {
                        PlayerHelper.GetItemCount(itemId, out int currentCount);
                        return Math.Max(0, item.KeepAmount - currentCount);
                    },
                    (amount) => KeepAmount = amount))
                    return false;

                // Finally try KeepBuying (buy max affordable)
                if (TryPurchaseItem(shopExchange, currencyAmount,
                    (item, itemId) => item.KeepBuying ? int.MaxValue : 0,
                    (amount) => KeepAmount = amount))
                    return false;

                return true;
            }

            return false;
        }

        private static bool TryPurchaseItem(
            ShopExchangeCurrency shopExchange,
            uint currencyAmount,
            Func<CosmoShoppingList, uint, int> getTargetAmount,
            Action<int> setAmount)
        {
            foreach (var itemId in C.CosmoShoppingOrder)
            {
                if (!C.CosmoShopping.TryGetValue(itemId, out var item))
                    continue;

                int targetAmount = getTargetAmount(item, itemId);
                if (targetAmount <= 0)
                    continue;

                var shopExchangeItem = shopExchange.BasicShopItems.FirstOrDefault(x => x.ItemId == itemId);
                if (shopExchangeItem == null)
                    continue;

                int maxAffordable = (int)(currencyAmount / shopExchangeItem.CostAmount);
                if (maxAffordable <= 0)
                    continue;

                int buyAmount = Math.Min(maxAffordable, targetAmount);
                if (buyAmount > 99) buyAmount = 99;

                if (EzThrottler.Throttle("Selecting Item to Buy"))
                {
                    shopExchangeItem.Select(buyAmount);
                    setAmount(buyAmount);
                    ItemId = itemId;
                }
                return true;
            }
            return false;
        }

        public static bool CanPurchaseAnyItem()
        {
            // Get current currency amount (you'll need to determine how to get this without the shop window)

            PlayerHelper.GetItemCount(45690, out var currencyAmount);

            // Try BuyAmount first
            if (CanPurchaseItem(currencyAmount,
                (item, itemId) => item.BuyAmount))
                return true;

            // Then try KeepAmount (accounting for what player already has)
            if (CanPurchaseItem(currencyAmount,
                (item, itemId) =>
                {
                    PlayerHelper.GetItemCount(itemId, out int currentCount);
                    return Math.Max(0, item.KeepAmount - currentCount);
                }))
                return true;

            // Finally try KeepBuying (buy max affordable)
            if (CanPurchaseItem(currencyAmount,
                (item, itemId) => item.KeepBuying ? int.MaxValue : 0))
                return true;

            // Nothing can be purchased
            return false;
        }

        private static bool CanPurchaseItem(int currencyAmount, Func<CosmoShoppingList, uint, int> getTargetAmount)
        {
            foreach (var itemId in C.CosmoShoppingOrder)
            {
                if (!C.CosmoShopping.TryGetValue(itemId, out var item))
                    continue;

                int targetAmount = getTargetAmount(item, itemId);
                if (targetAmount <= 0)
                    continue;

                // Check if item exists in the cosmocredit shop dictionary
                if (!Shop_Cosmocredits.CosmocreditShop.TryGetValue(itemId, out var shopItem))
                    continue;

                int maxAffordable = (int)(currencyAmount / shopItem.Cost);
                if (maxAffordable <= 0)
                    continue;

                // We can afford to buy at least one of this item
                return true;
            }
            return false;
        }
    }
}
