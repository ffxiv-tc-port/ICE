using System;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>一個研究項目（宇宙資料 I～V）讀到的值：目前、這一階需要量、上限。</summary>
internal readonly record struct ResearchTypeValue(byte Type, ushort Current, ushort Needed, ushort Max);

/// <summary>一個職業的宇宙工具在目前階段的完成度。</summary>
internal enum ResearchCompletion : byte
{
    /// <summary>讀不到或資料不足以判斷；顯示成「?」，接力不採用。</summary>
    Unknown,
    /// <summary>這一階還有項目沒達到需要量。</summary>
    InProgress,
    /// <summary>這一階各項都達到需要量、還不是最終階段：可以去研究員那裡繳交升級。</summary>
    StageFull,
    /// <summary>最終階段各項都到上限。</summary>
    FinalFull,
}

/// <summary>接力時某個職業沒被選上的原因。</summary>
internal enum RelaySkip : byte
{
    None,
    AlreadyTried,
    Unknown,
    StageFull,
    FinalFull,
    NoMission,
    NoGearset,
}

/// <summary>接力評估一個職業所需的全部事實（由呼叫端讀遊戲狀態填好）。</summary>
internal readonly record struct RelayCandidate(ResearchCompletion Completion, int UsefulMissions, bool HasGearset);

/// <summary>
/// 宇宙工具研究進度的判準與「工具經驗滿了換下一個職業」的挑選規則。
/// </summary>
/// <remarks>純函式、不碰遊戲狀態，任何執行緒都可呼叫。</remarks>
internal static class ToolResearchRules
{
    /// <summary>
    /// 與 <c>Task_CheckState.RelicInfo</c> 同判準：非最終階段比需要量、最終階段比上限。
    /// 階段 0 或沒有任何可用項目＝<see cref="ResearchCompletion.Unknown"/>（RelicInfo 會把空表當成滿，接力不跟）。
    /// </summary>
    internal static ResearchCompletion Classify(int stage, int maxStage, IReadOnlyList<ResearchTypeValue> types)
    {
        if (stage <= 0 || types.Count == 0)
            return ResearchCompletion.Unknown;

        var atFinal = stage >= maxStage;
        foreach (var t in types)
        {
            if (t.Current < (atFinal ? t.Max : t.Needed))
                return ResearchCompletion.InProgress;
        }

        return atFinal ? ResearchCompletion.FinalFull : ResearchCompletion.StageFull;
    }

    /// <summary>還沒達標的研究項目編號（1～5），判準同 <see cref="Classify"/>。</summary>
    internal static HashSet<int> IncompleteTypes(int stage, int maxStage, IReadOnlyList<ResearchTypeValue> types)
    {
        var result = new HashSet<int>();
        var atFinal = stage >= maxStage;
        foreach (var t in types)
        {
            if (t.Current < (atFinal ? t.Max : t.Needed))
                result.Add(t.Type);
        }

        return result;
    }

    /// <summary>任務的宇宙工具經驗獎勵裡，有沒有一項落在還沒達標的研究項目上。</summary>
    internal static bool RewardsAny(IReadOnlyDictionary<int, int> relicXp, HashSet<int> incomplete)
    {
        foreach (var kv in relicXp)
        {
            if (kv.Value > 0 && incomplete.Contains(kv.Key))
                return true;
        }

        return false;
    }

    internal static RelaySkip Judge(RelayCandidate c) => c.Completion switch
    {
        ResearchCompletion.Unknown => RelaySkip.Unknown,
        ResearchCompletion.StageFull => RelaySkip.StageFull,
        ResearchCompletion.FinalFull => RelaySkip.FinalFull,
        _ when c.UsefulMissions <= 0 => RelaySkip.NoMission,
        _ when !c.HasGearset => RelaySkip.NoGearset,
        _ => RelaySkip.None,
    };

    /// <summary>
    /// 照 <paramref name="jobPrio"/> 挑下一個要換過去的職業；沒有就回 null。
    /// </summary>
    /// <remarks>
    /// 契約：<paramref name="tried"/> 是這一輪已經換過去（或起始）的職業，選中的職業會被加進去 ⇒ 每個職業每輪最多換一次，
    /// 一輪最多換「職業數 − 1」次，必定收斂。沒選中的職業不加：它們的狀態（例如勾選、套裝）在下次評估時可能不同。
    /// <paramref name="evaluate"/> 只對沒試過、不是目前職業的職業呼叫。
    /// </remarks>
    internal static uint? PickNext(IReadOnlyList<uint> jobPrio, uint currentJob, HashSet<uint> tried,
                                   Func<uint, RelayCandidate> evaluate, List<(uint Job, RelaySkip Why)> skipped)
    {
        tried.Add(currentJob);
        foreach (var job in jobPrio)
        {
            if (job == currentJob)
                continue;
            if (tried.Contains(job))
            {
                skipped.Add((job, RelaySkip.AlreadyTried));
                continue;
            }

            var why = Judge(evaluate(job));
            if (why == RelaySkip.None)
            {
                tried.Add(job);
                return job;
            }

            skipped.Add((job, why));
        }

        return null;
    }
}
