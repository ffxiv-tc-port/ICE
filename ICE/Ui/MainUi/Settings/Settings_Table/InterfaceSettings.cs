using Dalamud.Interface;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    // 「介面」分頁：哪些側欄項目要顯示、哪些要藏起來。
    //
    // 🔴 這一頁**永遠不可以**被任何 C.Show_* 開關藏起來：
    //    分頁的顯示/隱藏開關本身就住在這裡。它自己也能被藏的話，使用者就沒有
    //    任何辦法把藏掉的分頁叫回來，只能去手動改設定檔 —— 等於把人鎖在門外。
    //    對應的側欄項在 SelectableSidebar 裡也刻意沒有加 Show_* 條件，
    //    要動那一段之前先想清楚這件事。
    //
    // 📌 下面五個核取方塊是 UI 重構第三批從 Misc 設定頁的「顯示／隱藏分頁」那一節
    //    整段搬過來的，設定鍵、標籤、###id、副作用（寫值後 C.Save()）全部逐字未改。
    //    ###id 尤其不要順手改：它是 ImGui 的控制項識別，改了等於換一個新控制項。
    internal class InterfaceSettings
    {
        public static void Draw()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.WindowRestore, "Show / Hide Tabs".Loc());
            ImGui.Dummy(new(0, 5));

            bool showStopWhen = C.Show_StopWhen;
            if (ImGui.Checkbox("Show Stop When... Tab".Loc() + "###ICEShowStopWhenTab", ref showStopWhen))
            {
                C.Show_StopWhen = showStopWhen;
                C.Save();
            }

            bool showGProfile = C.Show_GatheringProfile;
            if (ImGui.Checkbox("Show Gathering Profile Tab".Loc() + "###ICEShowGatheringProfileTab", ref showGProfile))
            {
                C.Show_GatheringProfile = showGProfile;
                C.Save();
            }

            bool showMissionPrio = C.Show_MissionPriority;
            if (ImGui.Checkbox("Show Mission Priority Tab".Loc() + "###ICEShowMissionPriorityTab", ref showMissionPrio))
            {
                C.Show_MissionPriority = showMissionPrio;
                C.Save();
            }

            bool showMisc = C.Show_MiscSettings;
            if (ImGui.Checkbox("Show Misc Settings Tab".Loc() + "###ICEShowMiscSettingsTab", ref showMisc))
            {
                C.Show_MiscSettings = showMisc;
                C.Save();
            }

            bool showHubActivities = C.Show_HubActivities;
            if (ImGui.Checkbox("Show Hub Activities Section".Loc() + "###ICEShowHubActivitiesSection", ref showHubActivities))
            {
                C.Show_HubActivities = showHubActivities;
                C.Save();
            }
        }
    }
}
