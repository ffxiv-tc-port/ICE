using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using System.Collections.Generic;
using static MissionTimer;

namespace ICE.Ui.MainUi.ModeSelect
{
    internal partial class modeSelect_TableInfo
    {
        #region Table Tools

        private static void WindowSpacer()
        {
            ImGui.Dummy(new Vector2(0, 5));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 5));
        }

        // Center a checkbox both horizontally and vertically in the current table cell
        private static bool Table_CenterCheckbox(string id, ref bool value)
        {
            var cursorPos = ImGui.GetCursorPos();
            var availWidth = ImGui.GetContentRegionAvail().X;

            var checkboxSize = ImGui.GetFrameHeight();
            ImGui.SetCursorPosX(cursorPos.X + (availWidth - checkboxSize) * 0.5f);
            ImGui.AlignTextToFramePadding();

            return ImGui.Checkbox($"##{id}", ref value);
        }
        private static void Table_VertCenterText(string text)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(text);
        }
        private static void Table_FullCenterText(string text)
        {
            var cursorPosX = ImGui.GetCursorPosX();
            var availWidth = ImGui.GetContentRegionAvail().X;
            var textWidth = ImGui.CalcTextSize(text).X;

            ImGui.SetCursorPosX(cursorPosX + (availWidth - textWidth) * 0.5f);
            ImGui.AlignTextToFramePadding();

            ImGui.TextUnformatted(text);
        }
        private static void Table_FullCenterText(string icon, Vector4 color)
        {
            var cursorPosX = ImGui.GetCursorPosX();
            var availWidth = ImGui.GetContentRegionAvail().X;
            var textWidth = ImGui.CalcTextSize(icon).X;

            ImGui.SetCursorPosX(cursorPosX + (availWidth - textWidth) * 0.5f);
            ImGui.AlignTextToFramePadding();

            FontAwesome.Print(color, icon);
        }
        private static bool Table_CenteredButton(string label, Vector2? buttonSize = null)
        {
            var cursorPosX = ImGui.GetCursorPosX();
            var availWidth = ImGui.GetContentRegionAvail().X;

            Vector2 actualButtonSize;
            if (buttonSize.HasValue)
            {
                actualButtonSize = buttonSize.Value;
            }
            else
            {
                var textSize = ImGui.CalcTextSize(label);
                var framePadding = ImGui.GetStyle().FramePadding;
                actualButtonSize = new Vector2(textSize.X + framePadding.X * 2 + 10f, textSize.Y + framePadding.Y * 2);
            }

            ImGui.SetCursorPosX(cursorPosX + (availWidth - actualButtonSize.X) * 0.5f);
            return ImGui.Button(label, actualButtonSize);
        }
        private static void Table_FontCenter(FontAwesomeIcon icon)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(IconText(icon));
            ImGui.PopFont();
        }

        // The helpers below give the same answers as the LINQ they replace (First / Last / Overlaps / Where+FirstOrDefault)
        // without boxing the HashSet enumerator, once per row per frame. An empty set still throws like First() / Last().
        private static uint FirstJob(HashSet<uint> jobs)
        {
            foreach (var job in jobs)
                return job;
            return jobs.First();
        }

        private static uint LastJob(HashSet<uint> jobs)
        {
            var any = false;
            uint last = 0;
            foreach (var job in jobs)
            {
                last = job;
                any = true;
            }
            return any ? last : jobs.Last();
        }

        private static bool AnyGatheringJob(HashSet<uint> jobs)
        {
            foreach (var job in jobs)
            {
                if (CosmicHelper.GatheringJobList.Contains(job))
                    return true;
            }
            return false;
        }

        // UI thread only. Digits of an unsigned value do not depend on the culture, so one string per value is enough.
        private static readonly Dictionary<uint, string> NumberTextCache = new();

        private static string NumberText(uint value)
        {
            if (!NumberTextCache.TryGetValue(value, out var text))
            {
                text = value.ToString();
                NumberTextCache[value] = text;
            }
            return text;
        }

        // UI thread only. ToIconString() builds a new one-character string on every call.
        private static readonly Dictionary<FontAwesomeIcon, string> IconTextCache = new();

        private static string IconText(FontAwesomeIcon icon)
        {
            if (!IconTextCache.TryGetValue(icon, out var text))
            {
                text = icon.ToIconString();
                IconTextCache[icon] = text;
            }
            return text;
        }

        private static uint FirstCrafterJob(HashSet<uint> jobs)
        {
            foreach (var job in jobs)
            {
                if (CosmicHelper.CrafterJobList.Contains(job))
                    return job;
            }
            return 0;
        }

        public static List<uint> GetOnlyPreviousMissionsRecursive(uint missionId)
        {
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var missionInfo) || missionInfo.PreviousMissions.Contains(0))
                return [];

            var chain = GetOnlyPreviousMissionsRecursive(missionInfo.PreviousMissions.First());
            chain.Add(missionInfo.PreviousMissions.First());
            return chain;
        }
        private static List<uint> GetOnlyNextMissionsRecursive(uint missionId)
        {
            uint? nextMissionId = CosmicHelper.SheetMissionDict
                .Where(m => m.Value.PreviousMissions.First() == missionId)
                .Select(m => (uint?)m.Key)
                .FirstOrDefault();

            if (!nextMissionId.HasValue)
                return [];

            var chain = new List<uint> { nextMissionId.Value };
            chain.AddRange(GetOnlyNextMissionsRecursive(nextMissionId.Value));
            return chain;
        }
        private static void CompletionStatus_Formatted(uint id)
        {
            var (isCompleted, isGold) = MissionStatusHelper.GetStatus(id);

            float availableWidth = ImGui.GetContentRegionAvail().X;

            if (isCompleted)
            {
                if (isGold)
                {
                    // Center the image
                    float imageWidth = 23f;
                    float offsetX = (availableWidth - imageWidth) * 0.5f;

                    if (offsetX > 0)
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

                    if (Svc.Texture.GetFromGame("ui/uld/WKSMission_hr1.tex") is { } tex)
                    {
                        if (tex.TryGetWrap(out var wrap, out var exc))
                        {
                            var cursorPos = ImGui.GetCursorPos();
                            var availWidth = ImGui.GetContentRegionAvail().X;
                            var availHeight = ImGui.GetFrameHeight();

                            // Center horizontally
                            ImGui.SetCursorPosX(cursorPos.X + (availWidth - 23) * 0.5f);

                            // Center vertically
                            ImGui.SetCursorPosY(cursorPos.Y + (availHeight - 23) * 0.5f);
                            ImGui.Image(wrap.Handle, new Vector2(23, 23), new Vector2(0.2347f, 0.3500f), new Vector2(0.2959f, 0.6500f));
                        }
                    }
                }
                else
                {
                    ImGui.AlignTextToFramePadding();
                    Table_FullCenterText(FontAwesome.Check, EColor.Green);
                }
            }
            else
            {
                ImGui.AlignTextToFramePadding();
                Table_FullCenterText(FontAwesome.Cross, EColor.Red);
            }
        }
        private static void CompletionStatus_Normal(uint id)
        {
            var (isCompleted, isGold) = MissionStatusHelper.GetStatus(id);

            var containerSize = new Vector2(23, 23);

            // Create a consistent container for all elements
            var cursorPos = ImGui.GetCursorPos();
            ImGui.InvisibleButton("##status_container", containerSize);
            ImGui.SetCursorPos(cursorPos);

            if (isCompleted)
            {
                if (isGold)
                {
                    if (Svc.Texture.GetFromGame("ui/uld/WKSMission_hr1.tex") is { } tex)
                    {
                        if (tex.TryGetWrap(out var wrap, out var exc))
                        {
                            ImGui.Image(wrap.Handle, containerSize, new Vector2(0.2347f, 0.3500f), new Vector2(0.2959f, 0.6500f));
                        }
                    }
                }
                else
                {
                    // Center the FontAwesome icon within the container
                    var textSize = ImGui.CalcTextSize(FontAwesome.Check);
                    var offset = (containerSize - textSize) * 0.5f;
                    offset += new Vector2(-2f, 1f);
                    ImGui.SetCursorPos(cursorPos + offset);
                    FontAwesome.Print(EColor.Green, FontAwesome.Check);
                }
            }
            else
            {
                var textSize = ImGui.CalcTextSize(FontAwesome.Cross);
                var offset = (containerSize - textSize) * 0.5f;
                offset += new Vector2(-2f, 1f);
                ImGui.SetCursorPos(cursorPos + offset);
                FontAwesome.Print(EColor.Red, FontAwesome.Cross);
            }

            // Reset cursor to after the container
            ImGui.SetCursorPos(cursorPos + new Vector2(containerSize.X, 0));
        }
        private static void UpdateSelectedMission(uint missionId)
        {
            if (ImGui.IsItemClicked())
            {
                selectedMission = missionId;
            }
        }
        private static void DrawColoredStar(TurninState state)
        {
            Vector4 color = state switch
            {
                TurninState.Bronze => new Vector4(0.8f, 0.5f, 0.3f, 1.0f),  // Bronze
                TurninState.Silver => new Vector4(0.75f, 0.75f, 0.75f, 1.0f), // Silver
                TurninState.Gold => new Vector4(1.0f, 0.84f, 0.0f, 1.0f),    // Gold
                TurninState.Critical => new Vector4(1.0f, 0.84f, 0.0f, 1.0f), // Gold
                _ => new Vector4(0, 0, 0, 0) // Transparent/none
            };

            if (state != TurninState.None)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, color);
                ImGui.PushFont(UiBuilder.IconFont); // Make sure you're using the icon font
                ImGui.Text(FontAwesomeIcon.Star.ToIconString());
                ImGui.PopFont();
                ImGui.PopStyleColor();
            }
        }

        /// <summary>
        /// 任務資訊表裡的一列「銅／銀／金星門檻」。
        /// </summary>
        /// <remarks>
        /// 判斷沿用 <see cref="CosmicHelper.CosmicInfo.IsTimeGraded"/>（＝排程器交件邏輯與
        /// 疊加層在用的同一個旗標），不另立判斷，才不會出現「表格說是分數、交件卻按時間走」
        /// 的分岔。
        /// </remarks>
        private static void DrawMedalRequirementRow(string label, uint rawValue, bool timeGraded)
        {
            if (rawValue == 0)
                return;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text(label);

            ImGui.TableNextColumn();

            if (!timeGraded)
            {
                ImGui.Text($"{rawValue}");
                return;
            }

            ImGui.Text("?? or more remaining".Loc(GameTextUtil.FormatDuration((int)(rawValue / 10))));
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("Silver and gold on this mission are judged on how much time is left when you " +
                                  "turn in, not on a score.\nRaw sheet value: ?? (remaining seconds x 10).").Loc(rawValue));
            }
        }

        #endregion
    }
}
