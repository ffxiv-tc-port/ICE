using Dalamud.Memory;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
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
        private const string Handle = "[Task: Buy Cosmo Items]";

        public static void Enqueue()
        {
            ResetStepWait();
            P.TaskManager.EnqueueMulti
                (
                    new(PathToCreditVendor, "Pathing to the credit vendor"),
                    new(TalkToCreditNPC, "Talking to the credit NPC to start the buying process"),
                    new(SelectShop, "Selecting the shop entry we want to go to"),
                    new(BuyItems, "Buying items from the vendor", Utils.TaskConfig),
                    new(CloseShop, "Closing the shop menu")
                );
        }

        #region 卡住偵測

        // 🔴 TalkToCreditNPC／SelectShop 都是用 NeoTaskManager 的預設設定排進去的
        //    （TimeLimitMS=30000、AbortOnTimeout=true）。它們卡住時的失敗形式是：
        //    30 秒後整條佇列被 Abort() 清掉 —— 連 Task_HubActivities 結尾的
        //    ResetAll／走回製作點／State=GrabMission 一起消失。CosmoBuy 沒被清掉、
        //    State 還停在 HubReturn ⇒ SchedulerMain.Tick 又排一次 ⇒ 無聲無限迴圈。
        //    這一組計時器只做一件事：在預設逾時之前先留下一行 Information 級的訊息，
        //    說明「當下到底哪些視窗開著」，然後走既有的 AbortToStateCheck 出口。
        private const long StepWaitLimitMs = 20000;
        private static long StepWaitStart;

        private static void ResetStepWait() => StepWaitStart = 0;

        /// <summary>回 true 代表這一步等太久了（訊息已經記進 log），呼叫端應該收手。</summary>
        private static bool StepWaitExpired(string what)
        {
            if (StepWaitStart == 0)
            {
                StepWaitStart = Environment.TickCount64;
                return false;
            }

            if (Environment.TickCount64 - StepWaitStart < StepWaitLimitMs)
                return false;

            IceLogging.Info(
                $"{what} 等了 {StepWaitLimitMs / 1000} 秒仍然沒有進展，中止這次購物並回到狀態判斷。" +
                $"目前開著的相關視窗：{DescribeOpenAddons()}",
                Handle);
            ResetStepWait();
            return true;
        }

        // 卡住時要回報的視窗清單。名字寫在這裡而不是散在各處，是為了讓那一行 log 自己就足夠定位問題。
        private static readonly string[] WatchedAddons =
        [
            "SelectIconString", "SelectString", "Talk", "SelectYesno",
            "ShopExchangeCurrency", "ShopExchangeCurrencyDialog",
            "Shop", "InclusionShop", "ShopExchangeItem", "ShopExchangeItemDialog",
        ];

        private static unsafe string DescribeOpenAddons()
        {
            var open = new List<string>();
            foreach (var name in WatchedAddons)
            {
                if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var addon) || addon == null)
                    continue;
                open.Add(GenericHelpers.IsAddonReady(addon) ? name : $"{name}(未就緒)");
            }
            return open.Count == 0 ? "（沒有任何相關視窗）" : string.Join(", ", open);
        }

        #endregion

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
            // 🔴 台服真因（2026-08-07）：兌換 NPC（ENpcBase 1052588／1052600／1052607／1052608）
            //    掛了**兩個** SpecialShop —— 1770945「宇宙信用點數交易」（60 筆商品）與
            //    1770977（台服整列空白、零筆商品）。實機表現是互動之後遊戲**直接開兌換視窗**，
            //    完全不會出現 SelectIconString 選單。
            //    上游這一步只認 SelectIconString，於是在台服永遠回不了 true：
            //    人站在 NPC 前面、交易視窗開著、每 500ms 重送一次互動，不換頁也不購物
            //    —— 使用者回報的「卡在交易視窗」逐字就是這個狀態。
            //    實機 log 佐證（2026-08-07 16:30:43~16:31:11）：整段只有 Task_HubActivities 的
            //    「Starting Cosmo Buy task」與重複的 Target Game Object，
            //    "Icon string is visible! Time to shop" 在**所有**留存的 log 裡出現 0 次，
            //    而同一份 log 裡研究員 NPC 的 SelectIconString 是正常運作的（對照組成立）。
            if (GenericHelpers.TryGetAddonMaster<ShopExchangeCurrency>("ShopExchangeCurrency", out var directShop) && directShop.IsAddonReady)
            {
                if (EzThrottler.Throttle("ICE: credit shop opened directly", 10000))
                    IceLogging.Info("兌換視窗已經直接開啟（這個 NPC 沒有商店選單），略過選單步驟。", Handle);
                ResetStepWait();
                return true;
            }

            if (GenericHelpers.TryGetAddonMaster<SelectIconString>("SelectIconString", out var iconString) && iconString.IsAddonReady)
            {
                IceLogging.Info("Icon string is visible! Time to shop");
                ResetStepWait();
                return true;
            }

            // NPC 先跳一段對話的情況。Task_RelicTurnin.TalkToResearchWay 是同一套處理；
            // 沒有這一段的話，只要對話框擋著就等於永遠卡住。
            if (GenericHelpers.TryGetAddonMaster<Talk>("Talk", out var talk) && talk.IsAddonReady)
            {
                if (EzThrottler.Throttle("Clicking the credit npc talk dialog", 100))
                    talk.Click();
                return false;
            }

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

            if (!Utils.TryGetNpcObject(npcEntry, out var researchNpc) || researchNpc == null)
            {
                if (EzThrottler.Throttle("ICE: credit npc object missing", 5000))
                {
                    IceLogging.Warning(
                        $"找不到兌換 NPC（設定的 DataId：{string.Join(", ", npcEntry.AlternateNpcIds.Prepend(npcEntry.NpcId))}），" +
                        $"設定座標 {npcEntry.NpcLocation}。",
                        Handle);
                }
            }
            else if (EzThrottler.Throttle("Interacting with researchingway"))
            {
                Utils.TargetgameObject(researchNpc);
                Utils.InteractWithObject(researchNpc);
            }

            if (StepWaitExpired("與兌換 NPC 互動"))
            {
                SchedulerMain.AbortToStateCheck();
                return true;
            }

            return false;
        }

        private static bool? SelectShop()
        {
            // 兌換視窗已經開著就直接往下走（無論是選單選出來的，還是 NPC 直接開的）。
            if (GenericHelpers.TryGetAddonMaster<ShopExchangeCurrency>("ShopExchangeCurrency", out var shopExchange) && shopExchange.IsAddonReady)
            {
                ResetStepWait();
                return true;
            }

            if (GenericHelpers.TryGetAddonMaster<SelectIconString>("SelectIconString", out var iconString) && iconString.IsAddonReady)
            {
                if (EzThrottler.Throttle("Selecting Materia Selection"))
                {
                    var entries = iconString.Entries;
                    if (entries.Length == 0)
                    {
                        if (EzThrottler.Throttle("ICE: credit shop menu empty", 5000))
                            IceLogging.Info("NPC 的商店選單目前沒有任何項目。", Handle);
                    }
                    else
                    {
                        var texts = new string[entries.Length];
                        for (int i = 0; i < entries.Length; i++)
                            texts[i] = SafeEntryText(iconString, i);

                        var index = PickCosmoShopEntry(texts);
                        IceLogging.Info(
                            $"商店選單共 {entries.Length} 項（{string.Join(" / ", texts)}），選第 {index} 項「{texts[index]}」。",
                            Handle);
                        entries[index].Select();
                    }
                }
            }

            if (StepWaitExpired("等待兌換視窗開啟"))
            {
                SchedulerMain.AbortToStateCheck();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 在 NPC 的商店選單裡挑出「用宇宙點數付款」的那一項。
        /// </summary>
        /// <remarks>
        /// 🔴 上游寫死 <c>Entries[1]</c>（第二項）。台服兌換 NPC 的第二個 SpecialShop（1770977）
        /// 是空白佔位列，挑到它等於開一個空商店。改成用遊戲資料裡的商店名稱比對；
        /// 對不到才退回上游行為，而且先做邊界檢查（只有一項時挑 <c>Entries[1]</c> 會直接擲例外）。
        /// </remarks>
        private static int PickCosmoShopEntry(string[] entryTexts)
        {
            var names = Shop_Cosmocredits.CosmocreditShopNames;
            if (names.Count > 0)
            {
                for (int i = 0; i < entryTexts.Length; i++)
                {
                    var text = entryTexts[i];
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    foreach (var name in names)
                    {
                        if (string.IsNullOrWhiteSpace(name))
                            continue;
                        if (text == name || text.Contains(name) || name.Contains(text))
                            return i;
                    }
                }
            }

            // 對不到名稱：維持上游的「第二項」，但不越界。
            return entryTexts.Length > 1 ? 1 : 0;
        }

        /// <summary>
        /// 讀選單項目的文字。ECommons 的 <c>Entry.Text</c> 直接對 <c>EntryNames[Index]</c> 解參考、
        /// 不判空，而這裡會把**每一項**都讀一遍（上游只讀被選中的那一項），所以自己補上判空。
        /// 空指標解參考是 AVE，<c>try/catch</c> 攔不到。
        /// </summary>
        private static unsafe string SafeEntryText(SelectIconString master, int index)
        {
            var addon = master.Addon;
            if (addon == null)
                return string.Empty;

            var popup = &addon->PopupMenu.PopupMenu;
            if (popup->EntryNames == null)
                return string.Empty;
            if (index < 0 || index >= popup->EntryCount)
                return string.Empty;

            var ptr = popup->EntryNames[index].Value;
            if (ptr == null)
                return string.Empty;

            return MemoryHelper.ReadSeStringNullTerminated((nint)ptr).GetText();
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

                ReportShopContents(shopExchange, currencyAmount);

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

                if (EzThrottler.Throttle("ICE: cosmo shopping list done", 10000))
                    IceLogging.Info("採購清單裡沒有還需要買（且買得起、且商店有賣）的項目，關閉兌換視窗。", Handle);

                return true;
            }

            return false;
        }

        /// <summary>
        /// 兌換視窗打開之後把「外掛實際看到什麼」記一行。
        /// </summary>
        /// <remarks>
        /// 📌 <see cref="ShopExchangeCurrency"/> 的 AtkValue 索引（4／84／454／1064）是照國際服寫死的，
        /// 離線無法證明台服佈局相同。這一行 log 就是唯一能定錨它的東西：
        /// <c>AtkValue 總數</c> 太小、<c>NumEntries</c> 明顯不合理（台服商店是 39 件）、
        /// 或者「解析到 0 件」都代表索引在台服是錯的，而不是使用者設定的問題。
        /// 沒有它的話「沒有購物」與「商店讀成空的」在 log 上完全分不出來。
        /// </remarks>
        private static void ReportShopContents(ShopExchangeCurrency shopExchange, uint currencyAmount)
        {
            if (!EzThrottler.Throttle("ICE: cosmo shop contents", 10000))
                return;

            var items = shopExchange.BasicShopItems;
            var wanted = new List<string>();
            foreach (var itemId in C.CosmoShoppingOrder)
            {
                if (!C.CosmoShopping.TryGetValue(itemId, out var setting))
                    continue;
                PlayerHelper.GetItemCount(itemId, out var have);
                var inShop = items.Any(x => x.ItemId == itemId);
                wanted.Add($"{itemId}{(inShop ? "" : "(商店沒有)")}[持有 {have}/保留 {setting.KeepAmount}/指定買 {setting.BuyAmount}{(setting.KeepBuying ? "/一直買" : "")}]");
            }

            IceLogging.Info(
                $"兌換視窗：持有點數 {currencyAmount}、AtkValue 總數 {shopExchange.AtkValueCount}、" +
                $"回報商品數 {shopExchange.NumEntries}、實際解析到 {items.Length} 件。" +
                $"採購清單：{(wanted.Count == 0 ? "（空）" : string.Join("；", wanted))}",
                Handle);
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

                // 上游沒有防 0：CostAmount 讀成 0（AtkValue 索引在台服對不上就會這樣）
                // 會直接變成除以零。回 0 代表「買不起」，比整個佇列被例外清掉安全。
                if (shopExchangeItem.CostAmount == 0)
                {
                    if (EzThrottler.Throttle($"ICE: cosmo shop zero cost {itemId}", 10000))
                        IceLogging.Info($"商店回報道具 {itemId} 的單價是 0，跳過（多半代表 AtkValue 佈局對不上）。", Handle);
                    continue;
                }

                int maxAffordable = (int)(currencyAmount / shopExchangeItem.CostAmount);
                if (maxAffordable <= 0)
                    continue;

                int buyAmount = Math.Min(maxAffordable, targetAmount);
                if (buyAmount > 99) buyAmount = 99;

                if (EzThrottler.Throttle("Selecting Item to Buy"))
                {
                    IceLogging.Info($"向商店送出購買：道具 {itemId} × {buyAmount}（單價 {shopExchangeItem.CostAmount}、持有點數 {currencyAmount}）。", Handle);
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

                if (shopItem.Cost == 0)
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
