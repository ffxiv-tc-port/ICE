using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ECommons.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.MainUi.HelpFolder
{
    internal class helpSelect_Required
    {
        public static void Draw()
        {
            ImGui.TextWrapped("These are a list of the following plugins that are required for the plugin to function. If you don't have these installed, it will not function properly".Loc());

            ImGui.Separator();
            ImGuiEx.IconWithText(FontAwesomeIcon.Hammer, "Crafting".Loc());
            HasPlugin("https://love.puni.sh/ment.json", "Artisan");

            ImGui.Separator();
            ImGuiEx.IconWithText(FontAwesomeIcon.Feather, "Gathering".Loc());
            ImGui.Text("For botanist/miner/fisher".Loc());
            HasPlugin("https://puni.sh/api/repository/veyn", "vnavmesh");
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.Text("For fisher only".Loc());
            HasPlugin("https://love.puni.sh/ment.json", "AutoHook");

            ImGui.Separator();
            ImGuiEx.IconWithText(FontAwesomeIcon.Running, "Automating Hub Activities".Loc());
            HasPlugin("https://puni.sh/api/repository/veyn", "vnavmesh");
        }

        public static void HasPlugin(string repo, string pluginName)
        {
            bool isInstalled = DalamudReflector.HasRepo($"{repo}");
            if (isInstalled)
            {
                FontAwesome.Print(EColor.Green, FontAwesome.Check);
                ImGui.SameLine();
                ImGui.Text("?? Repo is Installed".Loc(pluginName));
            }
            else
            {
                FontAwesome.Print(EColor.Red, FontAwesome.Cross);
                ImGui.SameLine();
                if (ImGui.Button("Install ?? Repo".Loc(pluginName) + $"###ICEInstallRepo_{pluginName}"))
                {
                    DalamudReflector.AddRepo(repo, true);
                    DalamudReflector.SaveDalamudConfig();
                }
            }

            bool hasPlugin = Utils.HasPlugin($"{pluginName}");

            if (hasPlugin)
            {
                FontAwesome.Print(EColor.Green, FontAwesome.Check);
                ImGui.SameLine();
                ImGui.Text("?? is installed".Loc(pluginName));
            }
            else
            {
                FontAwesome.Print(EColor.Red, FontAwesome.Cross);
                ImGui.SameLine();
                using (ImRaii.Disabled(installingPlugin))
                {
                    if (ImGui.Button("Install ??".Loc(pluginName) + $"###ICEInstallPlugin_{pluginName}"))
                    {
                        _ = InstallPlugin(repo, pluginName);
                    }
                }
            }
        }

        private static bool installingPlugin = false;
        private static async Task InstallPlugin(string repo, string pluginName)
        {
            if (installingPlugin) return; // Already installing

            installingPlugin = true;
            try
            {
                await DalamudReflector.AddPlugin(repo, pluginName);
                DalamudReflector.SaveDalamudConfig();
            }
            finally
            {
                installingPlugin = false;
            }
        }
    }
}
