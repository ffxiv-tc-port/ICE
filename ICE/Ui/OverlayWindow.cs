using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Config;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

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
            var currentMissionId = CosmicHelper.CurrentLunarMission;
            if (CosmicHelper.SheetMissionDict.TryGetValue(currentMissionId, out var missionName) && SchedulerMain.State != IceState.AbandonMission)
            {
                ImGui.Text("Current Mission: [??]".Loc(currentMissionId));
                ImGui.SameLine(0, 4);
                DrawMissionTypeTag(missionName);
                ImGui.Text(missionName.Name);
                ImGui.SameLine();
                DrawMissionStatusIcons(currentMissionId);
                DrawObjectives(currentMissionId);
            }
            else
            {
                ImGui.Text("Current Mission: None".Loc());

                // 還沒接到任務時，如果排程器已經選定了要去領哪一個，就把那個目標顯示出來。
                // 標籤刻意跟「目前任務」不同，避免被誤讀成已經接了。
                var target = Task_FindMission.TargetMissionId;
                if (target != 0 && CosmicHelper.SheetMissionDict.TryGetValue(target, out var targetInfo))
                {
                    ImGui.TextColored(ImGuiColors.DalamudYellow, "Heading to pick up: [??]".Loc(target));
                    ImGui.SameLine(0, 4);
                    DrawMissionTypeTag(targetInfo);
                    ImGui.TextColored(ImGuiColors.DalamudYellow, targetInfo.Name);
                    ImGui.SameLine();
                    DrawMissionStatusIcons(target);
                }
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
            // GetMissionsForHour() 已經濾掉「這個客戶端查不到資料」的任務
            //（見 PlayerHandlers.KnownMissionsOnly），所以下面的 TryGetValue 正常情況不會落空。
            var (currentList, nextList) = PlayerHandlers.GetMissionsForHour();
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
                        // ⚠️ 來源是寫死的 PhaennaMapV2/SinusMapV2，跟 SheetMissionDict 沒有共同保證。
                        //    PhaennaMapV2 的任務 ID 是 574..1004 —— 台服 WKSMissionUnit **有這些列，
                        //    但整列是空的**（第二顆星 Phaenna 的預留列），所以建不進 SheetMissionDict。
                        //    上游那條路徑目前走不到只是因為台服沒有 territory 1291。
                        ImGui.Text($"{(CosmicHelper.SheetMissionDict.TryGetValue(mission.MissionId, out var timedEntry) ? timedEntry.Name : "???")}");
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
                        // ⚠️ 來源是寫死的 PhaennaMapV2/SinusMapV2，跟 SheetMissionDict 沒有共同保證。
                        //    PhaennaMapV2 的任務 ID 是 574..1004 —— 台服 WKSMissionUnit **有這些列，
                        //    但整列是空的**（第二顆星 Phaenna 的預留列），所以建不進 SheetMissionDict。
                        //    上游那條路徑目前走不到只是因為台服沒有 territory 1291。
                        ImGui.Text($"{(CosmicHelper.SheetMissionDict.TryGetValue(mission.MissionId, out var timedEntry) ? timedEntry.Name : "???")}");
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

        /// <summary>
        /// 畫出目前任務各目標的完成進度、時間限制剩餘時間，以及（沒有逐項目標列時的）分數進度。
        /// 目標列資料完全來自遊戲的 <c>WKSMissionInfomation</c> 面板文字（見
        /// <see cref="MissionObjectiveReader"/>），抓不到符合形狀的資料就什麼都不畫，不會退回去猜。
        /// 目標列與分數列可以並存：目標列優先顯示，分數列（若讀得到）附加在後面。
        /// </summary>
        private static void DrawObjectives(uint missionId)
        {
            var objectives = MissionObjectiveReader.Get(missionId);

            foreach (var objective in objectives)
            {
                ImGui.Text("    ");
                ImGui.SameLine(0, 0);
                if (objective.Done)
                {
                    ImGui.TextColored(ImGuiColors.HealerGreen, $"{objective.Text}  {objective.Progress}");
                    ImGui.SameLine(0, 4);
                    ImGuiEx.Icon(ImGuiColors.HealerGreen, FontAwesomeIcon.Check);
                }
                else
                {
                    ImGui.TextUnformatted($"{objective.Text}  {objective.Progress}");
                }
            }

            var timeRemaining = MissionObjectiveReader.TimeRemaining;
            if (timeRemaining != null)
            {
                ImGui.Text("    ");
                ImGui.SameLine(0, 0);
                ImGui.TextUnformatted("Time Remaining: ??".Loc(timeRemaining));
            }

            var scoreReadable = DrawScoreProgress(missionId);

            LogScanDiagnosticsOnce(missionId, objectives.Count, scoreReadable, timeRemaining != null);
        }

        /// <summary>
        /// 評價型任務（沒有逐項目標列、只看分數的任務，例如採集特殊裝甲板材料）的分數進度：
        /// 目前評價／銀星／金星門檻，達標的門檻文字變綠。高難任務另外附加緊急進度那一段。
        /// 資料來源是 ECommons AddonMaster 的 <c>WKSMissionInfomation.CurrentScore/SilverScore/
        /// GoldScore/CriticalScore</c>，型別全是 <c>uint?</c>——讀不到就整段不畫，<b>不要 <c>?? 0</c></b>：
        /// 0 會畫出一個假的「評價 0」，讓掛機使用者誤判進度。
        /// </summary>
        /// <returns>供 <see cref="LogScanDiagnosticsOnce"/> 用的診斷旗標，不影響畫面。</returns>
        private static bool DrawScoreProgress(uint missionId)
        {
            if (!GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) || !missionInfo.IsAddonReady)
                return false;

            bool scoreReadable;
            if (missionInfo.CurrentScore is uint current && missionInfo.SilverScore is uint silver && missionInfo.GoldScore is uint gold)
            {
                scoreReadable = true;

                ImGui.Text("    ");
                ImGui.SameLine(0, 0);
                ImGui.TextUnformatted("Current Score: ??".Loc(current.ToString("N0", CultureInfo.InvariantCulture)));

                ImGui.Text("    ");
                ImGui.SameLine(0, 0);
                var silverText = "Silver Threshold: ??".Loc(silver.ToString("N0", CultureInfo.InvariantCulture));
                if (current >= silver)
                    ImGui.TextColored(ImGuiColors.HealerGreen, silverText);
                else
                    ImGui.TextUnformatted(silverText);

                ImGui.SameLine(0, 8);
                var goldText = "Gold Threshold: ??".Loc(gold.ToString("N0", CultureInfo.InvariantCulture));
                if (current >= gold)
                    ImGui.TextColored(ImGuiColors.HealerGreen, goldText);
                else
                    ImGui.TextUnformatted(goldText);
            }
            else
            {
                scoreReadable = false;
            }

            var isCritical = CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var missionEntry)
                && missionEntry.Attributes.HasFlag(MissionAttributes.Critical);

            if (isCritical && missionInfo.CriticalScore is { } critical)
            {
                ImGui.Text("    ");
                ImGui.SameLine(0, 0);
                var criticalText = "Critical Progress: ??/1".Loc(critical);
                if (critical >= 1)
                    ImGui.TextColored(ImGuiColors.HealerGreen, criticalText);
                else
                    ImGui.TextUnformatted(criticalText);
            }

            return scoreReadable;
        }

        /// <summary>
        /// 任務類型標籤——用 <see cref="CosmicHelper.GetMissionCategoryKey"/> 同一套跟主視窗任務分頁
        /// （緊急任務／天氣限定／時間限定／連續任務／A~D 階，互斥、依優先序判斷）一致的分類，
        /// 畫在任務名稱前面。查不到分類（理論上不會發生，Rank 保底是 1）就什麼都不畫，不畫假標籤。<br/>
        /// 這跟任務名稱本身可能自帶的「【高難】」前綴（部分任務在台服 sheet 資料裡原文就寫死這個
        /// 詞——不是 ICE 加的，也跟這裡的分類無關，見 CosmicHelper.SheetMissionDict 的 Name 來源）
        /// 是兩件事，兩者同時出現不算重複標記。<br/>
        /// 緊急任務用醒目橘色，其餘分類用中性灰色，跟疊加層既有配色
        /// （DalamudRed／HealerGreen／DalamudYellow／ParsedGold）並列不衝突。呼叫端要自己在呼叫前
        /// 先 SameLine 出間距；這裡畫完標籤後也會自己 SameLine 一次，方便緊接著畫任務名。
        /// </summary>
        private static void DrawMissionTypeTag(CosmicHelper.CosmicInfo info)
        {
            var key = CosmicHelper.GetMissionCategoryKey(info);
            if (key == null)
                return;

            var color = key == "Critical" ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudGrey3;
            ImGui.TextColored(color, $"【{key.Loc()}】");
            ImGui.SameLine(0, 4);
        }

        /// <summary>
        /// 疊加層版的任務狀態標記——跟主視窗任務清單「✓」欄＋任務名稱後面的旗標圖示同一份資料源
        /// （<see cref="ICE.Utilities.MissionStatusHelper"/>），只是換成疊加層自己慣用的
        /// <c>ImGuiEx.Icon</c> 畫法，不是照抄主視窗那份用材質裁切金牌圖的程式碼。
        /// 金牌＝已完成且已拿金章；綠勾＝已完成但還沒金章；紅叉＝尚未完成。
        /// 另外比照主視窗，有採集座標旗標／緊急任務地點的任務也一併標出來。
        /// </summary>
        private static void DrawMissionStatusIcons(uint missionId)
        {
            var (completed, gold) = MissionStatusHelper.GetStatus(missionId);
            if (completed)
            {
                if (gold)
                    ImGuiEx.Icon(ImGuiColors.ParsedGold, FontAwesomeIcon.Medal);
                else
                    ImGuiEx.Icon(ImGuiColors.HealerGreen, FontAwesomeIcon.Check);
            }
            else
            {
                ImGuiEx.Icon(ImGuiColors.DalamudRed, FontAwesomeIcon.Times);
            }

            if (CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var info))
            {
                if (info.MarkerId != 0)
                {
                    ImGui.SameLine(0, 4);
                    ImGuiEx.Icon(FontAwesomeIcon.Flag);
                }

                if (GatheringUtil.CriticalLocations.ContainsKey(missionId))
                {
                    ImGui.SameLine(0, 4);
                    ImGuiEx.Icon(FontAwesomeIcon.FlagCheckered);
                }
            }
        }

        /// <summary>已經為哪個任務印過掃描結果診斷，避免每幀洗記錄檔——同款節流手法見 <see cref="MissionObjectiveReader"/>。</summary>
        private static uint diagnosticsLoggedForMission;

        /// <summary>
        /// 任務切換時把「目標列幾筆／分數面板讀不讀得到／時間限制讀不讀得到」寫一次 Information，
        /// 供日後校準其他任務型別（例如製作型可能真的有目標列）時比對用。
        /// </summary>
        private static void LogScanDiagnosticsOnce(uint missionId, int objectiveCount, bool scoreReadable, bool timeReadable)
        {
            if (diagnosticsLoggedForMission == missionId)
                return;
            diagnosticsLoggedForMission = missionId;

            IceLogging.Info(
                $"任務 {missionId}：目標列 {objectiveCount} 筆／分數面板{(scoreReadable ? "可讀" : "不可讀")}" +
                $"／時間限制{(timeReadable ? "可讀" : "不可讀")}。（供疊加層顯示校準用）",
                "[OverlayWindow]");
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