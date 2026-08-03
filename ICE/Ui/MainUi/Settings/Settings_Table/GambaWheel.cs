using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.SettingTabs
{
    internal class GambaWheel
    {
        private static bool gambaEnabled = C.GambaEnabled;
        private static int gambaDelay = C.GambaDelay;
        private static int gambaCreditsMinimum = C.GambaCreditsMinimum;
        private static bool gambaPreferSmallerWheel = C.GambaPreferSmallerWheel;

        public static void Draw()
        {
            DrawManualRun();
            ImGui.Separator();

            if (ImGui.Checkbox("Enable Auto Gamba".Loc() + "###ICEEnableAutoGamba", ref gambaEnabled))
            {
                C.GambaEnabled = gambaEnabled;
                C.Save();
            }
            ImGuiEx.HelpMarker("If you want to let it auto select the wheels and gamba, enable this. If you want to not auto run when you're running the gamble wheel, disable this.".Loc());
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt("Mininum credits to keep".Loc() + "###ICEGambaCreditsMinimum", ref gambaCreditsMinimum, 0, 10000))
            {
                C.GambaCreditsMinimum = gambaCreditsMinimum;
                C.SaveDebounced();
            }
            bool gambaBetween = C.GambaBetweenRuns;
            if (ImGui.Checkbox("Gamble Between Runs".Loc() + "###ICEGambaBetweenRuns", ref gambaBetween))
            {
                C.GambaBetweenRuns = gambaBetween;
                C.Save();
            }
            ImGui.SameLine();
            GambaSlider();
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt("Gamba Delay".Loc() + "###ICEGambaDelay", ref gambaDelay, 50, 2000))
            {
                C.GambaDelay = gambaDelay;
                C.SaveDebounced();
            }

            if (ImGui.Checkbox("Prefer smaller wheel".Loc() + "###ICEGambaPreferSmallerWheel", ref gambaPreferSmallerWheel))
            {
                C.GambaPreferSmallerWheel = gambaPreferSmallerWheel;
                C.Save();
            }
            ImGuiEx.HelpMarker("This will make the Gamba prefer wheels with less items.".Loc());
            ImGui.Separator();
            ImGui.TextUnformatted("Configure the weights for each item in the Gamba. Higher weight = more desirable.".Loc());
            ImGui.Spacing();
            foreach (GambaType type in Enum.GetValues(typeof(GambaType)))
            {
                var itemsType = C.GambaItemWeights.Where(x => x.Type == type).OrderBy(x => x.ItemId).ToList();
                if (itemsType.Count == 0) continue;
                if (ImGui.TreeNodeEx("?? (??)".Loc(type.ToString().Loc(), itemsType.Count) + $"##gamba_type_{type}", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    foreach (var gamba in itemsType)
                    {
                        var itemName = ExcelItemHelper.GetName(gamba.ItemId);
                        // 台服的 Item 表對「尚未開放的道具」會保留列但把 Name 留成空字串
                        // （實測 46782 / 46795 / 46840 / 47095 / 47973 都是這樣）。
                        // ExcelItemHelper.GetName 只處理「找不到列」的情況會回 #id，
                        // 名稱為空時會原樣回傳空字串，UI 上就變成「[47973] 」後面一片空白。
                        if (string.IsNullOrWhiteSpace(itemName))
                            itemName = "(not released on this client)".Loc();
                        int weight = gamba.Weight;
                        ImGui.SetNextItemWidth(120f);
                        if (ImGui.InputInt($"[{gamba.ItemId}] {itemName}##gamba_weight", ref weight))
                        {
                            gamba.Weight = weight;
                            C.Save();
                        }
                    }
                    ImGui.Unindent();
                    ImGui.TreePop();
                }
            }
            if (ImGui.Button("Reset Weights".Loc() + "###ICEResetGambaWeights"))
            {
                Task_Gamba.EnsureGambaWeightsInitialized(true);
            }
        }

        /// <summary>
        /// 手動觸發一次轉盤。原本這顆按鈕只掛在 Debug 視窗（<c>Hud_WheelofFortune</c>），
        /// 一般使用者根本按不到，於是「只想轉一次」的人只剩下打開 <c>GambaEnabled</c> 這條路——
        /// 🔴 而那個開關會在 WKSLottery 出現時呼叫 <c>SchedulerMain.EnablePlugin()</c>
        /// 把整套 ICE 任務排程叫起來（<c>GenericManager.DelayedTick</c>），完全不是同一件事。
        /// 這裡直接走 <see cref="Task_Gamba.Enqueue"/>：排一串轉盤專用的任務，跑完就回 Idle。
        /// </summary>
        private static void DrawManualRun()
        {
            var busy = SchedulerMain.State != IceState.Idle || P.TaskManager.NumQueuedTasks > 0;
            var ready = !busy && Player.Available && Task_Gamba.CanRunManually();

            using (ImRaii.Disabled(!ready))
            {
                if (ImGui.Button("Run the Cosmowheel once now".Loc() + "###ICEGambaRunOnce"))
                    Task_Gamba.Enqueue();
            }

            ImGuiEx.HelpMarker("Runs one Cosmowheel session using the weights below, then stops. This does NOT start ICE's mission automation.".Loc());

            if (busy)
                ImGuiEx.Text(ImGuiColors.DalamudYellow, "ICE is already running something; stop it first.".Loc());
            else if (!ready)
                ImGuiEx.Text(ImGuiColors.DalamudGrey, "Go to a cosmic exploration zone (or open the Cosmowheel) first.".Loc());
        }

        private static int[] allowedValues = { 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000 };
        private static void GambaSlider()
        {
            int currentIndex = Array.IndexOf(allowedValues, C.GambaAtAmount);
            if (currentIndex == -1)
            {
                currentIndex = 0;
                C.GambaAtAmount = allowedValues[0];
                C.SaveDebounced();
            }

            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt("Start Gambling @".Loc() + "###ICEGambaAtAmount", ref currentIndex, 0, allowedValues.Length - 1,
                allowedValues[currentIndex].ToString()))
            {
                C.GambaAtAmount = allowedValues[currentIndex];
                C.SaveDebounced();
            }
        }
    }
}
