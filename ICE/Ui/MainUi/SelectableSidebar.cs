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

                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Cosmic Helper".Loc(), icon: FontAwesomeIcon.ListAlt))
                {
                    DrawSelectableWithIcon(FontAwesomeIcon.List, "Standard".Loc(), "modeSelect_Standard");
                    DrawSelectableWithIcon(FontAwesomeIcon.Trophy, "Completion".Loc(), "modeSelect_Completion");
                }
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Settings".Loc(), icon: FontAwesomeIcon.Cog))
                {
                    if (C.Show_StopWhen)
                        DrawSelectableWithIcon(FontAwesomeIcon.Stop, "Stop When...".Loc(), "setting_StopWhen");
                    if (C.Show_GatheringProfile)
                        DrawSelectableWithIcon(FontAwesomeIcon.Leaf, "Gathering Profile".Loc(), "setting_GatheringProfile");
                    if (C.Show_MissionPriority)
                        DrawSelectableWithIcon(FontAwesomeIcon.SortAmountUp, "Mission Priority".Loc(), "setting_MissionPriority");
                    if (C.Show_MiscSettings)
                        DrawSelectableWithIcon(FontAwesomeIcon.UserCog, "Misc Settings".Loc(), "setting_Misc");

                    // 刻意不加 C.Show_* 開關：這一頁的重點就是「讓使用者知道採集路線可以自己改」，
                    // 藏在偵錯視窗底下等於沒有。
                    DrawSelectableWithIcon(FontAwesomeIcon.MapSigns, "Gathering Routes".Loc(), "setting_GatherRoutes");

                    DrawSelectableWithIcon(FontAwesomeIcon.Cog, "All Settings".Loc(), "helpSelect_AllSettings");
                }
                if (C.Show_HubActivities)
                {
                    if (ImGui_Tools.DrawCategoryHeader_AutoSize("Hub Activities".Loc(), icon: FontAwesomeIcon.Home))
                    {
                        DrawSelectableWithImage(65112, "Credit Shopping".Loc(), "hubActivities_CreditShopping");
                        DrawSelectableWithImage(65127, "Gambling Settings".Loc(), "hubActivites_GambaSetting");
                    }
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
                if (ImGui_Tools.DrawCategoryHeader_AutoSize("Help".Loc(), icon: FontAwesomeIcon.QuestionCircle))
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
