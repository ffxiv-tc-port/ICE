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

                // Settings
                case "setting_StopWhen":
                    StopWhen.Draw();
                    break;
                case "setting_GatheringProfile":
                    GatherSettings.Draw();
                    break;
                case "setting_MissionPriority":
                    Priority_Settings.Draw();
                    break;
                case "setting_Misc":
                    Misc_Settings.Draw();
                    break;
                case "setting_GatherRoutes":
                    GatherRouteCustomization.Draw();
                    break;
                // 📌 舊的 "helpSelect_AllSettings"（把六個設定頁塞進一列按鈕的「全部設定」頁）
                //    已於 UI 重構第三批整頁移除，六個目的地在新側欄各自都有入口。
                //    它自帶的 DrawCategoryTab / settingsTabs 是那一頁專用的，一併消失；
                //    ⚠️ modeSelect_Standard 用的 EndCategoryButtonRow 是 ImGui_Tools 裡的**另一個**同名方法，不受影響。

                // ── UI 重構第二批新增的路由 ──────────────────────────────
                // 這三個 case 呼叫的是原本就住在 Misc 設定頁裡的那幾節，**內容一個字都沒改**，
                // 只是各自獨立成一頁。對應的節已經從 Misc_Settings.Draw() 移除，
                // 所以是「搬家」不是「複製」—— 同一組設定不會同時出現在兩個地方。
                case "setting_MechaOps":
                    Misc_Settings.DrawMechaOpsPage();
                    break;
                case "setting_Display":
                    Misc_Settings.DrawDisplayPage();
                    break;
                case "setting_Safety":
                    Misc_Settings.DrawSafetyPage();
                    break;
                case "setting_Interface":
                    InterfaceSettings.Draw();
                    break;


                // Hub Activities
                case "hubActivities_CreditShopping":
                    ShoppingTab.Draw();
                    break;
                case "hubActivites_GambaSetting":
                    GambaWheel.Draw();
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
