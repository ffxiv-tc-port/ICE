using Dalamud.Interface.Colors;
using ICE.Utilities.GatheringHelper;
using System.Collections.Generic;
using System.IO;

namespace ICE.Ui.MainUi.Settings.Settings_Table;

/// <summary>
/// 「採集路線」設定頁：讓使用者知道每一條路線現在是內建還是自訂的，並且能自己改。
/// </summary>
/// <remarks>
/// UI 準則（使用者明示過的）：<b>「隨時掃視」的資訊放列上、「起疑才查」的放 tooltip；
/// 但「這是自訂的」「這個檔載入失敗了」本身要在列上看得見</b>，不能只寫進 log。
/// 所以「來源」是一個獨立欄位，載入失敗則是頁面最上方一塊固定顯示的警告，
/// tooltip 只放檔案完整路徑與採集點編號這種起疑才會去看的東西。
/// </remarks>
internal static class GatherRouteCustomization
{
    private static Dictionary<(uint Territory, Vector2 Flag), List<string>>? _missionNames;

    private static string _lastActionMessage = string.Empty;
    private static bool _lastActionSuccess;
    private static DateTime _lastActionTime = DateTime.MinValue;

    public static void Draw()
    {
        ImGui.TextWrapped(("The plugin ships with a built-in gathering route for every mission flag. " +
                           "Drop your own .yaml into the custom folder and it replaces the built-in route " +
                           "for that same zone and flag - the built-in one is never deleted, so you can always go back.").Loc());

        ImGui.Dummy(new Vector2(0, 5));

        ImGui.TextUnformatted("Custom route folder:".Loc());
        ImGui.SameLine();
        var dir = SafeCustomDirectory();
        ImGui.TextColored(ImGuiColors.DalamudGrey, dir);

        if (ImGui.Button("Open Custom Route Folder".Loc()))
            OpenCustomFolder(dir);

        ImGui.SameLine();

        if (ImGui.Button("Reload Routes".Loc()))
        {
            try
            {
                GatheringRouteLoader.ReloadRoutes();
                _missionNames = null;
                SetMessage(("Routes reloaded: ?? built-in, ?? custom, ?? failed.")
                           .Loc(GatheringRouteLoader.BuiltInRouteCount,
                                GatheringRouteLoader.CustomRouteCount,
                                GatheringRouteLoader.LoadErrors.Count), true);
            }
            catch (Exception ex)
            {
                SetMessage("Reload failed: ??".Loc(ex.Message), false);
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(("Re-reads the custom folder right now - no need to restart the game. " +
                              "Unsaved edits made in the debug route editor are discarded.").Loc());

        ImGui.Dummy(new Vector2(0, 5));

        var pickClosest = C.GatherPickClosestNode;
        if (ImGui.Checkbox("Always head for the closest available node".Loc(), ref pickClosest))
        {
            C.GatherPickClosestNode = pickClosest;
            C.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(("On: after finishing a node, pick whichever node on the route is closest to you " +
                              "and can still be gathered. Off: walk the route in the order the file lists them, " +
                              "which is how it worked before - turn this off if you see the character moving back and forth.").Loc());

        ImGui.Dummy(new Vector2(0, 5));

        DrawSummary();
        DrawErrors();
        DrawMessage();

        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 5));

        DrawRouteTable();
    }

    private static void DrawSummary()
    {
        int builtIn = GatheringRouteLoader.BuiltInRouteCount;
        int custom = GatheringRouteLoader.CustomRouteCount;
        int overridden = GatheringRouteLoader.OverriddenRouteCount;
        int failed = GatheringRouteLoader.LoadErrors.Count;

        ImGui.TextUnformatted("Built-in: ??".Loc(builtIn));
        ImGui.SameLine();
        ImGui.TextColored(custom > 0 ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudGrey,
                          "Custom: ?? (?? replacing a built-in route)".Loc(custom, overridden));
        ImGui.SameLine();
        // 🔴 「載入失敗 0 個」也要畫出來 —— 只有在有問題時才出現的欄位，會讓使用者
        //    分不出「沒問題」與「這個畫面根本沒在檢查」。
        ImGui.TextColored(failed > 0 ? ImGuiColors.DalamudRed : ImGuiColors.DalamudGrey,
                          "Failed to load: ??".Loc(failed));
    }

    private static void DrawErrors()
    {
        var errors = GatheringRouteLoader.LoadErrors;
        if (errors.Count == 0)
            return;

        ImGui.Dummy(new Vector2(0, 3));
        ImGui.TextColored(ImGuiColors.DalamudRed,
                          ("?? custom route file(s) could not be read and were skipped. " +
                           "Those flags are still using the built-in route.").Loc(errors.Count));

        foreach (var error in errors)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudOrange, error.FileName);
            ImGui.SameLine();
            ImGui.TextWrapped(error.Reason);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(error.FullPath);
        }
        ImGui.Dummy(new Vector2(0, 3));
    }

    private static void DrawRouteTable()
    {
        Dictionary<uint, Dictionary<Vector2, List<Resources.GatheringRoutes.GathNodeInfo>>> routes;
        try
        {
            routes = GatheringRouteLoader.LoadAllRoutes();
        }
        catch (Exception ex)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "Could not load routes: ??".Loc(ex.Message));
            return;
        }

        var missionNames = GetMissionNames();

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit
                       | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("ICE_GatherRouteCustomization", 6, tableFlags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
            return;

        ImGui.TableSetupColumn("Missions".Loc(), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Zone".Loc());
        ImGui.TableSetupColumn("Flag".Loc());
        ImGui.TableSetupColumn("Job".Loc());
        ImGui.TableSetupColumn("Nodes".Loc());
        ImGui.TableSetupColumn("Source".Loc());
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        foreach (var (zoneId, zoneRoutes) in routes.OrderBy(x => x.Key))
        {
            foreach (var (flag, nodes) in zoneRoutes.OrderBy(x => x.Key.X).ThenBy(x => x.Key.Y))
            {
                ImGui.TableNextRow();
                ImGui.PushID($"{zoneId}_{flag.X}_{flag.Y}");

                var (zoneName, job) = GatheringRouteLoader.GetRouteLabel(zoneId, flag);
                var source = GatheringRouteLoader.GetRouteSource(zoneId, flag);
                var customPath = GatheringRouteLoader.GetCustomFilePath(zoneId, flag);

                // 任務名稱 —— 使用者認得的是「採集有益菌類」，不是 (748, 101)
                ImGui.TableNextColumn();
                var names = missionNames.TryGetValue((zoneId, flag), out var list) ? list : new List<string>();
                if (names.Count == 0)
                {
                    // ⚠️ 查不到對應任務就直說「查不到」，不要畫成空白讓人以為沒有資料欄位。
                    ImGui.TextColored(ImGuiColors.DalamudGrey, "(no mission uses this flag)".Loc());
                }
                else
                {
                    ImGui.TextUnformatted(string.Join("、", names));
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(zoneName);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{(int)flag.X}, {(int)flag.Y}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(job);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(nodes.Count.ToString());
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(string.Join(", ", nodes.Select(x => x.NodeId)));

                ImGui.TableNextColumn();
                if (source == GatheringRouteSource.Custom)
                {
                    ImGui.TextColored(ImGuiColors.ParsedGreen, "Custom".Loc());
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(customPath ?? string.Empty);

                    ImGui.SameLine();
                    if (ImGui.SmallButton("Revert".Loc()))
                        RevertRoute(zoneId, flag);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Delete the custom file and go back to the built-in route.".Loc());
                }
                else
                {
                    ImGui.TextColored(ImGuiColors.DalamudGrey, "Built-in".Loc());

                    ImGui.SameLine();
                    if (ImGui.SmallButton("Customise".Loc()))
                        CustomiseRoute(zoneId, flag);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(("Copy this route into the custom folder so you can edit it. " +
                                          "It takes effect immediately.").Loc());
                }

                ImGui.PopID();
            }
        }

        ImGui.EndTable();
    }

    private static void CustomiseRoute(uint zoneId, Vector2 flag)
    {
        try
        {
            var path = GatheringRouteLoader.SaveAsCustomRoute(zoneId, flag);
            _missionNames = null;
            SetMessage("Custom copy written to ??".Loc(path), true);
        }
        catch (Exception ex)
        {
            SetMessage("Could not create the custom copy: ??".Loc(ex.Message), false);
        }
    }

    private static void RevertRoute(uint zoneId, Vector2 flag)
    {
        try
        {
            if (GatheringRouteLoader.DeleteCustomRoute(zoneId, flag))
            {
                _missionNames = null;
                SetMessage("Custom route deleted, back to the built-in one.".Loc(), true);
            }
            else
            {
                SetMessage("Could not find the custom file to delete.".Loc(), false);
            }
        }
        catch (Exception ex)
        {
            SetMessage("Could not delete the custom route: ??".Loc(ex.Message), false);
        }
    }

    private static string SafeCustomDirectory()
    {
        try
        {
            return GatheringRouteLoader.CustomRoutesDirectory;
        }
        catch (Exception ex)
        {
            return $"<{ex.Message}>";
        }
    }

    private static void OpenCustomFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            GenericHelpers.ShellStart(dir);
        }
        catch (Exception ex)
        {
            SetMessage("Could not open the folder: ??".Loc(ex.Message), false);
        }
    }

    /// <summary>
    /// 旗標座標 → 用到這個旗標的任務名稱。
    /// </summary>
    /// <remarks>
    /// 每列都去掃一次 <c>SheetMissionDict</c> 的話，是「路線數 × 任務數」次比對<b>每一幀</b>。
    /// 建一次表存起來，重新載入或改動時把它設成 null 就好。
    /// </remarks>
    private static Dictionary<(uint, Vector2), List<string>> GetMissionNames()
    {
        if (_missionNames != null)
            return _missionNames;

        var map = new Dictionary<(uint, Vector2), List<string>>();
        try
        {
            foreach (var (id, info) in CosmicHelper.SheetMissionDict)
            {
                if (string.IsNullOrWhiteSpace(info.Name))
                    continue;

                var key = (info.TerritoryId, info.MapPosition);
                if (!map.TryGetValue(key, out var list))
                    map[key] = list = new List<string>();

                list.Add(info.Name.Trim());
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to build the mission-name lookup for the route page: {ex.Message}");
        }

        _missionNames = map;
        return map;
    }

    private static void SetMessage(string message, bool success)
    {
        _lastActionMessage = message;
        _lastActionSuccess = success;
        _lastActionTime = DateTime.Now;
    }

    private static void DrawMessage()
    {
        if (string.IsNullOrEmpty(_lastActionMessage))
            return;

        if ((DateTime.Now - _lastActionTime).TotalSeconds > 10)
            return;

        ImGui.TextWrapped(_lastActionMessage);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddRect(min, max,
            ImGui.GetColorU32(_lastActionSuccess ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed));
    }
}
