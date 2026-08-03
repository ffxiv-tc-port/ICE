using System.Collections.Generic;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;

namespace ICE.Utilities;

/// <summary>
/// 「叫 Artisan 製作 → Artisan 不忙了 → 再叫一次」這條路徑的斷路器。
/// </summary>
/// <remarks>
/// 🔴 <c>Task_Craft.WaitingForArtisan</c> 與 <c>Task_DualClass.WaitingForArtisan</c> 原本是
/// <b>無條件重試</b>：只要 <c>P.Artisan.IsBusy()</c> 變成 false 就當成「做完了，繼續」，
/// 完全不看有沒有真的做出東西來。Artisan 那邊只要停下來（不管是製作成功、素材不足、
/// 還是它自己的錯誤斷路器把耐力關掉），這裡看到的都是同一件事。<br/>
/// <br/>
/// 2026-08-03 實機事故的形狀：Artisan 因為一個永遠為假的閘門而無法開始製作，
/// 每次都在幾秒後把耐力關掉 → ICE 立刻重下同一個指令 → <b>每 1.35 秒一輪、近 30 次、
/// 完全沒有收斂跡象</b>，而使用者在畫面上只看到 ICE 停在 Idle，沒有任何訊息說明原因。<br/>
/// <br/>
/// 這個守衛做兩件事：<br/>
/// 1. <b>用背包實際數量判斷有沒有進展</b>（不是問 Artisan，Artisan 正是壞掉的那一端）。<br/>
/// 2. 連續 <see cref="MaxConsecutiveNoProgress"/> 次沒有進展就<b>停下來並在聊天視窗說明是哪個道具、
///    缺什麼</b>；中間先用退避拉開間隔，不要用 1.35 秒的節奏猛敲。<br/>
/// <br/>
/// ⚠️ 「讀不到背包」不算失敗。傳送／換區途中 <c>InventoryManager</c> 一律回 0，
/// 把那個狀態算成「沒有進展」就會重演 <c>Task_Fishing</c> 那次「身上有 999 個餌卻被判成沒餌」
/// 的誤判 —— 所以要先過 <see cref="PlayerHelper.InventoryReadable"/>。
/// </remarks>
internal static class CraftProgressGuard
{
    /// <summary>連續幾次「叫了但完全沒有做出東西」就停止。</summary>
    internal const int MaxConsecutiveNoProgress = 5;

    /// <summary>每次失敗後在下一次嘗試前至少要等多久（秒）。索引 = 已連續失敗次數 - 1。</summary>
    private static readonly int[] BackoffSeconds = [3, 6, 12, 24, 24];

    private static ushort _recipeId;
    private static uint _itemId;
    private static int _countAtRequest;
    private static bool _armed;
    private static Dictionary<uint, int> _requiredItems = new();

    private static int _consecutive;
    private static DateTime _nextAttemptAt = DateTime.MinValue;

    internal static int ConsecutiveFailures => _consecutive;

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

        // 讀不到背包時不要記一個假的 0 當基準 —— 那會讓下一次比較永遠「有進展」。
        _armed = PlayerHelper.InventoryReadable() && PlayerHelper.GetItemCount(itemId, out _countAtRequest);
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

        // 讀不到背包 → 這一輪不做判斷（既不算成功也不算失敗）。
        if (!PlayerHelper.InventoryReadable() || !PlayerHelper.GetItemCount(_itemId, out var now))
            return;

        if (now > _countAtRequest)
        {
            Reset();
            return;
        }

        _consecutive++;
        var backoff = BackoffSeconds[Math.Min(_consecutive, BackoffSeconds.Length) - 1];
        _nextAttemptAt = DateTime.Now.AddSeconds(backoff);

        // Information 而非 Debug：使用者跑 LogLevel 2，Debug 收不到。
        IceLogging.Info(
            $"要求 Artisan 製作配方 {_recipeId}（{NameOf(_itemId)}）之後，持有數量仍是 {now}（要求前 {_countAtRequest}）—— "
            + $"連續第 {_consecutive}/{MaxConsecutiveNoProgress} 次沒有任何進展，等 {backoff} 秒再試。",
            handle);
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
        SchedulerMain.DisablePlugin();
    }

    internal static void Reset()
    {
        _consecutive = 0;
        _armed = false;
        _nextAttemptAt = DateTime.MinValue;
        _recipeId = 0;
        _itemId = 0;
        _requiredItems = new();
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
}
