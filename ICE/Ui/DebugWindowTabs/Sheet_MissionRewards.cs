using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Sheet_MissionRewards
    {
        public static void Draw()
        {
            List<uint> SheetMissionIds = new() { 1, 22, 566, 625 };

            ImGuiTableFlags tableFlags = ImGuiTableFlags.RowBg |
                            ImGuiTableFlags.Borders |
                            ImGuiTableFlags.Reorderable |
                            ImGuiTableFlags.Hideable |
                            ImGuiTableFlags.SizingFixedFit;

            if (ImGui.BeginTable("Mission Reward Sheet", 18, tableFlags))
            {
                // Setup columns - these names won't be directly visible
                ImGui.TableSetupColumn("Mission ID");
                ImGui.TableSetupColumn("Column 0");
                ImGui.TableSetupColumn("Column 1");
                ImGui.TableSetupColumn("Column 2");
                ImGui.TableSetupColumn("Column 3");
                ImGui.TableSetupColumn("Column 4");
                ImGui.TableSetupColumn("Column 5");
                ImGui.TableSetupColumn("Column 6");
                ImGui.TableSetupColumn("Column 7");
                ImGui.TableSetupColumn("Column 8");
                ImGui.TableSetupColumn("Column 9");
                ImGui.TableSetupColumn("Column 10");
                ImGui.TableSetupColumn("Column 11");
                ImGui.TableSetupColumn("Column 12");
                ImGui.TableSetupColumn("Column 13");
                ImGui.TableSetupColumn("Column 14");
                ImGui.TableSetupColumn("Column 15");
                ImGui.TableSetupColumn("Column 16");
                ImGui.TableSetupColumn("Column 17");

                // Draw custom header row with tooltips
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

                // Column 0: Mission ID
                ImGui.TableSetColumnIndex(0);
                ImGui.TableHeader("Mission ID");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Row ID".Loc());
                    ImGui.EndTooltip();
                }

                // Column 1
                ImGui.TableSetColumnIndex(1);
                ImGui.TableHeader("CosmoCredits");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown0");
                    ImGui.EndTooltip();
                }

                // Column 2
                ImGui.TableSetColumnIndex(2);
                ImGui.TableHeader("PlanetCredits");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown1");
                    ImGui.EndTooltip();
                }

                // Column 3
                ImGui.TableSetColumnIndex(3);
                ImGui.TableHeader("Reward [0]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown2");
                    ImGui.EndTooltip();
                }

                // Column 4
                ImGui.TableSetColumnIndex(4);
                ImGui.TableHeader("Reward [1]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown3");
                    ImGui.EndTooltip();
                }

                // Column 5
                ImGui.TableSetColumnIndex(5);
                ImGui.TableHeader("Reward [2]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown4");
                    ImGui.EndTooltip();
                }

                // Column 6
                ImGui.TableSetColumnIndex(6);
                ImGui.TableHeader("Reward Amount");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown8");
                    ImGui.EndTooltip();
                }

                // Column 7
                ImGui.TableSetColumnIndex(7);
                ImGui.TableHeader("Tool [0]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown9");
                    ImGui.EndTooltip();
                }

                // Column 8
                ImGui.TableSetColumnIndex(8);
                ImGui.TableHeader("Tool [1]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown10");
                    ImGui.EndTooltip();
                }

                // Column 9
                ImGui.TableSetColumnIndex(9);
                ImGui.TableHeader("Tool [2]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown11");
                    ImGui.EndTooltip();
                }

                // Column 10
                ImGui.TableSetColumnIndex(10);
                ImGui.TableHeader("Exp Type [0]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown12");
                    ImGui.EndTooltip();
                }

                // Column 11
                ImGui.TableSetColumnIndex(11);
                ImGui.TableHeader("Exp Type [1]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown13");
                    ImGui.EndTooltip();
                }

                // Column 12
                ImGui.TableSetColumnIndex(12);
                ImGui.TableHeader("Exp Type [2]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown14");
                    ImGui.EndTooltip();
                }

                // Column 13
                ImGui.TableSetColumnIndex(13);
                ImGui.TableHeader("Reward ItemID");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown15");
                    ImGui.EndTooltip();
                }

                // Column 14
                ImGui.TableSetColumnIndex(14);
                ImGui.TableHeader("ExpModifier [0]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown16");
                    ImGui.EndTooltip();
                }

                // Column 15
                ImGui.TableSetColumnIndex(15);
                ImGui.TableHeader("ExpModifier [1]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown17");
                    ImGui.EndTooltip();
                }

                // Column 16
                ImGui.TableSetColumnIndex(16);
                ImGui.TableHeader("ExpModifier [2]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown18");
                    ImGui.EndTooltip();
                }

                // Column 17
                ImGui.TableSetColumnIndex(17);
                ImGui.TableHeader("? ? ?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Unknown19");
                    ImGui.EndTooltip();
                }

                foreach (var id in SheetMissionIds)
                {
                    if (Svc.Data.GetExcelSheet<WKSMissionReward>().TryGetRow(id, out var rewardSheet))
                    {
                        ImGui.TableNextRow();

                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text($"{rewardSheet.RowId}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown0}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown1}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown2}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown3}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown4}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown8}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown9}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown10}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown11}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown12}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown13}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown14}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown15}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown16}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown17}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown18}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"{rewardSheet.Unknown19}");
                    }
                }

                ImGui.EndTable();
            }
        }
    }
}
