using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Hud_MoonRecipe
    {
        public static void Draw()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSRecipeNotebook>("WKSRecipeNotebook", out var x) && x.IsAddonReady)
            {
                ImGui.Text(x.SelectedCraftingItem);

                if (ImGui.Button("Fill NQ".Loc()))
                {
                    x.NQItemInput();
                }
                ImGui.SameLine();

                if (ImGui.Button("Fill HQ".Loc()))
                {
                    x.HQItemInput();
                }
                ImGui.SameLine();

                if (ImGui.Button("Fill Both".Loc()))
                {
                    x.NQItemInput();
                    x.HQItemInput();
                }
                ImGui.SameLine();

                if (ImGui.Button("Synthesize".Loc()))
                {
                    x.Synthesize();
                }

                foreach (var m in x.CraftingItems)
                {
                    if (ImGui.Button($"Select ###Select + {m.Name}"))
                    {
                        m.Select();
                    }
                    ImGui.SameLine();
                    ImGui.Text($"{m.Name}");
                }
            }
            else
            {
                ImGui.Text("Waiting for \"WKSRecipeNotebook\" to be visible".Loc());
            }
        }
    }
}
