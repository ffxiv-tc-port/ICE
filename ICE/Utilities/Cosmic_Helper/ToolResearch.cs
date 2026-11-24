using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>讀一個職業的研究進度時，讀不到的原因。</summary>
internal enum ResearchReadState : byte
{
    Ok,
    ModuleNotLoaded,
    FunctionMissing,
    JobLocked,
    NoTypes,
}

/// <summary>一個職業宇宙工具研究進度的受管理快照（不含任何原生指標）。</summary>
internal sealed class JobResearch
{
    public uint JobId;
    public ResearchReadState State;
    public byte Stage;
    public readonly List<ResearchTypeValue> Types = new(5);
    public ResearchCompletion Completion;
}

/// <summary>
/// 讀 11 個能工巧匠／大地使者職業的宇宙工具研究進度（<c>WKSResearchModule</c>）。
/// </summary>
/// <remarks>
/// 本檔假設與契約：
/// ① 只在框架執行緒呼叫；原生指標只在單次呼叫內使用，回傳的都是受管理快照。
/// ② 四個查詢函式以參數 toolClass 索引 11×5 的陣列、不看目前職業；唯一的職業閘門是「該職業等級不為 0」
///    （台服執行檔離線讀碼）。伺服器是否送齊非目前職業的資料無法離線證明 ⇒ 換成目前職業時比對一次並記 Information。
/// </remarks>
internal static class ToolResearch
{
    internal static readonly uint[] AllJobs = [8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];

    internal static unsafe JobResearch Read(uint jobId)
    {
        var r = new JobResearch { JobId = jobId };
        var manager = WKSManager.Instance();
        if (manager == null || manager->ResearchModule == null || !manager->ResearchModule->IsLoaded)
        {
            r.State = ResearchReadState.ModuleNotLoaded;
            return r;
        }

        if (GameFunctionGuard.ResearchAnalysisMissing())
        {
            r.State = ResearchReadState.FunctionMissing;
            return r;
        }

        // 遊戲函式對等級 0 的職業一律回 0，不是「0 點進度」。
        if (Player.GetUnsyncedLevel((Job)jobId) <= 0)
        {
            r.State = ResearchReadState.JobLocked;
            return r;
        }

        var research = manager->ResearchModule;
        var toolClassId = (byte)(jobId - 7);
        r.Stage = research->CurrentStages[toolClassId - 1];
        for (byte type = 1; type <= 5; type++)
        {
            if (!research->IsTypeAvailable(toolClassId, type))
                continue;
            r.Types.Add(new ResearchTypeValue(type,
                research->GetCurrentAnalysis(toolClassId, type),
                research->GetNeededAnalysis(toolClassId, type),
                research->GetMaxAnalysis(toolClassId, type)));
        }

        if (r.Types.Count == 0)
        {
            r.State = ResearchReadState.NoTypes;
            return r;
        }

        r.Completion = ToolResearchRules.Classify(r.Stage, CosmicHelper.MaxRelicLevel, r.Types);
        NoteCrossJobReading(r);
        return r;
    }

    internal static List<JobResearch> ReadAll()
    {
        var list = new List<JobResearch>(AllJobs.Length);
        foreach (var job in AllJobs)
            list.Add(Read(job));
        return list;
    }

    /// <summary>
    /// 這個職業在目前區域「接了會讓還沒達標的研究項目增加」的一般任務有幾個；挑選條件比照宇宙工具經驗模式的候選池
    /// （勾選、手動模式、ICE 跑得動、等級、掌握）。只算一般任務：緊急／臨時任務不是隨時都有。
    /// </summary>
    internal static int CountUsefulMissions(uint jobId, JobResearch research)
    {
        if (research.State != ResearchReadState.Ok)
            return 0;

        var incomplete = ToolResearchRules.IncompleteTypes(research.Stage, CosmicHelper.MaxRelicLevel, research.Types);
        if (incomplete.Count == 0)
            return 0;

        var level = Player.GetUnsyncedLevel((Job)jobId);
        PlayerHelper.UpdateHasManip();
        var count = 0;
        foreach (var (id, info) in CosmicHelper.SheetMissionDict)
        {
            if (info.TerritoryId != Player.Territory || !info.Jobs.Contains(jobId))
                continue;
            if ((info.Attributes & NonStandard) != 0)
                continue;
            if (!C.MissionConfig.TryGetValue(id, out var config))
                continue;
            if (C.XPRelicOnlyEnabled && !config.Enabled)
                continue;
            if (C.XPRelicIgnoreManual && config.ManualMode)
                continue;
            if (MissionSupport.IsUnsupported(id))
                continue;
            if (level < MinLevelForRank(info.Rank))
                continue;
            if (info.Attributes.HasFlag(MissionAttributes.ExpertCraft))
            {
                var craftJob = info.Jobs.FirstOrDefault(j => CosmicHelper.CrafterJobList.Contains(j));
                if (!PlayerHelper.ManipClassInfo.TryGetValue(craftJob, out var manip) || !manip.HasUnlocked)
                    continue;
            }
            if (!ToolResearchRules.RewardsAny(info.RelicXpInfo, incomplete))
                continue;
            count++;
        }

        return count;
    }

    private const MissionAttributes NonStandard = MissionAttributes.Critical | MissionAttributes.ProvisionalTimed
        | MissionAttributes.ProvisionalWeather | MissionAttributes.ProvisionalSequential;

    /// <summary>與 <c>Task_FindMission.FindBestRelicMission</c> 的等級門檻同表。</summary>
    private static int MinLevelForRank(uint rank) => rank switch
    {
        2 => 50,
        3 => 90,
        4 or 5 or 6 => 100,
        _ => 10,
    };

    private static readonly Dictionary<uint, (long At, byte Stage, string Values)> readAsOtherJob = new();
    private static readonly HashSet<uint> crossChecked = new();

    /// <summary>非目前職業讀到的值，在 30 秒內換成目前職業時比對一次（每個職業每次載入只記一次）。</summary>
    private static void NoteCrossJobReading(JobResearch r)
    {
        var now = Environment.TickCount64;
        var values = string.Join("、", r.Types.Select(t => $"{t.Current}/{t.Needed}/{t.Max}"));
        if (r.JobId != Player.JobId)
        {
            readAsOtherJob[r.JobId] = (now, r.Stage, values);
            return;
        }

        if (crossChecked.Contains(r.JobId) || !readAsOtherJob.Remove(r.JobId, out var before) || now - before.At > 30000)
            return;

        crossChecked.Add(r.JobId);
        var same = before.Stage == r.Stage && before.Values == values;
        IceLogging.Info(
            $"研究進度表：職業 {r.JobId} 換成目前職業前後讀到的研究進度{(same ? "一致" : "不一致")}。"
            + $"換之前（非目前職業）：第 {before.Stage} 階 {before.Values}；換之後：第 {r.Stage} 階 {values}。"
            + (same ? "" : "非目前職業的數值可能不可信。"),
            "[宇宙工具研究進度]");
    }
}
