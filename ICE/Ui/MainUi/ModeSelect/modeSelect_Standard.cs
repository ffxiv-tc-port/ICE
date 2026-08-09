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
        // ── 任務頁的搜尋字串（2026-08-09 UI 重構第五批）────────────────────────────
        // 🔴 刻意**不寫進設定檔**：這是「我現在想找哪個任務」的暫時狀態，不是偏好。
        //    存起來的話，下次開遊戲會看到任務表莫名其妙只剩兩三列，而使用者不會聯想到
        //    是上次的搜尋還開著 —— 那是典型「靜默把功能弄壞」的設定。
        // 📌 它只影響「表上畫不畫這一列」，不影響分類按鈕上的計數，也不影響自動化挑任務。
        public static string MissionNameFilter = string.Empty;

        /// <summary>
        /// 搜尋列的比對：任務名稱含關鍵字（不分大小寫）或任務 ID 含這串數字。
        /// 空字串一律回 true —— 預設狀態與加這個功能之前逐字相同。
        /// </summary>
        public static bool PassesNameFilter(uint id, string? name)
        {
            if (string.IsNullOrWhiteSpace(MissionNameFilter))
                return true;

            var needle = MissionNameFilter.Trim();

            if (!string.IsNullOrEmpty(name) && name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;

            return id.ToString().Contains(needle, StringComparison.Ordinal);
        }

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

                // ── 模式選擇：彈窗改成列上的下拉（2026-08-09 UI 重構第五批）──────────
                // 原本是「模式選擇」按鈕 → 彈窗 → 三個圓鈕，要三步才換得了模式，
                // 而且彈窗會蓋住任務表。現在目前模式直接寫在下拉上，換模式一次點兩下。
                // ⚠️ 三個選項寫進設定的動作與原本的圓鈕**逐字相同**（含
                //    「選宇宙工具就關掉限定任務」這種互斥副作用），只是換了外觀。
                // 📌 原本掛在每個圓鈕旁的說明改成「滑鼠移到選項上」才出現 ——
                //    文字一個字都沒改，所以既有的翻譯照樣命中。
                ImGuiEx.IconWithText(modeIcon, "Mode".Loc());

                ImGui.SameLine(0, 8 * scale);

                // Adjust the Y position to center the combo vertically with the text
                float textHeight = ImGui.GetTextLineHeight();
                float buttonHeight = ImGui.GetFrameHeight();
                float yOffset = (textHeight - buttonHeight) / 2f;
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + yOffset);

                ImGui.SetNextItemWidth(160 * scale);
                if (ImGui.BeginCombo("###ICEModeSelectCombo", modeType))
                {
                    if (ImGui.Selectable("Standard".Loc() + "###ICEModeStandard", standard))
                    {
                        C.XPRelicGrind = false;
                        C.GrindProvisionals = false;
                        C.Save();
                    }
                    if (standard)
                        ImGui.SetItemDefaultFocus();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("Stand Mode \n" +
                                          "-> Used to select which missions you want to grind. It'll priortize in the following order:\n" +
                                          "-> Critical -> Provisional [Sequence/Timed/Weather] -> Standard [A->D]\n" +
                                          "-> Select which missions you want to do, and go at it.").Loc());
                    }

                    if (ImGui.Selectable("Relic Grind".Loc() + "###ICEModeRelicGrind", relicMode))
                    {
                        C.XPRelicGrind = true;
                        C.GrindProvisionals = false;
                        C.Save();
                    }
                    if (relicMode)
                        ImGui.SetItemDefaultFocus();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("Relic Grind\n" +
                                          "-> Automatically select which missions that are best to finish up your relic\n" +
                                          "-> These are weighed based on what is needed to complete the tool to the next step\n" +
                                          "-> If you want to only do certain missions, enable the option and select which ones you want to do").Loc());
                    }

                    if (ImGui.Selectable("Provisional".Loc() + "###ICEModeProvisionalGrind", provisionalMode))
                    {
                        C.XPRelicGrind = false;
                        C.GrindProvisionals = true;
                        C.Save();
                    }
                    if (provisionalMode)
                        ImGui.SetItemDefaultFocus();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("Provisional Grind\n" +
                                          "-> Grind provisional missions [Weather | Timed | Sequence] that you have enabled\n" +
                                          "-> Use this to grind all classes. You can set the priority for which classes and " +
                                          "types of missions that you want to do\n" +
                                          "-> Useful if you're aiming to grind out score/tokens across all classes, or want to do specific missions at certain times").Loc());
                    }

                    ImGui.EndCombo();
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

            // ── 任務頁設定：整組收進一個分節（2026-08-09 UI 重構第五批）──────────────
            // 這一排設定標題原本永遠佔著任務表上方的位置，但裡面九成是「設好就不會再動」
            // 的東西。常用的三個篩選（隱藏不支援／只顯示所選職業／只顯示未金星）已經
            // 搬到下面的篩選列，剩下的整組收進這個分節，預設收合。
            // ⚠️ 分節裡的內容一個字都沒改，只是外面多包一層收合。
            if (ImGui_Tools.PageSection("Mission Page Settings".Loc(), "ICESecMissionPage")
                && ImGui.BeginTable("modeSelect_TableHeader", 3, ImGuiTableFlags.SizingFixedFit, Vector2.Zero))
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

                // 📌 這裡原本還有第四欄「完成度表格設定」，裡面就是「只顯示所選職業」與
                //    「只顯示未金星」兩個勾選項。兩個都搬到篩選列了（只在完成度檢視下出現，
                //    可達性與原本相同），所以這一欄整個沒有內容可放，不是功能被拿掉。

                bool showNextColumn = tableSettingExpanded || missionSettingExpanded || (relicGrindExpanded && C.XPRelicGrind);

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

                        // 同一個「宇宙工具完成就自動繳交」開關在任務設定欄也有一份。
                        // 兩份原本是各自寫的複製碼（連 tooltip 都逐字重複）——
                        // 已收斂成 Settings_TableColumns 的單一實作，控制項 id 逐字保留。
                        Settings_TableColumns.DrawRelicTurninCheckbox("RelicGrind");

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

                }

                ImGui.EndTable();
            }

            DrawFilterRow(scale);

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

        /// <summary>
        /// 篩選列（2026-08-09 UI 重構第五批）。
        /// 這一列只裝「會改變你現在看到什麼」的控制項，所以永遠可見、就在任務表正上方；
        /// 「設好就不會再動」的東西留在上面的「任務頁設定」分節裡。
        /// <br></br>
        /// 📌 檢視切換（任務清單／完成度）與側欄那兩個項目是**同一件事的兩個入口**：
        /// 它改的就是側欄目前的選取項，不是另外一份狀態，所以兩邊永遠一致。
        /// 🔴 這裡**不能**直接寫 <c>C.ShowCompletionWindow</c> —— 那個值每一幀都會被
        /// <c>MainWindow.MainBody()</c> 依側欄選取項覆寫回去，直接寫會在下一幀被靜默還原
        /// （看起來就是「點了沒反應」）。
        /// <br></br>
        /// 📌 三個勾選項是從「表格設定」／「完成度表格設定」搬上來的，程式碼逐字照搬，
        /// 包含「只顯示所選職業」會順手關掉 <c>ShowCompletionOnlyJob</c> 這個既有副作用。
        /// <br></br>
        /// ⚠️ 整列包在一個開了水平捲軸的子視窗裡（與下面那排分類按鈕同一個作法）：
        /// 繁中標籤比英文長，視窗窄的時候後面幾項會被切掉而**不會換行** ——
        /// 有捲軸至少永遠碰得到。
        /// </summary>
        private static void DrawFilterRow(float scale)
        {
            float scrollbarSize = ImGui.GetStyle().ScrollbarSize;
            float rowHeight = ImGui.GetFrameHeight() + scrollbarSize + 4 * scale;

            using (var filterRow = ImRaii.Child("##modeSelect_FilterRow", new Vector2(0, rowHeight), false, ImGuiWindowFlags.HorizontalScrollbar))
            {
                if (!filterRow.Success)
                    return;

                bool completionView = SelectableSidebar.currentSelection == "modeSelect_Completion";
                if (ImGui.RadioButton("Mission List".Loc() + "###ICEViewMissionList", !completionView))
                    SelectableSidebar.currentSelection = "modeSelect_Standard";
                ImGui.SameLine(0, 6 * scale);
                if (ImGui.RadioButton("Completion".Loc() + "###ICEViewCompletion", completionView))
                    SelectableSidebar.currentSelection = "modeSelect_Completion";

                ImGui.SameLine(0, 14 * scale);

                ImGui.SetNextItemWidth(180 * scale);
                var needle = MissionNameFilter;
                if (ImGui.InputTextWithHint("###ICEMissionSearch", "Search by name or ID".Loc(), ref needle, 64))
                    MissionNameFilter = needle;

                // 清除鈕只在真的有在篩選時出現 —— 它同時是「現在有東西被藏起來」的指示：
                // 任務表突然變短的時候，這顆按鈕就在旁邊，不必去猜是不是壞了。
                if (!string.IsNullOrEmpty(MissionNameFilter))
                {
                    ImGui.SameLine(0, 4 * scale);
                    if (ImGuiEx.IconButton(FontAwesomeIcon.Times, "ICEMissionSearchClear"))
                        MissionNameFilter = string.Empty;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Clear".Loc());
                }

                ImGui.SameLine(0, 14 * scale);

                bool hideUnsupported = C.HideUnsupportedMissions;
                if (ImGui.Checkbox("Hide Unsupported Missions".Loc() + "###ICEHideUnsupportedMissions", ref hideUnsupported))
                {
                    C.HideUnsupportedMissions = hideUnsupported;
                    C.Save();
                }

                // 這兩個只在完成度檢視下有作用（任務表的列迴圈也是這樣判的），
                // 所以也只在那時候出現 —— 與它們原本住在「完成度表格設定」欄裡的可達性相同。
                if (C.ShowCompletionWindow)
                {
                    ImGui.SameLine(0, 14 * scale);
                    bool showSelectedJobOnly = C.ShowSelectedJobOnly;
                    if (ImGui.Checkbox("Show only selected job".Loc() + "###ICEShowSelectedJobOnly", ref showSelectedJobOnly))
                    {
                        C.ShowSelectedJobOnly = showSelectedJobOnly;
                        if (showSelectedJobOnly)
                            C.ShowCompletionOnlyJob = false;
                        C.Save();
                    }

                    ImGui.SameLine(0, 14 * scale);
                    bool nonGold = C.ShowCompletion_MissingGold;
                    if (ImGui.Checkbox("Show Only Non-Gold Missions".Loc() + "###ICEShowCompletionMissingGold", ref nonGold))
                    {
                        C.ShowCompletion_MissingGold = nonGold;
                        C.Save();
                    }
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
