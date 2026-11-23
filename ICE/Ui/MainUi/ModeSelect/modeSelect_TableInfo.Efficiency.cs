using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ICE.Config;
using System.Collections.Generic;
using static MissionTimer;

namespace ICE.Ui.MainUi.ModeSelect
{
    // 任務表的四個效益欄。數字一律取自 MissionStatsCalculator.CalculateEfficiency，排序鍵（NumberKey）與詳情頁也是，
    // 三處不會各算各的。只在繪製執行緒用。
    internal partial class modeSelect_TableInfo
    {
        private readonly record struct EfficiencyInputs(int Total, int Failed, int Gold, int Records, double Abandoned, double Average, uint Credit);

        private sealed class EfficiencyText
        {
            public EfficiencyInputs Inputs;
            public bool Enough;
            public string[] Cells = new string[4];
            public string Tooltip = string.Empty;
        }

        // 輸入沒變就沿用上一幀的字串：交件、放棄、重設都會改到 Total／Failed，所以這組鍵足以判斷。
        private static readonly Dictionary<uint, EfficiencyText> EfficiencyCache = new();

        private static void EfficiencyHeader(ref int columnIndex, string label, int sortOption, string description)
        {
            ImGui.TableSetColumnIndex(columnIndex);
            var active = C.TableSortOption == sortOption;
            ImGui.TableHeader(active ? label.Loc() + " ▼" : label.Loc());
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(description.Loc());
                ImGui.TextUnformatted("Fewer than ?? attempts: shown faded as 'few samples' and sorted last.".Loc(MissionConfigs.MinGoldRateSampleSize));
                ImGui.TextUnformatted(active
                    ? "The table is sorted by this column (Settings > Table > Sort By).".Loc()
                    : "Click to sort the table by this column (same as Settings > Table > Sort By).".Loc());
                ImGui.EndTooltip();
            }
            if (!active && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                C.TableSortOption = sortOption;
                C.Save();
            }
            columnIndex++;
        }

        private static void DrawEfficiencyCells(uint id, MissionSettings cfg, uint cosmoCredit)
        {
            var text = EfficiencyTextFor(id, cfg, cosmoCredit);
            for (var i = 0; i < 4; i++)
            {
                ImGui.TableNextColumn();
                var faded = !text.Enough;
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, faded))
                    Table_FullCenterText(text.Cells[i]);
                if (text.Tooltip.Length > 0 && ImGui.IsItemHovered())
                    ImGui.SetTooltip(text.Tooltip);
            }
        }

        private static EfficiencyText EfficiencyTextFor(uint id, MissionSettings cfg, uint cosmoCredit)
        {
            var inputs = new EfficiencyInputs(cfg.TotalCompletions, cfg.FailedCounters, cfg.GoldCompletions,
                                              cfg.TurninRecords?.Count ?? 0, cfg.AbandonedTimeSeconds, cfg.AverageTime, cosmoCredit);
            if (EfficiencyCache.TryGetValue(id, out var cached) && cached.Inputs == inputs)
                return cached;

            cached ??= EfficiencyCache[id] = new EfficiencyText();
            cached.Inputs = inputs;

            var eff = MissionStatsCalculator.CalculateEfficiency(cfg, cosmoCredit);
            cached.Enough = eff.EnoughSamples;
            if (eff.Attempts == 0)
            {
                for (var i = 0; i < 4; i++)
                    cached.Cells[i] = "--";
                cached.Tooltip = "No attempts recorded yet.".Loc();
                return cached;
            }

            if (!eff.EnoughSamples)
            {
                for (var i = 0; i < 4; i++)
                    cached.Cells[i] = "few samples".Loc();
                cached.Tooltip = "Only ?? attempt(s) so far: gold rate ??%, average time ??, gold/hr ??, credits/hr ??. Not used for sorting until there are ??."
                    .Loc(eff.Attempts, eff.GoldRatePercent.ToString("F0"), TimeText(eff), RateText(eff, eff.GoldPerHour, "F1"),
                         RateText(eff, eff.CreditsPerHour, "N0"), MissionConfigs.MinGoldRateSampleSize);
                return cached;
            }

            cached.Cells[0] = eff.GoldRatePercent.ToString("F0") + "%";
            cached.Cells[1] = TimeText(eff);
            cached.Cells[2] = RateText(eff, eff.GoldPerHour, "F1");
            cached.Cells[3] = RateText(eff, eff.CreditsPerHour, "N0");
            cached.Tooltip = eff.HasRecords
                ? "?? attempts (?? gold). Gold/hr and credits/hr count the time lost to abandoned runs.".Loc(eff.Attempts, cfg.GoldCompletions)
                : "?? attempts, all abandoned: no turn-in time recorded, so the time columns are unknown.".Loc(eff.Attempts);
            return cached;
        }

        private static string TimeText(MissionStatsCalculator.Efficiency eff)
            => eff.HasRecords ? $"{(int)(eff.AverageSeconds / 60)}:{(int)(eff.AverageSeconds % 60):D2}" : "--";

        private static string RateText(MissionStatsCalculator.Efficiency eff, double value, string format)
            => eff.HasRecords ? value.ToString(format) : "--";
    }
}
