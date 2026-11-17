using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ICE.Config;
using ICE.Ui.DebugWindowTabs;
using ICE.Utilities.ImGuiTools;
using ICE.Utilities.MechaOps;
using Lumina.Excel.Sheets;
using Pictomancy;
using System.Collections.Generic;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    internal partial class Misc_Settings
    {
        private static void AutoUse()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.PersonRays, "Auto-Use".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            // 📌 英文鍵刻意維持 "Auto-Use Moon Sprint" 不改：那是 ini 的查找鍵，
            //    改掉會讓既有的繁中條目與上游合併同時失配。中文譯名（官方術語「宇宙衝刺」）在 ini 那一側。
            bool AutoMoonSprint = C.MoonSprint;
            if (ImGui.Checkbox("Auto-Use Moon Sprint".Loc() + "###ICEAutoMoonSprint", ref AutoMoonSprint))
            {
                C.MoonSprint = AutoMoonSprint;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("?");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("In a Cosmic Exploration zone, keeps Stellar Sprint up while you are moving.\n" +
                                  "Crafters and gatherers get Stellar Sprint (the 1-second-recast one). " +
                                  "Any other job falls back to the ordinary Sprint, exactly as before.\n" +
                                  "It never fires while mounted, casting, gathering, crafting, in a cutscene, or between areas.\n" +
                                  "It also pauses while you are taking part in a mecha event, or whenever ICE cannot tell whether you are.").Loc());
            }

            bool DisableLunarAura = C.RemoveStellarStatus;
            if (ImGui.Checkbox("Auto-Remove Stellar Status".Loc() + "###ICEAutoRemoveStellarStatus", ref DisableLunarAura))
            {
                C.RemoveStellarStatus = DisableLunarAura;
                C.Save();
            }

            bool DisableRedAlertPathing = C.DisablePathfindingToRedAlert;
            if (ImGui.Checkbox("Disable Pathfinding to Red Alerts".Loc() + "###ICEDisableRedAlertPathing", ref DisableRedAlertPathing))
            {
                C.DisablePathfindingToRedAlert = DisableRedAlertPathing;
                C.Save();
            }
        }

        private static void RepairSettings()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.Hammer, "Repair Settings".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            bool repairAtVendor = C.RepairAtVendor;
            if (ImGui.Checkbox("Repair at Vendor".Loc() + "###ICERepairAtVendor", ref repairAtVendor))
            {
                C.RepairAtVendor = repairAtVendor;
                C.Save();
            }

            using (ImRaii.Disabled(repairAtVendor))
            {
                bool selfRepairGather = C.SelfRepairGather;
                if (ImGui.Checkbox("Self Repair Gather".Loc() + "###ICESelfRepairGather", ref selfRepairGather))
                {
                    C.SelfRepairGather = selfRepairGather;
                    C.Save();
                }

                bool selfRepairCrafter = C.SelfRepairCrafter;
                if (ImGui.Checkbox("Self Repair Crafter".Loc() + "###ICESelfRepairCrafter", ref selfRepairCrafter))
                {
                    C.SelfRepairCrafter= selfRepairCrafter;
                    C.Save();
                }
            }

            float repairAmount = C.RepairPercent;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderFloat("###Repair %", ref repairAmount, 0f, 99f, "%.0f%%"))
            {
                if (C.RepairPercent != repairAmount)
                {
                    C.RepairPercent = (int)repairAmount;
                    C.SaveDebounced();
                }
            }
        }

        private static void TimeRecords()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.Clock, "Record Settings".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            int TimeHistory = C.TimeHistoryLimit;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Average Time History to keep".Loc() + "###ICETimeHistoryLimit", ref TimeHistory))
            {
                C.TimeHistoryLimit = TimeHistory;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("?");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("Anything below 0 to keep all logs\n" +
                                 "Above 0 to keep a set limit").Loc());
            }
        }

        private static bool visualizeRadius = false;
        private static bool visualizeDismountRadius = false;
        private static Dictionary<uint, string> availableMounts = new();

        private static string mountSearchText = "";
        private static int mountDisplayOffset = 0;
        private static int mountItemsPerPage = 10;

        private static unsafe void MountSelection()
        {
            bool mountOutsideMission = C.UseMountOutsideMission;
            bool mountInMission = C.UseMountInMission;
            float minMountRange = C.MountRadius;
            float dismountRange = C.DismountRadius;

            ImGuiEx.IconWithText(FontAwesomeIcon.Feather, "Mount Settings".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            if (ImGui.Button("Select Mounting Option".Loc() + "###ICESelectMountingOption"))
            {
                availableMounts.Clear();
                availableMounts[0] = "Mount Roulette";

                var mountSheet = Svc.Data.GetExcelSheet<Mount>();

                foreach (var mountItem in Utils.MountUnlockQueryMissing() ? Enumerable.Empty<Mount>() : mountSheet)
                {
                    //Checking to see if the current mount is unlocked
                    if (!PlayerState.Instance()->IsMountUnlocked(mountItem.RowId)) continue;

                    string mountName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(mountItem.Singular.ToString().ToLower());
                    uint id = mountItem.RowId;

                    availableMounts[id] = mountName;
                }

                mountSearchText = "";
                mountDisplayOffset = 0;

                ImGui.OpenPopup("Mount Options");
            }
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Mount: ??".Loc(C.MountName.Loc()));

            if (ImGui.BeginPopup("Mount Options"))
            {
                // Search box
                ImGui.InputText("Search".Loc() + "###ICEMountSearch", ref mountSearchText, 100);

                // Filter mounts based on search
                var filteredMounts = availableMounts
                    .Where(kvp => string.IsNullOrEmpty(mountSearchText) ||
                                  kvp.Value.Contains(mountSearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Calculate page count here, just to peeps know how many pages there are
                int totalItems = filteredMounts.Count;
                int maxOffset = Math.Max(0, totalItems - mountItemsPerPage);
                mountDisplayOffset = Math.Min(mountDisplayOffset, maxOffset);

                // Display current page of mounts
                var displayMounts = filteredMounts
                    .Skip(mountDisplayOffset)
                    .Take(mountItemsPerPage);

                foreach (var mount in displayMounts)
                {
                    if (ImGui.Selectable($"{mount.Value.Loc()}##{mount.Key}"))
                    {
                        C.MountId = mount.Key;
                        C.MountName = mount.Value;
                        C.Save();
                        ImGui.CloseCurrentPopup();
                    }
                }

                // Navigation buttons
                ImGui.Separator();

                if (ImGui.Button("Previous".Loc() + "###ICEMountPrev") && mountDisplayOffset > 0)
                {
                    mountDisplayOffset = Math.Max(0, mountDisplayOffset - mountItemsPerPage);
                }

                ImGui.SameLine();
                ImGui.Text("??-?? of ??".Loc(mountDisplayOffset + 1, Math.Min(mountDisplayOffset + mountItemsPerPage, totalItems), totalItems));

                ImGui.SameLine();
                if (ImGui.Button("Next".Loc() + "###ICEMountNext") && mountDisplayOffset < maxOffset)
                {
                    mountDisplayOffset = Math.Min(maxOffset, mountDisplayOffset + mountItemsPerPage);
                }

                ImGui.EndPopup();
            }

            if (ImGui.Checkbox("Use mount outside mission".Loc() + "###ICEUseMountOutsideMission", ref mountOutsideMission))
            {
                C.UseMountOutsideMission = mountOutsideMission;
                C.Save();
            }

            if (ImGui.Checkbox("Use mount in mission".Loc() + "###ICEUseMountInMission", ref mountInMission))
            {
                C.UseMountInMission = mountInMission;
                C.Save();
            }

            ImGui.SetNextItemWidth(100);
            if (ImGui.DragFloat("Minimum Mounting Range".Loc() + "###ICEMinMountRange", ref minMountRange, 1))
            {
                C.MountRadius = minMountRange;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.Checkbox("Visualize radius".Loc() + "###ICEVisualizeMountRadius", ref visualizeRadius);
            ImGui.SetNextItemWidth(100);
            if (ImGui.DragFloat("Dismount Target Range".Loc() + "###ICEDismountRange", ref dismountRange, 1))
            {
                C.DismountRadius = dismountRange;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.Checkbox("Visualize Dismount Radius".Loc() + "###ICEVisualizeDismountRadius", ref visualizeDismountRadius);

            using (var drawList = PictoService.Draw())
            {
                if (drawList == null)
                    return;

                var playerPos = Player.Position;

                if (visualizeRadius)
                    PictoService.VfxRenderer.AddCircle("Mount_Radius Circle", playerPos, C.MountRadius, Utils.FromUintABGR(2616716297));
                if (visualizeDismountRadius)
                    PictoService.VfxRenderer.AddCircle("Dismount_Radius Circle", playerPos, C.DismountRadius, Utils.FromUintABGR(2601121571));
            }
        }

        // 任務預設組：儲存目前啟用的任務組合，載入時只切各任務的
        //    Enabled，不動優先順序或個別任務設定。
        private static string playlistName = string.Empty;

        private static void MissionPlaylists()
        {
            C.MissionPlaylists ??= new();

            ImGuiEx.IconWithText(FontAwesomeIcon.List, "任務預設");
            ImGui.Dummy(new Vector2(0, 5));
            ImGui.TextWrapped("儲存目前啟用的任務組合。載入預設會關閉未列入的任務，但不會改變任務優先順序或個別任務設定。");

            ImGui.SetNextItemWidth(220);
            ImGui.InputText("預設名稱", ref playlistName, 100);
            ImGui.SameLine();

            var normalizedName = playlistName.Trim();
            using (ImRaii.Disabled(normalizedName.Length == 0))
            {
                if (ImGui.Button("儲存目前啟用項目"))
                {
                    C.MissionPlaylists[normalizedName] = C.MissionConfig
                        .Where(entry => entry.Value.Enabled)
                        .Select(entry => entry.Key)
                        .Distinct()
                        .Order()
                        .ToList();
                    playlistName = string.Empty;
                    C.Save();
                }
            }

            if (C.MissionPlaylists.Count == 0)
            {
                ImGui.TextDisabled("尚未建立任務預設。");
                return;
            }

            string? deletePlaylist = null;
            foreach (var (name, missionIds) in C.MissionPlaylists)
            {
                var savedMissionIds = missionIds ?? [];
                ImGui.PushID(name);
                var knownMissionCount = savedMissionIds.Distinct().Count(C.MissionConfig.ContainsKey);
                ImGui.Text($"{name}（{knownMissionCount} 個任務）");
                ImGui.SameLine();
                if (ImGui.Button("載入"))
                {
                    var selected = savedMissionIds.ToHashSet();
                    foreach (var (missionId, settings) in C.MissionConfig)
                        settings.Enabled = selected.Contains(missionId);
                    C.Save();
                }
                ImGui.SameLine();
                if (ImGuiEx.IconButton(FontAwesomeIcon.Trash, "刪除"))
                    deletePlaylist = name;
                ImGui.PopID();
            }

            if (deletePlaylist != null)
            {
                C.MissionPlaylists.Remove(deletePlaylist);
                C.Save();
            }
        }

        // 📌 顯示／隱藏分頁的開關在 InterfaceSettings.Draw()，這裡不畫。

        /// <summary>交件拿到金評時請 TataruPraise 誇一句。純通知，不影響交件流程。</summary>
        private static void TurninPraiseSettings()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.Comment, "Mission Complete Announcements".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            bool praiseOnGold = C.PraiseOnGoldTurnin;
            if (ImGui.Checkbox("Have Tataru praise you on a gold turn-in (requires TataruPraise)".Loc() + "###ICEPraiseOnGoldTurnin", ref praiseOnGold))
            {
                C.PraiseOnGoldTurnin = praiseOnGold;
                C.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("Calls TataruPraise once per mission that is turned in with a gold rating.\n" +
                                  "Does nothing at all when TataruPraise is not installed, and never affects turn-in or scheduling.\n" +
                                  "TataruPraise also has to have its own master switch on, be off cooldown, and have voiced lines in the \"宇宙\" category.").Loc());
            }
        }

        private static void PostMissionCommands()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.Play, "Post Mission Commands".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            ImGui.TextWrapped(("Input below a list of commands that you would like to run after a run has been completed. \n" +
                              "This is kind of my way of letting you somewhat script/set up a sequence of other things that you would like to do that might not be included in the plugin itself. \n" +
                              "If you want something more complex, just make an SND script at that point. And have this run that script post lol.").Loc());

            if (ImGui.Button("Add New Command".Loc() + "###ICEAddNewCommand"))
            {
                C.PostMissionCommands.Add(new Config.MissionCommand 
                { 
                    command = "", 
                    Delay = 0,
                });
                C.Save();
            }

            MissionCommand? toRemove = null;
            int entryCounter = 0;

            if (ImGui.BeginTable("Mission Commands", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders))
            {
                ImGui.TableSetupColumn("Command".Loc());
                ImGui.TableSetupColumn("Delay".Loc());
                ImGui.TableSetupColumn("Remove".Loc());

                ImGui.TableHeadersRow();

                foreach (var entry in C.PostMissionCommands)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.SetNextItemWidth(200);

                    ImGui.PushID($"{entryCounter}_MissionCommand");
                    string command = entry.command;
                    if (ImGui.InputText("##Command", ref command))
                    {
                        entry.command = command;
                        C.SaveDebounced();
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(100);
                    int delay = entry.Delay;
                    if (ImGui.InputInt("###Delay", ref delay))
                    {
                        entry.Delay = delay;
                        C.SaveDebounced();
                    }

                    ImGui.TableNextColumn();
                    if (ImGuiEx.IconButton(FontAwesomeIcon.Trash, $"remove{C.PostMissionCommands.IndexOf(entry)}"))
                    {
                        toRemove = entry;
                    }
                    ImGui.PopID();
                    entryCounter += 1;
                }

                if (toRemove != null)
                {
                    C.PostMissionCommands.Remove(toRemove);
                    C.Save();
                }

                ImGui.EndTable();
            }
        }

        private static void Separator()
        {
            ImGui.Dummy(new Vector2(0, 5));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 5));
        }
    }
}
