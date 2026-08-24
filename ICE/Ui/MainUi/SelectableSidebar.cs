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

                // ── 側欄導覽（2026-08-08 UI 重構第四批：少頁多節）──────────────────
                // 使用者對第二／三批的反饋是：分類拆了很多種但每個底下只有一兩項，
                // 整理不出效果；灰字「已隱藏 N 項」又佔位置又點不動。
                // ⇒ 一級項收斂成 6 個，每一頁自己用 ImGui_Tools.PageSection 分節；
                //   灰字提示整個拿掉（隱藏就隱藏乾淨），逃生門改成「設定→介面與導覽」永遠可達。
                //
                // ⚠️ 每個 DrawSelectableWithIcon 的第三個參數必須在 MainWindow.MainBody() 的
                //    switch 裡有對應的 case —— 打錯**不會編譯失敗**，只會變成一片空白頁。
                //    這一批的 9 個 id 與 MainBody 的 9 個 case 已逐一走過。
                // ⚠️ 分類標題一律傳明確的 id（第 4 個具名參數），不要讓已在地化的 label 當 key。

                // ① 任務 —— 唯一保留子項的導覽分類，而且它本來就有三個真的不同的目的地。
                //    ⚠️ 標準／完成度刻意「沒有」合併：它們不是同一頁的兩個名字，而是切換
                //    C.ShowCompletionWindow 的**唯二**開關（見 MainBody 的兩個 case），
                //    併成一項等於刪功能。
                if (C.Show_Page_Missions)
                {
                    if (ImGui_Tools.DrawCategoryHeader_AutoSize("Missions".Loc(), icon: FontAwesomeIcon.ListAlt, id: "cat_Missions"))
                    {
                        DrawSelectableWithIcon(FontAwesomeIcon.List, "Standard".Loc(), "modeSelect_Standard");
                        DrawSelectableWithIcon(FontAwesomeIcon.Trophy, "Completion".Loc(), "modeSelect_Completion");
                        DrawSelectableWithIcon(FontAwesomeIcon.SortAmountUp, "Mission Priority".Loc(), "setting_MissionPriority");
                    }
                }

                // ②~⑤ 單頁一級項：不再包一層只裝一兩項的分類，直接就是一列可點的頁。
                //     indentPx: 0 讓它們與分類標題對齊（分類底下的子項才縮排）。
                if (C.Show_Page_Gathering)
                    DrawSelectableWithIcon(FontAwesomeIcon.Leaf, "Gathering".Loc(), "page_Gathering", indentPx: 0f);
                if (C.Show_Page_HubActivities)
                    DrawSelectableWithIcon(FontAwesomeIcon.Home, "Hub Activities".Loc(), "page_HubActivities", indentPx: 0f);
                if (C.Show_Page_MechaOps)
                    DrawSelectableWithIcon(FontAwesomeIcon.Robot, "Mecha Ops".Loc(), "page_MechaOps", indentPx: 0f);

                // 🔴 「設定」是唯一不受任何 C.Show_Page_* 影響的一級項，而且必須保持如此：
                //    分頁顯示/隱藏的開關本身住在它的「介面與導覽」節裡。
                //    如果它自己也能被藏起來，使用者就沒有任何辦法把藏掉的東西叫回來 ——
                //    那是個把自己鎖在門外、只能去手改設定檔才救得回來的狀態。
                DrawSelectableWithIcon(FontAwesomeIcon.Cog, "Settings".Loc(), "page_Settings", indentPx: 0f);

                // ⑦~⑨ 側欄下半的三個「內嵌控件組」——它們不切頁，控件直接畫在側欄裡。
                //    2026-08-09 補上顯示開關（使用者原話「我是指 像界面導覽一樣 可以關閉」）：
                //    在這之前它們只能收合、沒辦法關掉，是側欄唯三關不掉的東西。
                //
                // 📌 這裡刻意用 `C.Show_Side_X && DrawCategoryHeader_AutoSize(...)` 的短路寫法，
                //    而不是像上面 ① 那樣多包一層 if：短路的效果完全相同（關掉時標題與內容
                //    都不會畫），但省掉整組四十幾行的重新縮排，diff 讀得出真正改了什麼。
                var currentJob = C.SelectedJob;
                if (C.Show_Side_MoonSelection
                    && ImGui_Tools.DrawCategoryHeader_AutoSize("Moon Selection".Loc(), FontAwesomeIcon.Moon, id: "cat_MoonSelection"))
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
                // 🔴 這一段「自動跟著目前職業」的同步**刻意留在顯示開關外面**：它改的是
                //    C.SelectedJob，而整個外掛（採集／製作／任務篩選）都吃那個值。
                //    把它一起關掉的話，使用者只是想收掉側欄上一塊佔位置的圖示，卻會連帶
                //    讓自動選職業停止運作 —— 那是關掉一個顯示開關不該有的副作用。
                if (C.AutoPickCurrentJob && (CosmicHelper.CrafterJobList.Contains(Player.JobId) || CosmicHelper.GatheringJobList.Contains(Player.JobId)) && C.SelectedJob != Player.JobId)
                {
                    C.SelectedJob = Player.JobId;
                    C.Save();
                }
                if (C.Show_Side_ClassSelection
                    && ImGui_Tools.DrawCategoryHeader_AutoSize("Class Selection".Loc(), imageTexture: GreyscaleJob(), id: "cat_ClassSelection"))
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
                if (C.Show_Side_ToolRelicXp
                    && ImGui_Tools.DrawCategoryHeader_AutoSize("Tool Relic XP".Loc(), icon: FontAwesomeIcon.ArrowUpRightDots, id: "cat_ToolRelicXp"))
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
                // ⑥ 說明與診斷 —— 刻意保留成兩個子項而不是併成一頁：
                //    「ICE 日誌」整頁是一個吃滿剩餘高度的子視窗（helpSelect_Logs 的
                //    ImRaii.Child(new Vector2(0, 0))），跟必要插件清單擠同一頁時
                //    高度會被壓到只剩幾列。這一項的兩個目的地本來就不同性質，維持兩入口。
                if (C.Show_Page_Help)
                {
                    if (ImGui_Tools.DrawCategoryHeader_AutoSize("Help & Diagnostics".Loc(), icon: FontAwesomeIcon.QuestionCircle, id: "cat_Help"))
                    {
                        DrawSelectableWithIcon(FontAwesomeIcon.Question, "Requirements".Loc(), "helpSelect_Requirements");
                        DrawSelectableWithIcon(FontAwesomeIcon.Book, "Ice Logs".Loc(), "helpSelect_Logs");
                    }
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
        // 📌 2026-08-09 追記：月球選擇多了 C.Show_Side_MoonSelection 這個顯示開關，
        //    所以本檔這份副本現在是「開關開著 ＋ 分類展開」才跑得到。這**沒有製造新的
        //    故障模式** —— 原本「分類收合」就已經是同一個不執行的狀態，而 modeSelect_Standard
        //    那份副本不受顯示開關影響，仍然照舊。
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

        // 📌 這裡原本有 DrawHiddenNotice()：被藏起來的位置留一行灰字「已隱藏 N 項」。
        //    2026-08-08 使用者裁決整個拿掉 ——「灰字隱藏指示讓他不能點，沒有整理的效果」。
        //    逃生門改成結構性的：「設定」一級項永遠不可藏，所有顯示開關都在它的
        //    「介面與導覽」節裡，所以任何時候都回得去，不需要在每個分類下掛提示。

        /// <param name="indentPx">
        /// 左側縮排。分類底下的子項用預設的 16（縮排才看得出從屬關係）；
        /// 一級項自己就是一頁時傳 0，與分類標題對齊。
        /// </param>
        private static void DrawSelectableWithIcon(FontAwesomeIcon icon, string label, string id, float indentPx = 16f)
        {
            bool isSelected = currentSelection == id;
            float scale = ImGuiHelpers.GlobalScale;

            // Change background color if selected
            if (isSelected)
            {
                ImGui.PushStyleColor(ImGuiCol.Header, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            }

            // Indent for items under categories (scaled)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indentPx * scale);

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

        // 🔴 死碼（2026-08-08 起）：唯二呼叫端是舊側欄「據點活動」底下的點數購物／賭博設定，
        //    第四批把那一類收斂成單一頁之後就沒有呼叫端了（靜態掃描；本 repo 無反射式 UI 探索）。
        //    保留不刪 —— 它裡面那段「圖示載不到時不能 SameLine」的處置是台服實測出來的教訓，
        //    下次有人想在側欄放遊戲圖示時會需要它（見方法內的註解）。
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
                //
                // 📌 補記（2026-08-24，來自 cycleapple api13-tw `8a7c91ac`）：那顆圖示的
                //    台服可用版本是 **65126**，上游直接把 65127 換成它。我方走的是上面這條
                //    fallback，而且 2026-08-08 的側欄重構已經把整個側欄改成 FontAwesome 圖示
                //    （`DrawSelectableWithIcon`），全 repo 已經沒有任何一處傳圖示 ID 進來
                //    ⇒ **上游那顆在我方沒有對應的呼叫點可改，只留這行紀錄。**
                //    ⚠️ 這個方法（`DrawSelectableWithImage`）目前沒有任何呼叫者（沒有刪，
                //    因為它是「要用圖示側欄時的既有實作」），哪天要重新用圖示，
                //    請直接寫 65126、不要再寫 65127。
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
