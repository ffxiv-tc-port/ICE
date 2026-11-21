using System.Collections.Generic;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;

namespace ICE.Utilities;

/// <summary>
/// 「叫 Artisan 製作 → Artisan 不忙了 → 再叫一次」這條路徑的斷路器。
/// </summary>
/// <remarks>
/// ⚠️ 「讀不到背包」不算失敗。傳送／換區途中 <c>InventoryManager</c> 一律回 0，
/// 把那個狀態算成「沒有進展」是誤判 —— 所以要先過 <see cref="PlayerHelper.InventoryReadable"/>。
/// <para>
/// 契約：「做完一場但製作失敗」（遊戲的製作失敗訊息，見 <see cref="CraftFailureWatcher"/>）證明 Artisan 有在動，
/// 不計入停機斷路器；改由同一個任務實例累計 <see cref="MaxSynthesisFailuresPerMission"/> 次就放棄該任務。
/// </para>
/// </remarks>
internal static class CraftProgressGuard
{
    /// <summary>連續幾次「叫了但完全沒有做出東西」就停止。</summary>
    internal const int MaxConsecutiveNoProgress = 5;

    /// <summary>
    /// 同一個任務實例累計幾次「做完但製作失敗」就放棄。專家配方實機成功率約六成、一場約 2 分鐘：
    /// 兩次都失敗的機率約 16%，再試只是賭；而高難+ 中間素材一輪只發兩份材料，第三次本來就做不了。
    /// </summary>
    internal const int MaxSynthesisFailuresPerMission = 2;

    /// <summary>每次失敗後在下一次嘗試前至少要等多久（秒）。索引 = 已連續失敗次數 - 1。</summary>
    private static readonly int[] BackoffSeconds = [3, 6, 12, 24, 24];

    private static ushort _recipeId;
    private static uint _itemId;
    private static int _countAtRequest;
    private static bool _armed;
    private static Dictionary<uint, int> _requiredItems = new();
    private static Dictionary<uint, int> _materialsAtRequest = new();
    private static bool _synthesisFailedSinceArm;

    private static int _consecutive;
    private static DateTime _nextAttemptAt = DateTime.MinValue;

    // 與 _consecutive 分開：換配方（前置素材→成品）不清，換任務實例才清。
    private static uint _failMissionId;
    private static int _failCount;

    internal static int ConsecutiveFailures => _consecutive;

    /// <summary>ICE 已要求製作、還在等 Artisan 停下來。</summary>
    internal static bool IsArmed => _armed;

    /// <summary>連續失敗次數已達上限，呼叫端必須停止並回報。</summary>
    internal static bool LimitReached => _consecutive >= MaxConsecutiveNoProgress;

    /// <summary>
    /// 在「即將要求 Artisan 製作」時呼叫，記下判斷進展所需的基準。
    /// </summary>
    /// <param name="recipeId">配方編號。</param>
    /// <param name="itemId">這個配方做出來的成品道具編號 —— 進展就是用它的持有數量判斷的。</param>
    /// <param name="requiredItems">配方需要的素材（道具 → 每做一個要幾個），只用於失敗時的說明文字。</param>
    internal static void Arm(ushort recipeId, uint itemId, IReadOnlyDictionary<uint, int>? requiredItems = null)
    {
        // 換了配方就重新計數：斷路器要判的是「同一個配方連續沒有進展」，
        // 不是「總共失敗過幾次」。混在一起會在正常的雙配方輪替裡誤觸發。
        if (recipeId != _recipeId)
            Reset();

        _recipeId = recipeId;
        _itemId = itemId;
        _requiredItems = requiredItems is null ? new() : new(requiredItems);

        _synthesisFailedSinceArm = false;

        // 讀不到背包時不要記一個假的 0 當基準 —— 那會讓下一次比較永遠「有進展」。
        _armed = PlayerHelper.InventoryReadable() && PlayerHelper.GetItemCount(itemId, out _countAtRequest);

        _materialsAtRequest = new();
        if (_armed)
            foreach (var material in _requiredItems.Keys)
                if (material != 0 && PlayerHelper.GetItemCount(material, out var held))
                    _materialsAtRequest[material] = held;
    }

    /// <summary>
    /// 遊戲回報「製作失敗」時由 <see cref="CraftFailureWatcher"/> 呼叫；只在已要求製作、還沒判定時記下。
    /// </summary>
    internal static void NoteSynthesisFailed(uint logMessageId, int chatType, string text)
    {
        if (!_armed || _synthesisFailedSinceArm)
            return;
        _synthesisFailedSinceArm = true;

        // 要使用者回報的診斷：訊號本身的校準錨（哪一則 LogMessage、聊天類型原值）。
        IceLogging.Info(
            $"收到遊戲的製作失敗訊息「{text}」（LogMessage {logMessageId}，聊天類型 {chatType}），配方 {_recipeId}。",
            "[Craft Failure]");
    }

    /// <summary>新的任務實例開始（ICE 進入執行任務狀態）時呼叫：清掉上一個實例的製作失敗累計。</summary>
    internal static void NoteMissionStarted(uint missionId)
    {
        _failMissionId = missionId;
        _failCount = 0;
    }

    /// <summary>
    /// 現在可以再要求 Artisan 製作嗎？失敗後的退避期間回 false。
    /// </summary>
    internal static bool MayAttemptNow(out double waitSeconds)
    {
        var remaining = (_nextAttemptAt - DateTime.Now).TotalSeconds;
        waitSeconds = remaining > 0 ? remaining : 0;
        return remaining <= 0;
    }

    /// <summary>
    /// 在 Artisan 停下來的那一刻呼叫：比對成品數量，決定這一輪算不算有進展。
    /// </summary>
    internal static void OnArtisanStopped(string handle)
    {
        if (!_armed)
            return;
        _armed = false;
        var synthesisFailed = _synthesisFailedSinceArm;
        _synthesisFailedSinceArm = false;

        // 讀不到背包 → 這一輪不做判斷（既不算成功也不算失敗）。
        if (!PlayerHelper.InventoryReadable() || !PlayerHelper.GetItemCount(_itemId, out var now))
            return;

        if (now > _countAtRequest)
        {
            Reset();
            return;
        }

        if (synthesisFailed)
        {
            // Artisan 有做完一場：停機斷路器歸零、不退避，改記在這個任務實例的失敗累計上。
            _consecutive = 0;
            _nextAttemptAt = DateTime.MinValue;
            var mission = CosmicHelper.CurrentLunarMission;
            if (mission != _failMissionId)
            {
                _failMissionId = mission;
                _failCount = 0;
            }
            _failCount++;

            // 要使用者回報的診斷。
            IceLogging.Info(
                $"要求 Artisan 製作配方 {_recipeId}（{NameOf(_itemId)}）：做完了但製作失敗（遊戲回報），"
                + $"不算「沒有進展」；任務 {mission} 這一輪第 {_failCount}/{MaxSynthesisFailuresPerMission} 次製作失敗。",
                handle);
            return;
        }

        _consecutive++;
        var backoff = BackoffSeconds[Math.Min(_consecutive, BackoffSeconds.Length) - 1];
        _nextAttemptAt = DateTime.Now.AddSeconds(backoff);

        // 要使用者回報的診斷。素材有少卻沒收到製作失敗訊息＝訊號沒對上，是校準製作失敗偵測的錨。
        var consumed = DescribeConsumedMaterials();
        IceLogging.Info(
            $"要求 Artisan 製作配方 {_recipeId}（{NameOf(_itemId)}）之後，持有數量仍是 {now}（要求前 {_countAtRequest}）—— "
            + $"連續第 {_consecutive}/{MaxConsecutiveNoProgress} 次沒有任何進展，等 {backoff} 秒再試。"
            + (consumed.Length > 0 ? $" 素材有被用掉（{consumed}），但沒有收到遊戲的製作失敗訊息。" : string.Empty),
            handle);
    }

    private static string DescribeConsumedMaterials()
    {
        var parts = new List<string>();
        foreach (var (material, before) in _materialsAtRequest)
            if (PlayerHelper.GetItemCount(material, out var after) && after < before)
                parts.Add($"{NameOf(material)} {before}→{after}");
        return string.Join("、", parts);
    }

    /// <summary>
    /// 這個任務實例的製作失敗累計已達上限：放棄任務（走既有的回報／放棄收尾），
    /// 並套用「已經不可能拿到金星」同一份排到最後的冷卻。
    /// </summary>
    private static void AbandonAfterSynthesisFailures(string handle)
    {
        var missionId = _failMissionId;
        var failures = _failCount;
        _failCount = 0;

        var minutes = C.XPRelicAvoidGoldUnreachableMinutes;
        if (C.XPRelicGrind && C.XPRelicAvoidGoldUnreachable && minutes > 0
            && GoldUnreachableCooldown.Mark(missionId, Environment.TickCount64, minutes))
            IceLogging.Info(
                $"任務 {MissionChain.DescribeMission(missionId)} 這一輪製作失敗 {failures} 次；宇宙工具經驗模式接下來 "
                + $"{minutes} 分鐘內挑任務時會把它排到最後（不移除，拿到金星或插件重載就清掉）。", handle);

        IceLogging.Info(
            $"任務 {MissionChain.DescribeMission(missionId)} 這一輪已經製作失敗 {failures} 次（品質沒到必要品質之類），"
            + "放棄這個任務，走既有的回報／放棄收尾。", handle);
        IceLogging.ChatInfo($"這個任務這一輪製作失敗 {failures} 次，先放棄、換別的任務。", "[ICE]");

        SchedulerMain.State = IceState.AbandonMission;
        P.TaskManager.Tasks.Clear();
    }

    /// <summary>
    /// 已達上限時呼叫：停止 ICE，並在聊天視窗說明是哪個道具、缺什麼。
    /// </summary>
    internal static void ReportAndStop(string handle)
    {
        var itemName = NameOf(_itemId);
        var shortage = DescribeMaterials();

        IceLogging.ChatError(
            $"連續 {MaxConsecutiveNoProgress} 次要求 Artisan 製作「{itemName}」（配方 {_recipeId}）都沒有做出任何東西，已停止 ICE。"
            + (shortage.Length > 0 ? $" 素材狀況：{shortage}。" : string.Empty)
            + " 常見原因：素材真的不夠、Artisan 沒有為這個配方指派到素材、或是 Artisan 那邊自己停用了耐力模式。",
            "[ICE]");

        IceLogging.Info(
            $"斷路器觸發：配方 {_recipeId}（item {_itemId} {itemName}）連續 {_consecutive} 次沒有進展，"
            + $"持有數量停在 {_countAtRequest}。素材：{(shortage.Length > 0 ? shortage : "（無資料）")}",
            handle);

        Reset();
        SchedulerMain.DisablePlugin(StopReasons.Error);
    }

    internal static void Reset()
    {
        _consecutive = 0;
        _armed = false;
        _nextAttemptAt = DateTime.MinValue;
        _recipeId = 0;
        _itemId = 0;
        _requiredItems = new();
        _materialsAtRequest = new();
        _synthesisFailedSinceArm = false;
    }

    private static string DescribeMaterials()
    {
        if (_requiredItems.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var material in _requiredItems)
        {
            if (material.Key == 0 || material.Value <= 0)
                continue;

            var held = PlayerHelper.GetItemCount(material.Key, out var n) ? n.ToString() : "?";
            parts.Add($"{NameOf(material.Key)} 持有 {held} / 每次需要 {material.Value}");
        }
        return string.Join("、", parts);
    }

    private static string NameOf(uint itemId)
    {
        if (itemId == 0)
            return "?";
        try
        {
            var sheet = Svc.Data?.GetExcelSheet<Item>();
            if (sheet is not null && sheet.TryGetRow(itemId, out var row))
            {
                var name = row.Name.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch
        {
            // 名稱只是說明文字，查不到不該讓斷路器本身失敗。
        }
        return $"item {itemId}";
    }

    /// <summary>
    /// 等 Artisan 的步驟在「Artisan 不忙了」之後的共同收尾，一律回 true（這一步結束）。
    /// 🔴 「Artisan 不忙了」不等於「做出東西來了」：用背包實際數量判斷有沒有進展，
    ///    連續失敗就退避，達上限就停下來並說明原因。
    /// </summary>
    /// <param name="clearQueue">沒達上限時清空 ICE 佇列（雙職業流程的既有行為；製作流程不清）。</param>
    internal static bool FinishArtisanWait(string handle, bool clearQueue)
    {
        OnArtisanStopped(handle);
        if (LimitReached)
        {
            ReportAndStop(handle);
            return true;
        }
        if (_failCount >= MaxSynthesisFailuresPerMission && _failMissionId != 0)
        {
            AbandonAfterSynthesisFailures(handle);
            return true;
        }
        if (clearQueue)
            P.TaskManager.Tasks.Clear();
        return true;
    }
}
