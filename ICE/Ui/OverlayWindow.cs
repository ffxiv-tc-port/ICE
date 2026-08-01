using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Config;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ICE.Ui
{
    internal class OverlayWindow : Window
    {
        private uint selectedJob = C.SelectedJob;
        public OverlayWindow() : base("ICE Overlay".Loc() + "###ICEOverlayWindow", ImGuiWindowFlags.AlwaysAutoResize)
        {
            P.windowSystem.AddWindow(this);

            // Do not swallow the game's ESC key while this window is focused.
            // Trade-off: ESC no longer closes this window; toggle it in ICE settings.
            RespectCloseHotkey = false;
        }

        public void Dispose()
        {
            P.windowSystem.RemoveWindow(this);
        }

        public override bool DrawConditions()
        {
            return C.ShowOverlay
                && (PlayerHelper.IsInCosmicZone());
        }

        public override void Draw()
        {
            ImGui.Text("Current state: ".Loc() + SchedulerMain.State.ToString());
            if (CosmicHelper.SheetMissionDict.TryGetValue(CosmicHelper.CurrentLunarMission, out var missionName) && SchedulerMain.State != IceState.AbandonMission)
            {
                ImGui.Text("Current Mission: [??] ??".Loc(CosmicHelper.CurrentLunarMission, missionName.Name));
            }
            else
            {
                ImGui.Text("Current Mission: None".Loc());
            }
#if DEBUG
            if (C.ShowDebugGatherInfo)
            {
                ImGui.Text($"Current Collectable State: {Mission_Settings.CollectableStep}");
                ImGui.Text($"Current Node Count: {Mission_Settings.nodeTotal}");
            }
#endif

            ImGuiHelpers.ScaledDummy(2);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(2);

            (string currentWeather, uint currentWeatherId, string nextWeather, uint nextWeatherId, string nextWeatherTime) = WeatherForecastHandler.GetNextWeather();

            if (currentWeather != null)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Weather Forcast:".Loc());
                Svc.Texture.TryGetFromGameIcon(currentWeatherId, out var currentWeatherIcon);
                ImGui.SameLine(0, 2);
                ImGui.Image(currentWeatherIcon.GetWrapOrEmpty().Handle, new Vector2(23, 23));
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"{currentWeather}");
                    ImGui.EndTooltip();
                }
                ImGui.SameLine(0, 2);
                ImGui.AlignTextToFramePadding();
                ImGuiEx.Icon(FontAwesomeIcon.LongArrowAltRight);
                Svc.Texture.TryGetFromGameIcon(nextWeatherId, out var nextWeatherIcon);
                ImGui.SameLine(0, 2);
                ImGui.Image(nextWeatherIcon.GetWrapOrEmpty().Handle, new Vector2(23, 23));
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"{nextWeather}");
                    ImGui.EndTooltip();
                }
                ImGui.SameLine(0, 2);
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Next in: ??".Loc(nextWeatherTime));
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Timed Mission(s): ".Loc());
            var currentList = PlayerHandlers.GetMissionsForHour().currentMissions;
            var nextList = PlayerHandlers.GetMissionsForHour().nextMissions;
            foreach (var mission in currentList)
            {
                if (CosmicHelper.JobIconDict.TryGetValue(mission.ClassId, out var jobIcon))
                {
                    ImGui.SameLine(0, 2);
                    var imageSize = new Vector2(23, 23);
                    ImGui.Image(jobIcon.GetWrapOrEmpty().Handle, imageSize);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"[{mission.MissionId}]");
                        ImGui.SameLine(0, 2);
                        ImGui.Text($"{CosmicHelper.SheetMissionDict[mission.MissionId].Name}");
                        ImGui.EndTooltip();
                    }
                }
            }
            ImGui.SameLine(0, 2);
            ImGui.AlignTextToFramePadding();
            ImGuiEx.Icon(FontAwesomeIcon.LongArrowAltRight);
            ImGui.SameLine();
            foreach (var mission in nextList)
            {
                if (CosmicHelper.JobIconDict.TryGetValue(mission.ClassId, out var jobIcon))
                {
                    ImGui.SameLine(0, 2);
                    var imageSize = new Vector2(23, 23);
                    ImGui.Image(jobIcon.GetWrapOrEmpty().Handle, imageSize);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"[{mission.MissionId}]");
                        ImGui.SameLine(0, 2);
                        ImGui.Text($"{CosmicHelper.SheetMissionDict[mission.MissionId].Name}");
                        ImGui.EndTooltip();
                    }
                }
            }
            if (PlayerHelper.UsingSupportedJob())
            {
                if (CosmicHelper.CurrentLunarMission != 0)
                {
                    var missionId = CosmicHelper.CurrentLunarMission;
                    if (CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var missionInfo))
                    {
                        foreach (var jobId in missionInfo.Jobs)
                        {
                            if (CosmicHelper.JobIconDict.TryGetValue(jobId, out var jobIcon))
                            {
                                var imageSize = new Vector2(23, 23);
                                ImGui.Image(jobIcon.GetWrapOrEmpty().Handle, imageSize);
                                ImGui.SameLine();
                                ImGui.AlignTextToFramePadding();
                                Relic_XP.DrawScoreBar(new Vector2(340, 10), false, jobId);
                            }
                        }
                    }
                }
                else
                {
                    var jobId = Player.JobId;
                    if (CosmicHelper.JobIconDict.TryGetValue(jobId, out var jobIcon))
                    {
                        var imageSize = new Vector2(23, 23);
                        ImGui.Image(jobIcon.GetWrapOrEmpty().Handle, imageSize);
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        Relic_XP.DrawScoreBar(new Vector2(340, 10), false);
                    }
                }
            }
            if (C.ShowTotalScore)
            {
                (uint TotalScore, uint TotalComplete, uint MaxScore, Dictionary<uint, uint> ClassInfo) = Relic_XP.GetTotalScores();
                var ScoreBarSize = new Vector2(340, 10);
                Relic_XP.DrawXPBar("Total Score | Completed: [?? / 11]".Loc(TotalComplete), TotalScore, MaxScore, ScoreBarSize);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    foreach (var job in ClassInfo)
                    {
                        var jobIdInfo = job.Key;
                        var jobScore = job.Value;
                        var jobImage = CosmicHelper.JobIconDict[jobIdInfo];
                        ImGui.Image(jobImage.GetWrapOrEmpty().Handle, new Vector2(23, 23));
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("Score: ??".Loc(jobScore.ToString("N0")));
                    }
                    ImGui.EndTooltip();
                }
            }

            ImGuiHelpers.ScaledDummy(2);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(2);

            if (ImGuiEx.IconButton(FontAwesomeIcon.Home, "Open ICE"))
            {
                P.mainWindow.IsOpen = true;
            }
            ImGui.SameLine();

            // Start button (disabled while already ticking).
            using (ImRaii.Disabled(SchedulerMain.State != IceState.Idle || !PlayerHelper.UsingSupportedJob()))
            {
                if (ImGui.Button("Start".Loc() + "###ICEOverlayStart"))
                {
                    SchedulerMain.EnablePlugin();
                }
            }

            ImGui.SameLine();

            // Stop button (disabled while not ticking).
            using (ImRaii.Disabled(SchedulerMain.State == IceState.Idle))
            {
                if (ImGui.Button("Stop".Loc() + "###ICEOverlayStop"))
                {
                    SchedulerMain.DisablePlugin();
                }
            }
            ImGui.SameLine();
            ImGui.Checkbox("Stop after current mission".Loc() + "###ICEOverlayStopAfterCurrent", ref Mission_Settings.StopAfterCurrent);

            ImGuiHelpers.ScaledDummy(2);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(2);

            if (C.ShowExpBars)
            {
                var currentJobId = Player.JobId;

                bool showExp = (CosmicHelper.CrafterJobList.Contains(currentJobId) || CosmicHelper.GatheringJobList.Contains(currentJobId));

                if (CosmicHelper.CrafterJobList.Contains(currentJobId) || CosmicHelper.GatheringJobList.Contains(currentJobId))
                {
                    if (ImGui.CollapsingHeader("Relic Tool XP".Loc() + "###ICEOverlayRelicToolXP"))
                    {
                        Relic_XP.DrawRelicXP((uint)currentJobId);
                    }
                }
            }
        }

        void DrawScore()
        {
            try
            {
                var (classScore, cappedClassScore, totalScores, classId) = CosmicHelper.GetCosmicClassScores();

                ImGui.TextUnformatted(string.Create(CultureInfo.InvariantCulture,
                    $"{Svc.Data.GetExcelSheet<ClassJob>().GetRow(classId).Abbreviation}: {(float)cappedClassScore / 500_000:P} ({classScore:N0})"));
                ImGui.SameLine();
                using (ImRaii.Disabled())
                {
                    ImGui.TextUnformatted("--");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(string.Create(CultureInfo.InvariantCulture,
                        $"All: {(float)totalScores / 11 / 500_000:P} ({SeIconChar.CrossWorld.ToIconChar()} {11 * 500_000 - totalScores:N0})"));
                }
            }
            catch
            {
                // meh
            }
        }
    }
}