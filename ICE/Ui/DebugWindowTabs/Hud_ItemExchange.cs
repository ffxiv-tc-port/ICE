using Lumina.Excel.Sheets;
using ICE.Utilities.AddonMasters;
using ICE.Utilities.Cosmic_Helper;
using System.Text;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Hud_ItemExchange
    {
        public static unsafe void Draw()
        {
            ImGuiTableFlags tableFlags = ImGuiTableFlags.RowBg |
                             ImGuiTableFlags.Borders |
                             ImGuiTableFlags.SizingFixedFit |
                             ImGuiTableFlags.Resizable |           // Allow column resizing
                             ImGuiTableFlags.Reorderable |         // Allow column reordering
                             ImGuiTableFlags.Hideable;             // Allow hiding columns via right-click

            if (GenericHelpers.TryGetAddonMaster<global::ICE.Utilities.AddonMasters.InclusionShop>("InclusionShop", out var itemExchange) && itemExchange.IsAddonReady)
            {

                ImGui.Text($"Currency Amount: {itemExchange.CurrencyAmount}");

                if (ImGui.BeginTable("Item Exchange Window", 3, tableFlags))
                {
                    ImGui.TableSetupColumn("##ItemId");
                    ImGui.TableSetupColumn("##ItemName");
                    ImGui.TableSetupColumn("##Cost1", ImGuiTableColumnFlags.WidthStretch);

                    // 🔴 不要用 `i < NumEntries` 去索引 ShopItems：ShopItems 會跳過 itemId==0 的格子，
                    //    長度必定 <= NumEntries，用 NumEntries 當上界就是繪製路徑上的 IndexOutOfRange。
                    //    ImGui 繪製路徑擲一次例外 ⇒ UiBuilder 把 Draw 設成 null，整個 ICE 介面到重開遊戲前都不會回來。
                    foreach (var entry in itemExchange.ShopItems)
                    {
                        var itemId = entry.ItemId;
                        var currencyId = entry.CurrencyId;
                        var cost = entry.Cost;

                        var sheet = Svc.Data.GetExcelSheet<Item>();
                        // GetRow 查無此列是擲 ArgumentOutOfRangeException，不是回 null。
                        var itemName = sheet.TryGetRow(itemId, out var itemRow) ? itemRow.Name.ToString() : $"#{itemId}";

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text($"{itemId}");

                        ImGui.TableNextColumn();
                        ImGui.Text(itemName);

                        ImGui.TableNextColumn();
                        if (sheet.TryGetRow(currencyId, out var currencyRow) && currencyRow.Icon is { } icon)
                        {
                            if (Svc.Texture.TryGetFromGameIcon((int)icon, out var texture) && texture != null)
                            {
                                ImGui.Image(texture.GetWrapOrEmpty().Handle, new Vector2(20, 20));
                                ImGui.SameLine();
                            }
                        }
                        ImGui.Text($"{cost}");
                        ImGui.SameLine();
                        if (ImGui.Button("Buy Item"))
                        {
                            entry.Select();
                        }
                    }
                    ImGui.EndTable();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<ShopExchangeCurrency>("ShopExchangeCurrency", out var shopExchange) && shopExchange.IsAddonReady)
            {
                var sheet = Svc.Data.GetExcelSheet<Item>();

                uint currencyIcon = sheet.TryGetRow(shopExchange.CurrencyId, out var currencyRow) && currencyRow.Icon is { } ci ? ci : 0u;
                Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? texture = null;
                if (currencyIcon != 0 && Svc.Texture.TryGetFromGameIcon((int)currencyIcon, out var currencyTex))
                    texture = currencyTex?.GetWrapOrEmpty();

                // 這一行本來就是唯一能看出「寫死的 AtkValue 索引在台服對不對」的地方，把數字補齊。
                ImGui.Text($"{shopExchange.CurrencyAmount}  (AtkValue 總數 {shopExchange.AtkValueCount}／回報 {shopExchange.NumEntries} 件／解析到 {shopExchange.BasicShopItems.Length} 件)");
                if (ImGui.Button("Copy Item List"))
                {
                    var sb = new StringBuilder();
                    foreach (var entry in shopExchange.BasicShopItems)
                    {
                        var itemId = entry.ItemId;
                        var itemName = sheet.TryGetRow(itemId, out var itemRow) ? itemRow.Name.ToString() : $"#{itemId}";

                        sb.AppendLine($"[{itemId}] = new ItemInfo");
                        sb.AppendLine($"\t{{");
                        sb.AppendLine($"\t\tName = \"{itemName}\",");
                        sb.AppendLine($"\t\tCost = {entry.CostAmount},");
                        sb.AppendLine($"\t}},");
                    }
                    ImGui.SetClipboardText(sb.ToString());
                }

                if (ImGui.BeginTable("Item Exchange Window", 5, tableFlags))
                {
                    ImGui.TableSetupColumn("##ItemId");
                    ImGui.TableSetupColumn("##ItemName");
                    ImGui.TableSetupColumn("##Cost1");
                    ImGui.TableSetupColumn("##Cost2");
                    ImGui.TableSetupColumn("##Cost3");

                    foreach (var entry in shopExchange.BasicShopItems)
                    {
                        var itemId = entry.ItemId;
                        var cost = entry.CostAmount;

                        var itemName = sheet.TryGetRow(itemId, out var itemRow) ? itemRow.Name.ToString() : $"#{itemId}";

                        ImGui.PushID(itemId);

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text($"{itemId}");

                        ImGui.TableNextColumn();
                        ImGui.Text(itemName);

                        ImGui.TableNextColumn();
                        if (texture != null)
                        {
                            ImGui.Image(texture.Handle, new Vector2(20, 20));
                            ImGui.SameLine();
                        }
                        ImGui.Text($"{entry.CostAmount}");

                        ImGui.TableNextColumn();
                        if (ImGui.Button("Buy 1 Item"))
                        {
                            entry.Select();
                        }

                        ImGui.TableNextColumn();
                        if (ImGui.Button("Buy Max"))
                        {
                            if (cost == 0)
                            {
                                IceLogging.Info($"道具 {itemId} 的單價讀成 0，不送出購買（多半代表 AtkValue 佈局對不上）。", "[Hud_ItemExchange]");
                            }
                            else if (EzThrottler.Throttle("Buying from shop throttle"))
                            {
                                var amount = shopExchange.CurrencyAmount;
                                var buyAmount = amount / cost;
                                entry.Select((int)buyAmount);
                            }
                        }

                        ImGui.PopID();
                    }
                    ImGui.EndTable();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<Shop>("Shop", out var Shop) && Shop.IsAddonReady)
            {
                var sheet = Svc.Data.GetExcelSheet<Item>();

                var currencyIcon = 65002;
                // TryGetFromGameIcon 失敗時 out 參數是 null；原本直接 .GetWrapOrEmpty() 會 NRE，
                // 而這是 ImGui 繪製路徑 —— 擲一次例外整個 ICE 介面就到重開遊戲前都不會回來。
                Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? texture = null;
                if (Svc.Texture.TryGetFromGameIcon(currencyIcon, out var gilTex))
                    texture = gilTex?.GetWrapOrEmpty();
                PlayerHelper.GetItemCount(1, out var amount);
                if (texture != null)
                {
                    ImGui.Image(texture.Handle, new Vector2(26, 26));
                    ImGui.SameLine();
                }
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{amount}");

                if (ImGui.Button("Copy Item List"))
                {
                    var sb = new StringBuilder();
                    foreach (var entry in Shop.ShopItems)
                    {
                        var itemId = entry.ItemId;
                        var itemName = sheet.TryGetRow(itemId, out var copyRow) ? copyRow.Name.ToString() : $"#{itemId}";

                        sb.AppendLine($"[{itemId}] = new ItemInfo");
                        sb.AppendLine($"\t{{");
                        sb.AppendLine($"\t\tName = \"{itemName}\",");
                        sb.AppendLine($"\t\tCost = {entry.CostAmount},");
                        sb.AppendLine($"\t}},");
                    }
                    ImGui.SetClipboardText(sb.ToString());
                }

                if (ImGui.BeginTable("Item Exchange Window", 5, tableFlags))
                {
                    ImGui.TableSetupColumn("##ItemId");
                    ImGui.TableSetupColumn("##ItemName");
                    ImGui.TableSetupColumn("##Cost1");
                    ImGui.TableSetupColumn("##Cost2");
                    ImGui.TableSetupColumn("##Cost3");

                    foreach (var entry in Shop.ShopItems)
                    {
                        var itemId = entry.ItemId;
                        var cost = entry.CostAmount;

                        var itemName = sheet.TryGetRow(itemId, out var itemRow) ? itemRow.Name.ToString() : $"#{itemId}";

                        ImGui.PushID(itemId);

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text($"{itemId}");

                        ImGui.TableNextColumn();
                        ImGui.Text(itemName);

                        ImGui.TableNextColumn();
                        if (texture != null)
                        {
                            ImGui.Image(texture.Handle, new Vector2(20, 20));
                            ImGui.SameLine();
                        }
                        ImGui.Text($"{entry.CostAmount}");

                        ImGui.TableNextColumn();
                        if (ImGui.Button("Buy 1 Item"))
                        {
                            entry.Select();
                        }

                        ImGui.TableNextColumn();
                        if (ImGui.Button("Buy Max"))
                        {
                            if (cost == 0)
                            {
                                IceLogging.Info($"道具 {itemId} 的單價讀成 0，不送出購買（多半代表 AtkValue 佈局對不上）。", "[Hud_ItemExchange]");
                            }
                            else if (EzThrottler.Throttle("Buying from shop throttle"))
                            {
                                var buyAmount = amount / cost;
                                entry.Select((int)buyAmount);
                            }
                        }

                        ImGui.PopID();
                    }
                    ImGui.EndTable();
                }
            }
            else
            {
                ImGui.Text("Waiting for a shop exchange window to be open");
            }
        }
    }
}
