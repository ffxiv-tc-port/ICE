using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 連續任務（<c>WKSMissionUnit.LockedBehind</c>）的鏈結查詢。
/// </summary>
/// <remarks>
/// 📌 <b>資料形狀</b>（台服 7.20 逐列統計 <c>exd-tc/7.20/WKSMissionUnit.csv</c>）：<br/>
/// 1073 列裡有 <b>88 列</b>的 <c>LockedBehind</c> 非零，對應 <b>88 個互不重複</b>的前置任務
/// （一對一，沒有任何一個前置同時擋住兩個任務）。88 條邊構成 55 條鏈：22 條長度 1、33 條長度 2，
/// 也就是<b>最長是「前置 → 中繼 → 終點」三層</b>。88 個被擋任務與 88 個前置任務的
/// <c>Name</c> 全都非空 —— 也就是<b>兩端都在 <see cref="CosmicHelper.SheetMissionDict"/> 裡</b>，
/// 沒有指向未開放星球的斷邊。沒有自環。<br/><br/>
///
/// ⚠️ 儘管目前是一對一，這裡仍然用 <c>List</c> 存後續任務：改版新增資料時
/// 「一個前置擋住兩個任務」不會靜默丟掉其中一個。<br/>
/// ⚠️ 走訪一律帶 visited 集合。上游既有的
/// <c>modeSelect_TableInfo.GetOnlyNextMissionsRecursive</c> 是<b>無環偵測的遞迴</b>，
/// 資料一旦出現環就是無窮遞迴（StackOverflow 攔不到、直接帶走遊戲）。
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

    /// <summary>
    /// 「取得金星後自動停用」要不要<b>放過</b>這個任務，因為它是別人的前置。
    /// </summary>
    /// <param name="missionId">正打算停用的任務。</param>
    /// <param name="reason">要寫進記錄檔的理由（回傳 <see langword="true"/> 時才有意義）。</param>
    /// <returns><see langword="true"/> ＝<b>不要停用</b>。</returns>
    /// <remarks>
    /// 🔴 <b>兩邊的代價差好幾個數量級</b>：多接一次已金星的任務是浪費幾分鐘；
    /// 把鏈上的前置停掉則是整條後續任務<b>再也接不到</b>，而且完全沒有提示 ——
    /// 使用者只能自己回頭核對整張任務表。所以<b>判斷不出來時一律回 true（不停用）</b>。<br/><br/>
    ///
    /// 📌 為什麼「金星了就停用前置」會鎖死整條鏈：連續任務的後續<b>不是永久解鎖</b>，
    /// 而是要重跑前置才會再出現。<c>Task_TurninMission.GoldCheck</c> 裡那段
    /// 「沒拿到金星 → 重新啟用所有前置」就是上游自己對這個機制的認定，
    /// 而使用者 2026-08-07 也直接回報了鎖死的實況。<br/><br/>
    ///
    /// ⚠️ <b>不看下游任務的 <c>Enabled</c></b>：那個旗標正好會被本功能自己改掉，
    /// 拿它當判斷依據等於用結果去推原因。只看遊戲端的金星旗標 —— 那是唯一不會被我們污染的真值。
    /// </remarks>
    /// <summary>
    /// 「取得金星後排除任務」要不要對<b>緊急任務</b>網開一面。
    /// 使用者需求原話：「取得金星後排除任務 要能把緊急任務列例外」。
    ///
    /// 🔑 <b>為什麼緊急任務值得例外</b>：它們是磁暴／流星雨／孢子霧時段限定的，
    /// 一般任務金星之後就沒有再跑的理由，緊急任務卻是「這個時段只有這些能跑」——
    /// 把拿過金星的整批踢出候選池之後，紅色警報一來反而沒有任務可接。
    ///
    /// 🔬 <b>判別碼＝<c>MissionAttributes.Critical</c></b>，它在 <c>ICEDictornaryCreation</c>
    /// 直接來自 <c>WKSMissionUnit.IsSpecialQuest</c>。這個欄位在台服 7.20 已離線驗證
    /// （2026-08-08）：<c>IsSpecialQuest == true</c> 的恰好是 <b>33 個任務</b>（列 512..544），
    /// 而完全獨立的另一條資料鏈
    /// <c>WKSEmergencyMission → WKSEmergencyMissionGroup.WKSMissionUnit</c>
    /// 列出來的也是<b>同樣那 33 個</b>（33/33 逐筆相同，兩邊互為交叉驗證）。
    /// ⚠️ 別用 <c>exd-tc</c> CSV 的欄位<b>位置</b>去取這個值：那張表裡
    /// <c>WKSMissionLotterySpecialCond</c> 也含 "Special" 字樣，取錯欄會得到 3 筆而不是 33 筆
    /// （本輪第一次就是這樣算錯的，失敗形式是「數字看起來很合理」）。
    ///
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

        // WKSMissionUnit 的 [StaticAddress] 槽位在宇宙探索內容以外是 null。
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
    /// 🔴 這是在收拾本外掛自己造成的損害，<b>不是</b>推翻使用者的選擇，所以條件開得很緊，
    /// 四項<b>同時</b>成立才動手：<br/>
    /// ① <c>RemoveAfterGold</c> 目前是開的（關著的話 ICE 從來沒停用過任何東西，
    ///    停用中的都是使用者自己的選擇，不要碰）；<br/>
    /// ② 前置任務<b>已經拿到金星</b>（＝只有 <c>RemoveAfterGold</c> 停得掉的那一批）；<br/>
    /// ③ 前置任務目前是停用的；<br/>
    /// ④ 鏈上存在一個<b>使用者自己啟用</b>而且<b>尚未金星</b>的任務。<br/><br/>
    ///
    /// ④ 是關鍵：它代表使用者明確表達了「我要跑這個任務」，而在前置被停用的狀態下
    /// 那個任務<b>永遠不會出現</b> —— 這組設定沒有任何合理的解讀方式。<br/><br/>
    ///
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
