using Dalamud.Interface;
using ICE.Utilities.ImGuiTools;
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

        /// <summary>
        /// 收合時接在分節標題後面的順序摘要。
        /// 這一頁多數時候使用者只是想確認「現在是什麼順序」，不是要改它 ——
        /// 把答案放在標題列上，就不必為了看一眼而展開整組拖曳清單。
        /// </summary>
        private static string Summary(IEnumerable<string> items, int max = 99)
        {
            var list = items.ToList();
            if (list.Count == 0)
                return string.Empty;

            var shown = list.Take(max);
            var text = string.Join(" > ", shown);
            if (list.Count > max)
                text += " …";

            return "  —  " + text;
        }

        public static void Draw()
        {
            // ── UI 重構第五批：三組拖曳清單改成分節 ──────────────────────────────
            // 使用者點名的痛點：這一頁三組拖曳排序直接疊在一起，整頁很長，
            // 想調職業順序得一路捲到最下面。
            // ⇒ 四節，只有「挑選規則」預設展開（與其他頁「一頁只有第一節預設展開」的慣例一致）。
            //
            // ⚠️ 純換位置：每一節的內容、寫入設定的時機、Reset 按鈕的行為全部逐字照舊。
            //    收合的那一節不會被畫 ⇒ 不會有拖曳事件，等同「使用者沒去碰它」。
            //    這裡沒有任何「畫的時候無條件寫設定」的程式碼被跳過 ——
            //    三組清單都只在拖放成功（orderChanged/rankChanged/jobOrderChanged）之後才寫，
            //    階級那段的「補回缺少的階級」也只作用在本地副本上（真正的補回在 Task_FindMission）。

            // 階級清單要在分節外面先算好：標題上的摘要與節內的拖曳清單必須是同一份，
            // 否則收合時看到的順序會跟展開後看到的不一樣。
            var rankOrder = (C.RankPrio ?? []).ToList();
            // 設定檔若缺了某個階級就補回來，否則那一階的任務會永遠不被挑到。
            foreach (var r in DefaultRanks)
            {
                if (!rankOrder.Contains(r))
                    rankOrder.Add(r);
            }

            // 📌「挑選規則」這一節裝的是原本掛在「任務優先度排序」標題底下的兩個勾選項。
            //    它們管的是**同一階之內**的挑選順序，與下面三組「跨類別的排序」是兩回事，
            //    所以獨立成一節而不是併進任務優先度那一節。
            if (ImGui_Tools.PageSection("Mission Picking Rules".Loc(), "ICEPrioSecRules", defaultOpen: true))
            {
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
            }

            DrawMissionTypePriority();
            DrawRankPriority(rankOrder);
            DrawJobPriority();
        }

        private static void DrawMissionTypePriority()
        {
            // ⚠️ 標題文字是動態的（後面接目前順序），所以 id 一定要靠 PageSection 的
            //    第二個參數固定住 —— 用標籤當 id 的話，順序一變 ImGui 就認成另一個控制項，
            //    展開狀態會在拖曳的當下自己彈回收合。
            if (!ImGui_Tools.PageSection(
                    "Mission Priority Organizer".Loc() + Summary(C.MissionPrio.Select(GetMissionTypeName)),
                    "ICEPrioSecMission"))
                return;

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
        }

        // RANK PRIORITY SECTION（標準任務的階級順序，原本是寫死的 ExA→A→B→C→D）
        private static void DrawRankPriority(List<string> rankOrder)
        {
            if (!ImGui_Tools.PageSection(
                    "Rank Priority Organizer".Loc() + Summary(rankOrder),
                    "ICEPrioSecRank"))
                return;

            ImGui.Text("Drag items to reorder which mission rank is picked first (standard missions only):".Loc());
            ImGui.Separator();

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
        }

        // JOB PRIORITY SECTION
        private static void DrawJobPriority()
        {
            // 職業有 11 個，全列出來標題會爆長 ⇒ 摘要只給前三名再加省略號。
            if (!ImGui_Tools.PageSection(
                    "Job Priority Organizer".Loc() + Summary(C.JobPrio.Select(GetJobName), max: 3),
                    "ICEPrioSecJob"))
                return;

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