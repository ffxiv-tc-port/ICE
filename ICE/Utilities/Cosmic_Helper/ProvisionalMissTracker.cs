using ICE.IPC;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 「只刷未金星」連續沒金星就排到最後：把任務結果記進 <see cref="ProvisionalMissStreak"/>、挑任務時給出降級判定。
/// </summary>
/// <remarks>契約：只在框架執行緒呼叫（任務結束的匯流點與挑任務都跑在 NeoTaskManager 上）。</remarks>
internal static class ProvisionalMissTracker
{
    private const string Handle = "[ICE: Provisional Grind]";

    private static bool Active => C.GrindProvisionals && C.ProvisionalOnlyUngolded && C.ProvisionalDemoteRepeatedMisses;

    internal static int Threshold => Math.Max(1, C.ProvisionalDemoteAfterMisses);

    private static bool IsProvisional(uint missionId)
        => CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var info)
           && (info.Attributes.HasFlag(MissionAttributes.ProvisionalWeather)
               || info.Attributes.HasFlag(MissionAttributes.ProvisionalTimed)
               || info.Attributes.HasFlag(MissionAttributes.ProvisionalSequential));

    /// <summary>任務結束的結果。Critical／None（遊戲自己取消之類）不算拿到也不算失手。</summary>
    internal static void NoteTurnin(uint missionId, TurninState state)
    {
        if (state == TurninState.Gold)
        {
            if (ProvisionalMissStreak.RecordGold(missionId) && Active)
                IceLogging.Info($"任務 {MissionChain.DescribeMission(missionId)} 拿到金星，連續未金星計數清零、恢復原本順序。", Handle);
            return;
        }

        if (state is TurninState.Bronze or TurninState.Silver)
            NoteMiss(missionId, state == TurninState.Bronze ? "銅星交件" : "銀星交件");
    }

    internal static void NoteAbandon(uint missionId) => NoteMiss(missionId, "放棄");

    private static void NoteMiss(uint missionId, string how)
    {
        if (!Active || missionId == 0 || !IsProvisional(missionId))
            return;

        SyncArtisanVersion();
        var n = ProvisionalMissStreak.RecordMiss(missionId);
        if (n == Threshold)
            IceLogging.Info($"任務 {MissionChain.DescribeMission(missionId)} 本場連續 {n} 次沒拿到金星（這次：{how}），"
                            + "只刷未金星時改排到候選最後（不移除）；拿到金星、插件重載或 Artisan 換版就清零。", Handle);
        else
            IceLogging.Debug($"任務 {missionId} 本場連續 {n} 次沒拿到金星（這次：{how}），門檻 {Threshold}。", Handle);
    }

    /// <summary>Artisan 換版（含本場內更新重載）就清空全部計數。</summary>
    internal static void SyncArtisanVersion()
    {
        var cleared = ProvisionalMissStreak.SyncArtisanVersion(Utils.LoadedPluginVersion(ArtisanIPC.Name), out var previous);
        if (cleared > 0)
            IceLogging.Info($"Artisan 版本 {previous} → {Utils.LoadedPluginVersion(ArtisanIPC.Name)}，"
                            + $"清掉 {cleared} 個任務的連續未金星計數（製作能力可能變了，重新給機會）。", Handle);
    }

    /// <summary>挑任務用的降級判定；功能關著回 null（＝不降級，順序與關閉前逐筆相同）。</summary>
    internal static Func<uint, bool>? DemotionFilter()
    {
        if (!C.ProvisionalDemoteRepeatedMisses)
            return null;
        SyncArtisanVersion();
        if (ProvisionalMissStreak.Count == 0)
            return null;
        var threshold = Threshold;
        return id => ProvisionalMissStreak.Get(id) >= threshold;
    }

    private static string lastSignature = string.Empty;

    /// <summary>被降級的未金星候選有變動時寫一次 Information。</summary>
    internal static void ReportDemoted(IEnumerable<uint> demotedPending, int pending)
    {
        var demoted = demotedPending.Distinct().Order().ToList();
        var signature = string.Join(",", demoted);
        if (signature == lastSignature)
            return;
        lastSignature = signature;
        if (demoted.Count == 0)
            return;

        IceLogging.Info($"只刷未金星：{string.Join("、", demoted.Select(id => $"[{id}]×{ProvisionalMissStreak.Get(id)}"))} "
                        + $"本場連續沒拿到金星，排到候選最後（未移除；未金星共 {pending} 個）。"
                        + (demoted.Count >= pending ? "未金星的全部都在降級中，等於維持原順序。" : string.Empty), Handle);
    }
}
