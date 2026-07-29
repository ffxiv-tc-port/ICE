using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using ICE.Utilities.GatheringHelper;
using Pictomancy;
using System.Collections.Generic;
using System.IO;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentWKSMission;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Ui_GatherRoute_Editor
    {
        private static Vector2 selectedRoute = Vector2.Zero;
        private static uint selectedZone = 0;
        private static uint selectedNode = 0;

        private static bool showOnlyVisibleNodes = false;
        private static float maxDistance = 75.0f;

        private static bool showSelectedNode = false;
        private static bool showRouteBetween = true;

        private static FileDialogManager fileDialogManager = new FileDialogManager();

        public static unsafe void Draw()
        {
            var routes = GatheringRouteLoader.LoadAllRoutes();

            // used for picto drawing here
            List<(uint nodeId, Vector3 position)> AllNodes = new();



            // end picto stuff

            using (var quickAccess = ImRaii.Child("Quick Access Routes", new Vector2(200, 120), true))
            {
                if (!quickAccess.Success)
                    return;

                ImGui.Text("Export Settings");
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0, 5));

                // Author Name Input
                ImGui.Text("Author Name:");
                ImGui.SetNextItemWidth(200);
                string authorName = C.AuthorName;
                if (ImGui.InputText("##AuthorName", ref authorName, 100))
                {
                    C.AuthorName = authorName;
                    C.SaveDebounced();
                }
            }

            ImGui.SameLine(0, 10);

            using (var quickAccess2 = ImRaii.Child("Quick Access Routes 2", new Vector2(300, 120), true))
            {
                if (!quickAccess2.Success)
                    return;

                // Custom Path Display
                ImGui.Text("Export Location:");
                string displayPath = string.IsNullOrEmpty(C.CustomRoutePath)
                    ? "Using default plugin config folder"
                    : C.CustomRoutePath;

                ImGui.TextWrapped(displayPath);

                ImGui.Dummy(new Vector2(0, 5));

                // Browse button to set custom path
                if (ImGui.Button("Browse for Export Folder"))
                {
                    fileDialogManager.OpenFolderDialog("Select Export Folder", (success, path) =>
                    {
                        if (success && !string.IsNullOrEmpty(path))
                        {
                            C.CustomRoutePath = path;
                            C.Save();
                            PluginLog.Information($"Export path set to: {path}");
                        }
                    });
                }

                ImGui.SameLine();

                // Clear custom path button
                if (!string.IsNullOrEmpty(C.CustomRoutePath))
                {
                    if (ImGui.Button("Use Default"))
                    {
                        C.CustomRoutePath = string.Empty;
                        C.Save();
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Clear custom path and use default plugin config folder");
                    }
                }
            }

            ImGui.SameLine(0, 10);

            var remainingQuickAccess = ImGui.GetContentRegionAvail();
            using (var quickAccess3 = ImRaii.Child("Quick Access 3", new Vector2(remainingQuickAccess.X, 120), true))
            {
                if (!quickAccess3.Success)
                    return;

                GatheringRouteExportUI.DrawExportAllButton();

                ImGui.Dummy(new Vector2(0, 5));

                if (selectedRoute != Vector2.Zero)
                {
                    GatheringRouteExportUI.DrawExportSelectedButton(selectedZone, selectedRoute);
                }
            }

            using (var routeSelectorUi = ImRaii.Child("Route Selector Window", new Vector2(200, 0), true))
            {
                if (!routeSelectorUi.Success)
                    return;

                foreach (var planet in routes)
                {
                    var planetTerritory = planet.Key;
                    ImGui.Text($"Zone: {planetTerritory}");
                    foreach (var mapLocation in planet.Value)
                    {
                        var location = mapLocation.Key;

                        ImGui.PushID($"{location}");

                        bool isSelected = selectedRoute == location;
                        string selectable = isSelected ? $"-> X: {location.X}, Y: {location.Y}" : $"X: {location.X}, Y: {location.Y}";

                        var jobId = GetJobIdForFlag(planetTerritory, location);

                        ImGui.Image(CosmicHelper.JobIconDict[jobId].GetWrapOrEmpty().Handle, new Vector2(20, 20));
                        ImGui.SameLine(0, 4);

                        float availableWidth = ImGui.GetContentRegionAvail().X;

                        if (ImGui.Selectable(selectable, isSelected, ImGuiSelectableFlags.None, new Vector2(availableWidth, 20)))
                        {
                            selectedRoute = location;
                            selectedZone = planetTerritory;
                        }

                        ImGui.PopID();
                    }
                }
            }

            ImGui.SameLine(0, 5);

            var windowSizeRemaining = ImGui.GetContentRegionAvail();
            using (var routeEditorUi = ImRaii.Child("Route Editor Window", windowSizeRemaining, true))
            {
                if (!routeEditorUi.Success)
                    return;

                if (selectedRoute == Vector2.Zero)
                {
                    ImGui.Text("No route is selected");
                }
                else
                {
                    string missions = string.Join(", ", GetMissionsForFlag(selectedZone, selectedRoute));
                    ImGui.Text($"Location: {MoonName(selectedZone)}");
                    ImGui.Text($"Missions: {missions}");
                    ImGui.Text($"Map Zone: X:{selectedRoute.X}, Z: {selectedRoute.Y}");

                    ImGui.Dummy(new Vector2(0, 5));

                    ImGui.Separator();

                    ImGui.Dummy(new Vector2(0, 5));

                    var routeList = GatheringRouteLoader.GetRoute(selectedZone, selectedRoute);

                    using (var routeListUi = ImRaii.Child("Route List Ui Selector", new Vector2(150, 200), true))
                    {
                        if (!routeListUi.Success)
                            return;

                        for (int i = 0; i < routeList.Count; i++)
                        {
                            var routeItem = routeList[i];
                            var nodeId = routeItem.NodeId;

                            if (!AllNodes.Contains((nodeId, routeItem.Position)))
                            {
                                AllNodes.Add((nodeId, routeItem.Position));
                            }

                            ImGui.PushID($"{nodeId}_routeViewer");

                            var isSelected = nodeId == selectedNode;
                            if (ImGui.Selectable($"{nodeId}", isSelected))
                            {
                                selectedNode = nodeId;
                            }
                            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsItemHovered())
                            {
                                ImGui.OpenPopup("Options for node");
                            }

                            if (ImGui.BeginPopup("Options for node"))
                            {
                                if (ImGui.Selectable("Remove Node"))
                                {
                                    routeList.Remove(routeItem);
                                }
                                if (ImGui.Selectable("Path to node"))
                                {
                                    P.TaskManager.Enqueue(() => Task_NavmeshMove.Task_NavTo(routeItem.LandZone, stayMounted: true), Utils.TaskConfig);
                                }

                                ImGui.EndPopup();
                            }

                            // Drag source
                            if (ImGui.BeginDragDropSource())
                            {
                                unsafe
                                {
                                    int dragIndex = i;
                                    byte* dataPtr = (byte*)&dragIndex;
                                    ReadOnlySpan<byte> data = new ReadOnlySpan<byte>(dataPtr, sizeof(int));
                                    ImGui.SetDragDropPayload("ROUTE_REORDER", data);
                                }
                                ImGui.Text($"Moving: {nodeId}");
                                ImGui.EndDragDropSource();
                            }

                            // Drop target
                            if (ImGui.BeginDragDropTarget())
                            {
                                unsafe
                                {
                                    var payload = ImGui.AcceptDragDropPayload("ROUTE_REORDER");
                                    if (!payload.IsNull)
                                    {
                                        int sourceIndex = *(int*)payload.Data;

                                        // Reorder the list
                                        if (sourceIndex != i && sourceIndex >= 0 && sourceIndex < routeList.Count)
                                        {
                                            var item = routeList[sourceIndex];
                                            routeList.RemoveAt(sourceIndex);
                                            routeList.Insert(i, item);
                                        }
                                    }
                                }
                                ImGui.EndDragDropTarget();
                            }

                            ImGui.PopID();
                        }
                    }

                    ImGui.SameLine(0, 5);

                    using (var allNodeViewer = ImRaii.Child("All Node Ui Viewer", new Vector2(200, 200), true))
                    {
                        if (!allNodeViewer.Success)
                            return;

                        ImGui.Text("All Node Viewer");

                        foreach (var x in Svc.Objects.Where(x => x.ObjectKind == ObjectKind.GatheringPoint && Player.DistanceTo(x.Position) <= maxDistance)
                                                     .OrderBy(x => Player.DistanceTo(x.Position)))
                        {
                            ImGui.PushID($"{x.DataId}_{x.Position}_NodeViewer");

                            ImGui.Text($"Id: {x.DataId} | Distance: {Player.DistanceTo(x.Position):N2}");
                            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsItemHovered())
                            {
                                ImGui.OpenPopup("Node Viewer Popup");
                            }

                            if (ImGui.BeginPopup("Node Viewer Popup"))
                            {
                                if (ImGui.Selectable("Add node to list"))
                                {
                                    routeList.Add(new Resources.GatheringRoutes.GathNodeInfo()
                                    {
                                        NodeId = x.DataId,
                                        Position = x.Position,
                                        LandZone = Player.Position,
                                    });

                                    ImGui.CloseCurrentPopup();
                                }

                                ImGui.EndPopup();
                            }

                            ImGui.PopID();
                        }
                    }

                    var nodeEditorSpace = ImGui.GetContentRegionAvail();
                    using (var nodeEditorUi = ImRaii.Child("Node Editor Ui", nodeEditorSpace))
                    {
                        if (!nodeEditorUi.Success)
                            return;

                        var route = routeList.Where(x => x.NodeId == selectedNode).FirstOrDefault();
                        if (route != null)
                        {
                            ImGui.Text($"Node Id: {route.NodeId}");
                            ImGui.Text($"Node Position: {route.Position}");

                            // Player Land Zone (currently static, might change this later)
                            Vector3 playerLandZone = route.LandZone;
                            ImGui.Text("Player Land Zone");
                            ImGui.SetNextItemWidth(200);
                            if (ImGui.InputFloat3("##Player Land Zone", ref playerLandZone))
                            {
                                route.LandZone = playerLandZone;
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Set to current position"))
                            {
                                route.LandZone = Player.Position;
                            }

                            // Radius Start/End
                            ImGui.Text("Radius Info");
                            float radiusStart = route.RadiusStart;
                            float radiusEnd = route.RadiusEnd;

                            ImGui.SetNextItemWidth(100);
                            if (ImGui.DragFloat("Start##radiusStart", ref radiusStart, 1, -360, 360))
                            {
                                route.RadiusStart = radiusStart;
                            }

                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(100);
                            if (ImGui.DragFloat("End##radiusEnd", ref radiusEnd, 1, -360, 360))
                            {
                                route.RadiusEnd = radiusEnd;
                            }

                            // Min/Max Distance
                            ImGui.Text("Distance to Node");
                            float minDistance = route.MinDistance;
                            float maxDistance = route.MaxDistance;

                            ImGui.SetNextItemWidth(100);
                            if (ImGui.DragFloat("Start##minDistance", ref minDistance, 0.1f, 0, 5))
                            {
                                route.MinDistance = minDistance;
                            }

                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(100);
                            if (ImGui.DragFloat("End##maxDistance", ref maxDistance, 0.1f, 0, 5))
                            {
                                route.MaxDistance = maxDistance;
                            }

                            if (ImGui.Button("Path to node"))
                            {
                                P.TaskManager.Enqueue(() => Task_NavmeshMove.Task_NavTo(route.LandZone, stayMounted: true), Utils.TaskConfig);
                            }

                            if (ImGui.Button("Test Path to all Nodes"))
                            {
                                var firstPosition = Vector3.Zero;
                                foreach (var routeItem in routeList)
                                {
                                    if (firstPosition == Vector3.Zero)
                                        firstPosition = routeItem.LandZone;

                                    P.TaskManager.Enqueue(() => Task_NavmeshMove.Task_NavTo(routeItem.LandZone, stayMounted: true), Utils.TaskConfig);
                                }
                                P.TaskManager.Enqueue(() => Task_NavmeshMove.Task_NavTo(firstPosition, stayMounted: true), Utils.TaskConfig);
                            }

                            if (ImGui.Button("Stop Task"))
                            {
                                P.TaskManager.Tasks.Clear();
                                P.TaskManager.Abort();
                                P.Navmesh.Stop();
                            }
                        }
                    }
                }
            }

            fileDialogManager.Draw();

            using (var drawing = PictoService.Draw())
            {
                if (drawing == null)
                    return;

                if (showRouteBetween && AllNodes.Count > 1)
                {
                    for (int i = 0; i < AllNodes.Count; i++)
                    {
                        var lineColor = C.PictoColor_Circle;
                        var currentNode = AllNodes[i];

                        // Draw line from previous node to current (skip only the first node)
                        if (i != 0)
                        {
                            var prevNode = AllNodes[i-1];
                            drawing.AddLine(prevNode.position, currentNode.position, 0, lineColor);
                        }

                        // If this is the last node, draw closing line back to first
                        if (i == AllNodes.Count - 1)
                        {
                            var firstNode = AllNodes[0];
                            drawing.AddLine(currentNode.position, firstNode.position, 0, lineColor);
                        }

                        drawing.AddText(new Vector3(currentNode.position.X, currentNode.position.Y + 1, currentNode.position.Z), C.PictoColor_Dot, $"Node: {currentNode.nodeId}", 1);
                        drawing.AddDot(currentNode.position, 5, C.PictoColor_Dot);
                    }
                }
            }
        }

        private static uint GetJobIdForFlag(uint territoryId, Vector2 map)
        {
            var missionEntry = CosmicHelper.SheetMissionDict.Where(x => x.Value.MapPosition == map
                                                                && x.Value.TerritoryId == territoryId).FirstOrDefault();

            if (missionEntry.Key != 0)
            {
                if (missionEntry.Value.Jobs.Contains(17))
                    return 17;
                else if (missionEntry.Value.Jobs.Contains(16))
                    return 16;
                else
                    return 8;
            }
            else
                return 8;
        }

        private static List<uint> GetMissionsForFlag(uint territoryId, Vector2 map)
        {
            List<uint> missions = new();
            foreach (var mission in CosmicHelper.SheetMissionDict.Where(x => x.Value.MapPosition == map && x.Value.TerritoryId == territoryId))
            {
                missions.Add(mission.Key);
            }

            return missions;
        }

        private static string MoonName(uint territoryId)
        {
            if (territoryId == 1237)
                return "Sinus Ardorum";
            else if (territoryId == 1291)
                return "Phaenna";
            else
            {
                return "???";
            }
        }

        public static class GatheringRouteExportUI
        {
            private static string? _lastExportMessage = null;
            private static DateTime _lastExportMessageTime = DateTime.MinValue;
            private static bool _lastExportSuccess = false;

            public static void DrawExportAllButton()
            {
                if (ImGui.Button("Export All Routes"))
                {
                    try
                    {
                        GatheringRouteLoader.ExportAllRoutes();
                        var exportPath = GetDefaultExportPath();
                        _lastExportMessage = $"Successfully exported all routes to:\n{exportPath}";
                        _lastExportSuccess = true;
                        _lastExportMessageTime = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Export failed: {ex.Message}");
                        _lastExportMessage = $"Export failed: {ex.Message}";
                        _lastExportSuccess = false;
                        _lastExportMessageTime = DateTime.Now;
                    }
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Export all routes to plugin config folder");
                }

                DrawExportMessage();
            }

            public static void DrawExportSelectedButton(uint zoneId, Vector2 flag)
            {
                if (ImGui.Button("Export Selected Route"))
                {
                    try
                    {
                        GatheringRouteLoader.ExportRoute(zoneId, flag);
                        var exportPath = GetDefaultExportPath();
                        _lastExportMessage = $"Successfully exported route to:\n{exportPath}";
                        _lastExportSuccess = true;
                        _lastExportMessageTime = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Export failed: {ex.Message}");
                        _lastExportMessage = $"Export failed: {ex.Message}";
                        _lastExportSuccess = false;
                        _lastExportMessageTime = DateTime.Now;
                    }
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Export this route to plugin config folder");
                }

                DrawExportMessage();
            }

            public static void DrawExportAllButtonWithCustomPath()
            {
                if (ImGui.Button("Export All Routes (Choose Location)"))
                {
                    // TODO: Add file picker dialog integration
                    ImGui.OpenPopup("export_path_picker");
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Export all routes to a custom location");
                }

                // Placeholder for file picker popup
                if (ImGui.BeginPopup("export_path_picker"))
                {
                    ImGui.Text("File picker not yet implemented");
                    ImGui.Text("Use default location button for now");
                    ImGui.EndPopup();
                }
            }

            public static void DrawExportSelectedButtonWithCustomPath(uint zoneId, Vector2 flag)
            {
                if (ImGui.Button("Export Selected Route (Choose Location)"))
                {
                    // TODO: Add file picker dialog integration
                    ImGui.OpenPopup("export_path_picker_selected");
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Export this route to a custom location");
                }

                // Placeholder for file picker popup
                if (ImGui.BeginPopup("export_path_picker_selected"))
                {
                    ImGui.Text("File picker not yet implemented");
                    ImGui.Text("Use default location button for now");
                    ImGui.EndPopup();
                }
            }

            private static void DrawExportMessage()
            {
                if (_lastExportMessage != null && (DateTime.Now - _lastExportMessageTime).TotalSeconds < 5)
                {
                    var color = _lastExportSuccess
                        ? new Vector4(0.0f, 1.0f, 0.0f, 1.0f)  // Green
                        : new Vector4(1.0f, 0.0f, 0.0f, 1.0f); // Red

                    ImGui.TextColored(color, _lastExportMessage);
                }
            }

            private static string GetDefaultExportPath()
            {
                var configDir = Svc.PluginInterface.GetPluginConfigDirectory();
                return Path.Combine(configDir, "ExportedRoutes");
            }
        }
    }
}
