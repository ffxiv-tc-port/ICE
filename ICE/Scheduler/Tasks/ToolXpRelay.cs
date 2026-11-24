using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace ICE.Scheduler.Tasks;

/// <summary>
/// 宇宙工具經驗模式的接力：本職業工具這一階滿了，照職業優先度換到下一個這一階還沒滿、有可接任務的職業繼續
/// （<c>C.XPRelicAutoSwitchJob</c>，預設關）。
/// </summary>
/// <remarks>
/// 契約：只在 tool_xp_full 停止點接手，找不到任務（no_mission）不接手；停止原因代碼一律沿用既有的。
/// 換套裝只走 <see cref="GearsetHandler.TaskClassChange"/>，而且只換到「主手在身上」的套裝（不會碰主手確認框）；
/// 最多按 <see cref="MaxAttempts"/> 次，換不過去就停下。只在框架執行緒呼叫。
/// </remarks>
internal static class ToolXpRelay
{
    private static readonly HashSet<uint> tried = new();
    private static long switchDeadline;
    private static long nextAttemptAt;
    private static int attempts;

    private const int SwitchTimeoutMs = 10000;
    private const int AttemptGapMs = 2000;
    private const int MaxAttempts = 3;
    private const string Tag = "[宇宙工具接力]";

    /// <summary>每次開始執行時呼叫：清掉這一輪換過的職業。</summary>
    internal static void Reset()
    {
        tried.Clear();
        switchDeadline = 0;
        attempts = 0;
    }

    /// <summary>在 tool_xp_full 停止點呼叫。回 true＝已排好換職業，呼叫端不要停止。</summary>
    internal static bool TryTakeOver(uint currentMissionId)
    {
        if (!C.XPRelicGrind || !C.XPRelicAutoSwitchJob)
            return false;
        if (currentMissionId != 0)
        {
            IceLogging.Info($"任務 {currentMissionId} 還在進行中，不換職業，照原本設定停止。", Tag);
            return false;
        }
        if (!PlayerHelper.IsInCosmicZone())
            return false;

        var current = Player.JobId;
        var skipped = new List<(uint Job, RelaySkip Why)>();
        var useful = new Dictionary<uint, int>();
        var pick = ToolResearchRules.PickNext(C.JobPrio, current, tried, job =>
        {
            var research = ToolResearch.Read(job);
            var count = research.Completion == ResearchCompletion.InProgress ? ToolResearch.CountUsefulMissions(job, research) : 0;
            useful[job] = count;
            return new RelayCandidate(research.Completion, count, GearsetHandler.HasUsableGearset((Job)job));
        }, skipped);

        if (skipped.Count > 0)
        {
            IceLogging.Info("接力評估：" + string.Join("、", skipped.Select(s => $"{JobName(s.Job)}（{SkipText(s.Why)}）")), Tag);
        }

        if (pick is not uint next)
        {
            IceLogging.Info($"{JobName(current)} 的宇宙工具這一階已滿，其他職業都沒有可以接手的，照原本設定停止。", Tag);
            return false;
        }

        IceLogging.Info($"{JobName(current)} 的宇宙工具這一階已滿，換到 {JobName(next)}（有 {useful.GetValueOrDefault(next)} 個任務能補還沒滿的研究項目），繼續宇宙工具經驗模式。", Tag);
        IceLogging.ChatInfo("This job's cosmic tool is full for this stage. Switching to ?? to keep going.".Loc(JobName(next)), "[ICE]");

        attempts = 0;
        nextAttemptAt = 0;
        switchDeadline = Environment.TickCount64 + SwitchTimeoutMs;
        P.TaskManager.Tasks.Clear();
        P.TaskManager.Insert(() => SwitchStep(next), "Tool XP relay: switching job");
        SchedulerMain.State = IceState.Start;
        return true;
    }

    private static bool? SwitchStep(uint jobId)
    {
        if (Player.JobId == jobId)
        {
            Mission_Settings.StartJob = jobId;
            switchDeadline = 0;
            ToolResearch.Read(jobId);
            IceLogging.Info($"已經換到 {JobName(jobId)}，繼續找任務。", Tag);
            return true;
        }

        var now = Environment.TickCount64;
        if (now > switchDeadline)
        {
            switchDeadline = 0;
            IceLogging.Info($"按了 {attempts} 次換套裝，{SwitchTimeoutMs / 1000} 秒內還是沒換到 {JobName(jobId)}，停止（不再連按）。", Tag);
            IceLogging.ChatInfo("Could not switch to ?? for the next cosmic tool. ICE is stopping.".Loc(JobName(jobId)), "[ICE]");
            SchedulerMain.StopWithNotice("換到下一個職業的套裝沒有成功", StopReasons.ToolXpFull, needsHelp: true, playSound: true);
            SchedulerMain.DisablePlugin(StopReasons.ToolXpFull);
            return true;
        }

        if (attempts < MaxAttempts && now >= nextAttemptAt && !Player.IsBusy)
        {
            attempts++;
            nextAttemptAt = now + AttemptGapMs;
            GearsetHandler.TaskClassChange((Job)jobId);
        }

        return false;
    }

    /// <summary>接力開著、最後還是停下時：把「這一階滿了、可以去研究員那裡繳交」的職業寫進紀錄與聊天視窗。</summary>
    internal static void ReportTurninReady()
    {
        if (!C.XPRelicGrind || !C.XPRelicAutoSwitchJob)
            return;

        var ready = ToolResearch.ReadAll()
            .Where(r => r.Completion == ResearchCompletion.StageFull)
            .Select(r => JobName(r.JobId))
            .ToList();
        if (ready.Count == 0)
        {
            IceLogging.Info("沒有「這一階已滿、可以去繳交」的職業。", Tag);
            return;
        }

        var text = string.Join("、", ready);
        IceLogging.Info($"這一階已滿、可以去研究員那裡繳交的職業：{text}。", Tag);
        IceLogging.ChatInfo("Jobs ready to turn in their cosmic tool: ??".Loc(text), "[ICE]");
    }

    internal static string JobName(uint jobId)
        => Svc.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(jobId)?.Name.ToString() is { Length: > 0 } name ? name : jobId.ToString();

    private static string SkipText(RelaySkip why) => why switch
    {
        RelaySkip.AlreadyTried => "這一輪換過了",
        RelaySkip.Unknown => "讀不到研究進度",
        RelaySkip.StageFull => "這一階也滿了，可去繳交",
        RelaySkip.FinalFull => "最終階段已滿",
        RelaySkip.NoMission => "沒有能補研究的可接任務",
        RelaySkip.NoGearset => "沒有主手在身上的套裝",
        _ => why.ToString(),
    };
}
