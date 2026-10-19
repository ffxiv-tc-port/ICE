using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 連續任務（<c>WKSMissionUnit.LockedBehind</c>）的鏈結查詢。
/// </summary>
/// <remarks>
/// ⚠️ 儘管目前是一對一，這裡仍然用 <c>List</c> 存後續任務：改版新增資料時
/// 「一個前置擋住兩個任務」不會靜默丟掉其中一個。
/// </remarks>
internal static class MissionChain
{
    private static Dictionary<uint, List<uint>>? _followUps;
    private static int _builtFromCount = -1;

    /// <summary>前置任務 ID → 它擋著的那些任務 ID（只含直接的下一層）。</summary>
    private static Dictionary<uint, List<uint>> FollowUps
    {
        get
        {
            // SheetMissionDict 只在 DictionaryCreation 時整份重建，用筆數當快取失效條件就夠了
            // （筆數沒變＝同一份資料）。不做事件訂閱是刻意的：多一個訂閱就多一個生命週期問題。
            if (_followUps == null || _builtFromCount != CosmicHelper.SheetMissionDict.Count)
                Build();

            return _followUps!;
        }
    }

    private static void Build()
    {
        var map = new Dictionary<uint, List<uint>>();

        foreach (var (missionId, info) in CosmicHelper.SheetMissionDict)
        {
            foreach (var previous in info.PreviousMissions)
            {
                // 0 ＝「沒有前置」。ICEDictornaryCreation 一律把 LockedBehind.RowId 放進來，
                // 所以絕大多數任務這裡就是 { 0 }。
                if (previous == 0)
                    continue;

                if (!map.TryGetValue(previous, out var list))
                    map[previous] = list = new List<uint>();

                if (!list.Contains(missionId))
                    list.Add(missionId);
            }
        }

        _followUps = map;
        _builtFromCount = CosmicHelper.SheetMissionDict.Count;
    }

    /// <summary>這個任務有沒有擋著別的任務（是不是某條連續任務鏈的前置）。</summary>
    internal static bool IsPrerequisite(uint missionId) => FollowUps.ContainsKey(missionId);

    /// <summary>
    /// 這個任務往後整條鏈上的所有任務（不含自己），由近到遠。
    /// </summary>
    internal static List<uint> GetFollowUpsRecursive(uint missionId)
    {
        var result = new List<uint>();
        var visited = new HashSet<uint> { missionId };
        var queue = new Queue<uint>();
        queue.Enqueue(missionId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!FollowUps.TryGetValue(current, out var next))
                continue;

            foreach (var child in next)
            {
                if (!visited.Add(child))
                    continue;

                result.Add(child);
                queue.Enqueue(child);
            }
        }

        return result;
    }

    /// <param name="missionId">正打算停用的任務。</param>
    /// <param name="reason">要寫進記錄檔的理由（回傳 <see langword="true"/> 時才有意義）。</param>
    /// <returns><see langword="true"/> ＝<b>不要停用</b>。</returns>
    /// <summary>
    /// 「取得金星後排除任務」要不要對<b>緊急任務</b>網開一面。
    /// ⚠️ 查不到任務資料時回 <c>false</c>（＝不例外，維持現行行為）。
    /// 這個方向是安全的：例外只會「多留一個任務在池子裡」，而不例外只是照舊。
    /// </summary>
    internal static bool ShouldKeepEnabledForEmergency(uint missionId, out string reason)
    {
        reason = string.Empty;

        if (!C.RemoveAfterGoldKeepCritical)
            return false;

        if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var entry))
            return false;

        if (!entry.Attributes.HasFlag(MissionAttributes.Critical))
            return false;

        reason = "它是緊急任務，而你開了「緊急任務不受金星排除影響」";
        return true;
    }

    internal static unsafe bool ShouldKeepEnabledForChain(uint missionId, out string reason)
    {
        reason = string.Empty;

        var followUps = GetFollowUpsRecursive(missionId);
        if (followUps.Count == 0)
            return false;

        // WKSManager 的 [StaticAddress] 槽位在宇宙探索內容以外是 null。
        // 這裡讀不到金星旗標就代表「不知道」——而不知道的預設行為是不停用。
        var manager = WKSManager.Instance();
        if (manager == null)
        {
            reason = $"目前讀不到宇宙探索的任務狀態，無法確認後續的 {followUps.Count} 個任務拿到金星了沒";
            return true;
        }

        var pending = new List<uint>();
        foreach (var followUp in followUps)
        {
            if (!manager->IsMissionGolded(followUp))
                pending.Add(followUp);
        }

        if (pending.Count == 0)
            return false;

        reason = "後續任務 " + string.Join("、", pending.Select(DescribeMission)) + " 尚未拿到金星";
        return true;
    }

    /// <summary>
    /// 修復「前置被停用、但鏈上還有想跑的任務」這個自相矛盾的設定狀態。
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>WKSManager</c> 讀不到時<b>整個修復直接跳過</b>。那時 <c>IsMissionGolded</c> 一律回 false，
    /// 會把每一個前置都看成「沒金星」而大量重新啟用 —— 這個方向跟
    /// <see cref="ShouldKeepEnabledForChain"/> 相反，不能沿用同一套「不知道就放行」。
    /// </remarks>
    internal static unsafe void RepairSequentialPrerequisites(string handle)
    {
        if (!C.RemoveAfterGold)
            return;

        var manager = WKSManager.Instance();
        if (manager == null)
            return;

        var repaired = new List<string>();

        foreach (var prerequisiteId in FollowUps.Keys)
        {
            if (!C.MissionConfig.TryGetValue(prerequisiteId, out var prerequisiteConfig))
                continue;

            if (prerequisiteConfig.Enabled)
                continue;

            if (!manager->IsMissionGolded(prerequisiteId))
                continue;

            uint? blocked = null;
            foreach (var followUp in GetFollowUpsRecursive(prerequisiteId))
            {
                if (!C.MissionConfig.TryGetValue(followUp, out var followUpConfig) || !followUpConfig.Enabled)
                    continue;

                if (manager->IsMissionGolded(followUp))
                    continue;

                blocked = followUp;
                break;
            }

            if (blocked == null)
                continue;

            prerequisiteConfig.Enabled = true;
            repaired.Add($"{DescribeMission(prerequisiteId)}（為了 {DescribeMission(blocked.Value)}）");
        }

        if (repaired.Count == 0)
            return;

        C.Save();
        IceLogging.Info(
            $"重新啟用了 {repaired.Count} 個連續任務的前置：" + string.Join("、", repaired) + "。"
            + "這些前置先前因為「取得金星後自動停用」而被關掉，"
            + "但鏈上還有你啟用中、尚未金星的任務 —— 前置關著的話那些任務永遠不會出現。",
            handle);
    }

    /// <summary>記錄檔用的「[ID]名稱」。查不到名字時只印 ID，不要讓查表失敗變成例外。</summary>
    internal static string DescribeMission(uint missionId)
        => CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var info) && !string.IsNullOrEmpty(info.Name)
            ? $"[{missionId}]{info.Name}"
            : $"[{missionId}]";
}
