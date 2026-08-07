using Dalamud.Interface;
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

            if (ImGui.BeginTable("Current Shopping List", 10, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
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