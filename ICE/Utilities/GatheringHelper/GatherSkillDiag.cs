using ICE.Config;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Text;

namespace ICE.Utilities.GatheringHelper;

/// <summary>
/// 一般採集點（非收藏品／精選）每進一次記一行 Information：任務分類、採用的採集配置、進點時的 GP 與機率、
/// 實際放了哪些技能、配置裡勾了哪些。使用者回報「放了沒用的技能」時直接從 log 判斷是分類、配置還是條件的問題。
/// </summary>
/// <remarks>
/// 只在框架執行緒（TaskManager 任務）呼叫。一個採集點一行就是節流，不另用 EzThrottler（全插件共用 key）。
/// <see cref="Begin"/> 每幀都會被叫，非新採集點時必須在配置任何字串前就返回。
/// 換點以「看過缺耐久後又回滿」判定；石工之理／農夫之智把耐久補回滿時會多切一行，無害。
/// </remarks>
internal static class GatherSkillDiag
{
    private const int MaxCasts = 24;

    private static bool active;
    private static bool sawMissingDur;
    private static bool midNode;
    private static uint missionId;
    private static int profileId;
    private static string profileName = string.Empty;
    private static string enabledSkills = string.Empty;
    private static GatherProfileKind kind;
    private static string source = string.Empty;
    private static int startGp;
    private static int gatherChance;
    private static int boonChance;
    private static int droppedCasts;
    private static string slotSource = string.Empty;
    private static int? remainingNeeded;
    private static int bountifulMinItem;
    private static readonly List<string> casts = new();

    internal static void Begin(string source, uint missionId, MissionAttributes attributes, int profileId, GatherProfile profile,
                               int gp, int gatherChance, int boonChance, bool missingDur, string slotSource, int? remainingNeeded = null)
    {
        if (active && GatherSkillDiag.missionId == missionId && !(sawMissingDur && !missingDur))
        {
            sawMissingDur |= missingDur;
            return;
        }

        Flush();
        active = true;
        sawMissingDur = missingDur;
        midNode = missingDur;
        GatherSkillDiag.source = source;
        GatherSkillDiag.missionId = missionId;
        GatherSkillDiag.profileId = profileId;
        profileName = profile.Name;
        kind = GatherProfileKinds.Classify(attributes);
        startGp = gp;
        GatherSkillDiag.gatherChance = gatherChance;
        GatherSkillDiag.boonChance = boonChance;
        droppedCasts = 0;
        GatherSkillDiag.slotSource = slotSource;
        GatherSkillDiag.remainingNeeded = remainingNeeded;
        bountifulMinItem = profile.GatherBuffs.BountifulMinItem;
        casts.Clear();

        var sb = new StringBuilder();
        foreach (var (key, buff) in profile.GatherBuffs.Buffs)
        {
            if (!buff.Enabled)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(key).Append('@').Append(buff.MinGp);
        }
        enabledSkills = sb.Length > 0 ? sb.ToString() : "無";
    }

    internal static void Record(string key, int gpBefore, bool accepted)
    {
        if (!active)
            return;
        if (casts.Count >= MaxCasts)
        {
            droppedCasts++;
            return;
        }
        casts.Add(accepted ? $"{key}(GP {gpBefore})" : $"{key}(GP {gpBefore}，遊戲拒絕)");
    }

    internal static void Flush()
    {
        if (!active)
            return;
        active = false;

        var used = casts.Count == 0 ? "無" : string.Join("、", casts);
        if (droppedCasts > 0)
            used += $"（另有 {droppedCasts} 次未列出）";
        IceLogging.Info(
            $"任務 {missionId}（{GatherProfileKinds.LogName(kind)}）用採集配置 #{profileId}「{profileName}」"
            + $"{(midNode ? "（中途接手）" : string.Empty)}：進點 GP {startGp}；機率讀自{slotSource}：採集率 {gatherChance}%、"
            + (boonChance > 0 ? $"額外採集獎勵率 {boonChance}%；" : "額外採集獎勵率讀不到（饋贈一律不放）；")
            + (remainingNeeded is int left ? $"素材還缺 {left}（少於最少獲得數 {bountifulMinItem} 就不放高產II／豐收II）；" : string.Empty)
            + $"放了：{used}；配置有勾：{enabledSkills}",
            $"[採集技能診斷|{source}]");
        casts.Clear();
    }
}
