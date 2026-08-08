using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ICE.Ui;
using ICE.Ui.MainUi;
using ICE.Ui.MainUi.ModeSelect;
using ICE.Utilities.Cosmic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.GroupPoseModule;

namespace ICE.Utilities.ImGuiTools;

public static partial class ImGui_Tools
{
    // This is used to store the various category states for ImGui collapsible headers
    // Key: Category Name
    // Value: Is Expanded (true/false)
    // This is just a simple way of keeping track of all of them, so I can easily reference what state they're in
    public static Dictionary<string, bool> CategoryStates = new();

    // My Custom Header, Uses FontAwesomeIcon and a
    //
    // ⚠️ id 是 2026-08-08 UI 重構時加的**選填**參數，省略時完全等同以前的行為
    //    （key 就是 label 本身），所以既有呼叫端一個都不用改。
    //    加它的原因：CategoryStates 原本拿「已在地化的 label」當 key ——
    //    兩個分類只要翻成同一個字串就會**共用展開狀態**，失敗形式是靜默的
    //    （兩塊一起收合，看起來像 ImGui 壞掉）。新側欄分類一律傳明確、不翻譯的 id。
    // 📌 CategoryStates 只活在記憶體、不寫進設定檔，所以換 key 沒有遷移問題，
    //    最多是當次 session 的展開狀態回到預設（收合）。
    public static bool DrawCategoryHeader_AutoSize(string label, FontAwesomeIcon? icon = null, IDalamudTextureWrap? imageTexture = null, string? id = null)
    {
        float scale = ImGuiHelpers.GlobalScale;

        // Default Colors for Theming. This is really here to make sure it's formatted as I want it to be
        var headerColor = ImGui.GetColorU32(ImGuiCol.Header);
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);
        // var textColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);


        // This is here to make sure that
        // A: If it doesn't already exist, add it and just make it false (This makes it to where it's not expanded by default)
        //    - Could absolutely change that to true if I want to make it shown on inital creation
        // B: Returns the state in a form to where if that's true, then I could display the elements below it properly
        string categoryId = id ?? label;
        if (!CategoryStates.ContainsKey(categoryId))
            CategoryStates[categoryId] = false;

        bool isExpanded = CategoryStates[categoryId];

        // Need these here for two reasons:
        // 1: drawList allows me to create un-conventional things that isn't included in the Imgui Library
        // 2: curserPos allows me to grab the absolute position, which is necessary to make sure things are lined up properly
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // This is currently how I'm going to autosize it based on the contents of the window.
        // (as of typing this). It gets the width of the window and expands it on that (these are meant for the sidebar)
        // Height is scaled based on the global font scale
        float width = ImGui.GetContentRegionAvail().X;
        float height = 30 * scale;

        // Check for click, nice little rectangle area where it can be clicked at. This takes in account the above things to make sure it's only clicking within this area
        bool isHovered = ImGui.IsMouseHoveringRect(cursorPos, new Vector2(cursorPos.X + width, cursorPos.Y + height));
        bool isClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        if (isClicked)
        {
            CategoryStates[categoryId] = !CategoryStates[categoryId];
            isExpanded = CategoryStates[categoryId];
        }

        // Change header color slightly on hover, just a nice QOL
        if (isHovered)
            headerColor = ImGui.GetColorU32(ImGuiCol.HeaderHovered);

        // Drawing the rectangle itself/ custom thingy. This is the container for all the fancy smancy stuff.
        // Border radius scaled
        drawList.AddRectFilled(cursorPos, new Vector2(cursorPos.X + width, cursorPos.Y + height), headerColor, 5.0f * scale);

        // Calculating the vertical spacing here, need to make sure it fits within our custom box nice and cozy
        float imageSize = 23 * scale; // Used for images specifically, since I like things being aligned with each other
        float textHeight = ImGui.CalcTextSize(label).Y;
        float verticalPadding = (height - textHeight) / 2;

        // Adding some padding to the left, don't need it feeling like it's right against the box. We're making somewhat bubbly things
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + verticalPadding);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8 * scale);

        // Drawing either an image, icon, or nothing at all (should really only be the first 2, but on the off chance I decide to just use text)
        if (imageTexture != null)
        {
            // Calculating the offset here to center the image with the text (OCD here)
            float imageYOffset = (textHeight - imageSize) / 2;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + imageYOffset);

            // Adding a little bit of padding here for the image -> text (scaled)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 4 * scale); // Current set to -4 to bring it closer, but can be changed to give it more space (lowering the number) or removing more space (increasing number)

            ImGui.Image(imageTexture.Handle, new Vector2(imageSize, imageSize));

            // Resetting the Y position for text, to make sure it lines up (scaled spacing)
            ImGui.SameLine(0, 2 * scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - imageYOffset);
        }
        else if (icon.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, textColor); // Disabled text color here to match the rest of it
            ImGuiEx.Icon(icon.Value);
            ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        else
        {
            // No icon, just adding some sameline spacing
            ImGui.SameLine();
        }

        // Actual label here, disabled text color
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.Text(label);
        ImGui.PopStyleColor();

        // Replace the badge count section with the caret icon (scaled padding)
        float iconSize = ImGui.CalcTextSize(FontAwesomeIcon.CaretDown.ToIconString()).X;
        float rightPadding = 10 * scale;

        float iconXPos = cursorPos.X + width - iconSize - rightPadding;
        float iconYPos = cursorPos.Y + verticalPadding;

        ImGui.SetCursorScreenPos(new Vector2(iconXPos, iconYPos));
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGuiEx.Icon(isExpanded ? FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight);
        ImGui.PopStyleColor();

        // Last but not least, resetting it for the next thing here:
        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + height));
        ImGui.Spacing();

        return isExpanded;
    }

    /// <summary>
    /// 頁內分節用的收合標題（2026-08-08 UI 重構第四批「少頁多節」）。
    /// </summary>
    /// <param name="label">已在地化的標題文字。</param>
    /// <param name="id">
    /// 不翻譯的唯一識別碼（本函式自己加上 <c>###</c>）。
    /// 🔴 <b>一定要給</b>：ImGui 的收合狀態是用控制項 id 記的，而 id 預設就是標籤本身 ——
    /// 兩節只要翻成同一個字串就會共用開合狀態，失敗形式是靜默的（兩塊一起開合，看起來像壞掉）。
    /// 這與 <see cref="DrawCategoryHeader_AutoSize"/> 的 id 參數是同一個陷阱、同一個解法。
    /// </param>
    /// <param name="defaultOpen">
    /// 第一次出現時是否展開。慣例：<b>一頁只有第一節預設展開</b>，其餘收合 ——
    /// 打開一頁看到的是一份分節目錄，而不是一整捲設定。
    /// ⚠️ 使用者手動開合過之後，ImGui 會記住他的選擇，這個參數就不再生效。
    /// </param>
    public static bool PageSection(string label, string id, bool defaultOpen = false)
    {
        return ImGui.CollapsingHeader(label + "###" + id,
            defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
    }

    // 🔴 死碼：零呼叫端（靜態掃描；本 repo 無反射式 UI 探索）。
    //    ⚠️ grep "DrawCategoryHeader" 會在下面第 152 行附近多命中一次，那是**註解裡的字串**不是呼叫。
    //    實際在用的是上面的 DrawCategoryHeader_AutoSize。兩者共用同一個 CategoryStates 字典，
    //    所以若哪天要啟用它，label 撞名會和既有標題共用展開狀態。
    //    保留不刪（使用者裁決：死碼只要確認真的死，不用刪）。
    public static bool DrawCategoryHeader(string label, FontAwesomeIcon? icon = null)
    {
        // Default coloring here
        var headerColor = ImGui.GetColorU32(ImGuiCol.Header);
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);

        // Setting the values of the content size (padding, spacing, ect) that way it's used across the board
        float horizontalPadding = 8;
        float verticalPadding = 4;
        float iconTextSpacing = 4;

        // These are to make sure that they're drawn in place (Look at DrawCategoryHeader_AutoSize if I really need to go deep in refreshments)
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // We want this to fit within the whole thing so. Going to get the text size so it scales properly if people have their ui text more than 100%
        var textSize = ImGui.CalcTextSize(label);
        float contentWidth = ImGui.GetContentRegionAvail().X;
        float contentHeight = verticalPadding * 2 + textSize.Y;

        // If it doesn't already exist, then creating an entry in the category state. If it does exist, then grabbing it and storing it for later
        string categoryId = label;
        if (!CategoryStates.ContainsKey(categoryId))
            CategoryStates[categoryId] = false;

        bool isExpanded = CategoryStates[categoryId];
        bool isHovered = ImGui.IsMouseHoveringRect(cursorPos, new Vector2(cursorPos.X + contentWidth, cursorPos.Y + contentHeight));
        bool isClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        if (isClicked)
        {
            CategoryStates[categoryId] = !CategoryStates[categoryId];
            isExpanded = CategoryStates[categoryId];
        }

        // Draw background rectangle with rounded corners
        drawList.AddRectFilled(cursorPos, new Vector2(cursorPos.X + contentWidth, cursorPos.Y + contentHeight), headerColor, 5.0f);

        // Change header color slightly on hover
        if (isHovered)
            headerColor = ImGui.GetColorU32(ImGuiCol.HeaderHovered);

        // Position cursor with padding
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + horizontalPadding);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + verticalPadding);

        // Draw icon if provided
        if (icon.HasValue)
        {
            ImGuiEx.Icon(icon.Value);
            ImGui.SameLine(0, iconTextSpacing);
        }

        ImGui.Text(label);
        ImGui.SameLine(0, 5);
        ImGui.Text(""); // This is here, mainly to give that little bit more buffer. I don't like it being as close as it is to the lettering

        // Advance cursor past the header
        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + contentHeight));
        ImGui.Spacing();

        return isExpanded;
    }

    /// <summary>
    /// I realize that these are probably more over the top for a simple button than needs to be. But TBH this kind of does 2 things for me: <br></br>
    /// 1: Lets me use custom coloring like the tabs would. Tabs absolutely hate having anything in that selected tab change. (Ex. Changing the active missions currently enabled immediately kicks you off that tab)
    /// 2: Lets me create custom buttons in any form, and also lets me show the active state of them all. PLUS. Uses the expanded headers so works out for the showing of mission headers
    /// </summary>
    /// <param name="label"></param>
    /// <param name="categoryId"></param>
    /// <param name="icon"></param>
    /// <param name="spacingAfter"></param>
    /// <returns></returns>
    /// <summary>
    /// 選中時把標籤上的計數括號從 <c>[12]</c> 換成 <c>&lt;12&gt;</c>。
    /// <br></br>
    /// 這是刻意不依賴顏色的第二條辨識管道：一排長得一樣的篩選按鈕裡，即使使用者的
    /// 配色主題讓底色差異看不出來（Dalamud 預設主題就是這種情況），括號形狀仍然分得出
    /// 哪一個是開著的。
    /// <br></br>
    /// 找不到括號時（例如某個語言的翻譯把括號拿掉了）退回「整個標籤包角括號」，
    /// 確保任何翻譯下都還是有可見差異，而不是靜默地退回沒有標示。
    /// </summary>
    private static string ApplySelectionBrackets(string label, bool selected)
    {
        if (!selected || string.IsNullOrEmpty(label))
            return label;

        // 計數永遠在標籤結尾，所以兩邊都從後面找 —— 用 IndexOf 找左括號的話，
        // 名稱本身含中括號的翻譯（例如「[A] 階 [12]」）會被配成錯的一對。
        var open = label.LastIndexOf('[');
        var close = label.LastIndexOf(']');
        if (open >= 0 && close > open)
        {
            var chars = label.ToCharArray();
            chars[open] = '<';
            chars[close] = '>';
            return new string(chars);
        }

        return $"<{label}>";
    }

    public static bool DrawCategoryButton(string label, string categoryId, FontAwesomeIcon? icon = null, float spacingAfter = 5)
    {
        float scale = ImGuiHelpers.GlobalScale;

        // Default coloring here
        var headerColor = ImGui.GetColorU32(ImGuiCol.Button);
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);

        // Setting the values of the content size (padding, spacing, ect) that way it's used across the board
        float horizontalPadding = 8 * scale;
        float verticalPadding = 4 * scale;
        float iconTextSpacing = 4 * scale;

        // These are to make sure that they're drawn in place
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // If it doesn't already exist, then creating an entry in the category state
        // (狀態要在算文字寬度之前先讀出來，因為選中與否會改變標籤上的括號)
        if (!CategoryStates.ContainsKey(categoryId))
            CategoryStates[categoryId] = false;

        bool isExpanded = CategoryStates[categoryId];

        // Calculate text size
        var drawnLabel = ApplySelectionBrackets(label, isExpanded);
        var textSize = ImGui.CalcTextSize(drawnLabel);

        // 寬度取「選中」與「未選中」兩種寫法的較大者，按鈕才不會在點下去的瞬間左右跳動
        // （這一排是水平捲動的，跳動會讓後面的按鈕整排位移）
        float labelWidth = Math.Max(textSize.X, ImGui.CalcTextSize(ApplySelectionBrackets(label, !isExpanded)).X);

        // Calculate icon width if present
        float iconWidth = 0;
        if (icon.HasValue)
        {
            iconWidth = textSize.Y + iconTextSpacing;
        }

        // Calculate button dimensions based on content
        float contentWidth = horizontalPadding * 2 + iconWidth + labelWidth;
        float contentHeight = verticalPadding * 2 + textSize.Y;

        bool isHovered = ImGui.IsMouseHoveringRect(cursorPos, new Vector2(cursorPos.X + contentWidth, cursorPos.Y + contentHeight))
                      && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.ChildWindows);
        bool isClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        if (isClicked)
        {
            CategoryStates[categoryId] = !CategoryStates[categoryId];
            isExpanded = CategoryStates[categoryId];
            drawnLabel = ApplySelectionBrackets(label, isExpanded);
        }

        // Color changing! Based on the various states
        // ⚠️ 原本選中色用的是 ImGuiCol.TabActive，但 Dalamud 預設主題裡
        // Button = (0.227, 0.424, 0.659)、TabActive = (0.283, 0.425, 0.629)，
        // 相對亮度只差約 2%，肉眼等於完全分不出選中與否。改用 ButtonActive
        // （框架標準的「這顆按鈕是按下狀態」色，亮度差約 24%）。
        if (isExpanded)
            headerColor = ImGui.GetColorU32(ImGuiCol.ButtonActive);
        if (isHovered)
            headerColor = ImGui.GetColorU32(ImGuiCol.HeaderHovered);

        // Draw background rectangle with rounded corners (scaled)
        var contentMax = new Vector2(cursorPos.X + contentWidth, cursorPos.Y + contentHeight);
        drawList.AddRectFilled(cursorPos, contentMax, headerColor, 5.0f * scale);

        // 選中的再加一圈文字色外框。外框不依賴「某兩個主題色剛好不一樣」這個前提
        // ——文字色一定跟底色有對比（否則標籤本身就看不見了），所以任何配色主題下都成立。
        // 也因為 hover 會覆蓋底色，外框是滑鼠移上去時唯一還留著的選中標示。
        if (isExpanded)
            drawList.AddRect(cursorPos, contentMax, textColor, 5.0f * scale, 1.5f * scale);

        // Position cursor with padding
        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X + horizontalPadding, cursorPos.Y + verticalPadding));

        // Draw icon if provided
        if (icon.HasValue)
        {
            ImGuiEx.Icon(icon.Value);
            ImGui.SameLine(0, iconTextSpacing);
        }

        ImGui.Text(drawnLabel);

        // Create an invisible button to properly reserve space and handle layout
        ImGui.SetCursorScreenPos(cursorPos);
        ImGui.InvisibleButton($"##{categoryId}_btn", new Vector2(contentWidth, contentHeight));

        // Add spacing after the button (scaled)
        ImGui.SameLine(0, spacingAfter * scale);

        return isExpanded;
    }

    public static void EndCategoryButtonRow()
    {
        ImGui.NewLine();
        ImGui.Separator();
    }

    public static void DrawJobButtons(uint jobId, string tooltip)
    {
        float scale = ImGuiHelpers.GlobalScale;

        uint selectedJob = C.SelectedJob;
        bool state = selectedJob == jobId;
        ISharedImmediateTexture? icon = state ? CosmicHelper.JobIconDict[jobId] : CosmicHelper.GreyTexture[jobId];
        Vector2 size = new Vector2(26 * scale, 26 * scale);
        bool autoPickCurrentJob = C.AutoPickCurrentJob;

        if (StyledImageButton.DrawStyledImageButton(icon, size, state))
        {
            if (autoPickCurrentJob)
            {
                autoPickCurrentJob = false;
                C.AutoPickCurrentJob = autoPickCurrentJob;
            }

            C.SelectedJob = jobId;
            C.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(tooltip);
            ImGui.EndTooltip();
        }
    }
}
