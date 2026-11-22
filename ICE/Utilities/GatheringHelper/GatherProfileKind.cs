using ICE.Config;
using System.Collections.Generic;
using System.Text;

namespace ICE.Utilities.GatheringHelper;

/// <summary>採集配置檔對應的任務分類（看評分方式）。</summary>
/// <remarks>
/// 「自動建立採集配置檔」「套用至該類型任務」「新任務沿用同類配置」與採集診斷共用 <see cref="GatherProfileKinds.Classify"/>，
/// 不要在呼叫端各寫一份判斷。收藏品／精選任務走收藏品流程、不讀採集配置，所以歸 <see cref="None"/>。
/// </remarks>
internal enum GatherProfileKind
{
    None = 0,
    Limited,
    GatherX,
    Timed,
    Chained,
    Boon,
    ChainBoon,
    DualClass,
}

internal static class GatherProfileKinds
{
    /// <summary>與 <c>GatherSettings.MissionTypes</c> 下拉選單同索引。</summary>
    internal static readonly GatherProfileKind[] MissionTypeOrder =
    [
        GatherProfileKind.Limited,
        GatherProfileKind.GatherX,
        GatherProfileKind.Timed,
        GatherProfileKind.Chained,
        GatherProfileKind.Boon,
        GatherProfileKind.ChainBoon,
        GatherProfileKind.DualClass,
    ];

    internal static GatherProfileKind Classify(MissionAttributes a)
    {
        if (!a.HasFlag(MissionAttributes.Gather)
            || a.HasFlag(MissionAttributes.Collectables)
            || a.HasFlag(MissionAttributes.ReducedItems))
            return GatherProfileKind.None;

        // 雙職業任務沒有其他評分旗標，不先排除就會同時落進「採集指定數量」。
        if (a.HasFlag(MissionAttributes.Craft))
            return GatherProfileKind.DualClass;
        if (a.HasFlag(MissionAttributes.Limited))
            return GatherProfileKind.Limited;
        if (a.HasFlag(MissionAttributes.ScoreTimeRemaining))
            return GatherProfileKind.Timed;

        bool chain = a.HasFlag(MissionAttributes.ScoreChains);
        bool boon = a.HasFlag(MissionAttributes.ScoreGatherersBoon);
        if (chain && boon)
            return GatherProfileKind.ChainBoon;
        if (chain)
            return GatherProfileKind.Chained;
        if (boon)
            return GatherProfileKind.Boon;
        return GatherProfileKind.GatherX;
    }

    internal static GatherProfileKind FromSetupType(string type) => type switch
    {
        "limited" => GatherProfileKind.Limited,
        "timed" => GatherProfileKind.Timed,
        "chained" => GatherProfileKind.Chained,
        "boon" => GatherProfileKind.Boon,
        "boonChain" => GatherProfileKind.ChainBoon,
        "gatherX" => GatherProfileKind.GatherX,
        "dualCraft" => GatherProfileKind.DualClass,
        _ => GatherProfileKind.None,
    };

    /// <summary>log 用的繁中名稱（和設定頁「任務類型」下拉選單同詞）。</summary>
    internal static string LogName(GatherProfileKind kind) => kind switch
    {
        GatherProfileKind.Limited => "限量採集點",
        GatherProfileKind.GatherX => "採集指定數量",
        GatherProfileKind.Timed => "限時計分",
        GatherProfileKind.Chained => "連擊計分",
        GatherProfileKind.Boon => "恩惠計分",
        GatherProfileKind.ChainBoon => "連擊＋恩惠計分",
        GatherProfileKind.DualClass => "雙職業",
        _ => "收藏品／精選（不讀採集配置）",
    };

    /// <summary>
    /// 對 <paramref name="newMissionIds"/> 裡的每個任務，沿用「同分類的既有任務」最常用的非預設採集配置。
    /// 同分類沒有任何既有任務用非預設配置時維持 0（預設）。回傳實際改動的筆數。
    /// </summary>
    /// <remarks>只給剛建立的任務設定用；既有任務的配置是使用者的選擇，這裡不碰。</remarks>
    internal static int InheritProfilesForNewMissions(ICollection<uint> newMissionIds, out string summary)
    {
        summary = string.Empty;
        if (newMissionIds.Count == 0)
            return 0;

        var votes = new Dictionary<GatherProfileKind, Dictionary<int, int>>();
        foreach (var (id, settings) in C.MissionConfig)
        {
            if (newMissionIds.Contains(id) || settings.GProfileId == 0 || !C.GatherProfiles.ContainsKey(settings.GProfileId))
                continue;
            if (!CosmicHelper.SheetMissionDict.TryGetValue(id, out var info))
                continue;
            var kind = Classify(info.Attributes);
            if (kind == GatherProfileKind.None)
                continue;
            if (!votes.TryGetValue(kind, out var perProfile))
                votes[kind] = perProfile = new();
            perProfile[settings.GProfileId] = perProfile.GetValueOrDefault(settings.GProfileId) + 1;
        }

        var picked = new Dictionary<GatherProfileKind, int>();
        foreach (var (kind, perProfile) in votes)
        {
            int best = 0, bestCount = 0;
            foreach (var (pid, count) in perProfile)
            {
                // 票數相同取較小的 id，讓結果與字典列舉順序無關。
                if (count > bestCount || (count == bestCount && pid < best))
                {
                    best = pid;
                    bestCount = count;
                }
            }
            picked[kind] = best;
        }

        var changedPerKind = new Dictionary<GatherProfileKind, int>();
        int changed = 0;
        foreach (var id in newMissionIds)
        {
            if (!C.MissionConfig.TryGetValue(id, out var settings) || settings.GProfileId != 0)
                continue;
            if (!CosmicHelper.SheetMissionDict.TryGetValue(id, out var info))
                continue;
            var kind = Classify(info.Attributes);
            if (!picked.TryGetValue(kind, out var pid))
                continue;
            settings.GProfileId = pid;
            changedPerKind[kind] = changedPerKind.GetValueOrDefault(kind) + 1;
            changed++;
        }

        if (changed > 0)
        {
            var sb = new StringBuilder();
            foreach (var (kind, count) in changedPerKind)
            {
                if (sb.Length > 0)
                    sb.Append('、');
                var pid = picked[kind];
                sb.Append($"{LogName(kind)} {count} 筆→#{pid}「{C.GatherProfiles[pid].Name}」");
            }
            summary = sb.ToString();
        }
        return changed;
    }
}
