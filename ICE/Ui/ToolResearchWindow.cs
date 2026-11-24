using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace ICE.Ui;

/// <summary>
/// 全職業宇宙工具研究進度表：11 個職業的階段與各研究項目的目前值／需要量／上限，每列可「換到這職業」。
/// </summary>
/// <remarks>
/// 資料每 500 ms 重讀一次成受管理快照（<see cref="ToolResearch"/>），繪製不碰原生指標。
/// 「不知道」一律畫成灰色「?」並在 tooltip 說原因，不畫成 0。
/// </remarks>
internal sealed class ToolResearchWindow : Window
{
    private const string Unknown = "?";
    private const long RefreshMs = 500;

    private List<JobResearch> rows = new();
    private readonly Dictionary<uint, bool> usableGearset = new();
    private long nextRefreshAt;
    private uint refreshedForJob;

    public ToolResearchWindow() : base("Cosmic Tool Research Progress".Loc() + "###ICEToolResearchWindow")
    {
        P.windowSystem.AddWindow(this);
        AllowPinning = true;
        RespectCloseHotkey = false;
    }

    public override void PreDraw()
    {
        // Window 會再乘 GlobalScale，所以先除掉。
        SizeConstraints = new()
        {
            MinimumSize = UiScale.Px(520, 200) / Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale,
            MaximumSize = new Vector2(4000, 4000),
        };
        base.PreDraw();
    }

    public override void Draw()
    {
        var now = Environment.TickCount64;
        if (now >= nextRefreshAt || refreshedForJob != Player.JobId)
        {
            nextRefreshAt = now + RefreshMs;
            refreshedForJob = Player.JobId;
            rows = ToolResearch.ReadAll();
            foreach (var job in ToolResearch.AllJobs)
                usableGearset[job] = GearsetHandler.HasUsableGearset((Job)job);
        }

        if (!PlayerHelper.IsInCosmicZone())
            ImGui.TextWrapped("You have to be in a cosmic area for us to view this info. Blame square for not making it always accesable".Loc());

        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(("Each cell is current / needed for this stage (at the final stage: current / max). Hover a cell for the research name and max.\n" +
                              "Values come from the game's cosmic research data for every job, read in place without switching jobs.\n" +
                              "? means the value could not be read (outside a cosmic area, research data not loaded yet, or the job is not unlocked); hover it for the reason.\n" +
                              "When you switch to a job within 30 seconds of reading it here, the log records whether the values before and after match.").Loc());

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("###ICEToolResearchTable", 9, flags))
            return;

        try
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Job".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(110));
            ImGui.TableSetupColumn("Stage".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(40));
            foreach (var roman in Romans)
                ImGui.TableSetupColumn(roman, ImGuiTableColumnFlags.WidthFixed, UiScale.Px(80));
            ImGui.TableSetupColumn("Status".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(90));
            ImGui.TableSetupColumn("###ICEToolResearchSwitchColumn", ImGuiTableColumnFlags.WidthFixed, UiScale.Px(100));
            ImGui.TableHeadersRow();

            foreach (var row in rows)
                DrawRow(row);
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    private static readonly string[] Romans = ["I", "II", "III", "IV", "V"];

    private void DrawRow(JobResearch row)
    {
        var isCurrent = row.JobId == Player.JobId;
        ImGui.TableNextRow();
        if (isCurrent)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32(new Vector4(0.25f, 0.45f, 0.75f, 0.35f)));

        ImGui.TableNextColumn();
        if (CosmicHelper.JobIconDict.TryGetValue(row.JobId, out var icon))
        {
            ImGui.Image(icon.GetWrapOrEmpty().Handle, UiScale.Px(18, 18));
            ImGui.SameLine();
        }
        ImGui.TextUnformatted(ToolXpRelay.JobName(row.JobId));

        ImGui.TableNextColumn();
        if (row.State != ResearchReadState.Ok)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, Unknown);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(ReadStateText(row.State));
        }
        else
        {
            ImGui.TextUnformatted(row.Stage.ToString());
        }

        var atFinal = row.Stage >= CosmicHelper.MaxRelicLevel;
        for (byte type = 1; type <= 5; type++)
        {
            ImGui.TableNextColumn();
            if (row.State != ResearchReadState.Ok)
            {
                ImGui.TextColored(ImGuiColors.DalamudGrey, Unknown);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(ReadStateText(row.State));
                continue;
            }

            var found = row.Types.FindIndex(t => t.Type == type);
            if (found < 0)
            {
                ImGui.TextDisabled("-");
                continue;
            }

            var v = row.Types[found];
            var target = atFinal ? v.Max : v.Needed;
            var text = $"{v.Current:N0}/{target:N0}";
            if (v.Current >= target)
                ImGui.TextColored(ImGuiColors.HealerGreen, text);
            else
                ImGui.TextUnformatted(text);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("??\nCurrent: ??\nNeeded for this stage: ??\nMax: ??".Loc(TypeName(row.JobId, type), v.Current, v.Needed, v.Max));
        }

        ImGui.TableNextColumn();
        switch (row.Completion)
        {
            case ResearchCompletion.InProgress:
                ImGui.TextUnformatted("In progress".Loc());
                break;
            case ResearchCompletion.StageFull:
                ImGui.TextColored(ImGuiColors.HealerGreen, "Ready to turn in".Loc());
                break;
            case ResearchCompletion.FinalFull:
                ImGui.TextColored(ImGuiColors.HealerGreen, "Final stage maxed".Loc());
                break;
            default:
                ImGui.TextColored(ImGuiColors.DalamudGrey, Unknown);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(ReadStateText(row.State));
                break;
        }

        ImGui.TableNextColumn();
        DrawSwitchButton(row, isCurrent);
    }

    private void DrawSwitchButton(JobResearch row, bool isCurrent)
    {
        string? blocked = null;
        if (SchedulerMain.State != IceState.Idle)
            blocked = "ICE is running. Stop it first.".Loc();
        else if (isCurrent)
            blocked = "This is already your current job.".Loc();
        else if (row.State == ResearchReadState.JobLocked)
            blocked = "This job is not unlocked.".Loc();
        else if (!usableGearset.GetValueOrDefault(row.JobId))
            blocked = "No gearset for this job, or its main hand weapon is not in your inventory.".Loc();
        else if (Player.IsBusy)
            blocked = "Your character is busy right now.".Loc();

        var disabled = blocked != null;
        using (ImRaii.Disabled(disabled))
        {
            if (ImGui.Button("Switch to this job".Loc() + $"###ICEToolResearchSwitch{row.JobId}") && !disabled)
                GearsetHandler.TaskClassChange((Job)row.JobId);
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(blocked ?? "Equip this job's gearset. Only works while ICE is stopped.".Loc());
    }

    private static string ReadStateText(ResearchReadState state) => state switch
    {
        ResearchReadState.ModuleNotLoaded => "Research data is not loaded. It can only be read inside a cosmic exploration area.".Loc(),
        ResearchReadState.FunctionMissing => "The game function for reading research was not found after a game update.".Loc(),
        ResearchReadState.JobLocked => "This job is not unlocked.".Loc(),
        ResearchReadState.NoTypes => "The game reports no research items for this job's tool.".Loc(),
        _ => Unknown,
    };

    private readonly Dictionary<(uint, byte), string> typeNames = new();

    private string TypeName(uint jobId, byte type)
    {
        if (typeNames.TryGetValue((jobId, type), out var cached))
            return cached;

        var name = Romans[type - 1];
        if (Svc.Data.GetExcelSheet<WKSCosmoToolClass>().TryGetRow(jobId - 7, out var tool) && type - 1 < tool.Types.Count)
        {
            var sheetName = tool.Types[type - 1].Name.ValueNullable?.Name.ToString();
            if (!string.IsNullOrEmpty(sheetName))
                name = sheetName;
        }

        typeNames[(jobId, type)] = name;
        return name;
    }
}
