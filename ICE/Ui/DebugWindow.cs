using ICE.Ui.DebugWindowTabs;
using ICE.Ui.MainUi.HelpFolder;

namespace ICE.Ui;

internal class DebugWindow : Window
{
    public DebugWindow() :
        base($"ICE {P.GetType().Assembly.GetName().Version} Debugger ###IceCosmicDebug1")
    {
        Flags = ImGuiWindowFlags.None;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(100, 100),
            MaximumSize = new Vector2(3000, 3000)
        };
        P.windowSystem.AddWindow(this);

        // Do not swallow the game's ESC key while this window is focused.
        // Trade-off: ESC no longer closes this window; use the title bar X or /ice d.
        RespectCloseHotkey = false;
    }

    public void Dispose()
    {
        P.windowSystem.RemoveWindow(this);
    }

    private string[] DebugTypes = 
    [
        // HUD Elements
        "Hud: Moon Main",
        "Hud: Mission",
        "Hud: Mission Info",
        "Hud: Wheel of fortune!",
        "Hud: Moon Recipe",
        "Hud: Gather Collectable",
        "Hud: Item Exchange",
    
        // Table Elements
        "Table: Mission Info",
        "Table: Item List",
        "Table: Gathering Missions",
        "Table: Special Missions",
        "Table: Mission Text",
        "Table: Recipies",
    
        // UI Elements
        "Ui: Fishing Hole Editor",
        "Ui: Fishing Preset Editor",
        "Ui: Gather Editor",
        "Ui: Log Viewer",
    
        // Non-labeled Elements
        "Player Info",
        "Test Buttons",
        "IPC Testing",
        "Map Test",
        "Navmesh Testing",
        "Relic Info",
        "TaskManager Testing",
        "NPC Box Viewer",

        // Sheet Viewer Info
        "Sheet: Mission Rewards",

        // 機甲行動
        "Mecha: Event Recorder"
    ];

    int selectedDebugIndex = 0; // Keeping which tab I'm selecting here. Just persistant stuff.

    public override unsafe void Draw()
    {
        float spacing = 10f;
        float leftPanelWidth = 200f;
        float rightPanelWidth = ImGui.GetContentRegionAvail().X - leftPanelWidth - spacing;
        float childHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginChild("DebugSelector", new System.Numerics.Vector2(leftPanelWidth, childHeight), true))
        {
            for (int i = 0; i < DebugTypes.Length; i++)
            {
                bool isSelected = (selectedDebugIndex == i);
                string label = isSelected ? $"→ {DebugTypes[i]}" : $"   {DebugTypes[i]}";

                if (ImGui.Selectable(label, isSelected))
                {
                    selectedDebugIndex = i;
                }
            }
        }
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);

        if (ImGui.BeginChild("DebugContent", new System.Numerics.Vector2(rightPanelWidth, childHeight), true))
        {
            switch (selectedDebugIndex)
            {
                // HUD Elements (0-5)
                case 0: Hud_MainMoon.Draw(); break;
                case 1: Hud_Mission.Draw(); break;
                case 2: Hud_MissionInfo.Draw(); break;
                case 3: Hud_WheelofFortune.Draw(); break;
                case 4: Hud_MoonRecipe.Draw(); break;
                case 5: Hud_CollectableGathering.Draw(); break;
                case 6: Hud_ItemExchange.Draw(); break;

                // Table Elements (6-11)
                case 7: Table_MissionInfo.Draw(); break;
                case 8: Table_CustomItems.Draw(); break;
                case 9: Table_GatheringInfo.Draw(); break;
                case 10: Table_TimeWeather.Draw(); break;
                case 11: Table_MissionText.Draw(); break;
                case 12: Table_MoonRecipies.Draw(); break;

                // UI Elements (12-13)
                case 13: Ui_FishingEditor.Draw(); break;
                case 14: Ui_FishingMissionEditor.Draw(); break;
                case 15: Ui_GatherRoute_Editor.Draw(); break;
                case 16: helpSelect_Logs.Draw_Debug(); break;

                // Non-labeled Elements (14-21)
                case 17: Ui_PlayerInfo.Draw(); break;
                case 18: Ui_TestButtons.Draw(); break;
                case 19: Ui_IPCTesting.Draw(); break;
                case 20: Ui_MapTesting.Draw(); break;
                case 21: Ui_NavmeshTesting.Draw(); break;
                case 22: Ui_RelicInfo.Draw(); break;
                case 23: Ui_TaskManagerInfo.Draw(); break;
                case 24: Ui_NpcViewer.Draw(); break;

                case 25: Sheet_MissionRewards.Draw(); break;

                // 機甲事件錄製（Utilities/MechaOps/MechaEventRecorder.cs）
                case 26: Ui_MechaRecorder.Draw(); break;

                default: ImGui.Text("Unknown Debug View"); break;
            }
        }
        ImGui.EndChild();
    }
}
