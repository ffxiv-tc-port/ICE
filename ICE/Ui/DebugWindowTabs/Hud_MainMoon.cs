using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Hud_MainMoon
    {
        public static void Draw()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var HudAddon))
            {
                if (ImGui.Button("Mission".Loc()))
                {
                    HudAddon.Mission();
                }

                ImGui.SameLine();

                if (ImGui.Button("Mech".Loc()))
                {
                    HudAddon.Mech();
                }

                ImGui.SameLine();

                if (ImGui.Button("Steller"))
                {
                    HudAddon.Steller();
                }

                ImGui.SameLine();

                if (ImGui.Button("Infrastructor"))
                {
                    HudAddon.Infrastructor();
                }

                ImGui.SameLine();

                if (ImGui.Button("Research".Loc()))
                {
                    HudAddon.Research();
                }

                ImGui.SameLine();

                if (ImGui.Button("ClassTracker"))
                {
                    HudAddon.ClassTracker();
                }
            }
            else
            {
                ImGui.Text("Waiting for \"WKSHud\" to be visible".Loc());
            }
        }
    }
}
