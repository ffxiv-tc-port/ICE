using ICE.Ui.MainUi.Settings.Settings_Table;
using ICE.Ui.SettingTabs;
using ICE.Utilities.ImGuiTools;

namespace ICE.Ui.MainUi.Settings;

/// <summary>
/// 「據點活動」一級項（2026-08-08 UI 重構第四批）。
///
/// 📌 組頁函式，兩節的內容都是原封不動的既有頁面：
/// 點數購物 ＝ 舊的 <c>hubActivities_CreditShopping</c>（<see cref="ShoppingTab"/>）、
/// 幸運轉盤 ＝ 舊的 <c>hubActivites_GambaSetting</c>（<see cref="GambaWheel"/>）。
///
/// ⚠️ <see cref="GambaWheel"/> 住在 <c>ICE.Ui.SettingTabs</c> 而不是
/// <c>ICE.Ui.MainUi.Settings.Settings_Table</c>（同一個資料夾、不同命名空間），
/// 所以這裡要兩個 using —— 少一個的話錯誤訊息會指向「找不到型別」而不是命名空間。
/// </summary>
internal static class HubActivitiesPage
{
    public static void Draw()
    {
        if (ImGui_Tools.PageSection("Credit Shopping".Loc(), "ICESecCreditShopping", defaultOpen: true))
            ShoppingTab.Draw();

        if (ImGui_Tools.PageSection("Gambling Settings".Loc(), "ICESecGambling"))
            GambaWheel.Draw();
    }
}
