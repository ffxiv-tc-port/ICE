using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using ECommons;
using ICE.Config;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    internal class ShoppingTab
    {
        private static string ItemSearch = string.Empty;

        // 已回報過的無效採購項目，避免在繪製路徑上每幀重複寫 log。
        private static readonly HashSet<uint> ReportedBadShoppingItems = [];

        public static unsafe void Draw()
        {
            bool BuyItems = C.BuyItems;

            if (ImGui.Checkbox("Buy Items".Loc() + "###ICEBuyItems", ref BuyItems))
            {
                C.BuyItems = BuyItems;
                C.StopOnceHitCosmoCredits = false;
                C.Save();
            }

            int buyAtAmount = C.CosmoBuyAtAmount;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt("Go buy items when you reach".Loc() + "###ICECosmoBuyAtAmount", ref buyAtAmount, 1))
            {
                if (buyAtAmount < 0)
                    buyAtAmount = 0;
                if (buyAtAmount > 30000)
                    buyAtAmount = 30000;
                C.CosmoBuyAtAmount = buyAtAmount;
                C.Save();
            }

            DrawAlreadyLearnedPromptOption();

            CheckConfigState();
            if (Task_BuyCosmoItems.CanPurchaseAnyItem())
            {
                ImGui.Text("You can buy cosmocredit items from the list!".Loc());
            }
            else
            {
                ImGui.Text("You can't buy any items with your current credit value/items (tis fine, this just a test)".Loc());
            }

            if (ImGui.Button("Add Items to List".Loc() + "###ICEAddItemsToList"))
            {
                ImGui.OpenPopup("CosmocreditMateriaPopup");
            }

            ImGui.SetNextWindowSize(new Vector2(400, 0), ImGuiCond.Appearing);

            if (ImGui.BeginPopup("CosmocreditMateriaPopup"))
            {
                ImGui.SetNextItemWidth(380);
                ImGui.InputText("##Item Search", ref ItemSearch, 256);

                ImGui.Spacing();

                // Remove BeginChild and use table scrolling instead
                if (ImGui.BeginTable("Cosmo Materia Shop", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg, new Vector2(0, 250)))
                {
                    ImGui.TableSetupColumn("Icons", ImGuiTableColumnFlags.WidthFixed, 20);
                    ImGui.TableSetupColumn("Names", ImGuiTableColumnFlags.WidthStretch);

                    foreach (var item in Shop_Cosmocredits.CosmocreditShop)
                    {
                        var id = item.Key;
                        if (Svc.Data.GetExcelSheet<Item>().TryGetRow(id, out var itemInfo))
                        {
                            var name = itemInfo.Name.ToString();

                            if (!ItemSearch.IsNullOrWhitespace() && !name.ToLower().Contains(ItemSearch.ToLower()))
                            {
                                continue;
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.PushID(id);
                            if (itemInfo.Icon is { } itemIcon && Svc.Texture.TryGetFromGameIcon((int)itemIcon, out var texture))
                            {
                                ImGui.Image(texture.GetWrapOrEmpty().Handle, new Vector2(20, 20));
                            }
                            ImGui.TableNextColumn();
                            ImGui.Text($"{itemInfo.Name}");
                            if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                            {
                                AddItem(id);
                                C.Save();
                            }
                            ImGui.PopID();
                        }
                    }
                    ImGui.EndTable();
                }

                ImGui.EndPopup();
            }

            ImGui.Text("Order Count ??".Loc(C.CosmoShoppingOrder.Count));

            if (ImGui.BeginTable("Current Shopping List", 11, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
            {
                ImGui.TableSetupColumn("Up".Loc());
                ImGui.TableSetupColumn("Down".Loc());
                ImGui.TableSetupColumn("Name".Loc());
                ImGui.TableSetupColumn("Have".Loc());
                ImGui.TableSetupColumn("Cost".Loc());
                ImGui.TableSetupColumn("Kind".Loc());
                ImGui.TableSetupColumn("Keep".Loc());
                ImGui.TableSetupColumn("Buy".Loc());
                ImGui.TableSetupColumn("Keep Buying".Loc());
                ImGui.TableSetupColumn("Skip If Learned".Loc());
                ImGui.TableSetupColumn("Remove".Loc());

                ImGui.TableHeadersRow();

                for (int i = 0; i < C.CosmoShoppingOrder.Count; i++)
                {
                    uint itemId = C.CosmoShoppingOrder[i];

                    // CosmoShoppingOrder／CosmoShopping 都來自設定檔（使用者可編輯、也可能是別的版本
                    // 或別的服留下來的）。兩者可能不同步，道具 id 也可能不存在於台服的 Item 表。
                    // 字典索引子擲 KeyNotFoundException、GetRow() 擲 ArgumentOutOfRangeException，
                    // 而這裡在 ImGui 繪製路徑上 —— 擲一次就會讓 UiBuilder 把 Draw/OpenConfigUi
                    // 設為 null，整個 ICE 介面到重開遊戲前都不會回來。
                    // 同一張表在本檔上方的商店清單已經用 TryGetRow，這裡沿用同一套寫法。
                    if (!C.CosmoShopping.TryGetValue(itemId, out var setting) ||
                        !Svc.Data.GetExcelSheet<Item>().TryGetRow(itemId, out var itemInfo))
                    {
                        if (ReportedBadShoppingItems.Add(itemId))
                            IceLogging.Info($"採購清單：略過項目 {itemId}（設定檔沒有對應設定，或台服 Item 表查無此列）。", "[ShoppingTab]");

                        continue;
                    }

                    ImGui.TableNextRow();

                    ImGui.PushID(itemId);

                    ImGui.TableSetColumnIndex(0);
                    using (ImRaii.Disabled(i == 0))
                    {
                        if (ImGuiEx.IconButton(FontAwesomeIcon.ArrowUp, $"##drag_{itemId}"))
                        {
                            MoveItemUp(itemId);
                            C.Save();
                        }
                    }

                    ImGui.TableNextColumn();
                    if (ImGuiEx.IconButton(FontAwesomeIcon.ArrowDown, $"##drag_{itemId}"))
                    {
                        MoveItemDown(itemId);
                        C.Save();
                    }

                    // Name
                    ImGui.TableNextColumn();
                    if (itemInfo.Icon is { } itemIcon && Svc.Texture.TryGetFromGameIcon((int)itemIcon, out var texture))
                    {
                        ImGui.Image(texture.GetWrapOrEmpty().Handle, new Vector2(24, 24));
                        ImGui.SameLine();
                    }
                    ImGui.Text($"{itemInfo.Name}");

                    ImGui.TableNextColumn();
                    PlayerHelper.GetItemCount(itemId, out var count);
                    ImGui.Text($"{count}");
                    DrawUnlockTag(itemId, setting);

                    // Cost
                    ImGui.TableNextColumn();
                    if (Shop_Cosmocredits.CosmocreditShop.TryGetValue(itemId, out var shopInfo))
                    {
                        ImGui.Text($"{shopInfo.Cost}");
                    }

                    // Kind (you can add logic for this)
                    ImGui.TableNextColumn();
                    ImGui.Text("Material".Loc()); // Replace with actual kind logic

                    // Keep Amount
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(80);
                    var keepAmount = setting.KeepAmount;
                    if (ImGui.InputInt($"##keep_{itemId}", ref keepAmount))
                    {
                        setting.KeepAmount = keepAmount;
                        C.SaveDebounced();
                    }

                    // Buy Amount
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(80);
                    var buyAmount = setting.BuyAmount;
                    if (ImGui.InputInt($"##buy_{itemId}", ref buyAmount))
                    {
                        setting.BuyAmount = buyAmount;
                        C.SaveDebounced();
                    }

                    // Keep Buying
                    ImGui.TableNextColumn();
                    var keepBuying = setting.KeepBuying;
                    if (ImGui.Checkbox($"##keepbuying_{itemId}", ref keepBuying))
                    {
                        foreach (var enabled in C.CosmoShopping)
                        {
                            enabled.Value.KeepBuying = false;
                        }

                        setting.KeepBuying = keepBuying;
                        C.Save();
                    }

                    // Skip If Learned
                    // 逐項而不是全域：這幾件道具是**可交易**的（台服 Item 表核對過，
                    // 48211／48213／47985 的 IsUntradable 都是 False），有人買來就是要賣掉。
                    // 全域開關表達不出「擋樂譜、但別擋我要轉賣的那件」。
                    ImGui.TableNextColumn();
                    var skipIfUnlocked = setting.SkipIfUnlocked;
                    if (ImGui.Checkbox($"##skiplearned_{itemId}", ref skipIfUnlocked))
                    {
                        setting.SkipIfUnlocked = skipIfUnlocked;
                        C.Save();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("On: stop buying this item once the game reports that you have already learned it.\n" +
                                          "Orchestrion rolls and emote manuals disappear from your bags when learned, so Keep can never be reached and Keep Buying would drain your credits.\n" +
                                          "Leave this off if you buy this item in order to resell it.").Loc());
                    }

                    // Remove Button
                    ImGui.TableNextColumn();
                    if (ImGuiEx.IconButton(Dalamud.Interface.FontAwesomeIcon.Trash, "##Remove Item"))
                    {
                        RemoveItem(itemId);
                        C.Save();
                    }

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
        }
        /// <summary>
        /// 全域開關：遇到遊戲自己的「你已經學會這個了」確認框要不要放棄該件。
        /// </summary>
        /// <remarks>
        /// 做成全域而不是逐項，是因為這個判斷的依據是**遊戲跳出來的那句話**，
        /// 不是採購清單上的某一列 —— 它連沒被列進清單的道具都擋得到。
        /// 只想擋特定幾件的人請用清單裡的「已學會就不買」欄。
        /// </remarks>
        private static void DrawAlreadyLearnedPromptOption()
        {
            bool heedLearned = C.HeedAlreadyLearnedPrompt;
            if (ImGui.Checkbox("Heed the game's already-learned warning".Loc() + "###ICEHeedAlreadyLearned", ref heedLearned))
            {
                C.HeedAlreadyLearnedPrompt = heedLearned;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("?");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("Off (default): ICE answers Yes to every shop confirmation, including the game's own warning that you have already learned the item.\n" +
                                  "On: that warning is answered No instead, and the item is skipped for the rest of this shopping trip.\n" +
                                  "Leave this off if you deliberately buy already-learned items to resell them - the per-item Skip If Learned column blocks only the items you pick.").Loc());
            }

            // 🔴 「這個選項現在是廢的」必須在列上看得見，不能只藏在 tooltip 裡：
            //    比對基準是從遊戲的 Addon 表讀來的，讀不到的話這個勾選框會靜默無效。
            if (heedLearned && !Task_BuyCosmoItems.AlreadyLearnedPromptDetectable)
            {
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudOrange, "!");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("The game data does not have the confirmation text this option matches against, so it will do nothing.".Loc());
            }
        }

        /// <summary>
        /// 採購清單的「已學會」狀態標記。
        /// </summary>
        /// <remarks>
        /// 只在這一項開了「已學會就不買」時才畫 —— 沒開的人本來就不該感覺到差別。<br/>
        /// ⚠️ 「不知道」要畫成 <c>?</c>，不能什麼都不畫：那會讓「還沒學會」與
        /// 「問不到答案」長得一模一樣，而後者代表這個開關現在不會生效。
        /// </remarks>
        private static void DrawUnlockTag(uint itemId, CosmoShoppingList setting)
        {
            if (!setting.SkipIfUnlocked)
                return;

            switch (PlayerHelper.GetItemUnlockState(itemId))
            {
                case PlayerHelper.ItemUnlockState.Unlocked:
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudGrey, "Learned".Loc());
                    break;
                case PlayerHelper.ItemUnlockState.NotApplicable:
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudGrey, "n/a".Loc());
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("This item has nothing to learn, so Skip If Learned will never stop buying it.".Loc());
                    break;
                case PlayerHelper.ItemUnlockState.Unknown:
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudGrey, "?");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("The game has not answered whether this item is learned yet, so it will be bought as usual.".Loc());
                    break;
            }
        }

        private static void AddItem(uint itemId)
        {
            if (C.CosmoShopping.ContainsKey(itemId))
                return;


            C.CosmoShopping[itemId] = new CosmoShoppingList();
            C.CosmoShoppingOrder.Add(itemId);
        }

        private static void RemoveItem(uint itemId)
        {
            C.CosmoShopping.Remove(itemId);
            C.CosmoShoppingOrder.Remove(itemId);
        }

        public static void MoveItemUp(uint itemId)
        {
            int index = C.CosmoShoppingOrder.IndexOf(itemId);
            if (index > 0)
            {
                C.CosmoShoppingOrder.RemoveAt(index);
                C.CosmoShoppingOrder.Insert(index - 1, itemId);
            }
        }

        public static void MoveItemDown(uint itemId)
        {
            int index = C.CosmoShoppingOrder.IndexOf(itemId);
            if (index >= 0 && index < C.CosmoShoppingOrder.Count - 1)
            {
                C.CosmoShoppingOrder.RemoveAt(index);
                C.CosmoShoppingOrder.Insert(index + 1, itemId);
            }
        }

        private static void MoveItemToTop(uint itemId)
        {
            int index = C.CosmoShoppingOrder.IndexOf(itemId);
            if (index > 0)
            {
                C.CosmoShoppingOrder.RemoveAt(index);
                C.CosmoShoppingOrder.Insert(0, itemId);
            }
        }

        public static void CheckConfigState()
        {
            if (C.CosmoShopping == null)
            {
                C.CosmoShopping = new();
                C.Save();
            }
            if (C.CosmoShoppingOrder == null)
            {
                C.CosmoShoppingOrder = new();
                C.Save();
            }
        }
    }
}