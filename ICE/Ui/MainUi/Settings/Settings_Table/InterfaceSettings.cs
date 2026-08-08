using Dalamud.Interface;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    // 「介面與導覽」——「設定」頁裡的一節：哪些側欄一級項要顯示、哪些要藏起來。
    //
    // 🔴 這一節所在的「設定」一級項**永遠不可以**被藏起來（SelectableSidebar 那邊刻意
    //    沒有給它 C.Show_Page_* 條件）：分頁的顯示/隱藏開關本身就住在這裡。
    //    它自己也能被藏的話，使用者就沒有任何辦法把藏掉的分頁叫回來，只能去手改設定檔
    //    —— 等於把人鎖在門外。要動那一段之前先想清楚這件事。
    //
    // 📌 2026-08-08 UI 重構第四批：開關從「一個分頁一個」改成「一個一級項一個」，
    //    對得上新的側欄樹。全部用**新鍵**（Show_Page_*，預設全顯示），舊的五個 Show_*
    //    停止消費但留在設定類別裡（對應關係寫在 MissionConfigs.cs 的 Tab Hider 區）。
    internal class InterfaceSettings
    {
        public static void Draw()
        {
            ImGui.TextWrapped(("Turn off the sidebar entries you never use. " +
                               "The Settings entry itself can never be hidden - these switches live in it, " +
                               "so there is always a way back.").Loc());
            ImGui.Dummy(new(0, 5));

            bool showMissions = C.Show_Page_Missions;
            if (ImGui.Checkbox("Show Missions".Loc() + "###ICEShowPageMissions", ref showMissions))
            {
                C.Show_Page_Missions = showMissions;
                C.Save();
            }

            bool showGathering = C.Show_Page_Gathering;
            if (ImGui.Checkbox("Show Gathering".Loc() + "###ICEShowPageGathering", ref showGathering))
            {
                C.Show_Page_Gathering = showGathering;
                C.Save();
            }

            bool showHubActivities = C.Show_Page_HubActivities;
            if (ImGui.Checkbox("Show Hub Activities".Loc() + "###ICEShowPageHubActivities", ref showHubActivities))
            {
                C.Show_Page_HubActivities = showHubActivities;
                C.Save();
            }

            bool showMechaOps = C.Show_Page_MechaOps;
            if (ImGui.Checkbox("Show Mecha Ops".Loc() + "###ICEShowPageMechaOps", ref showMechaOps))
            {
                C.Show_Page_MechaOps = showMechaOps;
                C.Save();
            }

            bool showHelp = C.Show_Page_Help;
            if (ImGui.Checkbox("Show Help & Diagnostics".Loc() + "###ICEShowPageHelp", ref showHelp))
            {
                C.Show_Page_Help = showHelp;
                C.Save();
            }

            // ---- 側欄下半的三個內嵌控件組（2026-08-09）----
            // 上面五個管的是「會切頁」的一級項；這三個管的是直接畫在側欄裡的控件組。
            // 使用者原話：「我是指 像界面導覽一樣 可以關閉」——在這之前它們只能收合、關不掉。
            // 📌 全部預設 true，所以升上來的人版面完全不變。
            bool showMoonSelection = C.Show_Side_MoonSelection;
            if (ImGui.Checkbox("Show Moon Selection".Loc() + "###ICEShowSideMoonSelection", ref showMoonSelection))
            {
                C.Show_Side_MoonSelection = showMoonSelection;
                C.Save();
            }

            bool showClassSelection = C.Show_Side_ClassSelection;
            if (ImGui.Checkbox("Show Class Selection".Loc() + "###ICEShowSideClassSelection", ref showClassSelection))
            {
                C.Show_Side_ClassSelection = showClassSelection;
                C.Save();
            }

            bool showToolRelicXp = C.Show_Side_ToolRelicXp;
            if (ImGui.Checkbox("Show Tool Relic XP".Loc() + "###ICEShowSideToolRelicXp", ref showToolRelicXp))
            {
                C.Show_Side_ToolRelicXp = showToolRelicXp;
                C.Save();
            }

            ImGui.Dummy(new(0, 5));
            // 📌 這一行講的是「藏起來的東西去哪了」。舊版是在側欄每個被藏的位置留一行灰字
            //    「已隱藏 N 項」，使用者的回饋是那行點不動、又佔位置、沒有整理的效果，
            //    所以改成在這裡講一次就好 —— 藏起來的就藏乾淨。
            ImGui.TextDisabled(("Hidden entries leave no placeholder in the sidebar - " +
                                "come back here to bring them back.").Loc());
        }
    }
}
