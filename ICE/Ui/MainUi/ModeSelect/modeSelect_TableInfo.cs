using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using System.Collections.Generic;
using static MissionTimer;

namespace ICE.Ui.MainUi.ModeSelect
{
    internal partial class modeSelect_TableInfo
    {
        // 🔴 死碼：只有本檔的 DrawTabButton 讀寫它，而 DrawTabButton 本身零呼叫端。
        //    保留不刪（使用者裁決：死碼只要確認真的死，不用刪）。
        public static HashSet<string> selectedTabs = new HashSet<string>();
        public static uint selectedMission = 0;
        public static List<string> JokeList = new()
        {
            "What is a pirates favorite letter?\n" +
            "You might thing it's R, but tis first love was the C\n" +
            "(It helps if you verbally say it like a pirate)",

            "You know, I was reading this book about anti-gravity recently,\n" +
            "and honestly I'm having a hard time putting it down",

            "Why are tennis pros always hugging each other?\n" +
            "Because they start their match at \"Love All\"",

            "Why can't ghost have babies?\n" +
            "Because they have hallow-eenies",

            "How do you save a drowning pirate?\n" +
            "You give him Cprrrrrr",

            "What is a skeleton's favorite snack?\n" +
            "Ribs! Spare Ribs!",

            "Honestly, just wanted to say thank you for using my plugin, you're appreciated <3",


        };
        public static int jokeId = 0;

        // 🔴 死碼：只有 DrawCollapsibleHeader / DrawCollapsibleSection 用到，那兩個都進不來。
        //    保留不刪（使用者裁決：死碼只要確認真的死，不用刪）。
        public static Dictionary<string, bool> headerStates = new();

        public static Dictionary<string, List<Mission>> missionList = new()
        {
            ["Critical"] = new List<Mission>(),
            ["Weather"] = new List<Mission>(),
            ["Timed"] = new List<Mission>(),
            ["Sequence"] = new List<Mission>(),
            ["ARank"] = new List<Mission>(),
            ["BRank"] = new List<Mission>(),
            ["CRank"] = new List<Mission>(),
            ["DRank"] = new List<Mission>(),
            ["All Enabled"] = new List<Mission>(),
        };

        public class Mission
        {
            public uint id;
            public bool enabled;
        }

        public static List<Mission> SortMissionList(List<Mission> missions)
            => SortByTableOption(missions, m => m.id).ToList();

        /// <summary>
        /// 依「表格設定 → 排序方式」排序任意帶有任務 ID 的集合。
        /// 表格顯示與（啟用時）任務挑選共用同一份邏輯，避免兩邊各寫一次而漂移。
        /// ⚠️ 查不到任務資料的項目一律排到最後，不要用字典 indexer —— 那會丟例外。
        /// </summary>
        public static IEnumerable<T> SortByTableOption<T>(IEnumerable<T> items, Func<T, uint> idSelector)
        {
            var missionInfo = CosmicHelper.SheetMissionDict;

            string NameOf(T m) => missionInfo.TryGetValue(idSelector(m), out var i) ? i.Name : string.Empty;
            uint CosmoOf(T m) => missionInfo.TryGetValue(idSelector(m), out var i) ? i.CosmoCredit : 0;
            uint LunarOf(T m) => missionInfo.TryGetValue(idSelector(m), out var i) ? i.LunarCredit : 0;
            uint MarkerOf(T m) => missionInfo.TryGetValue(idSelector(m), out var i) ? i.MarkerId : uint.MaxValue;
            uint ScoreOf(T m) => missionInfo.TryGetValue(idSelector(m), out var i) ? i.ClassScore : 0;
            double ExpOf(T m, uint type) => missionInfo.TryGetValue(idSelector(m), out var i)
                ? i.RelicXpInfo.Where(exp => exp.Key == type).Sum(exp => exp.Value)
                : 0d;

            // 實測「有效」每分成果：加權總分 ÷（交件耗時＋放棄耗時）。
            // 沒有實測記錄（或查不到任務資料）一律回 0 —— 降冪排序下自然落到最後，
            // 與這個函式既有的「查不到就排最後」慣例一致。
            double EffectiveScoreOf(T m)
            {
                var id = idSelector(m);
                if (!missionInfo.TryGetValue(id, out var i)) return 0;
                if (!C.MissionConfig.TryGetValue(id, out var cfg)) return 0;
                return MissionStatsCalculator.CalculateEffectiveScorePerMinute(
                    cfg.TurninRecords, i.ClassScore, cfg.AbandonedTimeSeconds, cfg.TotalCompletions);
            }

            return C.TableSortOption switch
            {
                1 => items.OrderBy(NameOf),
                2 => items.OrderByDescending(CosmoOf),
                3 => items.OrderByDescending(LunarOf),
                4 => items.OrderByDescending(m => ExpOf(m, 1)),
                5 => items.OrderByDescending(m => ExpOf(m, 2)),
                6 => items.OrderByDescending(m => ExpOf(m, 3)),
                7 => items.OrderByDescending(m => ExpOf(m, 4)),
                8 => items.OrderByDescending(m => ExpOf(m, 5)),
                9 => items.OrderBy(MarkerOf),
                10 => items.OrderByDescending(ScoreOf),
                11 => items.OrderByDescending(EffectiveScoreOf),
                _ => items,   // 0 = 依 ID，也就是維持原本的順序
            };
        }

        /// <summary>
        /// 把秒數格式化成 h:mm:ss（小時不進位成天）。
        /// </summary>
        /// <remarks>
        /// 🔴 不要用 <c>TimeSpan.ToString("hh\:mm\:ss")</c> —— 那個 <c>hh</c> 是「一天之內的
        /// 小時數」(0~23)，超過 24 小時會**靜默**把天數丟掉：30 小時會顯示成 06:00:00，
        /// 看起來像個正常數字，不像壞掉。放棄耗時是長期累積值，一定會走到那個範圍。
        /// </remarks>
        private static string FormatTotalTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds > 0 ? seconds : 0);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        // 🔴 死碼（不可達，但**不是**零呼叫端 —— 差別要說清楚）：
        //    它有一個呼叫端，就是下面的 DrawCollapsibleSection，而那個才是零呼叫端。
        //    整叢（DrawCollapsibleHeader → DrawCollapsibleSection → DrawTabButton → selectedTabs/headerStates）
        //    從外面都進不來。若日後要清，必須整叢一起評估，只看單一方法的呼叫端數會得到錯的答案。
        //    保留不刪（使用者裁決：死碼只要確認真的死，不用刪）。
        public static void DrawCollapsibleHeader(string id, string label, float spacing = 4f, Vector4? borderColor = null, Vector4? backgroundColor = null)
        {
            const float padding = 6.0f;
            const float borderRadius = 2.0f;

            // Initialize header state if needed
            if (!headerStates.ContainsKey(id))
                headerStates[id] = false;

            // Calculate dimensions
            var drawList = ImGui.GetWindowDrawList();
            var cursorPos = ImGui.GetCursorScreenPos();
            var windowWidth = ImGui.GetContentRegionAvail().X;
            var textSize = ImGui.CalcTextSize(label);
            var bgHeight = textSize.Y + padding * 2;

            // Define header bounds
            var headerRectMin = cursorPos;
            var headerRectMax = new Vector2(cursorPos.X + windowWidth, cursorPos.Y + bgHeight);

            // Use provided colors or defaults
            var bgColor = backgroundColor ?? new Vector4(0.2f, 0.2f, 0.2f, 1f);
            var borderCol = borderColor ?? ImGuiColors.ParsedGold;

            // Draw background and border
            drawList.AddRectFilled(headerRectMin, headerRectMax, ImGui.GetColorU32(bgColor), borderRadius);
            drawList.AddRect(headerRectMin, headerRectMax, ImGui.GetColorU32(borderCol), borderRadius);

            // Draw centered label
            var textPos = new Vector2(
                cursorPos.X + (windowWidth - textSize.X) * 0.5f,
                cursorPos.Y + padding
            );
            drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), label);

            // Handle interaction
            ImGui.SetCursorScreenPos(cursorPos);
            ImGui.PushID(id);
            ImGui.InvisibleButton("##header", new Vector2(windowWidth, bgHeight));
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                headerStates[id] = !headerStates[id];
            ImGui.PopID();

            // Move cursor past header
            ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + bgHeight + spacing));
        }

        // 🔴 死碼：零呼叫端（靜態掃描；本 repo 無反射式 UI 探索）。
        //    ⚠️ 它唯一的內容繪製呼叫（MissionInfoV2）本來就已經被註解掉了，
        //       所以就算接回去也只會畫出一個空的可收合標題。
        //    保留不刪（使用者裁決：死碼只要確認真的死，不用刪）。
        public static void DrawCollapsibleSection(string id, string label, int enabled, List<Mission> missions)
        {
            DrawCollapsibleHeader(id, $"{label} | Enabled: {enabled}");
            if (headerStates.TryGetValue(id, out var isOpen) && isOpen)
            {
                // MissionInfoV2(id, SortMissionList(missions));
            }
        }

        // 🔴 死碼：零呼叫端（靜態掃描；本 repo 無反射式 UI 探索）。selectedTabs 的唯一讀寫者。
        //    保留不刪（使用者裁決：死碼只要確認真的死，不用刪）。
        public static bool DrawTabButton(string label, string tabIndex)
        {
            if (selectedTabs.Contains(tabIndex))
            {
                var activeColor = ImGui.GetStyle().Colors[(int)ImGuiCol.TabActive];
                ImGui.PushStyleColor(ImGuiCol.Button, activeColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, activeColor);
            }

            bool clicked = ImGui.Button(label);

            if (selectedTabs.Contains(tabIndex))
                ImGui.PopStyleColor(2);

            if (clicked)
            {
                if (selectedTabs.Contains(tabIndex))
                    selectedTabs.Remove(tabIndex);
                else
                    selectedTabs.Add(tabIndex);
            }

            return clicked;
        }

        public static unsafe void DrawMissionTablev2(string headerName, string tableName, List<Mission> missions)
        {
            // Setting up the size of the table here, ideally I want this to be the width of the current available space
            // Also setting up the header for here as well
            var availableSpace = ImGui.GetContentRegionAvail().X;
            var headerDisplay = "?? Missions".Loc(headerName.Loc());
            var textSize = ImGui.CalcTextSize(headerDisplay);
            var headerPadding = new Vector2(10, 5);
            var headerHeight = textSize.Y + headerPadding.Y * 2;

            // Custom header to display above the table. This is moreso for quick user viewability
            using (ImRaii.Child($"Table Header: {headerName}", new Vector2(availableSpace, headerHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                var centeredPosX = (availableSpace - textSize.X) / 2;

                ImGui.SetCursorPosY(headerPadding.Y);
                ImGui.SetCursorPosX(centeredPosX);
                ImGui.Text(headerDisplay);
            }

            // Table settings, just so I can sort it out visibly vs... being shoved in the table
            ImGuiTableFlags tableFlags = ImGuiTableFlags.RowBg |
                                ImGuiTableFlags.Borders |
                                ImGuiTableFlags.Reorderable |
                                ImGuiTableFlags.Hideable |
                                ImGuiTableFlags.SizingFixedFit;
            int tableTotalColumns = 18; // How many columns am I using. 

            // This is here to auto show/hide specific columns that might not be necessary (gathering profiles, and planetary tokens as Ex.)
            bool hasGathering = false;
            bool hasToken = false;

            foreach (var mission in missions)
            {
                var id = mission.id;
                var missionInfo = CosmicHelper.SheetMissionDict[id];

                if (AnyGatheringJob(missionInfo.Jobs))
                    hasGathering = true;
                if (missionInfo.RewardItemAmount > 0)
                    hasToken = true;
            }

            if (ImGui.BeginTable($"MissionList_{tableName}", tableTotalColumns, tableFlags))
            {
                #region Table Column Setup

                ImGui.TableSetupColumn("Enabled".Loc()); // 0
                ImGui.TableSetupColumn("Job".Loc());
                ImGui.TableSetupColumn("Manual".Loc());
                ImGui.TableSetupColumn("ID".Loc());
                ImGui.TableSetupColumn("✓".Loc());
                ImGui.TableSetupColumn("Mission Name".Loc());
                ImGui.TableSetupColumn("Cosmo".Loc());
                ImGui.TableSetupColumn("Lunar".Loc());
                ImGui.TableSetupColumn("Score".Loc());
                ImGui.TableSetupColumn("Reward Item".Loc()); // 9

                // Xp Columns Here
                float padding = 10f;
                float xpWidth = ImGui.CalcTextSize("III").X + padding;
                ImGui.TableSetupColumn("I".Loc()); // 10
                ImGui.TableSetupColumn("II".Loc());
                ImGui.TableSetupColumn("III".Loc());
                ImGui.TableSetupColumn("IV".Loc());
                ImGui.TableSetupColumn("V".Loc()); // 14

                ImGui.TableSetupColumn("Turnin Mode".Loc()); // 15
                ImGui.TableSetupColumn("Gathering Profile".Loc());
                ImGui.TableSetupColumn("Mission Notes".Loc()); // 17

                #endregion

                #region Auto-Hiding Columns

                ImGui.TableSetColumnEnabled(1, (C.ShowCompletionWindow || C.GrindProvisionals)); // Job Column (Useful for provisionals/Timed)
                ImGui.TableSetColumnEnabled(2, C.ShowManualMode);

                if (C.Auto_ShowTokens)
                {
                    ImGui.TableSetColumnEnabled(9, hasToken);
                }

                ImGui.TableSetColumnEnabled(16, hasGathering);

                #endregion

                #region Custom Header Stuff

                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                int columnIndexCount = 0;

                #region Enabled Column

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Enabled".Loc());
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    ImGui.OpenPopup("Enabled Options");
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Enable/disable mission for automation".Loc());
                    ImGui.Text("Left click for options".Loc());
                    ImGui.EndTooltip();
                }
                if (ImGui.BeginPopup("Enabled Options"))
                {
                    if (ImGui.Button("Enable All".Loc() + "###ICEEnableAllMissions"))
                    {
                        foreach (var mission in missions)
                        {
                            C.MissionConfig[mission.id].Enabled = true;
                            if (GetOnlyPreviousMissionsRecursive(mission.id).Count > 0)
                            {
                                foreach (var prevMission in GetOnlyPreviousMissionsRecursive(mission.id))
                                {
                                    var prevMissionConfig = C.MissionConfig[prevMission];
                                    prevMissionConfig.Enabled = true;
                                }
                            }
                        }
                        C.Save();
                    }

                    if (ImGui.Button("Disable All".Loc() + "###ICEDisableAllMissions"))
                    {
                        foreach (var mission in missions)
                        {
                            C.MissionConfig[mission.id].Enabled = false;
                        }
                        C.Save();
                    }

                    ImGui.EndPopup();
                }
                columnIndexCount++;

                #endregion

                #region Jobs

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Jobs".Loc());
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    ImGui.OpenPopup("Jobs Options");
                }
                if (ImGui.BeginPopup("Jobs Options"))
                {
                    bool showAllJobs = C.ShowCompletionOnlyJob;
                    if (ImGui.RadioButton("Show All Jobs".Loc() + "###ICEShowAllJobs", !showAllJobs))
                    {
                        C.ShowCompletionOnlyJob = false;
                        C.Save();
                    }
                    if (ImGui.RadioButton("Show Only Current Job".Loc() + "###ICEShowOnlyCurrentJob", showAllJobs))
                    {
                        C.ShowCompletionOnlyJob = true;
                        C.Save();
                    }
                    ImGui.EndPopup();
                }
                columnIndexCount++;

                #endregion

                #region Manual

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Manual".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Manual mode - requires manual intervention".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region ID

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("ID".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Mission ID number".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region Completed

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("✓".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Mission completion status".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region Mission Name

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Mission Name".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Click mission name to view details".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region Cosmocredits

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Cosmo".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Cosmic Credits reward".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region Planetary Credits

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Planetary".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Planetary Credits reward".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region Score

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Score".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Class Score reward".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region Planet Tokens

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Token".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Tokens that can be earned from this mission".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region Relic XP

                string[] xpLabels = { "I", "II", "III", "IV", "V" };
                for (int i = 0; i < 5; i++)
                {
                    ImGui.TableSetColumnIndex(columnIndexCount);
                    ImGui.TableHeader(xpLabels[i]);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Relic XP Type ?? reward".Loc(xpLabels[i]));
                        ImGui.EndTooltip();
                    }
                    columnIndexCount++;
                }

                #endregion

                #region Turnin Mode

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Turnin Mode".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Configure mission turnin settings".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region Gathering Profile

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Gathering Profile".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Select gathering profile for gather missions".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #region Mission Notes

                ImGui.TableSetColumnIndex(columnIndexCount);
                ImGui.TableHeader("Mission Notes".Loc());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Additional mission information and requirements".Loc());
                    ImGui.EndTooltip();
                }
                columnIndexCount++;

                #endregion

                #endregion

                foreach (var entry in missions)
                {
                    var Id = entry.id;
                    var missionConfig = C.MissionConfig[Id];
                    var missionInfo = CosmicHelper.SheetMissionDict[Id];

                    // 篩選列的搜尋：只影響「這一列畫不畫」。
                    // ⚠️ 刻意放在這裡，不放在建清單的地方 —— 分類按鈕上的計數（「已啟用 [12]」
                    //    那種）算的是真正啟用的筆數，被搜尋字串改掉會直接誤導人。
                    if (!modeSelect_Standard.PassesNameFilter(Id, missionInfo.Name))
                        continue;

                    // 判定與自動選任務共用同一個函式，表上顯示的與流程實際會跳過的一定一致。
                    bool unsupported = MissionSupport.IsUnsupported(Id, out var unsupportedReason);
                    bool hideUnsupported = C.HideUnsupportedMissions;

                    if (unsupported && hideUnsupported)
                        continue;

                    if (C.ShowCompletionWindow)
                    {
                        if (C.ShowCompletion_MissingGold)
                        {
                            var managerPtr = WKSManager.Instance();
                            if (managerPtr == null) continue;

                            var isGold = managerPtr->IsMissionGolded(Id);

                            if (isGold)
                                continue;
                        }
                        if (C.ShowSelectedJobOnly && !CosmicHelper.SheetMissionDict[Id].Jobs.Contains(C.SelectedJob))
                        {
                            continue;
                        }
                    }

                    ImGui.TableNextRow();
                    ImGui.PushID(Id);

                    #region Enabled Column Stuff

                    ImGui.TableSetColumnIndex(0);
                    bool enabled = missionConfig.Enabled;
                    if (Table_CenterCheckbox("##EnableMission", ref enabled))
                    {
                        missionConfig.Enabled = enabled;
                        if (missionConfig.Enabled == true)
                        {
                            if (GetOnlyPreviousMissionsRecursive(Id).Count >0)
                            {
                                foreach (var prevMission in GetOnlyPreviousMissionsRecursive(Id))
                                {
                                    var prevMissionConfig = C.MissionConfig[prevMission];
                                    prevMissionConfig.Enabled = true;
                                }
                            }
                        }

                        C.Save();
                    }
                    if (ImGui.IsItemClicked())
                    {
                        selectedMission = Id;
                    }

                    #endregion

                    #region Job Info

                    ImGui.TableNextColumn();
                    if (missionInfo.Jobs.Count > 1)
                    {
                        ISharedImmediateTexture? job1Icon = CosmicHelper.JobIconDict[FirstJob(missionInfo.Jobs)];
                        ISharedImmediateTexture? job2Icon = CosmicHelper.JobIconDict[LastJob(missionInfo.Jobs)];
                        Vector2 imageSize = new Vector2(23, 23);

                        ImGui.Image(job1Icon.GetWrapOrEmpty().Handle, imageSize);
                        ImGui.SameLine(0, 2);
                        ImGui.Image(job2Icon.GetWrapOrEmpty().Handle, imageSize);
                    }
                    else
                    {
                        ISharedImmediateTexture? job1Icon = CosmicHelper.JobIconDict[FirstJob(missionInfo.Jobs)];
                        Vector2 imageSize = new Vector2(23, 23);
                        ImGui.Image(job1Icon.GetWrapOrEmpty().Handle, imageSize);
                    }

                    #endregion

                    #region Manual Mode

                    ImGui.TableNextColumn();
                    bool manualMode = missionConfig.ManualMode;
                    if (Table_CenterCheckbox("##Manual Mode", ref manualMode))
                    {
                        missionConfig.ManualMode = manualMode;
                        C.Save();
                    }
                    if (ImGui.IsItemClicked())
                    {
                        selectedMission = Id;
                    }

                    #endregion

                    #region Mission Id

                    ImGui.TableNextColumn();
                    Table_FullCenterText(NumberText(Id));

                    #endregion

                    #region Completion Status

                    ImGui.TableNextColumn();
                    CompletionStatus_Formatted(Id);

                    #endregion

                    #region Mission Name + Flag Info

                    ImGui.TableNextColumn();
                    if (unsupported)
                    {
                        // 三角形圖示留著（既有使用者認得），但只有圖示的話滑過去才看得到原因；
                        // 補一個文字標記，讓「掃一眼表格」就分得出來，並與疊加層／聊天訊息用同一個詞。
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.0f, 0.0f, 1.0f)); // Red color (RGBA)
                        ImGuiEx.IconWithTooltip(FontAwesomeIcon.ExclamationTriangle, MissionSupport.ReasonText(unsupportedReason));
                        ImGui.SameLine(0, 4);
                        ImGui.TextUnformatted(MissionSupport.Marker);
                        ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.TextUnformatted(MissionSupport.ReasonText(unsupportedReason));
                            ImGui.EndTooltip();
                        }
                        if (ImGui.IsItemClicked())
                        {
                            selectedMission = Id;
                        }
                        ImGui.SameLine(0, 4);
                    }
                    if (missionInfo.Attributes.HasFlag(MissionAttributes.ExpertCraft))
                    {
                        if (EzThrottler.Throttle("Throttling the manip update every couple of seconds", 1000))
                            PlayerHelper.UpdateHasManip();

                        var crafterJobId = FirstCrafterJob(missionInfo.Jobs);
                        if (PlayerHelper.ManipClassInfo.TryGetValue(crafterJobId, out var manipInfo) && !manipInfo.HasUnlocked)
                        {
                            var color = EColor.Yellow;
                            ImGuiEx.IconWithTooltip(color, FontAwesomeIcon.ExclamationTriangle, 
                                                    ("This is an expert craft by the games definition, and you don't have manipulation unlocked on this class.\n" +
                                                    "You can enable this yourself, but do note that artisan will not allow you to craft with it until you've unlocked that skill.\n" +
                                                    "You can still make a macro if you'd like, but it's either that or go do the class quest up to like... 68").Loc());
                        }
                        ImGui.SameLine();
                    }
                    var missingUnlock = CollectableUnlock.Missing(Id);
                    if (missingUnlock != CollectableUnlock.Unlock.None)
                    {
                        ImGuiEx.IconWithTooltip(EColor.Yellow, FontAwesomeIcon.ExclamationTriangle, CollectableUnlock.TooltipText(missingUnlock));
                        ImGui.SameLine();
                    }

                    ImGui.Text(missionInfo.Name);
                    if (ImGui.IsItemClicked())
                    {
                        selectedMission = Id;
                    }
                    if (missionInfo.MarkerId != 0)
                    {
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.Text(IconText(FontAwesomeIcon.Flag));
                        ImGui.PopFont();
                        if (ImGui.IsItemClicked())
                        {
                            selectedMission = Id;
                            Utils.SetGatheringRing(missionInfo.TerritoryId, (int)missionInfo.MapPosition.X, (int)missionInfo.MapPosition.Y, missionInfo.Radius, missionInfo.Name);
                        }
                    }
                    if (GatheringUtil.CriticalLocations.TryGetValue(Id, out var criticalLoc))
                    {
                        ImGui.SameLine();
                        ImGuiEx.Icon(FontAwesomeIcon.FlagCheckered);
                        if (ImGui.IsItemClicked())
                        {
                            Utils.SetFlagForNPC(missionInfo.TerritoryId, criticalLoc.MapInfo.X, criticalLoc.MapInfo.Y);
                        }
                    }

                    #endregion

                    #region Cosmo | Planetary | Class Score | Tokens

                    ImGui.TableNextColumn();
                    Table_FullCenterText(NumberText(missionInfo.CosmoCredit));

                    ImGui.TableNextColumn();
                    Table_FullCenterText(NumberText(missionInfo.LunarCredit));

                    ImGui.TableNextColumn();
                    Table_FullCenterText(NumberText(missionInfo.ClassScore));

                    ImGui.TableNextColumn();
                    string itemAmount = missionInfo.RewardItemAmount > 0 ? NumberText(missionInfo.RewardItemAmount) : "-";
                    Table_FullCenterText(itemAmount);

                    #endregion

                    #region Relic Xp Info

                    for (int i = 1; i < 6; i++)
                    {
                        ImGui.TableNextColumn();
                        missionInfo.RelicXpInfo.TryGetValue(i, out var xpAmount);
                        var relicXp = xpAmount >= 0 ? NumberText((uint)xpAmount) : xpAmount.ToString();

                        if (relicXp == "0")
                        {
                            relicXp = "-";
                        }

                        Table_FullCenterText(relicXp);
                    }

                    #endregion

                    #region Mission Turnins

                    ImGui.TableNextColumn();
                    if (missionInfo.Attributes.HasFlag(MissionAttributes.ScoreTimeRemaining))
                    {
                        Table_FullCenterText("Auto".Loc());
                        if (missionConfig.AutoTurnin == false)
                        {
                            missionConfig.AutoTurnin = true;
                            missionConfig.TurninGold = false;
                            missionConfig.TurninSilver = false;
                            missionConfig.TurninBronze = false;

                            C.Save();
                        }
                    }
                    else
                    {
                        if (Table_CenteredButton("Select Turnin".Loc()))
                        {
                            ImGui.OpenPopup("Mission Turnin Settings");
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            if (missionConfig.AutoTurnin)
                                ImGui.Text("Auto Turnin - True".Loc());
                            else
                            {
                                if (missionConfig.TurninGold)
                                    ImGui.Text("Gold Enabled".Loc());
                                if (missionConfig.TurninSilver)
                                    ImGui.Text("Silver Enabled".Loc());
                                if (missionConfig.TurninBronze)
                                    ImGui.Text("Bronze Enabled".Loc());
                            }

                            ImGui.EndTooltip();
                        }

                        if (ImGui.BeginPopup("Mission Turnin Settings"))
                        {
                            bool anyTurnin = missionConfig.AutoTurnin;
                            bool goldTurnin = missionConfig.TurninGold;
                            bool silverTurnin = missionConfig.TurninSilver;
                            bool bronzeTurnin = missionConfig.TurninBronze;

                            ImGui.Text("Select Turnin Options".Loc());
                            ImGui.Dummy(new Vector2(0, 2));

                            if (ImGui.Checkbox("Auto".Loc() + "###ICETurninAuto", ref anyTurnin))
                            {
                                if (anyTurnin)
                                {
                                    missionConfig.TurninGold = false;
                                    missionConfig.TurninSilver = false;
                                    missionConfig.TurninBronze = false;

                                    missionConfig.AutoTurnin = anyTurnin;
                                }
                                else
                                {
                                    if (!(bronzeTurnin && silverTurnin && goldTurnin))
                                    {
                                        missionConfig.AutoTurnin = true;
                                    }
                                }

                                C.Save();
                            }
                            ImGuiEx.HelpMarker("This option will strive to get the best result, but will turn in any result if necessary without stopping.".Loc());

                            ImGui.Separator();

                            if (ImGui.Checkbox("Gold".Loc() + "###ICETurninGold", ref goldTurnin))
                            {
                                if (anyTurnin && goldTurnin)
                                    missionConfig.AutoTurnin = false;

                                missionConfig.TurninGold = goldTurnin;
                                C.Save();
                            }
                            if (ImGui.Checkbox("Silver".Loc() + "###ICETurninSilver", ref silverTurnin))
                            {
                                if (anyTurnin && silverTurnin)
                                    missionConfig.AutoTurnin = false;

                                missionConfig.TurninSilver = silverTurnin;
                                C.Save();
                            }
                            if (ImGui.Checkbox("Bronze".Loc() + "###ICETurninBronze", ref bronzeTurnin))
                            {
                                if (anyTurnin && bronzeTurnin)
                                    missionConfig.AutoTurnin = false;

                                missionConfig.TurninBronze = bronzeTurnin;
                                C.Save();
                            }

                            ImGui.EndPopup();
                        }
                    }

                    #endregion

                    #region Gathering Profile Settings

                    bool gatherProfile = missionInfo.Attributes.HasFlag(MissionAttributes.Gather);
                    bool collectable = missionInfo.Attributes.HasFlag(MissionAttributes.Collectables) || missionInfo.Attributes.HasFlag(MissionAttributes.ReducedItems);

                    ImGui.TableNextColumn();
                    if (gatherProfile && !collectable)
                    {
                        string profileName = "???";
                        if (C.GatherProfiles.TryGetValue(missionConfig.GProfileId, out var profileSetting))
                        {
                            profileName = profileSetting.Name;
                        }
                        else
                        {
                            profileName = "???";
                        }

                        if (Table_CenteredButton($"{profileName}"))
                        {
                            ImGui.OpenPopup("Selecting Gathering Profile");
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("Select profile to use".Loc());
                            ImGui.EndTooltip();
                        }
                        if (ImGui.BeginPopup("Selecting Gathering Profile"))
                        {
                            ImGui.Text("Currently Selected: ??".Loc(profileName));
                            ImGui.Separator();

                            foreach (var profile in C.GatherProfiles)
                            {
                                var id = profile.Key;
                                bool profileSelected = missionConfig.GProfileId == id;
                                ImGui.PushID($"{id}_{profile.Value.Name}");
                                if (ImGui.RadioButton(profile.Value.Name, profileSelected))
                                {
                                    missionConfig.GProfileId = id;
                                    C.Save();
                                }
                                ImGui.PopID();
                            }

                            ImGui.EndPopup();
                        }
                    }
                    else if (gatherProfile && collectable)
                    {
                        Table_FullCenterText("Auto".Loc());
                    }
                    else if (missionInfo.Attributes.HasFlag(MissionAttributes.Fish))
                    {
                        if (Table_CenteredButton("Select Profile".Loc()))
                        {
                            ImGui.OpenPopup("Select Fishing Profile");
                        }
                        if (ImGui.BeginPopup("Select Fishing Profile"))
                        {
                            ImGui.Text("Fishing profile: ??".Loc(MissionSupport.NameWithMarker(Id, missionInfo.Name)));
                            ImGui.Separator();

                            // 這個彈出視窗正好是使用者唯一能「救回」缺內建設定的釣魚任務的地方
                            // （自己填一個 AutoHook preset 名稱），所以把缺什麼直接寫在這裡。
                            if (unsupportedReason == MissionSupport.UnsupportedReason.MissingFishingPreset)
                            {
                                ImGui.TextColored(ImGuiColors.DalamudRed,
                                    ("ICE ships no built-in preset for this mission. Uncheck the box below and type the name of "
                                     + "your own AutoHook preset to make ICE run it.").Loc());
                                ImGui.Separator();
                            }

                            bool builtInPreset = missionConfig.Use_BuildinPreset;
                            if (ImGui.Checkbox("Use Built In Preset".Loc() + "###ICEUseBuiltInPreset", ref builtInPreset))
                            {
                                missionConfig.Use_BuildinPreset = builtInPreset;
                                C.Save();
                            }
                            ImGuiEx.HelpMarker(("Having this enabled means it will use the default preset that is included with the plugin for autohook. \n" +
                                               "If you would like to use one that you already have in autohook, you can un-checkmark this and type the name of it below").Loc());
                            using (ImRaii.Disabled(builtInPreset))
                            {
                                string presetName = missionConfig.AutoHookPresetName;
                                ImGui.SetNextItemWidth(200);
                                if (ImGui.InputText("Preset Name".Loc() + "###ICEAutoHookPresetName", ref presetName))
                                {
                                    missionConfig.AutoHookPresetName = presetName;
                                    C.Save();
                                }
                            }

                            ImGui.EndPopup();
                        }
                    }

                    #endregion

                    #region Notes

                    ImGui.TableNextColumn();
                    int notesCount = 0;

                    if (missionInfo.Attributes.HasFlag(MissionAttributes.ProvisionalTimed))
                    {
                        Table_FontCenter(FontAwesomeIcon.Clock);
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text($"{missionInfo.StartTime}:00 - {missionInfo.EndTime-1}:59");
                            ImGui.EndTooltip();
                        }
                        notesCount++;
                    }
                    if (missionInfo.Attributes.HasFlag(MissionAttributes.ProvisionalSequential))
                    {
                        Table_FontCenter(FontAwesomeIcon.ListOl);
                        if (ImGui.IsItemHovered())
                        {
                            var prevMissions = GetOnlyPreviousMissionsRecursive(Id);

                            ImGui.BeginTooltip();
                            ImGui.Text("Sequence Missions".Loc());
                            ImGui.Separator();
                            for (int i = 0; i < prevMissions.Count; i++)
                            {
                                var prevMission = prevMissions[i];
                                // GetOnlyPreviousMissionsRecursive 會把 LockedBehind 的 rowId 原封不動加進來，不保證在 SheetMissionDict 裡
                                // ⇒ 用 TryGetValue（查不到顯示 "???"）。這是每幀跑的 ImGui 迴圈，丟例外會讓整個視窗畫不出來。
                                var prevName = CosmicHelper.SheetMissionDict.TryGetValue(prevMission, out var prevEntry)
                                    ? prevEntry.Name : "???";
                                ImGui.Text($"{i + 1}: [{prevMission}] - {MissionSupport.NameWithMarker(prevMission, prevName)}");
                            }
                            ImGui.EndTooltip();
                        }
                        notesCount++;
                    }
                    if (missionInfo.Attributes.HasFlag(MissionAttributes.ProvisionalWeather))
                    {
                        if (notesCount > 0)
                            ImGui.SameLine(0, 2);

                        // 🔴 要守的是真正索引的 WeatherIconDict，不是 WeatherIds：WeatherIconDict 只在 TryGetFromGameIcon
                        //    **成功時**才寫入（見 ICEDictornaryCreation），所以它的鍵集合是 WeatherIds 的**子集**。
                        //    任何一張天氣圖示載不到，直接索引就會每幀丟 KeyNotFoundException 讓整個任務表畫不出來；
                        //    載不到就落到下面的 Cloud 圖示 fallback。
                        if (CosmicHelper.WeatherIconDict.TryGetValue(missionInfo.Weather, out var weatherIcon))
                        {
                            Vector2 ImageSize = new Vector2(23, 23);
                            ImGui.Image(weatherIcon.GetWrapOrEmpty().Handle, ImageSize);
                        }
                        else
                        {
                            Table_FontCenter(FontAwesomeIcon.Cloud);
                        }

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("Weather: ??".Loc(missionInfo.Weather));
                            ImGui.EndTooltip();
                        }
                        notesCount++;
                    }
                    if (CosmicHelper.MissionUnlock.TryGetValue(Id, out var unlock))
                    {
                        if (notesCount > 0)
                            ImGui.SameLine(0, 2);

                        if (Svc.Texture.GetFromGame("ui/uld/WKSMission_hr1.tex") is { } tex)
                        {
                            if (tex.TryGetWrap(out var wrap, out var exc))
                            {
                                ImGui.Image(wrap.Handle, new Vector2(23, 23), new Vector2(0.2347f, 0.3500f), new Vector2(0.2959f, 0.6500f));
                            }
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("The following missions are required to have gold before you can do this one".Loc());
                            foreach (var mission in unlock)
                            {
                                CompletionStatus_Normal(mission);
                                ImGui.SameLine();
                                // MissionUnlock 是寫死的表，跟 SheetMissionDict 沒有共同保證 ⇒ TryGetValue（查不到顯示 "???"）。
                                var unlockName = CosmicHelper.SheetMissionDict.TryGetValue(mission, out var unlockEntry) ? unlockEntry.Name : "???";
                                ImGui.Text($"[{mission}] - {MissionSupport.NameWithMarker(mission, unlockName)}");
                            }
                            ImGui.EndTooltip();
                        }
                        notesCount++;

                    }
                    if (missionInfo.Jobs.Count > 1)
                    {
                        if (notesCount > 0)
                            ImGui.SameLine(0, 2);

                        ISharedImmediateTexture? job1Icon = CosmicHelper.JobIconDict[FirstJob(missionInfo.Jobs)];
                        ISharedImmediateTexture? job2Icon = CosmicHelper.JobIconDict[LastJob(missionInfo.Jobs)];
                        Vector2 imageSize = new Vector2(23, 23);

                        ImGui.Image(job1Icon.GetWrapOrEmpty().Handle, imageSize);
                        ImGui.SameLine(0, 2);
                        ImGui.Image(job2Icon.GetWrapOrEmpty().Handle, imageSize);
                        notesCount++;
                    }
                    if (CosmicHelper.CustomMissionNotes.TryGetValue(Id, out var notes))
                    {
                        if (notesCount > 0)
                            ImGui.SameLine();
                        ImGuiEx.Icon(FontAwesomeIcon.Trophy);
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text(notes.NoteInfo);
                            ImGui.Text("Average Score Per Minute: ??".Loc(notes.SPM.ToString("N2")));

                            ImGui.EndTooltip();
                        }
                    }

                    #endregion

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
        }
    }
}
