using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using System.Collections.Generic;
using System.Text;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Ui_FishingMissionEditor
    {
        private static uint SelectedMission = 0;
        private static string newPresetItem = "";
        private static string newFishCategory = "";
        private static string selectedBaitFromDict = "";
        private static string selectedFishFromDict = "";
        private static string baitSearchText = "";
        private static string fishSearchText = "";

        private static int baitCurrentPage = 0;
        private static int fishCurrentPage = 0;
        private static int itemsPerPage = 5;

        public static void Draw()
        {
            if (ImGui.Button("Import missing missions"))
            {
                foreach (var mission in CosmicHelper.SheetMissionDict.Where(x => x.Value.Jobs.Contains(18)))
                {
                    if (!GatheringUtil.FishingPreset.ContainsKey(mission.Key))
                    {
                        GatheringUtil.FishingPreset[mission.Key] = new()
                        {
                            FishingPreset = new(),
                            AmountRequired = 0,
                            Baits = new(),
                            RequiredFish = new(),
                        };
                    }
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Export All Fishing Presets"))
            {
                var exportData = ExportAllFishingPresets();
                ImGui.SetClipboardText(exportData);
                Svc.Chat.Print("All fishing presets exported to clipboard!");
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(SelectedMission == 0))
            {
                if (ImGui.Button("Export Selected Mission"))
                {
                    var exportData = ExportSingleFishingPreset(SelectedMission);
                    ImGui.SetClipboardText(exportData);
                    Svc.Chat.Print($"Fishing preset for Mission {SelectedMission} exported to clipboard!");
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Copy All Mission IDs"))
            {
                var missionIds = string.Join(",", GatheringUtil.FishingPreset.Keys.OrderBy(x => x));
                ImGui.SetClipboardText(missionIds);
                Svc.Chat.Print($"All mission IDs copied to clipboard! ({GatheringUtil.FishingPreset.Count} missions)");
            }

            ImGui.SameLine();

            ImGui.SetNextItemWidth(100);
            ImGui.InputInt("Items per page", ref itemsPerPage);
            if (itemsPerPage < 1) itemsPerPage = 1;
            if (itemsPerPage > 50) itemsPerPage = 50;
            ImGui.Separator();


            if (ImGui.BeginTable("Fishing Mission Editor", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                if (ImGui.BeginChild("Mission Selection _ Fishing Edition", new Vector2(400, 0), true))
                {
                    foreach (var fishMission in GatheringUtil.FishingPreset.OrderBy(x => x.Key))
                    {
                        var id = fishMission.Key;

                        if (CosmicHelper.SheetMissionDict.TryGetValue(id, out var mission))
                        {
                            string rank = mission.Rank switch
                            {
                                1 => "D",
                                2 => "C",
                                3 => "B",
                                4 => "A",
                                5 => "Ex",
                                6 => "Ex+",
                                _ => "Unknown"  // Default case for invalid ranks
                            };

                            ImGui.PushID($"{id}_{mission.Name}");

                            bool isSelected = SelectedMission == id;
                            var listName = MissionSupport.NameWithMarker(id, mission.Name);
                            string label = isSelected ? $"→ [{id} - {rank}] - {listName}" : $"[{id} - {rank}] - {listName}";

                            if (ImGui.Selectable(label, isSelected))
                            {
                                SelectedMission = id;
                            }

                            // Right-click context menu for deletion
                            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsItemHovered())
                            {
                                ImGui.OpenPopup("Delete Mission");
                            }
                            if (ImGui.BeginPopup("Delete Mission"))
                            {
                                if (ImGui.MenuItem("Delete Mission Preset"))
                                {
                                    GatheringUtil.FishingPreset.Remove(id);
                                    if (SelectedMission == id) SelectedMission = 0;
                                }
                                ImGui.EndPopup();
                            }

                            ImGui.PopID();
                        }
                    }
                }
                ImGui.EndChild();

                ImGui.TableSetColumnIndex(1);
                if (ImGui.BeginChild("Mission Info _ Fishing Edition", new Vector2(0, 0), true))
                {
                    if (GatheringUtil.FishingPreset.TryGetValue(SelectedMission, out var missionData) &&
                        CosmicHelper.SheetMissionDict.TryGetValue(SelectedMission, out var mission))
                    {
                        ImGui.Text($"Editing Mission: [{SelectedMission}] {MissionSupport.NameWithMarker(SelectedMission, mission.Name)}");
                        string attribute = string.Join(", ", mission.Attributes);
                        ImGui.Text($"Mission Attributes: {attribute}");
                        if (ImGuiEx.IconButtonWithText(FontAwesomeIcon.Flag, "Mission Location"))
                        {
                            Utils.SetGatheringRing(mission.TerritoryId, (int)mission.MapPosition.X, (int)mission.MapPosition.Y, mission.Radius, mission.Name);
                        }
                        if (ImGui.Button("Move To Spot"))
                        {

                        }
                        ImGui.Separator();

                        // Amount Required Section
                        ImGui.Text("Amount Required:");
                        ImGui.SetNextItemWidth(100);
                        ImGui.InputInt("##AmountRequired", ref missionData.AmountRequired);
                        ImGui.Text("Unique Fish:");
                        ImGui.SameLine();
                        ImGui.Checkbox("##UniqueFish", ref missionData.UniqueFish);
                        ImGui.Separator();

                        // Fishing Preset Section
                        ImGui.Text("Fishing Preset Items:");
                        if (ImGui.Button("Import all presets"))
                        {
                            P.AutoHook.DeleteAllAnonymousPresets();
                            foreach (var preset in missionData.FishingPreset)
                            {
                                P.AutoHook.CreateAndSelectAnonymousPreset(preset);
                            }
                        }
                        ImGui.Indent();

                        // Add new preset item
                        ImGui.SetNextItemWidth(200);
                        ImGui.InputText("##NewPresetItem", ref newPresetItem, 3000);
                        ImGui.SameLine();
                        if (ImGui.Button("Add Preset Item") && !string.IsNullOrEmpty(newPresetItem))
                        {
                            missionData.FishingPreset.Add(newPresetItem);
                            newPresetItem = "";
                        }

                        // List existing preset items
                        for (int i = 0; i < missionData.FishingPreset.Count; i++)
                        {
                            ImGui.PushID($"preset_{i}");

                            var item = missionData.FishingPreset[i];
                            if (ImGui.InputText($"##preset_{i}", ref item, 100))
                            {
                                missionData.FishingPreset[i] = item;
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Remove".Loc()))
                            {
                                missionData.FishingPreset.RemoveAt(i);
                                i--; // Adjust index after removal
                            }

                            ImGui.PopID();
                        }

                        ImGui.Unindent();
                        ImGui.Separator();

                        // Baits Section
                        ImGui.Text("Baits by Name:");
                        ImGui.Indent();

                        // Add bait from dictionary
                        ImGui.Text("Add from MoonBaits Dictionary:");
                        ImGui.SetNextItemWidth(250);
                        ImGui.InputText("##BaitSearch", ref baitSearchText, 100);
                        ImGui.SameLine();
                        if (ImGui.Button("Search Baits"))
                        {
                            ImGui.OpenPopup("BaitSelector");
                        }

                        if (ImGui.BeginPopup("BaitSelector"))
                        {
                            ImGui.Text("Search and select bait:");
                            ImGui.SetNextItemWidth(300);
                            ImGui.InputText("##BaitSearchInPopup", ref baitSearchText, 100);

                            ImGui.Separator();

                            // Filter baits based on search text
                            var filteredBaits = GatheringUtil.MoonBaits
                                .Where(x => string.IsNullOrEmpty(baitSearchText) ||
                                           x.Key.ToLower().Contains(baitSearchText.ToLower()))
                                .OrderBy(x => x.Key)
                                .ToList();

                            int totalPages = (int)Math.Ceiling((double)filteredBaits.Count / itemsPerPage);
                            if (totalPages == 0) totalPages = 1;

                            // Reset to first page if current page is out of bounds
                            if (baitCurrentPage >= totalPages) baitCurrentPage = 0;

                            // Display current page items
                            var pagedBaits = filteredBaits
                                .Skip(baitCurrentPage * itemsPerPage)
                                .Take(itemsPerPage);

                            foreach (var baitEntry in pagedBaits)
                            {
                                if (ImGui.Selectable($"{baitEntry.Key} ({baitEntry.Value.Count} IDs)"))
                                {
                                    selectedBaitFromDict = baitEntry.Key;
                                    baitCurrentPage = 0; // Reset page on selection
                                    ImGui.CloseCurrentPopup();
                                }
                            }

                            ImGui.Separator();

                            // Pagination controls
                            ImGui.Text($"Page {baitCurrentPage + 1} of {totalPages} ({filteredBaits.Count} total results)");

                            if (ImGui.Button("< Previous") && baitCurrentPage > 0)
                            {
                                baitCurrentPage--;
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Next >") && baitCurrentPage < totalPages - 1)
                            {
                                baitCurrentPage++;
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("First"))
                            {
                                baitCurrentPage = 0;
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Last"))
                            {
                                baitCurrentPage = totalPages - 1;
                            }

                            ImGui.EndPopup();
                        }

                        // Show selected bait and add button
                        if (!string.IsNullOrEmpty(selectedBaitFromDict))
                        {
                            ImGui.Text($"Selected: {selectedBaitFromDict}");
                            ImGui.SameLine();
                            if (ImGui.Button("Add Selected Bait"))
                            {
                                if (GatheringUtil.MoonBaits.TryGetValue(selectedBaitFromDict, out var baitIds))
                                {
                                    if (!missionData.Baits.ContainsKey(selectedBaitFromDict))
                                    {
                                        missionData.Baits[selectedBaitFromDict] = new List<uint>();
                                    }

                                    // Add all IDs from the dictionary entry
                                    foreach (var baitId in baitIds)
                                    {
                                        if (!missionData.Baits[selectedBaitFromDict].Contains(baitId))
                                        {
                                            missionData.Baits[selectedBaitFromDict].Add(baitId);
                                        }
                                    }
                                    selectedBaitFromDict = "";
                                    baitSearchText = "";
                                }
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Clear Selection"))
                            {
                                selectedBaitFromDict = "";
                                baitSearchText = "";
                            }
                        }

                        // List existing bait categories and IDs
                        var baitCategoriesToRemove = new List<string>();
                        foreach (var baitCategory in missionData.Baits.OrderBy(x => x.Key))
                        {
                            ImGui.PushID($"baitCategory_{baitCategory.Key}");

                            bool isExpanded = ImGui.TreeNode($"{baitCategory.Key} ({baitCategory.Value.Count} IDs)");

                            // Right-click to delete bait category
                            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsItemHovered())
                            {
                                ImGui.OpenPopup("Delete Bait Category");
                            }
                            if (ImGui.BeginPopup("Delete Bait Category"))
                            {
                                if (ImGui.MenuItem($"Delete Bait '{baitCategory.Key}'"))
                                {
                                    baitCategoriesToRemove.Add(baitCategory.Key);
                                }
                                ImGui.EndPopup();
                            }

                            if (isExpanded)
                            {
                                var baitIdsToRemove = new List<uint>();

                                for (int i = 0; i < baitCategory.Value.Count; i++)
                                {
                                    ImGui.PushID($"baitId_{i}");
                                    var baitId = baitCategory.Value[i];

                                    ImGui.Text($"Bait ID: {baitId}");
                                    ImGui.SameLine();
                                    if (ImGui.Button("Remove".Loc()))
                                    {
                                        baitIdsToRemove.Add(baitId);
                                    }

                                    ImGui.PopID();
                                }

                                // Remove bait IDs that were marked for removal
                                foreach (var baitId in baitIdsToRemove)
                                {
                                    baitCategory.Value.Remove(baitId);
                                }

                                // Remove empty bait categories
                                if (baitCategory.Value.Count == 0)
                                {
                                    baitCategoriesToRemove.Add(baitCategory.Key);
                                }

                                ImGui.TreePop();
                            }

                            ImGui.PopID();
                        }

                        // Remove bait categories that were marked for removal
                        foreach (var baitCategory in baitCategoriesToRemove)
                        {
                            missionData.Baits.Remove(baitCategory);
                        }

                        ImGui.Unindent();
                        ImGui.Separator();

                        // Required Fish Section
                        ImGui.Text("Required Fish by Category:");
                        ImGui.Indent();

                        // Add fish from dictionary
                        ImGui.Text("Add from MoonFish Dictionary:");
                        ImGui.SetNextItemWidth(150);
                        ImGui.InputText("Category##NewCategoryForDict", ref newFishCategory, 50);
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(250);
                        ImGui.InputText("##FishSearch", ref fishSearchText, 100);
                        ImGui.SameLine();
                        if (ImGui.Button("Search Fish"))
                        {
                            ImGui.OpenPopup("FishSelector");
                        }

                        // Replace the FishSelector popup section with this:
                        if (ImGui.BeginPopup("FishSelector"))
                        {
                            ImGui.Text("Search and select fish:");
                            ImGui.SetNextItemWidth(300);
                            ImGui.InputText("##FishSearchInPopup", ref fishSearchText, 100);

                            ImGui.Separator();

                            // Filter fish based on search text
                            var filteredFish = GatheringUtil.MoonFish
                                .Where(x => string.IsNullOrEmpty(fishSearchText) ||
                                           x.Key.ToLower().Contains(fishSearchText.ToLower()))
                                .OrderBy(x => x.Key)
                                .ToList();

                            int totalPages = (int)Math.Ceiling((double)filteredFish.Count / itemsPerPage);
                            if (totalPages == 0) totalPages = 1;

                            // Reset to first page if current page is out of bounds
                            if (fishCurrentPage >= totalPages) fishCurrentPage = 0;

                            // Display current page items
                            var pagedFish = filteredFish
                                .Skip(fishCurrentPage * itemsPerPage)
                                .Take(itemsPerPage);

                            foreach (var fishEntry in pagedFish)
                            {
                                if (ImGui.Selectable($"{fishEntry.Key} ({fishEntry.Value.Count} IDs)"))
                                {
                                    selectedFishFromDict = fishEntry.Key;
                                    fishCurrentPage = 0; // Reset page on selection
                                    ImGui.CloseCurrentPopup();
                                }
                            }

                            ImGui.Separator();

                            // Pagination controls
                            ImGui.Text($"Page {fishCurrentPage + 1} of {totalPages} ({filteredFish.Count} total results)");

                            if (ImGui.Button("< Previous##Fish") && fishCurrentPage > 0)
                            {
                                fishCurrentPage--;
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Next >##Fish") && fishCurrentPage < totalPages - 1)
                            {
                                fishCurrentPage++;
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("First##Fish"))
                            {
                                fishCurrentPage = 0;
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Last##Fish"))
                            {
                                fishCurrentPage = totalPages - 1;
                            }

                            ImGui.EndPopup();
                        }

                        // Show selected fish and add button
                        // Show selected fish and add button
                        if (!string.IsNullOrEmpty(selectedFishFromDict))
                        {
                            ImGui.Text($"Selected: {selectedFishFromDict}");

                            ImGui.SameLine();
                            if (ImGui.Button("Add Selected Fish"))
                            {
                                if (GatheringUtil.MoonFish.TryGetValue(selectedFishFromDict, out var fishIds))
                                {
                                    // Use the fish name as category if no category specified
                                    string categoryToUse = string.IsNullOrEmpty(newFishCategory) ? selectedFishFromDict : newFishCategory;

                                    if (!missionData.RequiredFish.ContainsKey(categoryToUse))
                                    {
                                        missionData.RequiredFish[categoryToUse] = new List<uint>();
                                    }

                                    // Add all IDs from the dictionary entry
                                    int addedCount = 0;
                                    foreach (var fishId in fishIds)
                                    {
                                        if (!missionData.RequiredFish[categoryToUse].Contains(fishId))
                                        {
                                            missionData.RequiredFish[categoryToUse].Add(fishId);
                                            addedCount++;
                                        }
                                    }

                                    // Show feedback
                                    if (addedCount > 0)
                                    {
                                        Svc.Chat.Print($"Added {addedCount} fish ID(s) to category '{categoryToUse}'");
                                    }
                                    else
                                    {
                                        Svc.Chat.Print($"All fish IDs already exist in category '{categoryToUse}'");
                                    }

                                    selectedFishFromDict = "";
                                    fishSearchText = "";
                                    newFishCategory = ""; // Clear category after adding
                                }
                            }

                            ImGui.SameLine();
                            if (ImGui.Button("Clear Selection##Fish"))
                            {
                                selectedFishFromDict = "";
                                fishSearchText = "";
                            }
                        }

                        // List existing fish categories and fish
                        var categoriesToRemove = new List<string>();
                        foreach (var category in missionData.RequiredFish.OrderBy(x => x.Key))
                        {
                            ImGui.PushID($"category_{category.Key}");

                            bool isExpanded = ImGui.TreeNode($"{category.Key} ({category.Value.Count} fish)");

                            // Right-click to delete category
                            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsItemHovered())
                            {
                                ImGui.OpenPopup("Delete Category");
                            }
                            if (ImGui.BeginPopup("Delete Category"))
                            {
                                if (ImGui.MenuItem($"Delete Category '{category.Key}'"))
                                {
                                    categoriesToRemove.Add(category.Key);
                                }
                                ImGui.EndPopup();
                            }

                            if (isExpanded)
                            {
                                var fishToRemove = new List<uint>();

                                for (int i = 0; i < category.Value.Count; i++)
                                {
                                    ImGui.PushID($"fish_{i}");
                                    var fishId = category.Value[i];

                                    ImGui.Text($"Fish ID: {fishId}");
                                    ImGui.SameLine();
                                    if (ImGui.Button("Remove".Loc()))
                                    {
                                        fishToRemove.Add(fishId);
                                    }

                                    ImGui.PopID();
                                }

                                // Remove fish that were marked for removal
                                foreach (var fish in fishToRemove)
                                {
                                    category.Value.Remove(fish);
                                }

                                // Remove empty categories
                                if (category.Value.Count == 0)
                                {
                                    categoriesToRemove.Add(category.Key);
                                }

                                ImGui.TreePop();
                            }

                            ImGui.PopID();
                        }

                        // Remove categories that were marked for removal
                        foreach (var category in categoriesToRemove)
                        {
                            missionData.RequiredFish.Remove(category);
                        }

                        ImGui.Unindent();
                    }
                    else if (SelectedMission != 0)
                    {
                        ImGui.Text("Mission data not found or mission not in sheet dictionary.");
                    }
                    else
                    {
                        ImGui.Text("Select a mission to edit its fishing preset.");
                    }
                }
                ImGui.EndChild();

                ImGui.EndTable();
            }
        }

        private static string ExportAllFishingPresets()
        {
            var sb = new StringBuilder();
            sb.AppendLine("public static Dictionary<uint, FishingTools> FishingPreset = new()");
            sb.AppendLine("{");

            foreach (var preset in GatheringUtil.FishingPreset.OrderBy(x => x.Key))
            {
                sb.AppendLine($"\t[{preset.Key}] = new FishingTools()");
                sb.AppendLine("\t{");

                // Export FishingPreset list
                sb.AppendLine("\t\tFishingPreset = new List<string>()");
                sb.AppendLine("\t\t{");
                foreach (var item in preset.Value.FishingPreset)
                {
                    sb.AppendLine($"\t\t\t\"{item}\",");
                }
                sb.AppendLine("\t\t},");

                // Export AmountRequired
                sb.AppendLine($"\t\tAmountRequired = {preset.Value.AmountRequired},");

                // Export Baits
                sb.AppendLine("\t\tBaits = new Dictionary<string, List<uint>>()");
                sb.AppendLine("\t\t{");
                foreach (var baitCategory in preset.Value.Baits.OrderBy(x => x.Key))
                {
                    sb.AppendLine($"\t\t\t[\"{baitCategory.Key}\"] = new List<uint>()");
                    sb.AppendLine("\t\t\t{");
                    foreach (var baitId in baitCategory.Value.OrderBy(x => x))
                    {
                        sb.AppendLine($"\t\t\t\t{baitId},");
                    }
                    sb.AppendLine("\t\t\t},");
                }
                sb.AppendLine("\t\t},");

                // Export RequiredFish
                sb.AppendLine("\t\tRequiredFish = new Dictionary<string, List<uint>>()");
                sb.AppendLine("\t\t{");
                foreach (var category in preset.Value.RequiredFish.OrderBy(x => x.Key))
                {
                    sb.AppendLine($"\t\t\t[\"{category.Key}\"] = new List<uint>()");
                    sb.AppendLine("\t\t\t{");
                    foreach (var fish in category.Value.OrderBy(x => x))
                    {
                        sb.AppendLine($"\t\t\t\t{fish},");
                    }
                    sb.AppendLine("\t\t\t},");
                }
                sb.AppendLine("\t\t},");

                sb.AppendLine("\t},");
            }

            sb.AppendLine("};");
            return sb.ToString();
        }

        private static string ExportSingleFishingPreset(uint missionId)
        {
            if (!GatheringUtil.FishingPreset.TryGetValue(missionId, out var preset))
            {
                return $"// No fishing preset found for mission {missionId}";
            }

            var sb = new StringBuilder();

            // Add mission name if available
            if (CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var mission))
            {
                sb.AppendLine($"// Export for Mission [{missionId}] - {mission.Name}");
            }
            else
            {
                sb.AppendLine($"// Export for Mission [{missionId}]");
            }

            sb.AppendLine($"[{missionId}] = new FishingTools()");
            sb.AppendLine("{");

            // Export FishingPreset list
            sb.AppendLine("\tFishingPreset = new List<string>()");
            sb.AppendLine("\t\t\t{");
            foreach (var item in preset.FishingPreset)
            {
                sb.AppendLine($"\t\t\t\t\"{item}\",");
            }
            sb.AppendLine("\t\t\t},");

            // Export AmountRequired
            sb.AppendLine($"\tAmountRequired = {preset.AmountRequired},");
            sb.AppendLine($"\tUniqueFish = {preset.UniqueFish.ToString().ToLower()},");

            // Export Baits
            sb.AppendLine("\tBaits = new Dictionary<string, List<uint>>()");
            sb.AppendLine("\t{");
            foreach (var baitCategory in preset.Baits.OrderBy(x => x.Key))
            {
                sb.AppendLine($"\t\t[\"{baitCategory.Key}\"] = new List<uint>()");
                sb.AppendLine("\t\t{");
                foreach (var baitId in baitCategory.Value.OrderBy(x => x))
                {
                    sb.AppendLine($"\t\t\t{baitId},");
                }
                sb.AppendLine("\t\t},");
            }
            sb.AppendLine("\t},");

            // Export RequiredFish
            sb.AppendLine("\tRequiredFish = new Dictionary<string, List<uint>>()");
            sb.AppendLine("\t{");
            foreach (var category in preset.RequiredFish.OrderBy(x => x.Key))
            {
                sb.AppendLine($"\t\t[\"{category.Key}\"] = new List<uint>()");
                sb.AppendLine("\t\t\t\t{");
                foreach (var fish in category.Value.OrderBy(x => x))
                {
                    sb.AppendLine($"\t\t\t\t\t{fish},");
                }
                sb.AppendLine("\t\t\t\t},");
            }
            sb.AppendLine("\t},");

            sb.AppendLine("},");
            return sb.ToString();
        }
    }
}