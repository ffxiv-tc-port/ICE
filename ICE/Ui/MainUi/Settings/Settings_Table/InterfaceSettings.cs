using Dalamud.Interface;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    // 「介面」分頁。
    //
    // 🔴 這一頁**永遠不可以**被任何 C.Show_* 開關藏起來：
    //    分頁的顯示/隱藏開關本身就住在這裡（B3 從 Misc 設定的「顯示分頁」那節搬進來）。
    //    它自己也能被藏的話，使用者就沒有任何辦法把藏掉的分頁叫回來，
    //    只能去手動改設定檔 —— 等於把人鎖在門外。
    //    對應的側欄項在 SelectableSidebar 裡也刻意沒有加 Show_* 條件。
    //
    // 📌 UI 重構第二批只先把頁建起來（骨架）；內容第三批才進來。
    internal class InterfaceSettings
    {
        public static void Draw()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.SlidersH, "Interface".Loc());
            ImGui.Dummy(new Vector2(0, 5));
            ImGui.TextWrapped("Settings for showing and hiding sidebar tabs will move here.".Loc());
        }
    }
}
