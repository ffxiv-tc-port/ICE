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
            ResetVisitState();
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

        #region 已學會的道具

        // 這一趟採購裡被放棄的道具。沒有這個集合的話，被「已經學會」確認框擋下來之後，
        // 下一輪 TryPurchaseItem 會再挑同一件 → 再送出 → 再被擋，變成無聲的無限迴圈
        // （BuyItems 是用 Utils.TaskConfig 排的：30 分鐘、逾時不中止）。
        private static readonly HashSet<uint> DeclinedThisVisit = [];

        // 這一趟已經記過 log 的確認框文字，避免同一段字每 500ms 洗一次。
        private static readonly HashSet<string> ReportedPrompts = [];

        // 連續按了幾次「否」都關不掉確認框。用來把「按鈕點不動」換成看得見的訊息。
        private static int DeclineAttempts;

        private static void ResetVisitState()
        {
            DeclinedThisVisit.Clear();
            ReportedPrompts.Clear();
            DeclineAttempts = 0;
        }

        /// <summary>
        /// 遊戲自己的「你已經學會這個了」確認框，拿來當比對基準的 Addon 列。
        /// </summary>
        /// <remarks>
        /// 台服 7.20 的 <c>Addon</c> 表逐列核對過（<c>exd-tc/7.20/Addon.csv</c>）：<br/>
        /// 4937「確定要購買嗎？／目前已經學會了該道具對應的技能。」（一般商店）<br/>
        /// 11501「確定要交換嗎？／目前已經學會了該道具對應的內容。」<b>← 兌換商店走這條</b><br/>
        /// 11506「確定要領取嗎？／目前已經學會了該道具對應的內容。」<br/>
        /// ⚠️ 不寫死中文字串：用遊戲自己的表就自動跟著客戶端語言走。<br/>
        /// ⚠️ 這一族的鄰居（2436／11502／11503「無法穿戴」、11510「漁師等級不足」）
        /// 開頭同樣是「確定要購買／交換嗎？」，所以比對必須用**整列**文字，
        /// 只比第一句會把它們一起擋掉。
        /// </remarks>
        private static readonly uint[] AlreadyLearnedAddonRows = [4937, 11501, 11506];

        private static string[] AlreadyLearnedMarkersCache;

        /// <summary>
        /// 比對基準：整列 Addon 文字去掉所有空白之後的樣子。
        /// </summary>
        /// <remarks>
        /// ⚠️ 為什麼要去掉空白：這些 Addon 列中間有換行，而執行期讀到的確認框文字裡那個換行
        /// 到底是 <c>\r</c>、<c>\n</c>、還是被 <c>GetText()</c> 當成非文字 payload 整個丟掉，
        /// **離線證明不了**。兩邊都去掉空白之後三種情況都對得上。<br/>
        /// 📌 這是 AutoRetainer <c>GcHandin/GCContinuation</c> 用在同一族確認框
        /// （Addon 2436／11502）上的同一套做法，不是新發明的。
        /// </remarks>
        private static string[] AlreadyLearnedMarkers
        {
            get
            {
                if (AlreadyLearnedMarkersCache != null)
                    return AlreadyLearnedMarkersCache;

                var markers = new List<string>();
                var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Addon>();
                if (sheet != null)
                {
                    foreach (var rowId in AlreadyLearnedAddonRows)
                    {
                        // 🔴 GetRow 查無此列會擲 ArgumentOutOfRangeException。
                        if (!sheet.TryGetRow(rowId, out var row))
                            continue;

                        var text = StripWhitespace(row.Text.GetText());

                        // 太短的字串拿去做「包含」比對必然誤判（例如整列只剩「確定要購買嗎？」）。
                        // 寧可少一筆基準，也不要多擋掉正常的購買確認。
                        if (text.Length < 12)
                            continue;

                        markers.Add(text);
                    }
                }

                AlreadyLearnedMarkersCache = [.. markers];

                IceLogging.Info(
                    AlreadyLearnedMarkersCache.Length == 0
                        ? $"遊戲資料裡讀不到可用的「已經學會」提示文字（Addon {string.Join("／", AlreadyLearnedAddonRows)}），「尊重遊戲的『已經學會』提示」這個選項不會有任何作用。"
                        : $"「已經學會」確認框的比對基準共 {AlreadyLearnedMarkersCache.Length} 筆：{string.Join(" / ", AlreadyLearnedMarkersCache)}",
                    Handle);

                return AlreadyLearnedMarkersCache;
            }
        }

        /// <summary>設定畫面用：這台客戶端的遊戲資料到底有沒有可以比對的提示文字。</summary>
        public static bool AlreadyLearnedPromptDetectable => AlreadyLearnedMarkers.Length > 0;

        private static string StripWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                // char.IsWhiteSpace 一併涵蓋全形空白（U+3000）與不斷行空白（U+00A0），
                // ECommons 的 Cleanup() 只處理半形四種。
                if (char.IsWhiteSpace(c))
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 安全地讀 SelectYesno 的提示文字。
        /// </summary>
        /// <remarks>
        /// 🔴 ECommons 的 <c>SelectYesno.Text</c> 是 <c>ReadSeString(&amp;Addon-&gt;PromptText-&gt;NodeText)</c>，
        /// <c>PromptText</c> 為 null 時會從 null 加偏移再去讀 —— 那是 AVE，<c>try/catch</c> 攔不到。
        /// 這條路徑每 500ms 會走一次，所以自己補判空。
        /// </remarks>
        private static unsafe string SafePromptText(SelectYesno master)
        {
            var addon = master.Addon;
            if (addon == null)
                return string.Empty;

            var prompt = addon->PromptText;
            if (prompt == null)
                return string.Empty;

            return GenericHelpers.ReadSeString(&prompt->NodeText).GetText();
        }

        private static bool IsAlreadyLearnedPrompt(SelectYesno master, out string promptText)
        {
            promptText = SafePromptText(master);

            var markers = AlreadyLearnedMarkers;
            if (markers.Length == 0 || string.IsNullOrWhiteSpace(promptText))
                return false;

            var normalized = StripWhitespace(promptText);
            foreach (var marker in markers)
            {
                if (normalized.Contains(marker, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 這一趟採購要不要跳過這件道具。
        /// </summary>
        /// <remarks>
        /// 兩個來源：<br/>
        /// ① 逐項的「已學會就不買」（<c>SkipIfUnlocked</c>）—— 樂譜／演技教材學會之後
        /// 就從背包消失，<c>KeepAmount</c> 永遠達不到，配上 <c>KeepBuying</c> 會一路買到點數見底。<br/>
        /// ② 這一趟已經被遊戲的「已經學會」確認框擋下來的道具。<br/>
        /// 🔴 只有**確定**已學會才跳過。問不到答案（<c>Unknown</c>）一律當作沒學會 ——
        /// 這裡誤判的代價是「該買的沒買」，而且完全靜默。
        /// </remarks>
        private static bool ShouldSkipItem(uint itemId, CosmoShoppingList item, bool log)
        {
            // ⚠️ 這裡刻意再看一次 C.HeedAlreadyLearnedPrompt，而不是只看集合裡有沒有：
            //    DeclinedThisVisit 只在 Enqueue() 清空，而 Enqueue() 又要先過
            //    CanPurchaseAnyItem() —— 兩邊都擋住的話，使用者把開關關掉之後
            //    這件道具會永遠解不開（要重載外掛才會恢復），而且完全沒有提示。
            if (C.HeedAlreadyLearnedPrompt && DeclinedThisVisit.Contains(itemId))
                return true;

            if (!item.SkipIfUnlocked)
                return false;

            if (PlayerHelper.GetItemUnlockState(itemId) != PlayerHelper.ItemUnlockState.Unlocked)
                return false;

            if (log && EzThrottler.Throttle($"ICE: cosmo shop already unlocked {itemId}", 60000))
                IceLogging.Info($"道具 {itemId} 的內容已經學會了，依這一項的「已學會就不買」設定跳過。", Handle);

            return true;
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
                // Talk 類（按一次翻一頁、窗不會因為被按而消失）：守衛逃生口 15 幀，走到是常態、寫 Debug。
                if (EzThrottler.Throttle("Clicking the credit npc talk dialog", 100)
                    && AddonPressGuard.TryBeginPress("兌換 NPC 對話：翻頁", "Talk", talk, "Click", AddonPressGuard.RoutineRePressEscapeFrames))
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

                        if (texts.Any(t => AddonPressGuard.IsTextCorrupt("SelectIconString", t)))
                        {
                            // 選單文字讀到 U+FFFD ＝ 視窗記憶體正在變動（多半是關閉中），這一幀不碰。
                        }
                        else
                        {
                            var index = PickCosmoShopEntry(texts);
                            // 選單一選即關：守衛擋下時這一幀不選，下一輪節流再來（與原本節流擋下同一條路徑）。
                            if (AddonPressGuard.TryBeginPress("宇宙商店：選擇商店選單", "SelectIconString", iconString, AddonPressGuard.BuildPressKey(true, index)))
                            {
                                IceLogging.Info(
                                    $"商店選單共 {entries.Length} 項（{string.Join(" / ", texts)}），選第 {index} 項「{texts[index]}」。",
                                    Handle);
                                entries[index].Select();
                            }
                        }
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
                // 🔴 守衛順序：IsHeld → 讀文字 → MayPress → 按。同一扇確認框按過（確定或拒買）而且還沒觀察到它
                //    收掉的那幾幀，連文字都不讀、也不消耗節流；被擋＝跳過整個區塊 → 落到方法尾的 return false，
                //    與節流擋下走的是同一條既有路徑（區塊內沒有 Tasks.Clear／State 轉移，只有 C.Save 與計數器）。
                //    逐件購買連續彈出的 SelectYesno 若重用同一位址，由守衛的 PreFinalize／PostSetup 解除點處理。
                if (!YesnoPressGuard.IsHeld(YesNo) && EzThrottler.Throttle("Buy Item", 500))
                {
                    // 🔴 上游對**任何** SelectYesno 一律按下確定，所以遊戲的「你已經學會這個了」
                    //    提示完全擋不住重複購買。開了設定才走這一段；預設關＝行為與上游相同。
                    if (C.HeedAlreadyLearnedPrompt)
                    {
                        var learned = IsAlreadyLearnedPrompt(YesNo, out var promptText);

                        // 讀到 U+FFFD ＝ 視窗記憶體正在變動（多半是關閉中），這一幀不碰。
                        if (AddonPressGuard.IsTextCorrupt("SelectYesno", promptText))
                            return false;

                        // 沒比對到的確認框文字也記一次（同一段字只記一次）。
                        // 這個選項唯一會失效的方式就是「台服實際跳出來的字跟 Addon 表對不上」，
                        // 而那件事離線證明不了 —— 這一行是事後唯一能定錨它的東西。
                        if (!learned && !string.IsNullOrWhiteSpace(promptText) && ReportedPrompts.Add(promptText))
                            IceLogging.Info($"購物確認框（不符合「已經學會」的比對基準，照常按下確定）：「{promptText}」", Handle);

                        if (learned)
                        {
                            if (ItemId != 0)
                                DeclinedThisVisit.Add(ItemId);

                            DeclineAttempts++;
                            if (DeclineAttempts == 1)
                                IceLogging.Info($"遊戲提示已經學會過這件道具（「{promptText}」），放棄購買道具 {ItemId}，這一趟採購不再挑它。", Handle);

                            // 「否」按鈕點不動時（ClickButtonIfEnabled 對停用的按鈕是空操作）
                            // 這裡會每 500ms 重來一次。BuyItems 是用 Utils.TaskConfig 排的
                            // （30 分鐘、逾時不中止），沒有這個上限就是三十分鐘的無聲迴圈。
                            if (DeclineAttempts > 20)
                            {
                                IceLogging.Info($"連續 {DeclineAttempts} 次都關不掉「已經學會」的確認框，中止這次購物並回到狀態判斷。", Handle);
                                SchedulerMain.AbortToStateCheck();
                                return true;
                            }

                            // 🔴 守衛放在最後（有副作用）。上面 IsHeld 已經確認這扇窗沒被擋，這裡照樣走守衛是為了
                            //    把「拒買」這一次按壓記下來 —— 同一扇窗之後不管哪個呼叫點再按都有依據。
                            if (YesnoPressGuard.MayPress("宇宙商店：拒買已學會的道具", YesNo))
                            {
                                YesNo.No();
                                BuyAmount = 0;
                                KeepAmount = 0;
                                ItemId = 0;
                            }
                            return false;
                        }
                    }

                    if (YesnoPressGuard.MayPress("宇宙商店：購買確認", YesNo))
                    {
                        DeclineAttempts = 0;
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

                // 學會狀態一律報出來（不管有沒有開逐項開關）：樂譜這類道具學會之後就從背包消失，
                // 只看「持有 0／保留 5」完全看不出「它其實已經學會了、再買也只是拿去賣」。
                var unlock = PlayerHelper.GetItemUnlockState(itemId) switch
                {
                    PlayerHelper.ItemUnlockState.Unlocked => "/已學會",
                    PlayerHelper.ItemUnlockState.Unknown => "/學會狀態不明",
                    _ => "",
                };

                wanted.Add($"{itemId}{(inShop ? "" : "(商店沒有)")}[持有 {have}/保留 {setting.KeepAmount}/指定買 {setting.BuyAmount}{(setting.KeepBuying ? "/一直買" : "")}{(setting.SkipIfUnlocked ? "/學會就不買" : "")}{unlock}{(DeclinedThisVisit.Contains(itemId) ? "/這趟已放棄" : "")}]");
            }

            IceLogging.Info(
                $"兌換視窗：持有點數 {currencyAmount}、AtkValue 總數 {shopExchange.AtkValueCount}、" +
                $"回報商品數 {shopExchange.NumEntries}、實際解析到 {items.Length} 件。" +
                $"採購清單：{(wanted.Count == 0 ? "（空）" : string.Join("；", wanted))}",
                Handle);
        }

        /// <summary>
        /// 依照傳進來的「這一輪要買幾個」規則掃一遍採購清單，找到就送出購買。
        /// </summary>
        /// <returns>
        /// <c>true</c>＝<b>這一輪找到了該買的東西，呼叫端不要再試下一種策略</b>；<br/>
        /// <c>false</c>＝這種策略在清單裡找不到任何該買、買得起、而且商店有賣的東西。
        /// </returns>
        /// <remarks>
        /// ⚠️ <b>回傳值的語意不是「有沒有真的送出購買」，而是「要不要停止往下試」。</b>
        /// 底下那個 <c>return true</c> 刻意寫在
        /// <c>EzThrottler.Throttle("Selecting Item to Buy")</c> 的 <c>if</c> <b>外面</b> ——
        /// 節流擋下來（這一輪沒真的送出購買）時<b>照樣回 <c>true</c></b>。
        /// 看起來像 bug，實際上是這個函式唯一正確的行為，理由如下。<br/>
        /// <br/>
        /// 🔴 <b>改成「節流擋下就回 false」會把整趟採購提早收掉</b>：三個呼叫端
        /// （BuyAmount／KeepAmount／KeepBuying）共用<b>同一把</b>節流 key
        /// <c>"Selecting Item to Buy"</c>，所以節流一旦擋下第一個呼叫，同一個 tick 裡
        /// 另外兩個必然也被擋下 ⇒ 三個都回 <c>false</c> ⇒ 流程直接落到
        /// 「採購清單裡沒有還需要買（且買得起、且商店有賣）的項目」那一行並 <c>return true</c>，
        /// <c>BuyItems</c> 這一步就此完成、接著 <c>CloseShop</c> 把兌換視窗關掉。
        /// 也就是說「這一輪剛好被節流擋住」會被誤判成「沒東西要買了」，
        /// 把一趟還沒買完的採購提早結束（而且可能還有一扇購買確認框沒處理）。<br/>
        /// <br/>
        /// 📌 <b>現行寫法沒有實害</b>：三個呼叫端拿到 <c>true</c> 一律是
        /// <c>return false</c>（＝「還在忙，下個 tick 再來」），
        /// 而「找到了、但這一輪被節流擋住」本來就該下個 tick 再來。
        /// 所以這裡<b>維持現狀</b>，只補這段說明，免得下一個人把它「修」成上面那個回歸。
        /// </remarks>
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

                if (ShouldSkipItem(itemId, item, log: true))
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

                // 兌換視窗按後不關（開出 SelectYesno），屬多次互動窗：逃生口 15 幀，只擋「同一件同數量在窗走完前再選一次」。
                // 守衛擋下時與節流擋下同形（跳過區塊、照舊 return true）。
                if (EzThrottler.Throttle("Selecting Item to Buy")
                    && AddonPressGuard.TryBeginPress("宇宙商店：選購道具", "ShopExchangeCurrency", shopExchange, $"Select|{itemId}|{buyAmount}", AddonPressGuard.RoutineRePressEscapeFrames))
                {
                    IceLogging.Info($"向商店送出購買：道具 {itemId} × {buyAmount}（單價 {shopExchangeItem.CostAmount}、持有點數 {currencyAmount}）。", Handle);
                    shopExchangeItem.Select(buyAmount);
                    setAmount(buyAmount);
                    ItemId = itemId;
                }

                // ⚠️ 這個 return true 在節流的 if 外面是**刻意的**，不是漏縮排。
                //    完整理由見本方法的 <remarks>：改成節流擋下時回 false，
                //    會讓三個共用同一把節流 key 的呼叫端在同一個 tick 全部回 false，
                //    被誤判成「沒東西要買了」而提早關掉兌換視窗。
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

                // ⚠️ 這裡不記 log：CanPurchaseAnyItem 也被採購設定分頁在繪製路徑上每幀呼叫。
                if (ShouldSkipItem(itemId, item, log: false))
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
