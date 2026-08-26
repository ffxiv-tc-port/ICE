using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.SettingTabs
{
    internal class Priority_Settings
    {
        // 與 Task_FindMission.DefaultRankOrder 保持一致；兩邊都會把設定裡缺少的階級補回來，
        // 避免舊設定檔漏掉某一階導致那些任務永遠不被挑到。
        private static readonly string[] DefaultRanks = ["ExA", "A", "B", "C", "D"];

        public static void Draw()
        {
            ImGui.Text("Mission Priority Organizer".Loc());

            var prioritizeUngolded = C.PrioritizeUngoldedMissions;
            if (ImGui.Checkbox("Prioritize missions without a gold star".Loc() + "###ICEPrioritizeUngolded", ref prioritizeUngolded))
            {
                C.PrioritizeUngoldedMissions = prioritizeUngolded;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                "Within the same rank, missions you have not yet earned a gold star on are picked first. Once every mission is golded this option has no effect and ordering returns to normal.".Loc());

            var useTableSort = C.UseTableSortForMissionOrder;
            if (ImGui.Checkbox("Pick missions using the table sort order".Loc() + "###ICEUseTableSort", ref useTableSort))
            {
                C.UseTableSortForMissionOrder = useTableSort;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                ("Within the same rank, missions are picked in the order set by 'Sort By' in the table settings " +
                "(Exp I-V, Cosmo/Lunar credits, map location, class score...). Off means the game's own list order.\n" +
                "Can be combined with the gold star option above: the table order applies first, then missions " +
                "without a gold star are moved to the front.").Loc());

            ImGui.Separator();
            ImGui.Text("Drag items to reorder mission priority (higher = processed first):".Loc());
            ImGui.Separator();

            // Create a copy for manipulation
            var currentOrder = C.MissionPrio.ToList();
            bool orderChanged = false;

            for (int i = 0; i < currentOrder.Count; i++)
            {
                ImGui.PushID(i);

                var missionType = currentOrder[i];

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text(GetMissionTypeIcon(missionType));
                ImGui.PopFont();

                ImGui.SameLine();

                // Making the text the only selectable thing... trying to make it WITH the icon is messy
                string displayText = GetMissionTypeName(missionType);
                bool isSelected = false;
                ImGui.Selectable(displayText, isSelected, ImGuiSelectableFlags.None);

                // Handle drag and drop
                if (ImGui.BeginDragDropSource())
                {
                    // Store the index being dragged
                    unsafe
                    {
                        int draggedIndex = i;
                        byte* data = (byte*)&draggedIndex;
                        ImGui.SetDragDropPayload("MISSION_TYPE", new ReadOnlySpan<byte>(data, sizeof(int)));
                    }
                    ImGui.Text("Moving: ??".Loc(GetMissionTypeName(missionType)));
                    ImGui.EndDragDropSource();
                }

                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var payload = ImGui.AcceptDragDropPayload("MISSION_TYPE");
                        if (!payload.IsNull)
                        {
                            int draggedIndex = *(int*)payload.Data;

                            // Perform the reorder
                            if (draggedIndex != i && draggedIndex >= 0 && draggedIndex < currentOrder.Count)
                            {
                                var draggedItem = currentOrder[draggedIndex];
                                currentOrder.RemoveAt(draggedIndex);
                                currentOrder.Insert(i, draggedItem);
                                orderChanged = true;
                            }
                        }
                    }
                    ImGui.EndDragDropTarget();
                }

                // Show priority number
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "(Priority: ??)".Loc(i + 1));

                ImGui.PopID();
            }

            if (orderChanged)
            {
                // Priority has been changed. Updating the config now.
                C.MissionPrio = currentOrder;
                C.Save();
            }

            ImGui.Separator();

            // Reset to default button
            if (ImGui.Button("Reset to Default".Loc() + "##Mission"))
            {
                C.MissionPrio = new List<ProvisionalTypes>
                {
                    ProvisionalTypes.ProvisionalWeather,
                    ProvisionalTypes.ProvisionalSequential,
                    ProvisionalTypes.ProvisionalTimed
                };
                C.Save();
            }

            // Add spacing between sections
            ImGui.Spacing();
            ImGui.Spacing();

            // RANK PRIORITY SECTION（標準任務的階級順序，原本是寫死的 ExA→A→B→C→D）
            ImGui.Text("Rank Priority Organizer".Loc());
            ImGui.Text("Drag items to reorder which mission rank is picked first (standard missions only):".Loc());
            ImGui.Separator();

            var rankOrder = (C.RankPrio ?? []).ToList();
            // 設定檔若缺了某個階級就補回來，否則那一階的任務會永遠不被挑到。
            foreach (var r in DefaultRanks)
            {
                if (!rankOrder.Contains(r))
                    rankOrder.Add(r);
            }
            bool rankChanged = false;

            for (int i = 0; i < rankOrder.Count; i++)
            {
                ImGui.PushID($"rank_{i}");

                ImGui.Selectable(rankOrder[i], false, ImGuiSelectableFlags.None);

                if (ImGui.BeginDragDropSource())
                {
                    unsafe
                    {
                        int draggedIndex = i;
                        byte* data = (byte*)&draggedIndex;
                        ImGui.SetDragDropPayload("RANK_TYPE", new ReadOnlySpan<byte>(data, sizeof(int)));
                    }
                    ImGui.Text("Moving: ??".Loc(rankOrder[i]));
                    ImGui.EndDragDropSource();
                }

                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var payload = ImGui.AcceptDragDropPayload("RANK_TYPE");
                        if (!payload.IsNull)
                        {
                            int draggedIndex = *(int*)payload.Data;
                            if (draggedIndex != i && draggedIndex >= 0 && draggedIndex < rankOrder.Count)
                            {
                                var draggedItem = rankOrder[draggedIndex];
                                rankOrder.RemoveAt(draggedIndex);
                                rankOrder.Insert(i, draggedItem);
                                rankChanged = true;
                            }
                        }
                    }
                    ImGui.EndDragDropTarget();
                }

                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "(Priority: ??)".Loc(i + 1));

                ImGui.PopID();
            }

            if (rankChanged)
            {
                C.RankPrio = rankOrder;
                C.Save();
            }

            ImGui.Separator();
            if (ImGui.Button("Reset to Default".Loc() + "##Rank"))
            {
                C.RankPrio = DefaultRanks.ToList();
                C.Save();
            }

            ImGui.Spacing();
            ImGui.Spacing();

            // JOB PRIORITY SECTION
            ImGui.Text("Job Priority Organizer".Loc());
            ImGui.Text("Drag items to reorder job priority (higher = processed first):".Loc());
            ImGui.Separator();

            // Create a copy for manipulation
            var currentJobOrder = C.JobPrio.ToList();
            bool jobOrderChanged = false;

            for (int i = 0; i < currentJobOrder.Count; i++)
            {
                ImGui.PushID($"job_{i}");

                var jobId = currentJobOrder[i];

                if (CosmicHelper.JobIconDict.TryGetValue(jobId, out var icon))
                {
                    ImGui.Image(icon.GetWrapOrEmpty().Handle, new Vector2(24, 24));
                }
                else
                {
                    // Fallback to FontAwesome icon if texture not found
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.Text(GetJobIcon(jobId));
                    ImGui.PopFont();
                }

                ImGui.SameLine();

                ImGui.SameLine();

                string displayText = GetJobName(jobId);
                bool isSelected = false;
                ImGui.Selectable(displayText, isSelected, ImGuiSelectableFlags.None);

                // Handle drag and drop
                if (ImGui.BeginDragDropSource())
                {
                    unsafe
                    {
                        int draggedIndex = i;
                        byte* data = (byte*)&draggedIndex;
                        ImGui.SetDragDropPayload("JOB_TYPE", new ReadOnlySpan<byte>(data, sizeof(int)));
                    }
                    ImGui.Text("Moving: ??".Loc(GetJobName(jobId)));
                    ImGui.EndDragDropSource();
                }

                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var payload = ImGui.AcceptDragDropPayload("JOB_TYPE");
                        if (!payload.IsNull)
                        {
                            int draggedIndex = *(int*)payload.Data;

                            if (draggedIndex != i && draggedIndex >= 0 && draggedIndex < currentJobOrder.Count)
                            {
                                var draggedItem = currentJobOrder[draggedIndex];
                                currentJobOrder.RemoveAt(draggedIndex);
                                currentJobOrder.Insert(i, draggedItem);
                                jobOrderChanged = true;
                            }
                        }
                    }
                    ImGui.EndDragDropTarget();
                }

                // Show priority number
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "(Priority: ??)".Loc(i + 1));

                ImGui.PopID();
            }

            if (jobOrderChanged)
            {
                C.JobPrio = currentJobOrder;
                C.Save();
            }

            ImGui.Separator();

            // Reset to default button
            if (ImGui.Button("Reset to Default".Loc() + "##Job"))
            {
                C.JobPrio = new List<uint> { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
                C.Save();
            }
        }

        // Quick way of assigning a name to the missions (useful for enums, saves me a lot of typing
        private static string GetMissionTypeName(ProvisionalTypes type)
        {
            return type switch
            {
                ProvisionalTypes.ProvisionalWeather => "Weather Missions".Loc(),
                ProvisionalTypes.ProvisionalSequential => "Sequence Missions".Loc(),
                ProvisionalTypes.ProvisionalTimed => "Timed Missions".Loc(),
                _ => type.ToString()
            };
        }

        // Quick way of assigning Icons to the mission types
        private static string GetMissionTypeIcon(ProvisionalTypes type)
        {
            return type switch
            {
                ProvisionalTypes.ProvisionalWeather => FontAwesomeIcon.Cloud.ToIconString(),
                ProvisionalTypes.ProvisionalSequential => FontAwesomeIcon.ListOl.ToIconString(),
                ProvisionalTypes.ProvisionalTimed => FontAwesomeIcon.Clock.ToIconString(),
                _ => FontAwesomeIcon.Question.ToIconString()
            };
        }

        // Job name helper
        private static string GetJobName(uint jobId)
        {
            return jobId switch
            {
                8 => "Carpenter".Loc(),
                9 => "Blacksmith".Loc(),
                10 => "Armorer".Loc(),
                11 => "Goldsmith".Loc(),
                12 => "Leatherworker".Loc(),
                13 => "Weaver".Loc(),
                14 => "Alchemist".Loc(),
                15 => "Culinarian".Loc(),
                16 => "Miner".Loc(),
                17 => "Botanist".Loc(),
                18 => "Fisher".Loc(),
                _ => "Unknown Job".Loc()
            };
        }

        // Job icon helper
        private static string GetJobIcon(uint jobId)
        {
            return jobId switch
            {
                8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 => FontAwesomeIcon.Hammer.ToIconString(), // Crafters
                16 or 17 => FontAwesomeIcon.Mountain.ToIconString(), // MIN/BTN
                18 => FontAwesomeIcon.Fish.ToIconString(), // FSH
                _ => FontAwesomeIcon.Question.ToIconString()
            };
        }
    }
}