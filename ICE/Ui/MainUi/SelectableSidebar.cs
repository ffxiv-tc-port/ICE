using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using ICE.Ui.MainUi.ModeSelect;
using ICE.Utilities.ImGuiTools;
using SharpDX.Direct2D1.Effects;
using System.Collections.Generic;
using System.Reflection;

namespace ICE.Ui.MainUi
{
    internal class SelectableSidebar
    {
        public static Dictionary<string, bool> categoryStates = new Dictionary<string, bool>();

        private static string PluginIcon = "ICE.Resources.Icon.png";
        public static string currentSelection = "modeSelect_Standard";

        public static void Draw()
        {
            var scale = ImGuiHelpers.GlobalScaleSafe;
            int baseSize = 200;
            var scaledWidth = baseSize * scale;

            if (ImGui.BeginChild("MainUi_Sidebar", new Vector2(scaledWidth, -1), true))
            {
                // Image/Icon of the plugin
                var pluginIcon = Svc.Texture.GetFromManifestResource(Assembly.GetExecutingAssembly(), PluginIcon).GetWrapOrEmpty();

                if (pluginIcon != null)
                {
                    // Getting the image size via the texture wrap (using the * after to manipulate the size cause, she big)
                    Vector2 imageSize = new Vector2(pluginIcon.Width, pluginIcon.Height) * 0.35f;

                    // Calculate the offset/centering here
                    float sidebarWidth = ImGui.GetContentRegionAvail().X;
                    float offsetX = (sidebarWidth - imageSize.X) / 2.0f;

                    // Center and drawing the image now
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
                    ImGui.Image(pluginIcon.Handle, imageSize);
                    if (ImGui.IsItemClicked())
                    {
                        var random = new Random();
                        modeSelect_TableInfo.jokeId = random.Next(0, modeSelect_TableInfo.JokeList.Count-1);
                        modeSelect_TableInfo.selectedMission = 0;
                    }

                    // Add spacing after image
                    ImGui.Dummy(new Vector2(0, 10));
                    ImGui.Separator();
                    ImGui.Dummy(new Vector2(0, 10));
                }

                // ── 新側欄骨架（2026-08-08 UI 重構第二批）────────────────────────
                // 一級項按「使用者在想什麼」分組，不再按「這段碼住在哪個檔」分組。
                // ⚠️ 每個分類都傳明確的 id（第 4 個具名參數），不要讓已在地化的 label 當 key。
                // ⚠️ 每個 DrawSelectableWithIcon 的第三個參數必須在 MainWindow.MainBody() 的
                //    switch 裡有對應的 case —— 打錯**不會編譯失敗**，只會變成一片空白頁。
                //    這一批新增/更動的 id 全部走過一遍了。
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Missions".Loc(), icon: FontAwesomeIcon.ListAlt, id: "cat_Missions"))
                {
                    // ⚠️ 這兩項刻意「沒有」合併成單一入口：它們不是同一頁的兩個名字，
                    //    而是切換 C.ShowCompletionWindow 的**唯二**開關（見 MainBody 的兩個 case）。
                    //    併成一項會讓使用者再也無法在兩種顯示模式之間切換 —— 那是功能損失，
                    //    不是版面整理。真要併成一項，得先決定那個模式切換改放哪裡。
                    DrawSelectableWithIcon(FontAwesomeIcon.List, "Standard".Loc(), "modeSelect_Standard");
                    DrawSelectableWithIcon(FontAwesomeIcon.Trophy, "Completion".Loc(), "modeSelect_Completion");
                    if (C.Show_MissionPriority)
                        DrawSelectableWithIcon(FontAwesomeIcon.SortAmountUp, "Mission Priority".Loc(), "setting_MissionPriority");
                    else
                        DrawHiddenNotice(1);
                }
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Gathering".Loc(), icon: FontAwesomeIcon.Leaf, id: "cat_Gathering"))
                {
                    if (C.Show_GatheringProfile)
                        DrawSelectableWithIcon(FontAwesomeIcon.Leaf, "Gathering Profile".Loc(), "setting_GatheringProfile");
                    else
                        DrawHiddenNotice(1);

                    // 刻意不加 C.Show_* 開關：這一頁的重點就是「讓使用者知道採集路線可以自己改」，
                    // 藏在偵錯視窗底下等於沒有。
                    DrawSelectableWithIcon(FontAwesomeIcon.MapSigns, "Gathering Routes".Loc(), "setting_GatherRoutes");
                }
                if (C.Show_HubActivities)
                {
                    if (ImGui_Tools.DrawCategoryHeader_AutoSize("Hub Activities".Loc(), icon: FontAwesomeIcon.Home, id: "cat_HubActivities"))
                    {
                        DrawSelectableWithImage(65112, "Credit Shopping".Loc(), "hubActivities_CreditShopping");
                        DrawSelectableWithImage(65127, "Gambling Settings".Loc(), "hubActivites_GambaSetting");
                    }
                }
                else
                {
                    // ⚠️ 這一個 Show_* 藏的是**整個分類**（連標題一起），不是分類底下的某一項，
                    //    所以提示要畫在分類原本的位置、外面，不能塞進 header 的 if 裡。
                    DrawHiddenNotice(1);
                }
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Mecha Ops".Loc(), icon: FontAwesomeIcon.Robot, id: "cat_MechaOps"))
                {
                    DrawSelectableWithIcon(FontAwesomeIcon.Robot, "Mecha Skill Ranges".Loc(), "setting_MechaOps");
                }
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Display & Overlay".Loc(), icon: FontAwesomeIcon.WindowMaximize, id: "cat_Display"))
                {
                    DrawSelectableWithIcon(FontAwesomeIcon.WindowMaximize, "Overlay Window".Loc(), "setting_Display");
                }
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Automation & Safety".Loc(), icon: FontAwesomeIcon.ShieldAlt, id: "cat_Automation"))
                {
                    if (C.Show_StopWhen)
                        DrawSelectableWithIcon(FontAwesomeIcon.Stop, "Stop When...".Loc(), "setting_StopWhen");
                    DrawSelectableWithIcon(FontAwesomeIcon.ExclamationTriangle, "Safety Settings".Loc(), "setting_Safety");

                    // ⚠️ 「其他設定」這一項在重構計畫的一級項清單裡**沒有**被列到，但不能拿掉：
                    //    Misc 設定頁被拆走 3 節（疊加層／機甲／安全）之後還剩 5 節
                    //    （自動使用道具／自動修理／時間紀錄／坐騎選擇／任務後指令，
                    //     加上 B3 才會搬走的顯示分頁開關），這幾節在新結構裡沒有指定去處。
                    //    拿掉入口＝它們直接變成使用者摸不到的死頁，所以先掛在這裡。
                    //    放這一類是因為剩下的內容以自動化為主（自動使用／自動修理／任務後指令）。
                    //    等它們各自有家之後再把這一項移走。
                    // 📌 刻意不另外開一個「Other Settings」分類：那個字串與 "Misc Settings"
                    //    的中文翻譯**都是「其他設定」**，會變成父項與子項同名，看起來像畫錯了。
                    if (C.Show_MiscSettings)
                        DrawSelectableWithIcon(FontAwesomeIcon.UserCog, "Misc Settings".Loc(), "setting_Misc");

                    // 這一類底下有兩項各自可被藏：停止條件與其他設定。合起來數，只畫一行。
                    DrawHiddenNotice((C.Show_StopWhen ? 0 : 1) + (C.Show_MiscSettings ? 0 : 1));
                }
                // 🔴 「介面」是唯一不受任何 C.Show_* 影響的分頁，而且必須保持如此：
                //    分頁顯示/隱藏的開關本身住在這一頁（B3 從 Misc ⑦ 搬進來）。
                //    如果它自己也能被藏起來，使用者就沒有任何辦法把藏掉的東西叫回來 ——
                //    那是個把自己鎖在門外、只能去改設定檔才救得回來的狀態。
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Interface".Loc(), icon: FontAwesomeIcon.SlidersH, id: "cat_Interface"))
                {
                    DrawSelectableWithIcon(FontAwesomeIcon.WindowRestore, "Show / Hide Tabs".Loc(), "setting_Interface");
                }
                var currentJob = C.SelectedJob;
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Moon Selection".Loc(), FontAwesomeIcon.Moon))
                {
                    string SinusAsset = "ICE.Resources.Sinus_Ardorum.png";
                    string PhaennaAsset = "ICE.Resources.Phaenna.png";

                    bool autoSelectMoon = C.AutoSelectMoon;
                    if (ImGui.Checkbox("Auto Select Moon".Loc() + "###ICEAutoSelectMoon", ref autoSelectMoon))
                    {
                        C.AutoSelectMoon = autoSelectMoon;
                        C.Save();
                    }
                    SyncAutoSelect();
                    ImGui.Dummy(new (0, 3));

                    float iconSize = 23 * scale;
                    float iconSpacing = 4;
                    float availWidth = ImGui.GetContentRegionAvail().X;
                    float startX = (availWidth - (iconSize + iconSpacing) * 4 + iconSpacing) * 0.5f;

                    ImGui.SetCursorPosX(startX);
                    bool sinusEnabled = C.ShowSinusMissions;
                    var SinusTexture = Svc.Texture.GetFromManifestResource(Assembly.GetExecutingAssembly(), SinusAsset).GetWrapOrEmpty();
                    if (StyledImageButton.DrawStyledImageButton(SinusTexture, new Vector2(iconSize, iconSize), sinusEnabled))
                    {
                        C.ShowSinusMissions = !sinusEnabled;
                        C.AutoSelectMoon = false;
                        C.Save();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Sinus Ardorum".Loc());
                    }

                    ImGui.SameLine();
                    bool phaennaEnabled = C.ShowPhaennaMissions;
                    var PhaennaTextures = Svc.Texture.GetFromManifestResource(Assembly.GetExecutingAssembly(), PhaennaAsset).GetWrapOrEmpty();

                    if (StyledImageButton.DrawStyledImageButton(PhaennaTextures, new Vector2(iconSize, iconSize), phaennaEnabled))
                    {
                        C.ShowPhaennaMissions = !phaennaEnabled;
                        C.AutoSelectMoon = false;
                        C.Save();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Phaenna".Loc());
                    }
                }
                if (C.AutoPickCurrentJob && (CosmicHelper.CrafterJobList.Contains(Player.JobId) || CosmicHelper.GatheringJobList.Contains(Player.JobId)) && C.SelectedJob != Player.JobId)
                {
                    C.SelectedJob = Player.JobId;
                    C.Save();
                }
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Class Selection".Loc(), imageTexture: GreyscaleJob()))
                {
                    float iconSize = 26 * scale;
                    float iconSpacing = 4;
                    float availWidth = ImGui.GetContentRegionAvail().X;
                    float startX = (availWidth - (iconSize + iconSpacing) * 4 + iconSpacing) * 0.5f;
                    
                    ImGui.SetCursorPosX(startX);
                    bool autoSelectJob = C.AutoPickCurrentJob;
                    if (ImGui.Checkbox("Auto Select".Loc() + "##AutoSelectJob", ref autoSelectJob))
                    {
                        C.AutoPickCurrentJob = autoSelectJob;
                        C.Save();
                    }

                    ImGui.SetCursorPosX(startX);
                    ImGui_Tools.DrawJobButtons(8, "CRP".Loc());
                    ImGui.SameLine(0, iconSpacing);
                    ImGui_Tools.DrawJobButtons(9, "BSM".Loc());
                    ImGui.SameLine(0, iconSpacing);
                    ImGui_Tools.DrawJobButtons(10, "ARM".Loc());
                    ImGui.SameLine(0, iconSpacing);
                    ImGui_Tools.DrawJobButtons(11, "GSM".Loc());

                    ImGui.SetCursorPosX(startX);
                    ImGui_Tools.DrawJobButtons(12, "LTW".Loc());
                    ImGui.SameLine(0, iconSpacing);
                    ImGui_Tools.DrawJobButtons(13, "WVR".Loc());
                    ImGui.SameLine(0, iconSpacing);
                    ImGui_Tools.DrawJobButtons(14, "ALC".Loc());
                    ImGui.SameLine(0, iconSpacing);
                    ImGui_Tools.DrawJobButtons(15, "CUL".Loc());

                    ImGui.SetCursorPosX(startX);
                    ImGui_Tools.DrawJobButtons(16, "MIN".Loc());
                    ImGui.SameLine(0, iconSpacing);
                    ImGui_Tools.DrawJobButtons(17, "BTN".Loc());
                    ImGui.SameLine(0, iconSpacing);
                    ImGui_Tools.DrawJobButtons(18, "FSH".Loc());
                }
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Tool Relic XP".Loc(), icon: FontAwesomeIcon.ArrowUpRightDots))
                {
                    if (PlayerHelper.IsInCosmicZone())
                    {
                        var jobId = C.SelectedJob;
                        ImGui.Image(CosmicHelper.JobIconDict[jobId].GetWrapOrEmpty().Handle, new(24, 24));
                        ImGui.SameLine(0, 2);
                        ImGui.AlignTextToFramePadding();
                        Relic_XP.DrawRelicXP(jobId);
                    }
                    else
                    {
                        ImGui.TextWrapped("You have to be in a cosmic area for us to view this info. Blame square for not making it always accesable".Loc());
                    }
                }
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Help & Diagnostics".Loc(), icon: FontAwesomeIcon.QuestionCircle, id: "cat_Help"))
                {
                    DrawSelectableWithIcon(FontAwesomeIcon.Question, "Requirements".Loc(), "helpSelect_Requirements");
                    DrawSelectableWithIcon(FontAwesomeIcon.Book, "Ice Logs".Loc(), "helpSelect_Logs");
                }
            }
            ImGui.EndChild();
        }

        // 自動選星的同步副作用。原本這 15 行在兩個地方各有一份完全相同的副本
        // （本檔的「Moon Selection」區塊，以及 modeSelect_Standard.Draw() 開頭），
        // 兩邊都可能在同一幀跑到 —— 條件是「使用者停在 Standard/Completion 頁」
        // 且「Moon Selection 這個分類是展開的」。
        //
        // ⚠️ 這裡刻意「保留兩個呼叫點」而不是改成單一無條件呼叫：
        //    本檔的副本在 DrawCategoryHeader_AutoSize("Moon Selection") 的 if 裡面，
        //    而 CategoryStates 預設是 false（未展開）⇒ 分類收合時這份根本不會執行。
        //    改成無條件呼叫會讓「不在 Standard 頁 ＋ Moon Selection 收合」這個組合
        //    從「完全不同步」變成「會同步並寫檔」，那是行為改變，不在本批範圍內。
        //    改用每幀閘門達成「一幀最多跑一次」，可達性與原本逐字相同。
        //
        // 📌 副作用（自動改寫 ShowSinusMissions/ShowPhaennaMissions 並 C.Save()）是既有行為，
        //    不是這次新加的。
        private static int _autoSelectSyncedFrame = -1;

        public static void SyncAutoSelect()
        {
            var frame = ImGui.GetFrameCount();
            if (_autoSelectSyncedFrame == frame)
                return;
            _autoSelectSyncedFrame = frame;

            if (!C.AutoSelectMoon)
                return;

            if (PlayerHelper.IsInSinusArdorum() && (!C.ShowSinusMissions || C.ShowPhaennaMissions))
            {
                C.ShowSinusMissions = true;
                C.ShowPhaennaMissions = false;
                C.Save();
            }
            else if (PlayerHelper.IsInPhaenna() && (C.ShowSinusMissions || !C.ShowPhaennaMissions))
            {
                C.ShowSinusMissions = false;
                C.ShowPhaennaMissions = true;
                C.Save();
            }
        }

        // 灰字提示：這個位置本來有東西，被「介面」分頁的開關藏起來了。
        //
        // 🔑 為什麼要留這一行：藏起來的項目如果完全不留痕跡，使用者看到的是
        //    「功能不見了」，分不出是自己關的、還是外掛壞了。
        //    「不知道」本身要在列上看得見 —— tooltip 藏的是「為什麼」，不是「有沒有問題」。
        // 📌 用 TextWrapped ＋ TextDisabled 色而不是 TextDisabled()：側欄只有 200px 寬，
        //    單行的話中文會被裁掉。
        private static void DrawHiddenNotice(int hiddenCount)
        {
            if (hiddenCount <= 0)
                return;

            float scale = ImGuiHelpers.GlobalScale;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 16 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextWrapped("?? hidden - turn back on in Interface".Loc(hiddenCount));
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 2 * scale));
        }

        private static void DrawSelectableWithIcon(FontAwesomeIcon icon, string label, string id)
        {
            bool isSelected = currentSelection == id;
            float scale = ImGuiHelpers.GlobalScale;

            // Change background color if selected
            if (isSelected)
            {
                ImGui.PushStyleColor(ImGuiCol.Header, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            }

            // Indent for items under categories (scaled)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 16 * scale);

            float width = ImGui.GetContentRegionAvail().X;

            // Invisible selectable as the clickable area (scaled height)
            if (ImGui.Selectable($"##{id}", isSelected, ImGuiSelectableFlags.None, new Vector2(width, 25 * scale)))
            {
                currentSelection = id;
            }

            if (isSelected)
            {
                ImGui.PopStyleColor();

                // Draw colored bar on the left side
                var drawList = ImGui.GetWindowDrawList();
                var rectMin = ImGui.GetItemRectMin();
                var rectMax = ImGui.GetItemRectMax();

                // Draw a 3-4 pixel wide bar on the left (scaled)
                drawList.AddRectFilled(
                    rectMin,
                    new Vector2(rectMin.X + 3 * scale, rectMax.Y),
                    ImGui.GetColorU32(new Vector4(0.4f, 0.7f, 1.0f, 1.0f)) // Your accent color here
                );
            }

            // Get the position of that selectable we just drew
            float itemY = ImGui.GetItemRectMin().Y;

            // Set cursor back to draw icon and text on top (scaled offsets)
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMin().X + 8 * scale, itemY + 4 * scale));

            ImGuiEx.Icon(icon);
            ImGui.SameLine();
            ImGui.Text(label);

            // Add small spacing between items (scaled)
            ImGui.Dummy(new Vector2(0, 2 * scale));
        }

        private static void DrawSelectableWithImage(uint iconId, string label, string id)
        {
            bool isSelected = currentSelection == id;
            float scale = ImGuiHelpers.GlobalScale;

            // Change background color if selected
            if (isSelected)
            {
                ImGui.PushStyleColor(ImGuiCol.Header, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            }

            // Indent for items under categories (scaled)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 16 * scale);

            float width = ImGui.GetContentRegionAvail().X;

            // Invisible selectable as the clickable area (scaled height)
            if (ImGui.Selectable($"##{id}", isSelected, ImGuiSelectableFlags.None, new Vector2(width, 25 * scale)))
            {
                currentSelection = id;
            }

            if (isSelected)
            {
                ImGui.PopStyleColor();
            }

            // Get the position of that selectable we just drew
            float itemY = ImGui.GetItemRectMin().Y;

            // Set cursor back to draw image and text on top (scaled offsets)
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMin().X + 8 * scale, itemY + 2 * scale));

            Svc.Texture.TryGetFromGameIcon(iconId, out var iconImage);
            var drewIcon = false;
            if (iconImage != null)
            {
                var image = iconImage.GetWrapOrEmpty();
                Vector2 imageSize = new Vector2(25 * scale, 25 * scale); // Scaled image
                                                                         // Center image vertically in the scaled height
                float imageYOffset = (25 * scale - imageSize.Y) / 2;
                ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, ImGui.GetCursorScreenPos().Y + imageYOffset));
                ImGui.Image(image.Handle, imageSize);
                drewIcon = true;
            }

            if (drewIcon)
            {
                ImGui.SameLine();
            }
            else
            {
                // 🔴 圖示載不到時「不能」呼叫 SameLine：這一行前面沒有畫過任何東西，
                // SameLine 會把文字接到「上一個項目」的行上，位置整個跑掉，看起來就是
                // 一整列空白（實機：據點活動底下的「賭博設定」，圖示 65127 在台服載不到，
                // 而同一區的 65112「點數購物」載得到、顯示正常）。
                // 改成自己把游標挪到圖示原本會佔的寬度之後 —— 失敗形式變成「有字沒圖」。
                ImGui.SetCursorScreenPos(new Vector2(
                    ImGui.GetItemRectMin().X + (8 + 25 + 8) * scale,
                    itemY + 4 * scale));
            }
            ImGui.AlignTextToFramePadding();
            ImGui.Text(label);

            // Add small spacing between items (scaled)
            ImGui.Dummy(new Vector2(0, 2 * scale));
        }

        private static IDalamudTextureWrap? GreyscaleJob()
        {
            var jobId = C.SelectedJob;
            string greyJobIcon = jobId switch
            {
                8 => "ICE.Resources.GreyscaleJobs.CRP.png",
                9 => "ICE.Resources.GreyscaleJobs.BSM.png",
                10 => "ICE.Resources.GreyscaleJobs.ARM.png",
                11 => "ICE.Resources.GreyscaleJobs.GSM.png",
                12 => "ICE.Resources.GreyscaleJobs.LTW.png",
                13 => "ICE.Resources.GreyscaleJobs.WVR.png",
                14 => "ICE.Resources.GreyscaleJobs.ALC.png",
                15 => "ICE.Resources.GreyscaleJobs.CUL.png",
                16 => "ICE.Resources.GreyscaleJobs.MIN.png",
                17 => "ICE.Resources.GreyscaleJobs.BTN.png",
                18 => "ICE.Resources.GreyscaleJobs.FSH.png",
                _ => "ICE.Resources.GreyscaleJobs.Default.png",
            };

            return Svc.Texture.GetFromManifestResource(Assembly.GetExecutingAssembly(), greyJobIcon).GetWrapOrEmpty();
        }

        // 🔴 死碼：零呼叫端（靜態掃描；本 repo 無反射式 UI 探索）。
        //    本檔上面 7 個分類標題全部走 ImGui_Tools.DrawCategoryHeader_AutoSize，沒有一個走這裡。
        //    ⚠️ 與 ImGui_Tools 那兩個同名/近名方法是三個不同的實作，不要混：
        //       ImGui_Tools.DrawCategoryHeader_AutoSize＝實際在用的；
        //       ImGui_Tools.DrawCategoryHeader＝也是死碼；本方法＝死碼，且多一個 badgeCount 參數。
        //    ⚠️ 它用的是本類別自己的 categoryStates 字典，與 ImGui_Tools.CategoryStates 是**兩個**字典。
        //    保留不刪（使用者裁決：死碼只要確認真的死，不用刪）。
        public static bool DrawCategoryHeader(string label, FontAwesomeIcon? icon = null, IDalamudTextureWrap? imageTexture = null, int? badgeCount = null)
        {
            var drawList = ImGui.GetWindowDrawList();
            var cursorPos = ImGui.GetCursorScreenPos();

            // Get colors from current theme
            var headerColor = ImGui.GetColorU32(ImGuiCol.Header);
            var textColor = ImGui.GetColorU32(ImGuiCol.Text);
            var textDisabledColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);

            float width = ImGui.GetContentRegionAvail().X;
            float height = 30;

            // Check if this category is expanded (default to true)
            string categoryId = label;
            if (!categoryStates.ContainsKey(categoryId))
                categoryStates[categoryId] = true;

            bool isExpanded = categoryStates[categoryId];

            // Check for click
            bool isHovered = ImGui.IsMouseHoveringRect(cursorPos,
                new Vector2(cursorPos.X + width, cursorPos.Y + height));
            bool isClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

            if (isClicked)
            {
                categoryStates[categoryId] = !categoryStates[categoryId];
                isExpanded = categoryStates[categoryId];
            }

            // Change header color slightly on hover
            if (isHovered)
                headerColor = ImGui.GetColorU32(ImGuiCol.HeaderHovered);

            // Draw background rectangle WITH ROUNDED CORNERS
            drawList.AddRectFilled(cursorPos,
                new Vector2(cursorPos.X + width, cursorPos.Y + height),
                headerColor,
                5.0f);

            // Calculate vertical centering
            float imageSize = 23;
            float textHeight = ImGui.CalcTextSize(label).Y;
            float verticalPadding = (height - textHeight) / 2;

            // Add left padding
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + verticalPadding);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);

            // Draw icon or image
            if (imageTexture != null)
            {
                // Calculate offset to center image with text
                float imageYOffset = (textHeight - imageSize) / 2;
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + imageYOffset);

                ImGui.Image(imageTexture.Handle, new Vector2(imageSize, imageSize));

                // Reset Y position for text
                ImGui.SameLine();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() - imageYOffset);
            }
            else if (icon.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, textDisabledColor);
                ImGuiEx.Icon(icon.Value);
                ImGui.PopStyleColor();
                ImGui.SameLine();
            }
            else
            {
                // No icon, just add sameline spacing
                ImGui.SameLine();
            }

            ImGui.PushStyleColor(ImGuiCol.Text, textDisabledColor);
            ImGui.Text(label);
            ImGui.PopStyleColor();

            // Draw badge if count provided
            if (badgeCount.HasValue && badgeCount.Value > 0)
            {
                float badgeSize = 24;
                float rightPadding = 10;

                float badgeXPos = cursorPos.X + width - badgeSize - rightPadding;
                float badgeYPos = cursorPos.Y + (height / 2);

                var badgeColor = ImGui.GetColorU32(ImGuiCol.ButtonActive);
                var badgeCenter = new Vector2(badgeXPos + (badgeSize / 2), badgeYPos);

                drawList.AddCircleFilled(badgeCenter, 12, badgeColor);

                var numberStr = badgeCount.Value.ToString();
                var textSize = ImGui.CalcTextSize(numberStr);
                drawList.AddText(
                    new Vector2(badgeCenter.X - textSize.X / 2, badgeCenter.Y - textSize.Y / 2),
                    textColor,
                    numberStr);
            }

            ImGui.Dummy(new Vector2(0, 5));

            return isExpanded;
        }
    }
}
