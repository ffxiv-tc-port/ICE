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
        public class ScoringInfo
        {
            public int Multipler { get; set; } = 1;
            public double Score { get; set; } = 0;
            public double Cosmocredits { get; set; } = 0;
            public double Planetcredits { get; set; } = 0;
            public int TotalCompleted { get; set; } = 0;
        }
        public static Dictionary<string, ScoringInfo> MissionScores = new()
        {
            ["Critical"] = new(),
            ["Bronze"] = new() { Multipler = 1 },
            ["Silver"] = new() { Multipler = 4 },
            ["Gold"] = new() { Multipler = 5 },
        };

        public static void DrawMissionDetails()
        {
            if (CosmicHelper.SheetMissionDict.TryGetValue(selectedMission, out var mission))
            {
                var id = selectedMission;
                ImGui.PushID($"{mission}_{id}");

                #region Mission Name

                ImGui.Text("Mission:".Loc());
                ImGui.SameLine(0, 5);
                ImGui.TextDisabled($"[{id}]");
                ImGui.SameLine(0, 5);
                if (MissionSupport.IsUnsupported(id, out var detailReason))
                {
                    ImGui.TextColored(ImGuiColors.DalamudRed, MissionSupport.Marker);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(MissionSupport.ReasonText(detailReason));
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine(0, 4);
                }
                ImGui.Text($"{mission.Name}");

                #endregion

                if (ImGui.BeginTable("Detailed Mission Info", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders))
                {
                    ImGui.TableSetupColumn("Name".Loc());
                    ImGui.TableSetupColumn("Info".Loc());

                    // Row 1
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Cosmocredits".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{mission.CosmoCredit}");

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Planetary Credits".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{mission.LunarCredit}");

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Class Score:".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{mission.ClassScore}");

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Job(s)".Loc());

                    ImGui.TableNextColumn();
                    foreach (var job in mission.Jobs)
                    {
                        ISharedImmediateTexture? icon = CosmicHelper.JobIconDict[job];
                        Vector2 size = new Vector2(20, 20);
                        ImGui.Image(icon.GetWrapOrEmpty().Handle, size);
                        ImGui.SameLine();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Completed:".Loc());

                    ImGui.TableNextColumn();
                    CompletionStatus_Normal(selectedMission);

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Relic XP Kind".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text("Amounts".Loc());

                    foreach (var xp in mission.RelicXpInfo.OrderByDescending(x => x.Key))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        string type = "";
                        switch (xp.Key)
                        {
                            case 1:
                                type = "I";
                                break;
                            case 2:
                                type = "II";
                                break;
                            case 3:
                                type = "III";
                                break;
                            case 4:
                                type = "IV";
                                break;
                            case 5:
                                type = "V";
                                break;
                            default:
                                type = "???";
                                break;
                        }

                        ImGui.Text("Lv. ??".Loc(type));
                        ImGui.TableNextColumn();
                        ImGui.Text($"{xp.Value}");
                    }

                    DrawMedalRequirementRow("Bronze Requirement".Loc(), mission.BronzeScore, mission.IsTimeGraded);
                    DrawMedalRequirementRow("Silver Requirement".Loc(), mission.SilverScore, mission.IsTimeGraded);
                    DrawMedalRequirementRow("Gold Requirement".Loc(), mission.GoldScore, mission.IsTimeGraded);

                    if (mission.MarkerId != 0)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text("Gathering Zone".Loc());

                        ImGui.TableNextColumn();

                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.Text(FontAwesomeIcon.Flag.ToIconString());
                        ImGui.PopFont();
                        if (ImGui.IsItemClicked())
                        {
                            Utils.SetGatheringRing(mission.TerritoryId, (int)mission.MapPosition.X, (int)mission.MapPosition.Y, mission.Radius, mission.Name);
                        }
                    }

                    if (GatheringUtil.CriticalLocations.TryGetValue(selectedMission, out var criticalLoc))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text("Critical Area".Loc());

                        ImGui.TableNextColumn();
                        ImGuiEx.Icon(FontAwesomeIcon.Flag);
                        if (ImGui.IsItemClicked())
                        {
                            Utils.SetFlagForNPC(mission.TerritoryId, criticalLoc.MapInfo.X, criticalLoc.MapInfo.Y);
                        }
                    }

                    ImGui.EndTable();
                }

                if (mission.Crafts_Main.Count > 0)
                {
                    WindowSpacer();

                    var craftCount = 0;
                    var job = mission.Jobs.First(x => CosmicHelper.CrafterJobList.Contains(x));
                    ImGui.Text("Recipe Detailed Info".Loc());
                    foreach (var craft in mission.Crafts_Main)
                    {
                        craftCount++;
                        if (Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Recipe>().TryGetRow(craft.Key, out var recipe))
                        {
                            string itemName = recipe.ItemResult.Value.Name.ToString();
                            if (ImGui.CollapsingHeader($"{itemName} [{craftCount}]"))
                            {
                                if (ImGui.BeginTable($"Recipe Detailed Info: {itemName} {craftCount}", 2, ImGuiTableFlags.SizingFixedFit))
                                {
                                    var recipeInfo = CosmicHelper.SpecificRecipeInfo(job, craft.Key);

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.Text("Durability".Loc());

                                    ImGui.TableNextColumn();
                                    ImGui.Text($"{recipeInfo.Durability}");

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.Text("Progress".Loc());

                                    ImGui.TableNextColumn();
                                    ImGui.Text($"{recipeInfo.Progress}");

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.Text("Max Quality".Loc());

                                    ImGui.TableNextColumn();
                                    ImGui.Text($"{recipeInfo.Quality}");

                                    ImGui.EndTable();
                                }
                            }
                        }
                    }
                }

                WindowSpacer();

                ImGui.Text("Mission Atributes".Loc());
                if (mission.Attributes == MissionAttributes.None)
                {
                    ImGui.Text("None".Loc());
                    return;
                }
                else
                {
                    foreach (MissionAttributes flag in Enum.GetValues<MissionAttributes>())
                    {
                        if (flag != MissionAttributes.None && mission.Attributes.HasFlag(flag))
                        {
                            ImGui.Text(flag.ToString().Loc());
                        }
                    }
                }

                if (CosmicHelper.MissionUnlock.TryGetValue(selectedMission, out var unlock))
                {
                    ImGui.Text("The following missions are required to have gold before you can do this one".Loc());
                    foreach (var lockedMission in unlock)
                    {
                        CompletionStatus_Normal(lockedMission);
                        ImGui.SameLine();
                        // 來源是寫死的 MissionUnlock 表 ⇒ TryGetValue（查不到顯示 "???"）。
                        var lockedName = CosmicHelper.SheetMissionDict.TryGetValue(lockedMission, out var lockedEntry) ? lockedEntry.Name : "???";
                        ImGui.Text($"[{lockedMission}] - {MissionSupport.NameWithMarker(lockedMission, lockedName)}");
                    }

                }

                WindowSpacer();
                ImGui.Text("Mission Times!".Loc());

                if (C.MissionConfig.TryGetValue(selectedMission, out var config))
                {
                    bool allowDelete = (ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift)) && (ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl));

                    using (ImRaii.Disabled(!allowDelete))
                    {
                        if (ImGui.Button("Reset Stats".Loc() + "###ICEResetStats"))
                        {
                            P.MissionTimer.ResetTimers(selectedMission);
                        }
                    }
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Hold Shift + Control".Loc());
                        ImGui.EndTooltip();
                    }

                    if (config.TurninRecords.Count > 0)
                    {
                        ImGui.Text("Best Time: ??".Loc(TimeSpan.FromSeconds(config.BestTime).ToString("mm\\:ss\\.ff")));
                        ImGui.Text("Average Time: ??".Loc(TimeSpan.FromSeconds(config.AverageTime).ToString("mm\\:ss\\.ff")));
                    }
                    else
                    {
                        ImGui.Text("Best Time: --:--:--".Loc());
                        ImGui.Text("Average Time: --:--:--".Loc());
                    }

                    ImGui.Text("Amount of times completed: ??".Loc(config.TotalCompletions));
                    ImGui.Text("Amount of timed abandoned: ??".Loc(config.FailedCounters));

                    // 金星穩定度。分母是「含放棄」的嘗試次數 —— 放棄掉的那幾次也花了時間，
                    // 只除以完成次數會把不穩的任務看成穩的。
                    // ⚠️「不知道」要在列上看得見：樣本數 0 顯示 "--"，不要畫成 0%（那會誤導成「試過都沒中」）。
                    var goldAttempts = config.TotalCompletions + config.FailedCounters;
                    if (goldAttempts > 0)
                    {
                        var goldRate = 100.0 * config.GoldCompletions / goldAttempts;
                        ImGui.Text("Gold rate: ??% (??/?? attempts, ?? abandoned)".Loc(
                            goldRate.ToString("F0"), config.GoldCompletions, goldAttempts, config.FailedCounters));
                    }
                    else
                    {
                        ImGui.Text("Gold rate: -- (no attempts recorded yet)".Loc());
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("?");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("Share of attempts that ended in a gold turn-in, counting abandoned runs as attempts.\n" +
                                         "Time lost to abandons so far: ??\n" +
                                         "Used by the optional 'Minimum gold rate' rule in Mission Picking Rules, and by the " +
                                         "'Effective Score/Min' sort order. Display is never filtered by that rule.").Loc(
                            FormatTotalTime(config.AbandonedTimeSeconds)));
                    }

                    if (CosmicHelper.SheetMissionDict.TryGetValue(selectedMission, out var missionInfo))
                    {
                        var baseScore = missionInfo.ClassScore;
                        var comsoCredit = missionInfo.CosmoCredit;
                        var planetCredit = missionInfo.LunarCredit;

                        ImGui.Separator();
                        ImGui.Text("Estimated Score Per Hour:".Loc());
                        ImGui.SameLine();
                        ImGui.TextDisabled("?");
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("This is ASSUMING:".Loc());
                            ImGui.Text("1: You have immaculate rng of getting the mission you want every time".Loc());
                            ImGui.Text("2: You're hitting the threshold every time".Loc());
                            ImGui.Text("This is based on your average time. \nSo get a good couple of runs to get a good feel for the timing".Loc());
                            ImGui.EndTooltip();
                        }

                        bool ShowScorePerMinute = C.ShowSPM;
                        foreach (var kind in MissionScores)
                        {
                            var tier = kind.Key;
                            var info = kind.Value;

                            var averageTime = tier switch
                            {
                                "Critical" => config.AverageTime,
                                "Bronze" => config.AverageBronzeTime,
                                "Silver" => config.AverageSilverTime,
                                "Gold" => config.AverageGoldTime,
                                _ => 0
                            };

                            var totalComplete = tier switch
                            {
                                "Critical" => config.CriticalCompletions,
                                "Gold" => config.GoldCompletions,
                                "Silver" => config.SilverCompletions,
                                "Bronze" => config.BronzeCompletion,
                                _ => 0
                            };

                            info.Score = MissionStatsCalculator.CalculateCurrencyPerMinute(averageTime, baseScore, info.Multipler);
                            info.Cosmocredits = MissionStatsCalculator.CalculateCurrencyPerMinute(averageTime, comsoCredit, info.Multipler);
                            info.Planetcredits = MissionStatsCalculator.CalculateCurrencyPerMinute(averageTime, planetCredit, info.Multipler);
                            info.TotalCompleted = totalComplete;

                            if (!C.ShowSPM)
                            {
                                info.Score *= 60;
                                info.Cosmocredits *= 60;
                                info.Planetcredits *= 60;
                            }
                        }

                        if (mission.Attributes.HasFlag(MissionAttributes.Critical))
                        {
                            var criticalScore = MissionStatsCalculator.CalculateCurrencyPerMinute(config.AverageTime, baseScore, 1.0);
                            if (!C.ShowSPM)
                                criticalScore *= 60;

                            string showingX = ShowScorePerMinute ? "Per Minute".Loc() : "Per Hour".Loc();
                            if (ImGui.Checkbox("Showing score ?? currently".Loc(showingX) + "###ICEShowSPMCritical", ref ShowScorePerMinute))
                            {
                                C.ShowSPM = ShowScorePerMinute;
                                C.Save();
                            }
                            if (ImGui.BeginTable("Critical Scoring Info", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders))
                            {
                                ImGui.TableSetupColumn("Turnin".Loc());
                                ImGui.TableSetupColumn("Score".Loc());
                                ImGui.TableSetupColumn("Cosmo Credits".Loc());
                                ImGui.TableSetupColumn("Planet Credits".Loc());

                                ImGui.TableHeadersRow();

                                var entry = MissionScores["Critical"];

                                ImGui.TableNextRow();
                                ImGui.TableSetColumnIndex(0);
                                ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), "Critical".Loc());

                                ImGui.TableNextColumn();
                                ImGui.Text($"{entry.Score:N2}");

                                ImGui.TableNextColumn();
                                ImGui.Text($"{entry.Cosmocredits:N2}");

                                ImGui.TableNextColumn();
                                ImGui.Text($"{entry.Planetcredits:N2}");

                                ImGui.EndTable();
                            }
                        }
                        else
                        {
                            List<(string type, Vector4 color)> turninTypes = new()
                            {
                                new() { type = "Bronze", color = new Vector4(0.8f, 0.5f, 0.3f, 1.0f)},
                                new() { type = "Silver", color = new Vector4(0.7f, 0.7f, 0.7f, 1.0f)},
                                new() { type = "Gold", color = new Vector4(1.0f, 0.84f, 0.0f, 1.0f)}
                            };
                            string showingX = ShowScorePerMinute ? "Per Minute".Loc() : "Per Hour".Loc();
                            if (ImGui.Checkbox("Showing Score ?? Currently".Loc(showingX) + "###ICEShowSPMNormal", ref ShowScorePerMinute))
                            {
                                C.ShowSPM = ShowScorePerMinute;
                                C.Save();
                            }
                            if (ImGui.BeginTable("Critical Scoring Info", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders))
                            {
                                ImGui.TableSetupColumn("Turnin".Loc());
                                ImGui.TableSetupColumn("Score".Loc());
                                ImGui.TableSetupColumn("Cosmo Credits".Loc());
                                ImGui.TableSetupColumn("Planet Credits".Loc());

                                ImGui.TableHeadersRow();

                                foreach (var type in turninTypes)
                                {
                                    var entry = MissionScores[type.type];
                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.TextColored(type.color, "?? [??]".Loc(type.type.Loc(), entry.TotalCompleted));

                                    ImGui.TableNextColumn();
                                    ImGui.Text($"{entry.Score:N2}");

                                    ImGui.TableNextColumn();
                                    ImGui.Text($"{entry.Cosmocredits:N2}");

                                    ImGui.TableNextColumn();
                                    ImGui.Text($"{entry.Planetcredits:N2}");
                                }

                                ImGui.TableNextRow();
                                ImGui.TableSetColumnIndex(0);
                                ImGui.Text("Average".Loc());
                                ImGui.SameLine();
                                ImGui.TextDisabled("?");
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.SetTooltip(("This is judged based off your current completion rate of bronze/silver/gold.\n" +
                                                     "It calculates the average score you get across all, and assuming you get that you were to consistently get that average across the hour, \n" +
                                                     "then it will tell you what it would be for that one mission. \n" +
                                                     "This is just really nerdy way of getting a more accurate average based off your completion rate").Loc());
                                }

                                ImGui.TableNextColumn();
                                var score = MissionStatsCalculator.CalculateActualScorePerMinute(config.TurninRecords, baseScore);
                                ImGui.Text($"{score:N2}");

                                ImGui.TableNextColumn();
                                var credits = MissionStatsCalculator.CalculateActualScorePerMinute(config.TurninRecords, comsoCredit);
                                ImGui.Text($"{credits:N2}");

                                ImGui.TableNextColumn();
                                var planet = MissionStatsCalculator.CalculateActualScorePerMinute(config.TurninRecords, planetCredit);
                                ImGui.Text($"{planet:N2}");

                                // 「有效」＝把放棄掉的時間也算進分母。上面那列「平均」只除交件耗時，
                                // 兩列並排就能一眼看出放棄成本吃掉多少效率。
                                // ⚠️ 與「平均」那列一樣固定是每分鐘（不隨 ShowSPM 切換），兩列才可直接比較。
                                ImGui.TableNextRow();
                                ImGui.TableSetColumnIndex(0);
                                ImGui.Text("Effective".Loc());
                                ImGui.SameLine();
                                ImGui.TextDisabled("?");
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.SetTooltip(("Same as Average, but the time spent on abandoned runs is added to the denominator.\n" +
                                                     "If you abandon when gold becomes unreachable, that time is a real cost of your farming loop, " +
                                                     "and leaving it out systematically overrates missions you gold inconsistently.\n" +
                                                     "Time lost to abandons so far: ??\n" +
                                                     "This is what the 'Effective Score/Min' sort order ranks by.").Loc(
                                        FormatTotalTime(config.AbandonedTimeSeconds)));
                                }

                                ImGui.TableNextColumn();
                                var effScore = MissionStatsCalculator.CalculateEffectiveScorePerMinute(config.TurninRecords, baseScore, config.AbandonedTimeSeconds, config.TotalCompletions);
                                ImGui.Text($"{effScore:N2}");

                                ImGui.TableNextColumn();
                                var effCredits = MissionStatsCalculator.CalculateEffectiveScorePerMinute(config.TurninRecords, comsoCredit, config.AbandonedTimeSeconds, config.TotalCompletions);
                                ImGui.Text($"{effCredits:N2}");

                                ImGui.TableNextColumn();
                                var effPlanet = MissionStatsCalculator.CalculateEffectiveScorePerMinute(config.TurninRecords, planetCredit, config.AbandonedTimeSeconds, config.TotalCompletions);
                                ImGui.Text($"{effPlanet:N2}");

                                ImGui.EndTable();
                            }
                            /*
                            var ActualSPM = MissionStatsCalculator.CalculateActualScorePerMinute(config.TurninRecords, baseScore);
                            var bronzeScore = MissionStatsCalculator.CalculateCurrencyPerMinute(config.AverageBronzeTime, baseScore, 1.0);
                            var silverScore = MissionStatsCalculator.CalculateCurrencyPerMinute(config.AverageSilverTime, baseScore, 4.0);
                            var goldScore = MissionStatsCalculator.CalculateCurrencyPerMinute(config.AverageGoldTime, baseScore, 5.0);

                            // Convert to per-hour if needed
                            if (!C.ShowSPM)
                            {
                                ActualSPM *= 60;
                                bronzeScore *= 60;
                                silverScore *= 60;
                                goldScore *= 60;
                            }
                            string timeUnit = C.ShowSPM ? "pts/minute" : "pts/hr";
                            string actualTimeUnit = C.ShowSPM ? "Actual pts/minute" : "Actual pts/hr";

                            ImGui.Text($"Actual {timeUnit}: {ActualSPM:F2}");
                            ImGui.SameLine();
                            ImGui.TextDisabled("?");
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.BeginTooltip();
                                ImGui.Text("This is judged based off your current completion rate of bronze/silver/gold");
                                ImGui.Text("It calculates the average score you get across all, and assuming you get that you were to consistently get that average across the hour, then it will tell you what it would be for that one mission");
                                ImGui.Text("This is just really nerdy way of getting a more accurate average based off your completion rate");
                                ImGui.EndTooltip();
                            }
                            if (missionInfo.Attributes.HasFlag(MissionAttributes.ProvisionalSequential))
                            {
                                string averageSequenceScore = $"Average pts/minute [Sequence]: {MissionStatsCalculator.CalculateAverageSequenceScorePerMinute(id, 5):N2}";
                                ImGui.Text(averageSequenceScore);
                                ImGui.SameLine();
                                ImGui.TextDisabled("?");
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.BeginTooltip();
                                    ImGui.Text("This is ASSUMING: You're getting gold across all prior missions (Because you have to do this one)");
                                    ImGui.Text("This is really only here for people who are debating on doing sequential missions, \nso they can get the average and compare against just normal mission spamming");
                                    ImGui.Text("For example, I found out it was ~30 points per minute worse than if I were to just spam the single mission for one series \n(not using food/pots, but also saving on resources)");
                                    ImGui.EndTooltip();
                                }
                            }

                            ImGui.TextColored(new Vector4(0.8f, 0.5f, 0.3f, 1.0f), $"Bronze: {bronzeScore:F0} {timeUnit} [{config.BronzeCompletion}/{config.TotalCompletions}]");
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), $"Silver: {silverScore:F0} {timeUnit} [{config.SilverCompletions}/{config.TotalCompletions}]");
                            ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), $"Gold: {goldScore:F0} {timeUnit} [{config.GoldCompletions}/{config.TotalCompletions}]");
#if DEBUG
                            var creditPerMinute = MissionStatsCalculator.CalculateCurrencyPerMinute(config.AverageGoldTime, mission.CosmoCredit, 5.0);
                            ImGui.Text($"Credit/minute: {creditPerMinute:N2}");
#endif
                            */
                        }
                    }


                    if (config.TurninRecords.Count > 0 && ImGui.CollapsingHeader("View All Completed Times".Loc() + "###ICEViewAllCompletedTimes"))
                    {
                        for (int i = 0; i < config.TurninRecords.Count; i++)
                        {
                            var record = config.TurninRecords[i];

                            ImGui.Text($"[{i+1}] \u2192 {TimeSpan.FromSeconds(record.Time):mm\\:ss\\.ff}");
                            ImGui.SameLine();
                            DrawColoredStar(record.State);

                        }
                    }
                }

                ImGui.PopID();
            }
            else
            {
                string joke = JokeList[jokeId];
                ImGui.TextWrapped(joke);
            }
        }
    }
}
