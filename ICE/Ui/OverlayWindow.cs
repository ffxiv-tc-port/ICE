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

            // 沒有進行中的任務就把金星通知的門閂放掉：交件／放棄之後重接**同一個**任務時，
            // 門閂若還鎖在那個 ID 上，第二趟達標就會靜默地不通知。
            if (currentMissionId == 0)
                MedalNotifier.ObserveScore(0, null, null);

            if (CosmicHelper.SheetMissionDict.TryGetValue(currentMissionId, out var missionName) && SchedulerMain.State != IceState.AbandonMission)
            {
                ImGui.Text("Current Mission: [??]".Loc(currentMissionId));
                ImGui.SameLine(0, 4);
                DrawMissionTypeTag(missionName);
                DrawUnsupportedTag(currentMissionId);
                // ⚠️ 任務名開頭夾著私用區圖示字元（實測第 470 列是 U+E0BE），ImGui 的字型畫不出來，
                //    直接畫會在名字前面多一個「�」。任務類型另外有 DrawMissionTypeTag 的標籤在，
                //    剝掉不會少掉資訊。
                ImGui.Text(GameTextUtil.StripGameIcons(missionName.Name));
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
                    DrawUnsupportedTag(target);
                    ImGui.TextColored(ImGuiColors.DalamudYellow, GameTextUtil.StripGameIcons(targetInfo.Name));
                    ImGui.SameLine();
                    DrawMissionStatusIcons(target);
                }
            }

            DrawUnsupportedOnBoard();
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
                        ImGui.Text(CosmicHelper.SheetMissionDict.TryGetValue(mission.MissionId, out var timedEntry) ? GameTextUtil.StripGameIcons(timedEntry.Name) : "???");
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
                        ImGui.Text(CosmicHelper.SheetMissionDict.TryGetValue(mission.MissionId, out var timedEntry) ? GameTextUtil.StripGameIcons(timedEntry.Name) : "???");
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

                // ClassInfo 是空的＝這一輪讀不到 WKSManager（見 Relic_XP.GetTotalScores 的判空）。
                // 這種時候整條總分條都不畫，不要畫一條「0 / 5,500,000」——那會被讀成「進度被清空了」。
                // ⚠️ 刻意用 if 包起來而不是 return：底下還有開始／停止按鈕與相關工具經驗值。
                if (ClassInfo.Count > 0)
                {
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
        /// 畫出目前任務各目標的完成進度，以及底下那一行的獎章達成摘要
        /// （見 <see cref="DrawMedalProgress"/>）。
        /// 目標列資料完全來自遊戲的 <c>WKSMissionInfomation</c> 面板文字（見
        /// <see cref="MissionObjectiveReader"/>），抓不到符合形狀的資料就什麼都不畫，不會退回去猜。
        /// 目標列與獎章列可以並存：目標列優先顯示，獎章列附加在後面。
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

                    // ImGuiEx.Icon 內部無條件 SameLine（見 DrawMissionStatusIcons 的 remarks），
                    // 不收掉的話下一列目標（或底下的獎章列）會被黏到這一列尾巴。
                    ImGui.NewLine();
                }
                else
                {
                    ImGui.TextUnformatted($"{objective.Text}  {objective.Progress}");
                }
            }

            var scoreReadable = DrawMedalProgress(missionId);

            LogScanDiagnosticsOnce(missionId, objectives.Count, scoreReadable, MissionObjectiveReader.TimeRemaining != null);
        }

        /// <summary>讀不到的值一律畫這個，<b>絕不畫 0</b>。</summary>
        private const string UnknownMark = "?";

        /// <summary>
        /// 「目前任務」<b>下一行</b>的達成進度摘要（一行畫完）。
        /// </summary>
        /// <remarks>
        /// 📌 刻意另起一行而不是接在任務名後面：那一行已經有「[任務 ID]＋類型標籤＋任務名＋
        /// 完成狀態圖示」，再接下去會長到看不完。<br/><br/>
        /// 任務分兩型，畫法不同：<br/>
        /// • <b>評價型</b>——銀／金門檻是分數，比的是面板的「目前評價」。<br/>
        /// • <b>時間型</b>——銀／金門檻是「交件時剩餘時間要多少以上」，比的是面板的時間限制列。
        ///   🔴 這一型的 <c>AtkValues[2]</c>（目前評價）是 <c>Undefined</c>，所以舊的
        ///   <c>CurrentScore is uint</c> 條件一定不成立，整段<b>什麼都不會畫</b>——這正是使用者
        ///   2026-08-06 回報「目前任務底下一片空白」的原因。同時舊路徑若真的畫出來也是錯的：
        ///   ECommons 的 <c>SilverScore</c> 會把「剩餘時間 25:10以上」用
        ///   <c>Regex.Replace(@"[^\d]", "")</c> 壓成 <b>2510</b>，那不是分數。
        /// </remarks>
        /// <returns>供 <see cref="LogScanDiagnosticsOnce"/> 用的診斷旗標，不影響畫面。</returns>
        private static bool DrawMedalProgress(uint missionId)
        {
            CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var sheet);

            var silver = MissionObjectiveReader.SilverCondition;
            var gold = MissionObjectiveReader.GoldCondition;

            // 時間型的判定優先信面板自己印出來的條件文字（「剩餘時間 25:10以上」——零解讀）；
            // 面板沒開、讀不到文字時才退回資料表的旗標（CosmicInfo.IsTimeGraded，就是排程器
            // 交件邏輯在用的那個 ScoreTimeRemaining，兩邊共用不會分岔）。
            var timeGraded = silver.IsTimeBased || gold.IsTimeBased || (sheet?.IsTimeGraded ?? false);

            ImGui.Text("    ");
            ImGui.SameLine(0, 0);

            ImGui.BeginGroup();
            var readable = timeGraded
                ? DrawTimeGradedLine(sheet, silver, gold)
                : DrawScoreGradedLine(missionId, sheet, silver, gold);
            ImGui.EndGroup();

            // 「起疑才查」的明細放 tooltip：面板原文、資料表數字。
            if (ImGui.IsItemHovered())
                DrawMedalTooltip(sheet, silver, gold, timeGraded);

            DrawCriticalProgress(missionId);

            return readable;
        }

        /// <summary>
        /// 時間型任務那一行：<c>剩餘 26:34 / 30:00　✓ 銀星 25:10　✓ 金星 25:50</c>。
        /// 勾／叉表示「<b>現在交件</b>拿不拿得到」，隨時間推移會從勾變叉。
        /// </summary>
        private static bool DrawTimeGradedLine(CosmicHelper.CosmicInfo? sheet,
            MissionObjectiveReader.MedalCondition silver,
            MissionObjectiveReader.MedalCondition gold)
        {
            var remaining = MissionObjectiveReader.RemainingSeconds;
            var total = MissionObjectiveReader.TotalSeconds
                ?? (sheet is { TimeLimitSeconds: > 0 } ? (int)sheet.TimeLimitSeconds : null);

            // 🔴 讀不到就畫「?」，不要畫 0 —— 掛機的人看到 0:00 會以為任務已經超時。
            ImGui.TextUnformatted("Remaining: ?? / ??".Loc(
                remaining is int r ? GameTextUtil.FormatDuration(r) : UnknownMark,
                total is int t ? GameTextUtil.FormatDuration(t) : UnknownMark));

            var silverThreshold = TimeThreshold(silver, sheet?.SilverScore);
            var goldThreshold = TimeThreshold(gold, sheet?.GoldScore);

            DrawMedalTag("Silver".Loc(), DurationOrRaw(silverThreshold, silver), Reached(remaining, silverThreshold));
            DrawMedalTag("Gold".Loc(), DurationOrRaw(goldThreshold, gold), Reached(remaining, goldThreshold));

            return remaining != null;
        }

        /// <summary>
        /// 評價型任務那一行：<c>評價 1,820　✓ 銀星 1,200　✗ 金星 2,400　距金星還差 580</c>。
        /// </summary>
        /// <remarks>
        /// 尾巴那個「距金星還差 ??」<b>只有在目前評價與金星門檻兩邊都讀得到、而且還沒達標時才畫</b>：<br/>
        /// • 已達金星就不畫（畫「還差 0」等於在講廢話，還白白撐寬版面）。<br/>
        /// • 🔴 任一邊是 null 就不畫。拿 <c>current ?? 0</c> 去算差距會畫出「還差一整個門檻」——
        ///   一個看起來完全合理、卻是憑空捏造的數字。這一行的「不知道」由前面的
        ///   <c>評價 ?</c>（黃字）負責表達，不要在後面再補一個假數字。<br/><br/>
        /// 同一組數字順便餵給 <see cref="MedalNotifier"/> 做「剛跨過金星」的一次性通知——
        /// 這裡本來就每幀讀一次面板，<b>不是新開的輪詢</b>，而且那邊純通知、不改任何行為。
        /// </remarks>
        private static bool DrawScoreGradedLine(uint missionId, CosmicHelper.CosmicInfo? sheet,
            MissionObjectiveReader.MedalCondition silver,
            MissionObjectiveReader.MedalCondition gold)
        {
            uint? current = null, panelSilver = null, panelGold = null;
            if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) && missionInfo.IsAddonReady)
            {
                current = missionInfo.CurrentScore;
                panelSilver = missionInfo.SilverScore;
                panelGold = missionInfo.GoldScore;
            }

            // 面板讀不到門檻就退回資料表（評價型的 SilverScore/GoldScore 就是分數本身）。
            var silverThreshold = panelSilver ?? PositiveOrNull(sheet?.SilverScore);
            var goldThreshold = panelGold ?? PositiveOrNull(sheet?.GoldScore);

            // 🔴 讀不到分數時畫「評價 ?」而且是黃字（跟 DrawMedalTag 的第三態同色）——
            //    「不知道」本身要在列上看得見，不能只有一個不起眼的問號，更不能畫成 0。
            if (current is uint c)
            {
                ImGui.TextUnformatted("Rating: ??".Loc(c.ToString("N0", CultureInfo.InvariantCulture)));
            }
            else
            {
                ImGui.TextColored(ImGuiColors.DalamudYellow, "Rating: ??".Loc(UnknownMark));
            }

            DrawMedalTag("Silver".Loc(), ScoreOrRaw(silverThreshold, silver), Reached(current, silverThreshold));
            DrawMedalTag("Gold".Loc(), ScoreOrRaw(goldThreshold, gold), Reached(current, goldThreshold));

            if (current is uint cur && goldThreshold is uint goldTarget && cur < goldTarget)
            {
                ImGui.SameLine(0, 10);
                ImGui.TextColored(ImGuiColors.DalamudGrey3,
                    "?? to gold".Loc((goldTarget - cur).ToString("N0", CultureInfo.InvariantCulture)));
            }

            MedalNotifier.ObserveScore(missionId, current, goldThreshold);

            return current != null;
        }

        /// <summary>
        /// 一個獎章標記：圖示＋名稱＋門檻。三態——達成（綠勾）／未達成（灰叉）／
        /// <b>不知道</b>（黃問號）。
        /// </summary>
        /// <remarks>
        /// 🔴 第三態是刻意的：把「讀不到」畫成灰叉等於斷言「還沒達成」，那是在騙人。
        /// 顏色與圖示<b>同時</b>帶訊息，所以就算色弱也分得出來，且不疊背景色。
        /// </remarks>
        private static void DrawMedalTag(string label, string valueText, bool? achieved)
        {
            var (color, icon) = achieved switch
            {
                true => (ImGuiColors.HealerGreen, FontAwesomeIcon.Check),
                false => (ImGuiColors.DalamudGrey3, FontAwesomeIcon.Times),
                _ => (ImGuiColors.DalamudYellow, FontAwesomeIcon.Question),
            };

            ImGui.SameLine(0, 10);
            ImGuiEx.Icon(color, icon);
            ImGui.SameLine(0, 3);
            ImGui.TextColored(color, $"{label} {valueText}");
        }

        /// <summary>
        /// 時間型門檻：優先用面板條件文字解出來的秒數，面板沒開就換算資料表的值。
        /// </summary>
        /// <remarks>
        /// 資料表那條路的單位是「剩餘秒數 × 10」（<see cref="CosmicHelper.CosmicInfo.IsTimeGraded"/>
        /// 的註解裡有離線核對過程），所以要除以 10。
        /// </remarks>
        private static int? TimeThreshold(MissionObjectiveReader.MedalCondition condition, uint? sheetValue)
        {
            if (condition.RequiredRemainingSeconds is int fromPanel)
                return fromPanel;

            if (sheetValue is uint raw && raw > 0)
                return (int)(raw / 10);

            return null;
        }

        /// <summary>0 一律當成「沒有資料」——門檻 0 會讓「已達成」恆真。</summary>
        private static uint? PositiveOrNull(uint? value) => value is uint v && v > 0 ? v : null;

        /// <summary>
        /// 門檻的顯示文字：解析得出來就畫成 <c>25:10</c>；解析不出來但面板有原文就<b>原樣顯示</b>；
        /// 兩者皆無才畫「?」。
        /// </summary>
        private static string DurationOrRaw(int? seconds, MissionObjectiveReader.MedalCondition condition)
            => seconds is int s ? GameTextUtil.FormatDuration(s)
             : condition.HasText ? condition.Raw!
             : UnknownMark;

        /// <inheritdoc cref="DurationOrRaw"/>
        private static string ScoreOrRaw(uint? score, MissionObjectiveReader.MedalCondition condition)
            => score is uint v ? v.ToString("N0", CultureInfo.InvariantCulture)
             : condition.HasText ? condition.Raw!
             : UnknownMark;

        /// <summary>「達成了沒」——任一邊不知道就回 null（＝畫成問號），不要當成未達成。</summary>
        private static bool? Reached(int? currentValue, int? threshold)
            => currentValue is int c && threshold is int t ? c >= t : null;

        /// <inheritdoc cref="Reached(int?, int?)"/>
        private static bool? Reached(uint? currentValue, uint? threshold)
            => currentValue is uint c && threshold is uint t ? c >= t : null;

        /// <summary>
        /// 獎章那一行的 tooltip：面板原文與資料表數字。這些是「起疑才查」的東西，不占列上版面。
        /// </summary>
        private static void DrawMedalTooltip(CosmicHelper.CosmicInfo? sheet,
            MissionObjectiveReader.MedalCondition silver,
            MissionObjectiveReader.MedalCondition gold,
            bool timeGraded)
        {
            ImGui.BeginTooltip();

            ImGui.TextUnformatted("Time limit: ??".Loc(MissionObjectiveReader.TimeRemaining ?? "Not read from the mission panel".Loc()));
            ImGui.TextUnformatted("Silver condition: ??".Loc(silver.HasText ? silver.Raw! : "Not read from the mission panel".Loc()));
            ImGui.TextUnformatted("Gold condition: ??".Loc(gold.HasText ? gold.Raw! : "Not read from the mission panel".Loc()));

            if (!timeGraded
                && GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo)
                && missionInfo.IsAddonReady)
            {
                ImGui.TextUnformatted("Panel scores: current ?? / silver ?? / gold ??".Loc(
                    missionInfo.CurrentScore?.ToString() ?? UnknownMark,
                    missionInfo.SilverScore?.ToString() ?? UnknownMark,
                    missionInfo.GoldScore?.ToString() ?? UnknownMark));
            }

            if (sheet != null)
            {
                // ⚠️ 時間型任務的 SilverScore/GoldScore 單位是「剩餘秒數 × 10」不是分數，
                //    所以這裡刻意標成「資料表原始值」，不要讓人以為那是評價分數。
                ImGui.TextUnformatted("Sheet: limit ??s / silver ?? / gold ??".Loc(
                    sheet.TimeLimitSeconds, sheet.SilverScore, sheet.GoldScore));
            }

            ImGui.EndTooltip();
        }

        /// <summary>
        /// 高難任務的緊急進度。原行為不變（另起一行、達標變綠），只是從舊的
        /// <c>DrawScoreProgress</c> 拆出來，好讓上面兩型任務共用。
        /// </summary>
        private static void DrawCriticalProgress(uint missionId)
        {
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var missionEntry)
                || !missionEntry.Attributes.HasFlag(MissionAttributes.Critical))
                return;

            if (!GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo)
                || !missionInfo.IsAddonReady
                || missionInfo.CriticalScore is not { } critical)
                return;

            ImGui.Text("    ");
            ImGui.SameLine(0, 0);
            var criticalText = "Critical Progress: ??/1".Loc(critical);
            if (critical >= 1)
                ImGui.TextColored(ImGuiColors.HealerGreen, criticalText);
            else
                ImGui.TextUnformatted(criticalText);
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
        /// 「ICE 跑不動這個任務」的標記，畫在任務名稱前面（造型與 <see cref="DrawMissionTypeTag"/>
        /// 的類型標籤一致，但用紅色以示區別）。滑過去有一行說明為什麼跑不動。
        /// </summary>
        /// <remarks>
        /// 🔑 這是<b>遊戲裡</b>唯一看得到的標記位置——主視窗任務表上的紅色三角形使用者在接任務
        /// 的當下根本沒開。判定來源與挑選流程共用 <see cref="MissionSupport"/>，
        /// 不可能出現「表上說不支援、實際卻去跑」的分岔。
        /// </remarks>
        private static void DrawUnsupportedTag(uint missionId)
        {
            if (!MissionSupport.IsUnsupported(missionId, out var reason))
                return;

            ImGui.TextColored(ImGuiColors.DalamudRed, MissionSupport.Marker);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(MissionSupport.ReasonText(reason));
                ImGui.EndTooltip();
            }
            ImGui.SameLine(0, 4);
        }

        /// <summary>
        /// 遊戲的任務板（<c>WKSMission</c>）開著的時候，把板子上「ICE 跑不動」的任務列出來。
        /// </summary>
        /// <remarks>
        /// 🔑 使用者是<b>在遊戲裡接任務的當下</b>需要知道哪些不能自動跑，而遊戲的原生清單我們
        /// 不去改（改原生 UI 文字是另一個等級的風險）。這一段的作用是：任務板一打開，
        /// 疊加層就同步列出「這幾個接了 ICE 也不會跑」。板子關掉就什麼都不畫。<br/><br/>
        /// ⚠️ <c>StellerMissions</c> 每次讀都是從 addon 的 AtkValues 現場解析，
        /// <b>不跨幀保存任何東西</b>（同款用法見 <see cref="DrawScoreProgress"/>）。<br/>
        /// ⚠️ 板子上的任務 ID 不保證在 <c>SheetMissionDict</c> 裡，名字一律走 TryGetValue。
        /// </remarks>
        private static void DrawUnsupportedOnBoard()
        {
            if (!GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var board) || !board.IsAddonReady)
                return;

            List<(uint Id, string Name, MissionSupport.UnsupportedReason Reason)> unsupported = [];
            foreach (var entry in board.StellerMissions)
            {
                var id = entry.MissionId;
                if (id == 0 || !MissionSupport.IsUnsupported(id, out var reason))
                    continue;

                var name = CosmicHelper.SheetMissionDict.TryGetValue(id, out var info) ? GameTextUtil.StripGameIcons(info.Name) : "???";
                unsupported.Add((id, name, reason));
            }

            if (unsupported.Count == 0)
                return;

            ImGuiHelpers.ScaledDummy(2);
            ImGui.TextColored(ImGuiColors.DalamudRed,
                "?? mission(s) on the board cannot be automated by ICE:".Loc(unsupported.Count));
            foreach (var (id, name, reason) in unsupported)
            {
                ImGui.Text("    ");
                ImGui.SameLine(0, 0);
                ImGui.TextColored(ImGuiColors.DalamudRed, $"{MissionSupport.Marker}[{id}] {name}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(MissionSupport.ReasonText(reason));
                    ImGui.EndTooltip();
                }
            }
        }

        /// <summary>
        /// 疊加層版的任務狀態標記——跟主視窗任務清單「✓」欄＋任務名稱後面的旗標圖示同一份資料源
        /// （<see cref="ICE.Utilities.MissionStatusHelper"/>），只是換成疊加層自己慣用的
        /// <c>ImGuiEx.Icon</c> 畫法，不是照抄主視窗那份用材質裁切金牌圖的程式碼。
        /// 金牌＝已完成且已拿金章；綠勾＝已完成但還沒金章；紅叉＝尚未完成。
        /// 另外比照主視窗，有採集座標旗標／緊急任務地點的任務也一併標出來。
        /// </summary>
        /// <remarks>
        /// 🔴 <b>畫完會自己把那一行收掉（<c>ImGui.NewLine()</c>），呼叫端接下來畫的東西一定在下一行。</b><br/>
        /// 原因是 ECommons 的 <c>ImGuiEx.Icon</c> 內部<b>無條件</b>呼叫 <c>ImGui.SameLine()</c>
        /// （<c>ImGuiMethods/ImGuiEx/Text.cs</c> 的 <c>IconWithText</c>，即使沒有附帶文字也照做），
        /// 所以每個 <c>ImGuiEx.Icon</c> 都會留下一個<b>待處理的 SameLine</b>。這個函式最後一定
        /// 以 Icon 收尾，不收掉的話呼叫端下一個 widget 會被黏到任務名那一行尾巴——
        /// 使用者 2026-08-06 回報的「評價那串跑到任務名同一行」就是這樣來的。<br/><br/>
        /// ⚠️ 這裡用 <c>NewLine()</c> 是安全的：本函式必定至少畫過一個圖示，
        /// 也就是這一行必定有內容（<c>CurrLineSize.y &gt; 0</c>），此時 <c>NewLine()</c> 只是把行收掉、
        /// <b>不會</b>多插一行空白。（在空行上呼叫 <c>NewLine()</c> 才會多一行，那不是這裡的情況。）
        /// </remarks>
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

            // 把 ImGuiEx.Icon 留下的待處理 SameLine 收掉（理由見上面的 remarks）。
            ImGui.NewLine();
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