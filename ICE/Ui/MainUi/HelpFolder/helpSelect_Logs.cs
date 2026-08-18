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

        /// <summary>
        /// 日誌<b>寫入端</b>門檻的選單。放在日誌檢視器上，因為這裡就是這個設定唯一看得到效果的地方。
        /// </summary>
        /// <remarks>
        /// 🔴 只提供 Verbose／Debug／Info 三個選項，因為 <c>IceLogging.MinimumLevel</c> 的 setter
        /// 就把值夾在 Info 以下 —— Information 以上是回報診斷用的管道，不開放關掉。
        /// UI 少列幾個選項只是順帶；真正的保證在 setter，不在這裡。
        /// </remarks>
        private static void DrawMinimumLevelCombo()
        {
            // 等級名稱刻意用列舉原名（跟下面表格的「等級」欄逐字一致），不另外翻譯，
            // 免得同一個值在同一個視窗裡出現兩種寫法。
            var levels = new[] { LogLevel.Verbose, LogLevel.Debug, LogLevel.Info };
            var current = IceLogging.MinimumLevel;

            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            if (ImGui.BeginCombo("Minimum log level".Loc() + "###ICELogMinimumLevel", current.ToString()))
            {
                foreach (var level in levels)
                {
                    if (ImGui.Selectable(level.ToString(), level == current))
                    {
                        IceLogging.MinimumLevel = level;
                        // 存回設定檔時寫夾擠**之後**的值，免得設定檔留著一個永遠套不上的數字。
                        C.LogMinimumLevel = IceLogging.MinimumLevel;
                        C.Save();
                    }
                }
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Entries below this level are not written at all: no string is built and nothing enters the 3000-entry buffer.\nInformation and above can never be filtered out - that is the channel used for asking you to report diagnostics."
                    .Loc());
            }
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

            DrawMinimumLevelCombo();

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