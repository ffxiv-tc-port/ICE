using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Sounds;
using ICE.Ui.MainUi;
using ICE.Ui.MainUi.HelpFolder;
using ICE.Ui.MainUi.ModeSelect;
using ICE.Ui.MainUi.Settings;
using ICE.Ui.MainUi.Settings.Settings_Table;
using ICE.Ui.SettingTabs;
using ICE.Utilities.Cosmic;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Reflection;
using static MissionTimer;

namespace ICE.Ui
{
    internal class MainWindow : Window
    {
        public MainWindow() :
#if DEBUG
        base($"Ice's Cosmic Exploration {P.GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} [Debug Build] ###ICEMainWindow2")
#else
        base($"Ice's Cosmic Exploration {P.GetType().Assembly.GetName().Version} ###ICEMainWindow2")
#endif
        {
            Flags = ImGuiWindowFlags.NoScrollbar;
            SizeConstraints = new()
            {
                MinimumSize = new Vector2(100, 100),
                MaximumSize = new Vector2(4000, 4000),
            };
            TitleBarButtons.Add(new() { ShowTooltip = () => ImGui.SetTooltip("♥ Ko-fi (Buy me an ice coffee)".Loc()), Icon = FontAwesomeIcon.Heart, IconOffset = new(1, 1), Click = _ => GenericHelpers.ShellStart("https://ko-fi.com/ice643269") });

            P.windowSystem.AddWindow(this);

            AllowPinning = true;
            AllowClickthrough = true;

            // Do not swallow the game's ESC key while this window is focused.
            // Dalamud inhibits native addon close events AND the ESC system menu
            // for any focused window with RespectCloseHotkey == true.
            // Trade-off: ESC no longer closes this window; use the title bar X or /ice.
            RespectCloseHotkey = false;
        }

        public void Dispose()
        {
            P.windowSystem.RemoveWindow(this);
        }

        public override void Draw()
        {
            using var style = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 10).Push(ImGuiStyleVar.ChildBorderSize, 1);

            SelectableSidebar.Draw();

            ImGui.SameLine(0, 5);

            var windowSizeRemaining = ImGui.GetContentRegionAvail();
            using (var mainBody = ImRaii.Child("mainBody_WindowV3", windowSizeRemaining, true))
            {
                if (!mainBody.Success) return;
                MainBody();
            }
        }

        private static void MainBody()
        {
            switch (SelectableSidebar.currentSelection)
            {
                // Cosmic Helper
                case "modeSelect_Standard":
                    if (C.ShowCompletionWindow)
                    {
                        C.ShowCompletionWindow = false;
                        C.Save();
                    }
                    modeSelect_Standard.Draw();
                    break;
                case "modeSelect_Completion":
                    if (!C.ShowCompletionWindow)
                    {
                        C.ShowCompletionWindow = true;
                        C.Save();
                    }
                    modeSelect_Standard.Draw();
                    break;

                case "setting_MissionPriority":
                    Priority_Settings.Draw();
                    break;

                // ── UI 重構第四批：一級項＝一頁，頁內用 ImGui_Tools.PageSection 分節 ────
                // 使用者反饋「分頁拆了很多種但都只有一兩項，沒整理效果」⇒ 一級項收斂成 6 個。
                // 這幾個 case 呼叫的都是**組頁函式**，各節的內容一個字都沒改，只是換了位置。
                // 📌 已經消失的路由（都是搬進某一頁的一節，不是被刪掉的功能）：
                //    setting_StopWhen / setting_Safety / setting_Display / setting_Misc /
                //    setting_Interface   → 全部併進 page_Settings
                //    setting_GatheringProfile / setting_GatherRoutes → page_Gathering
                //    hubActivities_CreditShopping / hubActivites_GambaSetting → page_HubActivities
                //    setting_MechaOps    → page_MechaOps（同一頁改名，內容改成五節）
                case "page_Gathering":
                    GatheringPage.Draw();
                    break;
                case "page_HubActivities":
                    HubActivitiesPage.Draw();
                    break;
                case "page_MechaOps":
                    Misc_Settings.DrawMechaOpsPage();
                    break;
                case "page_Settings":
                    Misc_Settings.DrawSettingsPage();
                    break;

                // Help Section
                case "helpSelect_Requirements":
                    helpSelect_Required.Draw();
                    break;
                case "helpSelect_Logs":
                    helpSelect_Logs.Draw_Helper();
                    break;

                default:
                    ImGui.Text("No content for this tab yet.".Loc());
                    break;
            }
        }

        public static List<uint> GetOnlyPreviousMissionsRecursive(uint missionId)
        {
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var missionInfo) || missionInfo.PreviousMissions.Contains(0))
                return [];

            var chain = GetOnlyPreviousMissionsRecursive(missionInfo.PreviousMissions.First());
            chain.Add(missionInfo.PreviousMissions.First());
            return chain;
        }
        private List<uint> GetOnlyNextMissionsRecursive(uint missionId)
        {
            uint? nextMissionId = CosmicHelper.SheetMissionDict
                .Where(m => m.Value.PreviousMissions.First() == missionId)
                .Select(m => (uint?)m.Key)
                .FirstOrDefault();

            if (!nextMissionId.HasValue)
                return [];

            var chain = new List<uint> { nextMissionId.Value };
            chain.AddRange(GetOnlyNextMissionsRecursive(nextMissionId.Value));
            return chain;
        }
    }
}
