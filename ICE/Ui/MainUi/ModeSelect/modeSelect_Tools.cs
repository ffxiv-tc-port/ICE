using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.MainUi.ModeSelect;

public class modeSelect_Tools
{
    public static void DrawJobButtons(uint jobId, string tooltip)
    {
        uint selectedJob = C.SelectedJob;
        bool state = selectedJob == jobId;
        ISharedImmediateTexture? icon = state ? CosmicHelper.JobIconDict[jobId] : CosmicHelper.GreyTexture[jobId];
        Vector2 size = new Vector2(26, 26);
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
    // ⚠️ id 是**選填**參數，省略時完全等同以前的行為（key 就是 label 本身）。
    //    加它的原因與 ImGui_Tools.DrawCategoryHeader_AutoSize 的 id 參數完全相同：
    //    收合狀態原本拿「已在地化的 label」當 key —— 兩個標題只要翻成同一個字串就會
    //    **共用收合狀態**，失敗形式是靜默的（點一個、兩個一起開合，看起來像 ImGui 壞掉）。
    //    新的呼叫端一律傳明確、不翻譯的 id。
    // 📌 這裡用的是 SelectableSidebar.categoryStates，與 ImGui_Tools.CategoryStates 是
    //    **兩個**字典（見 SelectableSidebar.cs 的註解），所以 id 不會跟側欄的 cat_* 撞。
    //    兩個字典都只活在記憶體、不寫進設定檔，換 key 沒有遷移問題，最多是這幾個標題
    //    的收合狀態回到預設（收合）一次。
    public static bool DrawCompactCategoryHeader(string label, FontAwesomeIcon? icon = null, string? id = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Get colors from current theme
        var headerColor = ImGui.GetColorU32(ImGuiCol.Header);
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);

        // Calculate content size
        float horizontalPadding = 8;
        float verticalPadding = 4;
        float iconTextSpacing = 4;

        var textSize = ImGui.CalcTextSize(label);
        float contentWidth = ImGui.GetContentRegionAvail().X;
        float contentHeight = verticalPadding * 2 + textSize.Y;

        // Check if this category is expanded (default to false)
        string categoryId = id ?? label;
        if (!SelectableSidebar.categoryStates.ContainsKey(categoryId))
            SelectableSidebar.categoryStates[categoryId] = false;

        bool isExpanded = SelectableSidebar.categoryStates[categoryId];

        // Check for click
        bool isHovered = ImGui.IsMouseHoveringRect(cursorPos, new Vector2(cursorPos.X + contentWidth, cursorPos.Y + contentHeight))
                      && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.ChildWindows);
        bool isClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        if (isClicked)
        {
            SelectableSidebar.categoryStates[categoryId] = !SelectableSidebar.categoryStates[categoryId];
            isExpanded = SelectableSidebar.categoryStates[categoryId];
        }

        // Change header color slightly on hover
        if (isHovered)
            headerColor = ImGui.GetColorU32(ImGuiCol.HeaderHovered);

        // Draw background rectangle with rounded corners
        drawList.AddRectFilled(cursorPos,
            new Vector2(cursorPos.X + contentWidth, cursorPos.Y + contentHeight),
            headerColor,
            5.0f);

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
        ImGui.SameLine(0, 8);

        // Draw caret icon based on expanded state
        ImGuiEx.Icon(isExpanded ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight);

        ImGui.SameLine(0, 8);
        ImGui.Dummy(Vector2.Zero);

        // Advance cursor past the header
        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + contentHeight));
        ImGui.Spacing();

        return isExpanded;
    }
}
