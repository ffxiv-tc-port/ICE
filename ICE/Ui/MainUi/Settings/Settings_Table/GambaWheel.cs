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
