using ICE.Ui.MainUi.Settings.Settings_Table;
using ICE.Utilities.ImGuiTools;

namespace ICE.Ui.MainUi.Settings;

/// <summary>
/// 📌 這是一個**組頁函式**：兩節的內容都是既有頁面，這裡只決定排列。
/// ⚠️ 路線那一節刻意排在最後：它的表格會吃掉剩餘高度（<c>ImGuiTableFlags.ScrollY</c>），
/// 排在別的東西前面會把後面的內容擠到看不見。
/// </summary>
internal static class GatheringPage
{
    public static void Draw()
    {
        if (ImGui_Tools.PageSection("Gathering Settings".Loc(), "ICESecGatherSettings", defaultOpen: true))
            GatherSettings.Draw();

        if (ImGui_Tools.PageSection("Gathering Routes".Loc(), "ICESecGatherRoutes"))
            GatherRouteCustomization.Draw();
    }
}
