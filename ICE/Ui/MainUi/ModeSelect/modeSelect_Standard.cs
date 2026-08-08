using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using ICE.Ui.MainUi.Settings.Settings_Table;
using ICE.Utilities.ImGuiTools;
using System.Collections.Generic;
using System.Reflection;

namespace ICE.Ui.MainUi.ModeSelect
{
    internal class modeSelect_Standard
    {
        public static void Draw()
        {
            using var style = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 10).Push(ImGuiStyleVar.ChildBorderSize, 1);

            // Header at the top
            float scale = ImGuiHelpers.GlobalScale;

            // 原本這裡有一份與 SelectableSidebar 完全相同的 15 行自動選星同步碼，
            // 已抽成 SelectableSidebar.SyncAutoSelect()（含每幀閘門，一幀最多跑一次）。
            // 📌 副作用是既有行為，不是這次新加的。
            SelectableSidebar.SyncAutoSelect();

            using (var headerChild = ImRaii.Child("##modeSelect_StandardHeader", new Vector2(0, 45 * scale), true, ImGuiWindowFlags.NoScrollbar))
            {
                if (!headerChild.Success) return;

                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10 * scale);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5 * scale);

                string modeType = string.Empty;
                FontAwesomeIcon modeIcon = FontAwesomeIcon.List;

                bool relicMode = C.XPRelicGrind;
                bool provisionalMode = C.GrindProvisionals;
                bool standard = (!relicMode && !provisionalMode);

                if (standard)
                    modeType = "Standard".Loc();
                else if (relicMode)
                {
                    modeType = "Relic Grind".Loc();
                    modeIcon = FontAwesomeIcon.ArrowUpRightDots;
                }
                else if (provisionalMode)
                {
                    modeType = "Provisional".Loc();
                    modeIcon = FontAwesomeIcon.Cloud;
                }

                ImGuiEx.IconWithText(modeIcon, "?? Mode".Loc(modeType));

                ImGui.SameLine(0, 10 * scale);

                // Adjust the Y position to center the button vertically with the text
                float textHeight = ImGui.GetTextLineHeight();
                float buttonHeight = ImGui.GetFrameHeight();
                float yOffset = (textHeight - buttonHeight) / 2f;
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + yOffset);

                if (ImGuiEx.IconButtonWithText(FontAwesomeIcon.Play, "Mode Selection".Loc()))
                {
                    ImGui.OpenPopup("Mode Select | Select Mode Window");
                }
                if (ImGui.BeginPopup("Mode Select | Select Mode Window"))
                {
                    ImGui.Text("Select Mode".Loc());
                    ImGui.Separator();

                    if (ImGui.RadioButton("Standard".Loc() + "###ICEModeStandard", standard))
                    {
                        C.XPRelicGrind = false;
                        C.GrindProvisionals = false;
                        C.Save();
                    }
                    ImGuiEx.HelpMarker(("Stand Mode \n" +
                                       "-> Used to select which missions you want to grind. It'll priortize in the following order:\n" +
                                       "-> Critical -> Provisional [Sequence/Timed/Weather] -> Standard [A->D]\n" +
                                       "-> Select which missions you want to do, and go at it.").Loc());
                    if (ImGui.RadioButton("Relic Grind".Loc() + "###ICEModeRelicGrind", relicMode))
                    {
                        C.XPRelicGrind = true;
                        C.GrindProvisionals = false;
                        C.Save();
                    }
                    ImGuiEx.HelpMarker(("Relic Grind\n" +
                                       "-> Automatically select which missions that are best to finish up your relic\n" +
                                       "-> These are weighed based on what is needed to complete the tool to the next step\n" +
                                       "-> If you want to only do certain missions, enable the option and select which ones you want to do").Loc());
                    if (ImGui.RadioButton("Provisional Grind".Loc() + "###ICEModeProvisionalGrind", provisionalMode))
                    {
                        C.XPRelicGrind = false;
                        C.GrindProvisionals = true;
                        C.Save();
                    }
                    ImGuiEx.HelpMarker(("Provisional Grind\n" +
                                       "-> Grind provisional missions [Weather | Timed | Sequence] that you have enabled\n" +
                                       "-> Use this to grind all classes. You can set the priority for which classes and " +
                                       "types of missions that you want to do\n" +
                                       "-> Useful if you're aiming to grind out score/tokens across all classes, or want to do specific missions at certain times").Loc());

                    ImGui.EndPopup();
                }

                uint currentJobId = Player.JobId;
                bool usingSupportedJob = CosmicHelper.CrafterJobList.Contains(currentJobId) || CosmicHelper.GatheringJobList.Contains(currentJobId);

                ImGui.SameLine(0, 10 * scale);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + yOffset);

                using (ImRaii.Disabled(SchedulerMain.State != IceState.Idle || !usingSupportedJob))
                {
                    if (ImGui.Button("Start".Loc() + "###ICEModeSelectStart", new Vector2(150 * scale, 0)))
                    {
                        SchedulerMain.EnablePlugin();
                    }
                }

                ImGui.SameLine(0, 10 * scale);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + yOffset);
                using (ImRaii.Disabled(SchedulerMain.State == IceState.Idle))
                {
                    using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)))
                    using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.3f, 0.3f, 1.0f)))
                    using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.1f, 0.1f, 1.0f)))
                    {
                        if (ImGui.Button("Stop".Loc() + "###ICEModeSelectStop", new Vector2(150 * scale, 0)))
                        {
                            SchedulerMain.DisablePlugin();
                        }
                    }
                }
            }

            if (ImGui.BeginTable("modeSelect_TableHeader", 4, ImGuiTableFlags.SizingFixedFit, Vector2.Zero))
            {
                ImGui.TableSetupColumn("Class Selector".Loc());
                ImGui.TableSetupColumn("Other Settings".Loc());

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                bool tableSettingExpanded = modeSelect_Tools.DrawCompactCategoryHeader("Table Settings".Loc(), FontAwesomeIcon.Table);

                ImGui.TableNextColumn();
                bool missionSettingExpanded = modeSelect_Tools.DrawCompactCategoryHeader("Mission Settings".Loc(), FontAwesomeIcon.UserCog);

                bool relicGrindExpanded = false;
                if (C.XPRelicGrind)
                {
                    ImGui.TableNextColumn();
                    relicGrindExpanded = modeSelect_Tools.DrawCompactCategoryHeader("Relic Grind Settings".Loc(), FontAwesomeIcon.ArrowUpRightDots);
                }

                bool completionExpanded = false;
                if (C.ShowCompletionWindow)
                {
                    ImGui.TableNextColumn();
                    completionExpanded = modeSelect_Tools.DrawCompactCategoryHeader("Completion Table Settings".Loc(), FontAwesomeIcon.Trophy);
                }

                bool showNextColumn = tableSettingExpanded || missionSettingExpanded || (relicGrindExpanded && C.XPRelicGrind) || (completionExpanded && C.ShowCompletionWindow);

                if (showNextColumn)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (tableSettingExpanded)
                    {
                        Settings_TableColumns.ColumnSettings();
                    }

                    ImGui.TableNextColumn();
                    if (missionSettingExpanded)
                    {
                        Settings_TableColumns.GeneralMissionSettings();
                    }

                    if (C.XPRelicGrind && relicGrindExpanded)
                    {
                        ImGui.TableNextColumn();

                        bool relicTurnin = C.TurninRelic;
                        if (ImGui.Checkbox("Turnin if relic is complete".Loc() + "##RelicTurnin_RelicGrind", ref relicTurnin))
                        {
                            if (relicTurnin)
                                C.GrindProvisionals = false;

                            C.TurninRelic = relicTurnin;
                            C.Save();
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("?");
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(("THIS IS YOUR HEADS UP ON HOW THIS WORKS. If I change this in the future, this tooltip will also change.\n" +
                                             "1: This will check for your current CLASS [not menu class, actual current class] for relic turnin.\n" +
                                             "2: You must not have the tool eqipped for this to run full auto. \n" +
                                             "\t- This is due to the fact that I cba coding this in at this time. (might change my mind in the future *shrugs*)\n" +
                                             "3: This will take prio over \"Stop @ Relic Turnin\", in the sense that if you have both enabled, it will turnin vs stop. And continue about it's day\n" +
                                             "4: If you're on a crafting class, it will return you back to the stop you were crafting post turnin. \n" +
                                             "\t- This is optional, you can disable it at your own free will, I just like this so I can just go back to an isolated area of my choosing").Loc());
                        }

                        ImGui.Separator();

                        bool EnableRelicXp = C.XPRelicGrind;
                        if (ImGui.Checkbox("Auto-Pick For Relic XP".Loc() + "###ICEAutoPickForRelicXP", ref EnableRelicXp))
                        {
                            if (EnableRelicXp)
                            {
                                C.GrindProvisionals = false;
                            }
                            C.XPRelicGrind = EnableRelicXp;
                            C.Save();
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("?");
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(("By default this only grinds relic Exp under the basic mission tab.\n" +
                                               "Use the two options below to also consider the Critical and Provisional " +
                                               "[Sequence/Timed/Weather] tabs.").Loc());
                        }
                        if (EnableRelicXp)
                        {
                            bool OnlySelected = C.XPRelicOnlyEnabled;
                            if (ImGui.Checkbox("Only selected missions".Loc() + "###ICEXPRelicOnlyEnabled", ref OnlySelected))
                            {
                                C.XPRelicOnlyEnabled = OnlySelected;
                                C.Save();
                            }

                            bool includeCritical = C.XPRelicIncludeCritical;
                            if (ImGui.Checkbox("Also check Critical missions".Loc() + "###ICEXPRelicIncludeCritical", ref includeCritical))
                            {
                                C.XPRelicIncludeCritical = includeCritical;
                                C.Save();
                            }
                            ImGuiEx.HelpMarker(("Also look for relic XP missions on the Critical tab.\n" +
                                                "Checked before the basic tab, same order as Standard mode.\n" +
                                                "Only missions for your CURRENT job are considered - relic XP goes to the " +
                                                "tool of the mission's job, so taking another job's mission levels the wrong tool.").Loc());

                            bool includeProvisional = C.XPRelicIncludeProvisional;
                            if (ImGui.Checkbox("Also check Provisional missions".Loc() + "###ICEXPRelicIncludeProvisional", ref includeProvisional))
                            {
                                C.XPRelicIncludeProvisional = includeProvisional;
                                C.Save();
                            }
                            ImGuiEx.HelpMarker(("Also look for relic XP missions on the Provisional tab " +
                                                "[Sequence/Timed/Weather].\n" +
                                                "These are only available under their own conditions (weather, time window, " +
                                                "or a finished prerequisite mission), so most cycles will still fall through " +
                                                "to the basic tab.\n" +
                                                "Only missions for your CURRENT job are considered.").Loc());
                            if (C.ShowManualMode)
                            {
                                bool IgnoreManual = C.XPRelicIgnoreManual;
                                if (ImGui.Checkbox("Ignore Manual Mode Missions".Loc() + "###ICEXPRelicIgnoreManual", ref IgnoreManual))
                                {
                                    C.XPRelicIgnoreManual = IgnoreManual;
                                    C.Save();
                                }
                            }
                        }
                    }

                    if (C.ShowCompletionWindow && completionExpanded)
                    {
                        ImGui.TableNextColumn();
                        bool showSelectedJobOnly = C.ShowSelectedJobOnly;
                        if (ImGui.Checkbox("Show only selected job".Loc() + "###ICEShowSelectedJobOnly", ref showSelectedJobOnly))
                        {
                            C.ShowSelectedJobOnly = showSelectedJobOnly;
                            if (showSelectedJobOnly)
                                C.ShowCompletionOnlyJob = false;
                            C.Save();
                        }

                        bool nonGold = C.ShowCompletion_MissingGold;
                        if (ImGui.Checkbox("Show Only Non-Gold Missions".Loc() + "###ICEShowCompletionMissingGold", ref nonGold))
                        {
                            C.ShowCompletion_MissingGold = nonGold;
                            C.Save();
                        }
                    }
                }

                ImGui.EndTable();
            }

            using (var bodyChild = ImRaii.Child("##modeSelect_Body", new Vector2(0, -1), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                if (!bodyChild.Success) return;

                foreach (var missionType in modeSelect_TableInfo.missionList)
                {
                    missionType.Value.Clear();
                }

                foreach (var mission in CosmicHelper.SheetMissionDict)
                {
                    var Jobs = mission.Value.Jobs;
                    var territoryId = mission.Value.TerritoryId;
                    uint selectedJob = C.SelectedJob;
                    bool sinusEnabled = C.ShowSinusMissions;
                    bool phaennaEnabled = C.ShowPhaennaMissions;

                    if (C.ShowCompletionWindow)
                    {
                        if (C.ShowCompletionOnlyJob)
                        {
                            if (!Jobs.Contains(selectedJob))
                                continue;
                        }
                    }
                    else if (C.GrindProvisionals)
                    {
                        // honestly do nothing here, this is just to catch and show all the jobs for this mode here. Kinda lazy I realize but *-shrugs-*
                    }
                    else if (!Jobs.Contains(selectedJob))
                        continue;


                    if (!sinusEnabled && territoryId == 1237)
                        continue;

                    if (!phaennaEnabled && territoryId == 1291)
                        continue;

                    if (C.GrindProvisionals)
                    {
                        bool provisional = mission.Value.Attributes.HasFlag(MissionAttributes.ProvisionalWeather)
                                        || mission.Value.Attributes.HasFlag(MissionAttributes.ProvisionalTimed)
                                        || mission.Value.Attributes.HasFlag(MissionAttributes.ProvisionalSequential);

                        if (mission.Value.Attributes.HasFlag(MissionAttributes.ProvisionalWeather))
                            modeSelect_TableInfo.missionList["Weather"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        else if (mission.Value.Attributes.HasFlag(MissionAttributes.ProvisionalTimed))
                            modeSelect_TableInfo.missionList["Timed"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        else if (mission.Value.Attributes.HasFlag(MissionAttributes.ProvisionalSequential))
                            modeSelect_TableInfo.missionList["Sequence"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });

                        if (C.MissionConfig.ContainsKey(mission.Key) && C.MissionConfig[mission.Key].Enabled && provisional)
                        {
                            modeSelect_TableInfo.missionList["All Enabled"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        }
                    }
                    else
                    {
                        if (mission.Value.Attributes.HasFlag(MissionAttributes.Critical))
                            modeSelect_TableInfo.missionList["Critical"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        else if (mission.Value.Attributes.HasFlag(MissionAttributes.ProvisionalWeather))
                            modeSelect_TableInfo.missionList["Weather"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        else if (mission.Value.Attributes.HasFlag(MissionAttributes.ProvisionalTimed))
                            modeSelect_TableInfo.missionList["Timed"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        else if (mission.Value.Attributes.HasFlag(MissionAttributes.ProvisionalSequential))
                            modeSelect_TableInfo.missionList["Sequence"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        else if (mission.Value.Rank > 3)
                            modeSelect_TableInfo.missionList["ARank"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        else if (mission.Value.Rank == 3)
                            modeSelect_TableInfo.missionList["BRank"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        else if (mission.Value.Rank == 2)
                            modeSelect_TableInfo.missionList["CRank"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        else if (mission.Value.Rank == 1)
                            modeSelect_TableInfo.missionList["DRank"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });

                        if (C.MissionConfig.ContainsKey(mission.Key) && C.MissionConfig[mission.Key].Enabled)
                        {
                            modeSelect_TableInfo.missionList["All Enabled"].Add(new modeSelect_TableInfo.Mission { id = mission.Key, enabled = C.MissionConfig[mission.Key].Enabled });
                        }
                    }
                }

                int criticalEnabled = modeSelect_TableInfo.missionList.ContainsKey("Critical") ? modeSelect_TableInfo.missionList["Critical"].Count(mission => mission.enabled) : 0;
                int sequenceEnabled = modeSelect_TableInfo.missionList.ContainsKey("Sequence") ? modeSelect_TableInfo.missionList["Sequence"].Count(mission => mission.enabled) : 0;
                int weatherEnabled = modeSelect_TableInfo.missionList.ContainsKey("Weather") ? modeSelect_TableInfo.missionList["Weather"].Count(mission => mission.enabled) : 0;
                int timedEnabled = modeSelect_TableInfo.missionList.ContainsKey("Timed") ? modeSelect_TableInfo.missionList["Timed"].Count(mission => mission.enabled) : 0;
                int aRankEnabled = modeSelect_TableInfo.missionList.ContainsKey("ARank") ? modeSelect_TableInfo.missionList["ARank"].Count(mission => mission.enabled) : 0;
                int bRankEnabled = modeSelect_TableInfo.missionList.ContainsKey("BRank") ? modeSelect_TableInfo.missionList["BRank"].Count(mission => mission.enabled) : 0;
                int cRankEnabled = modeSelect_TableInfo.missionList.ContainsKey("CRank") ? modeSelect_TableInfo.missionList["CRank"].Count(mission => mission.enabled) : 0;
                int dRankEnabled = modeSelect_TableInfo.missionList.ContainsKey("DRank") ? modeSelect_TableInfo.missionList["DRank"].Count(mission => mission.enabled) : 0;
                int allEnabled = modeSelect_TableInfo.missionList.ContainsKey("All Enabled") ? modeSelect_TableInfo.missionList["All Enabled"].Count(mission => mission.enabled) : 0;

                float scrollbarSize = ImGui.GetStyle().ScrollbarSize;
                float buttonRowHeight = (ImGui.GetTextLineHeight() + 8 * scale + 4 * scale) + scrollbarSize;

                using (var missionButtons = ImRaii.Child("##tab_scroll", new Vector2(0, buttonRowHeight), false, ImGuiWindowFlags.HorizontalScrollbar))
                {
                    if (!missionButtons.Success)
                        return;

                    if (C.GrindProvisionals)
                    {
                        ImGui_Tools.DrawCategoryButton("All Enabled [??]".Loc(allEnabled), "main_AllEnabled");
                        ImGui_Tools.DrawCategoryButton("Sequence [??]".Loc(sequenceEnabled), "main_Sequence");
                        ImGui_Tools.DrawCategoryButton("Weather [??]".Loc(weatherEnabled), "main_Weather");
                        ImGui_Tools.DrawCategoryButton("Timed [??]".Loc(timedEnabled), "main_Timed");
                        ImGui_Tools.EndCategoryButtonRow();
                    }
                    else
                    {
                        ImGui_Tools.DrawCategoryButton("All Enabled [??]".Loc(allEnabled), "main_AllEnabled");
                        ImGui_Tools.DrawCategoryButton("Critical [??]".Loc(criticalEnabled), "main_Critical");
                        ImGui_Tools.DrawCategoryButton("Sequence [??]".Loc(sequenceEnabled), "main_Sequence");
                        ImGui_Tools.DrawCategoryButton("Weather [??]".Loc(weatherEnabled), "main_Weather");
                        ImGui_Tools.DrawCategoryButton("Timed [??]".Loc(timedEnabled), "main_Timed");
                        ImGui_Tools.DrawCategoryButton("A Rank [??]".Loc(aRankEnabled), "main_ARank");
                        ImGui_Tools.DrawCategoryButton("B Rank [??]".Loc(bRankEnabled), "main_BRank");
                        ImGui_Tools.DrawCategoryButton("C Rank [??]".Loc(cRankEnabled), "main_CRank");
                        ImGui_Tools.DrawCategoryButton("D Rank [??]".Loc(dRankEnabled), "main_DRank", spacingAfter: 0);
                        ImGui_Tools.EndCategoryButtonRow();
                    }
                }

                if (C.ShowExtraMissionInfo)
                {
                    if (ImGui.BeginTable("Mission Info | Extra Details", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable, Vector2.Zero))
                    {
                        ImGui.TableSetupColumn("Mission Selection Viewer".Loc(), ImGuiTableColumnFlags.WidthFixed, 200f);
                        ImGui.TableSetupColumn("Specific Mission Info".Loc(), ImGuiTableColumnFlags.WidthStretch);

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        MissionTableInfo();

                        ImGui.TableNextColumn();
                        using (var missionInfoChild = ImRaii.Child("##modeSelect_MissionInfo", new Vector2(0, 0), false))
                        {
                            modeSelect_TableInfo.DrawMissionDetails();
                        }

                        ImGui.EndTable();
                    }
                }
                else
                {
                    MissionTableInfo();
                }
            }
        }

        private static void MissionTableInfo()
        {
            using (var missionTableChild = ImRaii.Child("##modeSelect_MissionTables", new Vector2(0, 0), false))
            {
                var enabledTabs = ImGui_Tools.CategoryStates;
                if (C.GrindProvisionals)
                {
                    modeSelect_TableInfo.missionList["All Enabled"] = modeSelect_TableInfo.missionList["All Enabled"]
                        .OrderBy(x => C.JobPrio.IndexOf(CosmicHelper.SheetMissionDict[x.id].Jobs.First()))
                        .ToList(); // ToList() if you need a List<T>, otherwise the IOrderedEnumerable is fine

                    modeSelect_TableInfo.missionList["Sequence"] = modeSelect_TableInfo.missionList["Sequence"]
                        .OrderBy(x => C.JobPrio.IndexOf(CosmicHelper.SheetMissionDict[x.id].Jobs.First()))
                        .ToList();

                    modeSelect_TableInfo.missionList["Weather"] = modeSelect_TableInfo.missionList["Weather"]
                        .OrderBy(x => C.JobPrio.IndexOf(CosmicHelper.SheetMissionDict[x.id].Jobs.First()))
                        .ToList();

                    modeSelect_TableInfo.missionList["Timed"] = modeSelect_TableInfo.missionList["Timed"]
                        .OrderBy(x => C.JobPrio.IndexOf(CosmicHelper.SheetMissionDict[x.id].Jobs.First()))
                        .ToList();

                    if (enabledTabs["main_AllEnabled"])
                        modeSelect_TableInfo.DrawMissionTablev2("All Enabled", "All_Enabled", modeSelect_TableInfo.missionList["All Enabled"]);
                    if (enabledTabs["main_Sequence"])
                        modeSelect_TableInfo.DrawMissionTablev2("Sequence", "Sequence_Missions", modeSelect_TableInfo.missionList["Sequence"]);
                    if (enabledTabs["main_Weather"])
                        modeSelect_TableInfo.DrawMissionTablev2("Weather", "Weather_Missions", modeSelect_TableInfo.missionList["Weather"]);
                    if (enabledTabs["main_Timed"])
                        modeSelect_TableInfo.DrawMissionTablev2("Timed", "Timed_Missions", modeSelect_TableInfo.missionList["Timed"]);
                }
                else
                {
                    if (enabledTabs["main_AllEnabled"])
                    {
                        if (modeSelect_TableInfo.missionList["All Enabled"].Count > 0)
                        {
                            modeSelect_TableInfo.DrawMissionTablev2("All Enabled", "All_Enabled", modeSelect_TableInfo.SortMissionList(modeSelect_TableInfo.missionList["All Enabled"]));
                        }
                        else
                        {
                            ImGui.Text("HEY. ENABLE SOME MISSIONS SO WE CAN DISPLAY SOMETHING HERE".Loc());
                        }
                    }
                    if (enabledTabs["main_Critical"])
                        modeSelect_TableInfo.DrawMissionTablev2("Critical", "Critical_Missions", modeSelect_TableInfo.SortMissionList(modeSelect_TableInfo.missionList["Critical"]));
                    if (enabledTabs["main_Sequence"])
                        modeSelect_TableInfo.DrawMissionTablev2("Sequence", "Sequence_Missions", modeSelect_TableInfo.SortMissionList(modeSelect_TableInfo.missionList["Sequence"]));
                    if (enabledTabs["main_Weather"])
                        modeSelect_TableInfo.DrawMissionTablev2("Weather", "Weather_Missions", modeSelect_TableInfo.SortMissionList(modeSelect_TableInfo.missionList["Weather"]));
                    if (enabledTabs["main_Timed"])
                        modeSelect_TableInfo.DrawMissionTablev2("Timed", "Timed_Missions", modeSelect_TableInfo.SortMissionList(modeSelect_TableInfo.missionList["Timed"]));
                    if (enabledTabs["main_ARank"])
                        modeSelect_TableInfo.DrawMissionTablev2("A Rank", "A_RankMissions", modeSelect_TableInfo.SortMissionList(modeSelect_TableInfo.missionList["ARank"]));
                    if (enabledTabs["main_BRank"])
                        modeSelect_TableInfo.DrawMissionTablev2("B Rank", "B_RankMissions", modeSelect_TableInfo.SortMissionList(modeSelect_TableInfo.missionList["BRank"]));
                    if (enabledTabs["main_CRank"])
                        modeSelect_TableInfo.DrawMissionTablev2("C Rank", "C_RankMissions", modeSelect_TableInfo.SortMissionList(modeSelect_TableInfo.missionList["CRank"]));
                    if (enabledTabs["main_DRank"])
                        modeSelect_TableInfo.DrawMissionTablev2("D Rank", "D_RankMissions", modeSelect_TableInfo.SortMissionList(modeSelect_TableInfo.missionList["DRank"]));
                }
            }
        }
    }
}
