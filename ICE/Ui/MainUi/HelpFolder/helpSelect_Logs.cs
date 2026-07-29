using Dalamud.Interface.Utility.Raii;
using ICE.Utilities.Cosmic_Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ICE.Utilities.Cosmic_Helper.IceLogging;

namespace ICE.Ui.MainUi.HelpFolder
{
    internal class helpSelect_Logs
    {
        private static string searchFilter = string.Empty;

        public static void Draw_Helper()
        {
            using (var headerChild = ImRaii.Child("##helpSelect_Logs", new Vector2(0, 0), true, ImGuiWindowFlags.NoScrollbar))
            {
                if (!headerChild.Success) return; // Ensures that it was loaded properly before continuing.
                if (ImGui.BeginTabBar("Ice Log Tabs"))
                {
                    if (ImGui.BeginTabItem("Main Logs".Loc() + "###ICELogsMain"))
                    {
                        LogHelperViewer();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Destination Logs".Loc() + "###ICELogsDestination"))
                    {
                        DestinationLogViewer();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
            }
        }

        public static void Draw_Debug()
        {
            if (ImGui.Button("Copy logs to clipboard".Loc() + "###ICECopyLogsToClipboard"))
            {
                LogSystem.CopyToClipboard();
            }
            LogHelperViewer();
        }

        private static void LogHelperViewer()
        {
            // Search input
            ImGui.SetNextItemWidth(300);
            ImGui.InputTextWithHint("##LogSearch", "Search logs...".Loc(), ref searchFilter, 256);

            ImGui.SameLine();
            if (ImGui.Button("Clear".Loc() + "###ICEClearLogSearch"))
            {
                searchFilter = string.Empty;
            }

            ImGui.Spacing();

            ImGuiTableFlags flags = ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit;

            if (ImGui.BeginTable("LogTable", 4, flags))
            {
                ImGui.TableSetupColumn("Time".Loc());
                ImGui.TableSetupColumn("Level".Loc());
                ImGui.TableSetupColumn("Category".Loc());
                ImGui.TableSetupColumn("Message".Loc());
                ImGui.TableHeadersRow();

                // Filter logs based on search input
                var filteredLogs = LogSystem.Logs.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    filteredLogs = filteredLogs.Where(log =>
                        log.Message.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                        (log.Category?.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        log.Level.ToString().Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
                    );
                }

                foreach (var log in filteredLogs.OrderByDescending(l => l.Timestamp))
                {
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.Text(log.Timestamp.ToString("HH:mm:ss"));

                    ImGui.TableNextColumn();
                    // Color-code by level
                    var color = log.Level switch
                    {
                        LogLevel.Error => new Vector4(1, 0, 0, 1),
                        LogLevel.Warning => new Vector4(1, 1, 0, 1),
                        LogLevel.Info => new Vector4(0, 1, 1, 1),
                        _ => new Vector4(0.7f, 0.7f, 0.7f, 1)
                    };
                    ImGui.TextColored(color, log.Level.ToString());

                    ImGui.TableNextColumn();
                    ImGui.Text(log.Category ?? "");

                    ImGui.TableNextColumn();
                    ImGui.Text(log.Message);
                }

                ImGui.EndTable();
            }
        }

        private static void DestinationLogViewer()
        {
            ImGuiTableFlags flags = ImGuiTableFlags.RowBg |
                                    ImGuiTableFlags.Borders |
                                    ImGuiTableFlags.ScrollY |
                                    ImGuiTableFlags.SizingFixedFit;

            if (ImGui.BeginTable("Destination Log Viewer", 5, flags))
            {
                ImGui.TableSetupColumn("Timestamp".Loc());
                ImGui.TableSetupColumn("Start".Loc());
                ImGui.TableSetupColumn("Destination".Loc());
                ImGui.TableSetupColumn("Distance".Loc());

                ImGui.TableHeadersRow();

                var filteredLogs = DestinationLogs.Logs.AsEnumerable();
                var entryNumber = 0;

                foreach (var log in filteredLogs.OrderByDescending(l => l.Timestamp))
                {
                    ImGui.TableNextRow();

                    ImGui.PushID($"{log.PlayerDestination}_{entryNumber}");

                    ImGui.TableSetColumnIndex(0);
                    Table_VertCenterText(log.Timestamp.ToString("HH:mm:ss"));

                    ImGui.TableNextColumn();
                    Table_VertCenterText($"X: {log.PlayerStart.X:N2}, Y: {log.PlayerStart.Y:N2}, Z: {log.PlayerStart.Z:N2}");

                    ImGui.TableNextColumn();
                    Table_VertCenterText($"X: {log.PlayerDestination.X:N2}, Y: {log.PlayerDestination.Y:N2}, Z: {log.PlayerDestination.Z:N2}");

                    ImGui.TableNextColumn();
                    Table_VertCenterText($"{log.Distance}");

                    ImGui.TableNextColumn();
                    if (ImGui.Button("Copy Info".Loc() + "###ICECopyDestinationInfo"))
                    {
                        var clipboardText = new StringBuilder();
                        clipboardText.AppendLine($"Start: X: {log.PlayerStart.X:N2}, Y: {log.PlayerStart.Y:N2}, Z: {log.PlayerStart.Z:N2}");
                        clipboardText.Append($"End: X: {log.PlayerDestination.X:N2}, Y: {log.PlayerDestination.Y:N2}, Z: {log.PlayerDestination.Z:N2}");
                        ImGui.SetClipboardText($"{clipboardText}");
                        Notify.Success("Log copied to clipbard".Loc());
                    }
                    ImGui.PopID();

                    entryNumber += 1;
                }

                ImGui.EndTable();
            }
        }
        private static void Table_VertCenterText(string text)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(text);
        }
    }
}