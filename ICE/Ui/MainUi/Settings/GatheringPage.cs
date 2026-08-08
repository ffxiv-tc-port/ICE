using ICE.Ui.MainUi.Settings.Settings_Table;
using ICE.Utilities.ImGuiTools;

namespace ICE.Ui.MainUi.Settings;

/// <summary>
/// 「採集」一級項（2026-08-08 UI 重構第四批）。
///
/// 📌 這是一個**組頁函式**：兩節的內容都是原封不動的既有頁面，一個字都沒改，
/// 只是從兩個各自只有一項的側欄入口收成同一頁的兩節。
/// <list type="bullet">
/// <item>採集設定 ＝ 舊的 <c>setting_GatheringProfile</c>（<see cref="GatherSettings"/>，
///       含強心劑／食物／各職業設定檔與採集技能，本身就已經自帶收合分節）</item>
/// <item>採集路線 ＝ 舊的 <c>setting_GatherRoutes</c>（<see cref="GatherRouteCustomization"/>）</item>
/// </list>
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
