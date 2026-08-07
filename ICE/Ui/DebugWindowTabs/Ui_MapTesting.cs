using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Ui_MapTesting
    {
        private static int TableRow = 1;
        private static int posX = 0;
        private static int posY = 0;
        private static int posRadius = 0;

        public static unsafe void Draw()
        {
            ImGui.InputInt("TableId", ref TableRow);

            var MapInfo = ExcelHelper.MarkerSheet;

            if (ImGui.Button($"Test Radius"))
            {
                var agent = AgentMap.Instance();

                // TableRow 是 InputInt，使用者可以打任何值（含負數）。GetRow() 查無此列時擲
                // ArgumentOutOfRangeException，而這裡在 ImGui 繪製路徑上（DebugWindow 的 switch
                // 沒有 try/catch）—— 擲一次就會讓 UiBuilder 把 Draw/OpenConfigUi 設為 null，
                // 整個 ICE 介面到重開遊戲前都不會回來。台服 WKSMissionMapMarker 只有 0~100 列。
                if (TableRow < 0 || !MapInfo.TryGetRow((uint)TableRow, out var markerRow))
                {
                    IceLogging.Info($"地圖標記測試：WKSMissionMapMarker 沒有第 {TableRow} 列（台服有效範圍 0~{MapInfo.Count - 1}），不執行。", "[Ui_MapTesting]");
                }
                else
                {
                    int _x = markerRow.Unknown1.ToInt() - 1024;
                    int _y = markerRow.Unknown2.ToInt() - 1024;
                    int _radius = markerRow.Unknown3.ToInt();
                    IceLogging.Debug($"X: {_x} Y: {_y} Radius: {_radius}");

                    Utils.SetGatheringRing(1237, _x, _y, _radius);
                }
            }
            ImGui.SetNextItemWidth(125);
            ImGui.InputInt("Map X (Sheet)", ref posX);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(125);
            ImGui.InputInt("Map Y (Sheet)", ref posY);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(125);
            ImGui.InputInt("Map Radius", ref posRadius);
            if (ImGui.Button($"Test Map Marker from coords"))
            {
                var agent = AgentMap.Instance();
                int _x = posX - 1024;
                int _y = posY - 1024;
                IceLogging.Debug($"X: {_x} Y: {_y}");

                Utils.SetGatheringRing(agent->CurrentTerritoryId, _x, _y, posRadius);
            }
        }
    }
}
