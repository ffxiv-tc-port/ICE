using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Hud_CollectableGathering
    {
        public static unsafe void Draw()
        {
            if (GenericHelpers.TryGetAddonMaster<GatheringMasterpiece>("GatheringMasterpiece", out var gatherCollect) && gatherCollect.IsAddonReady)
            {
                ImGuiTableFlags tableFlags = ImGuiTableFlags.RowBg |
                             ImGuiTableFlags.Borders |
                             ImGuiTableFlags.SizingFixedFit |
                             ImGuiTableFlags.Resizable |           // Allow column resizing
                             ImGuiTableFlags.Reorderable |         // Allow column reordering
                             ImGuiTableFlags.Hideable;             // Allow hiding columns via right-click

                if (ImGui.BeginTable("Gathering_Collectable", 2, tableFlags))
                {
                    ImGui.TableSetupColumn("##AddonInfo");
                    ImGui.TableSetupColumn("##AddonValue");

                    // Row 1
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Item Name: ".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{gatherCollect.ItemName}");

                    // Row 2
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Item ID: ".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{gatherCollect.ItemID}");

                    // Row 3
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Current Collectability: ".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{gatherCollect.CurrentCollectability}");

                    // Row 4
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Item Integrity: ".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{gatherCollect.CurrentIntegrity} / {gatherCollect.TotalIntegrity}");

                    // Row 5
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Min Collectibility: ".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{gatherCollect.MinCollectability}");

                    // Row 6
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Mid Collectibility: ".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{gatherCollect.MidCollectability}");

                    // Row 7
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("High Collectibility: ".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{gatherCollect.HighCollectability}");

                    // Row 8
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Max Collectibility: ".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{gatherCollect.MaxCollectability}");

                    ImGui.EndTable();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<Gathering>("Gathering", out var gather) && gather.IsAddonReady)
            {
                if (ImGui.Button("Increase collectability".Loc()))
                {
                    foreach (var item in gather.GatheredItems)
                    {
                        if (item.ItemID != 0)
                        {
                            bool missingDur = gather.CurrentIntegrity < gather.TotalIntegrity;
                            bool useAction = Task_Gather.UseGatherAction(0, item.GatherChance, item.BoonChance, missingDur, PlayerHelper.GetGp());
                            IceLogging.Debug($"Used action: {useAction}");
                            break;
                        }
                    }
                }
            }
            else
            {
                ImGui.Text("Waiting for Gather Collectable window to be visible".Loc());
            }
        }
    }
}
