using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ECommons.Reflection;
using FFXIVClientStructs;
using ICE.Ui.MainUi.HelpFolder;
using ICE.Ui.MainUi.Settings.Settings_Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ICE.Ui
{
    internal class InfoWindow : Window
    {
        public InfoWindow() : base("Ice's Cosmic Exploration - Info".Loc() + "###ICEInfoWindow")
        {
            Flags = ImGuiWindowFlags.None;
            SizeConstraints = new()
            {
                MinimumSize = new Vector2(300, 300),
                MaximumSize = new Vector2(4000, 4000),
            };

            P.windowSystem.AddWindow(this);
            AllowPinning = true;
            AllowClickthrough = true;

            // Do not swallow the game's ESC key while this window is focused.
            // Trade-off: ESC no longer closes this window; use the title bar X or /ice i.
            RespectCloseHotkey = false;
        }

        public void Dispose()
        {
            P.windowSystem.RemoveWindow(this);
        }

        /// <summary>
        /// ⚠️ <b>這個欄位全 repo 沒有任何一處賦值</b>（只有這裡的宣告與下面那個 <c>if</c>），
        /// 所以 <c>!HasGatheringSetup</c> 恆為 true、<c>else</c> 那段文字實際上永遠畫不出來。
        ///
        /// 📌 <b>刻意保留原樣</b>：把它拆掉是在改既有行為（會少掉一段字串與一個未來的接點），
        /// 而這次要修的是「按鈕沒有防誤觸」這個真缺陷。兩件事分開，不要順手一起動。
        /// </summary>
        private static bool HasGatheringSetup = false;

        public override void Draw()
        {
            ImGui.Text("Hi! Welcome to Ice's Cosmic Exploration [Short form, I.C.E.]".Loc());
            ImGui.Bullet();
            ImGui.TextWrapped(("This plugin is meant to help you with your cosmic exploration needs, " +
                              "from automating the gathering and crafting process, to the buying of shop items or spending those planetary credits away.").Loc());

            helpSelect_Required.Draw();

            ImGui.Separator();

            ImGuiEx.IconWithText(FontAwesomeIcon.Feather, "Gathering Setup".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            ImGui.Text("If you would like to auto setup gathering to where all missions have their gathering buffs to what I would recommend".Loc());

            if (!HasGatheringSetup)
            {
                // 🔴🔴 這顆按鈕會**清光使用者所有自訂的採集配置檔**
                //    （SetupAllProfiles 會把 GatherProfiles 裡 key != 0 的整個移除，
                //      再把引用它們的任務設定 GProfileId 打回 0），然後套上七個內建配置。
                //
                // 🔴 這裡原本是 GatherSettings.SetupAllProfiles() 的**內聯複製**，而且複製時
                //    漏掉了原版的兩道防護：ImRaii.Disabled(!LeftShift) 與那段警告文字。
                //    上面的 HasGatheringSetup 又永遠是 false ⇒ 按鈕永遠可按、按下去就沒了。
                //    改成呼叫同一個入口，防護一併照抄——一份行為只留一份程式碼，
                //    下次有人改了那邊，這裡不會再默默留在舊版。
                using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.LeftShift)))
                {
                    if (ImGui.Button("Setup Gathering Profiles".Loc() + "###ICESetupGatheringProfiles"))
                    {
                        GatherSettings.SetupAllProfiles();

                        C.Save();
                    }
                }
                ImGuiEx.HelpMarker(("PLEASE NOTE:\n" +
                                   "This will wipe out all your current profiles, and apply what I would suggest for each one.\n" +
                                   "For most of you this would be fine, this is really only here if you don't know what to apply for each one." +
                                   "If you're okay with this, hold left shift and apply").Loc());
            }
            else
            {
                ImGui.Text("All gathering profile have been updated/automatically applied".Loc());
            }
        }
    }
}
