using Dalamud.Plugin.Ipc.Exceptions;
using ICE.IPC;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;

namespace ICE.Utilities;

/// <summary>
/// 宇宙製作時，透過 Artisan 的「臨時求解器」IPC 指定要用哪個求解器。
/// </summary>
/// <remarks>
/// <para>
/// 宇宙配方全部是專家配方（<c>IsExpert=True</c>、<c>ConditionsFlag=995</c>），
/// 而 Artisan 沒有指派時挑的是優先度最高的那個求解器，不保證是專家求解器。
/// 這個類別取代了「直接去改使用者 Artisan 設定檔裡的 <c>RecipeConfigs</c>」那種做法。
/// </para>
/// <para>
/// 🔴 <b>覆寫一定要還回去。</b>Artisan 的臨時設定活在它自己的記憶體裡，我們不還原的話，
/// 使用者接下來自己手動製作同一個配方時還會被我們設進去的求解器接管。
/// 所以還原點有三個：任務結束（<see cref="SyncMission"/> 每幀比對任務 id）、
/// ICE 停止（<c>SchedulerMain.DisablePlugin</c>）、外掛卸載（<c>ICE.Dispose</c>）。
/// </para>
/// <para>
/// 🔴 <b>fail-safe 一律是「照原樣製作」。</b>端點不存在（舊版 Artisan）、
/// 該配方沒有這個求解器可選、Artisan 拒絕設定 —— 三種情況都只寫一行 Information，
/// 不擋下製作、不改變任何既有行為。
/// </para>
/// <para>
/// 📌 <b>執行緒</b>：這裡每一支都只從 Framework 執行緒被呼叫
/// （<c>ICE.Tick</c>、ECommons <c>TaskManager</c> 的任務、<c>Dispose</c>），
/// 所以底下的裸 <c>HashSet</c> 是安全的。
/// ⚠️ <b>不要從 ImGui 的 Draw 回呼呼叫這裡的任何一支</b>：Artisan 端的
/// <c>IpcFrameworkGate</c> 在非 Framework 執行緒上會等主執行緒最多 5 秒，
/// 從繪製回呼進去等於把畫面卡住。設定改動由 <see cref="SyncMission"/> 在下一幀處理。
/// </para>
/// </remarks>
internal static class CosmicSolverOverride
{
    private const string Handle = "[Cosmic Solver Override]";

    /// <summary>訊息節流表的上限，避免內容意外發散時無限成長。</summary>
    private const int MaxTrackedMessages = 128;

    /// <summary>目前已經設過臨時求解器、還沒還原的配方。</summary>
    private static readonly HashSet<uint> Applied = new();

    /// <summary>套用當下的任務 id。任務一換就代表上一輪結束了。</summary>
    private static uint appliedMissionId;

    /// <summary>目前套進去的求解器型別全名。使用者中途改設定時要先還原再重套。</summary>
    private static string appliedSolverType = string.Empty;

    /// <summary>「Artisan 沒有這個端點」只講一次就好。</summary>
    private static bool loggedUnavailable;

    private static readonly HashSet<string> LoggedOnce = new(StringComparer.Ordinal);

    /// <summary>目前設定想要的求解器型別全名；<c>null</c> ＝不覆寫。</summary>
    internal static string? WantedSolverType => C.CraftSolverOverride switch
    {
        CosmicCraftSolver.Expert => ArtisanIPC.ExpertSolverTypeName,
        CosmicCraftSolver.Raphael => ArtisanIPC.RaphaelSolverTypeName,
        _ => null,
    };

    /// <summary>
    /// 要求 Artisan 製作 <paramref name="recipeId"/> 之前呼叫。
    /// 冪等：同一個配方在同一輪只會真的送一次 IPC。
    /// </summary>
    internal static void ApplyBeforeCraft(uint recipeId, string handle)
    {
        var wanted = WantedSolverType;
        if (wanted == null)
            return; // 設定是「不覆寫」；既有的覆寫由 SyncMission 還原。

        // 使用者中途換了求解器：先把舊的還回去，再照新設定重套。
        if (!string.Equals(appliedSolverType, wanted, StringComparison.Ordinal))
            ClearAll("求解器設定已變更", handle);

        if (Applied.Contains(recipeId))
            return;

        var artisan = P?.Artisan;
        if (artisan == null)
            return;

        // 🔴 端點不存在（舊版 Artisan）時 SafeWrapper 會吞掉 IpcNotReadyError 並回傳 default，
        //    對 string[] 來說就是 null —— 這與「Artisan 在，但這個配方一個求解器都沒有」
        //    （回空陣列）是兩件事，所以分開處理。
        var available = artisan.GetAvailableSolverTypes(recipeId);
        if (available == null)
        {
            if (!loggedUnavailable)
            {
                loggedUnavailable = true;
                IceLogging.Info("Artisan 沒有提供「依型別指定臨時求解器」的 IPC（多半是舊版），" +
                                "宇宙製作照 Artisan 目前的設定進行。", handle);
            }
            return;
        }

        if (!available.Contains(wanted, StringComparer.Ordinal))
        {
            if (ShouldLog($"missing|{recipeId}|{wanted}"))
                IceLogging.Info($"配方 {recipeId} 沒有「{wanted}」可選，照原樣製作。" +
                                $"（Artisan 回報可用的求解器：{FormatList(available)}）", handle);
            return;
        }

        if (!artisan.SetTemporarySolverByType(recipeId, wanted))
        {
            if (ShouldLog($"refused|{recipeId}|{wanted}"))
                IceLogging.Info($"Artisan 拒絕把配方 {recipeId} 的臨時求解器設成「{wanted}」，照原樣製作。", handle);
            return;
        }

        Applied.Add(recipeId);
        appliedSolverType = wanted;
        appliedMissionId = CosmicHelper.CurrentLunarMission;
        IceLogging.Info($"已把配方 {recipeId} 的臨時求解器設成「{wanted}」（任務 {appliedMissionId}）。", handle);
    }

    /// <summary>
    /// 每幀呼叫。設定被改掉、或任務已經換了／結束了（含遊戲端自己取消、離開宇宙探索區）
    /// 就把覆寫還給 Artisan。
    /// </summary>
    /// <remarks>
    /// 📌 身上沒有任何覆寫時第一行就回去，不做任何 IPC，也不讀遊戲結構。
    /// </remarks>
    internal static void SyncMission()
    {
        if (Applied.Count == 0)
            return;

        if (!string.Equals(appliedSolverType, WantedSolverType ?? string.Empty, StringComparison.Ordinal))
        {
            ClearAll("求解器設定已變更", Handle);
            return;
        }

        // CurrentLunarMission 自帶判空（不在宇宙探索內容裡時回 0），不會解參考到 null 的 WKSManager。
        if (CosmicHelper.CurrentLunarMission == appliedMissionId)
            return;

        ClearAll("任務已結束或已更換", Handle);
    }

    /// <summary>把所有還沒還原的臨時求解器還給 Artisan。重複呼叫是安全的。</summary>
    /// <remarks>
    /// 🔴 <c>Dispose</c> 路徑會走到這裡，而那時 Artisan 可能已經先被卸載了 ——
    /// 端點消失時 Dalamud 擲的是 <c>IpcNotReadyError</c>，所以逐筆包 try/catch：
    /// 一筆失敗不該讓其餘的配方留著覆寫。
    /// （EzIPC 這邊帶的是 <c>SafeWrapper.AnyException</c>，正常情況下例外不會傳到這裡，
    /// 但委派本身為 null（Init 失敗）時還是會擲 —— 那正是這個 catch 真正擋得住的。）
    /// </remarks>
    internal static void ClearAll(string reason, string handle = Handle)
    {
        appliedSolverType = string.Empty;
        if (Applied.Count == 0)
            return;

        var artisan = P?.Artisan;
        foreach (var recipeId in Applied)
        {
            try
            {
                artisan?.ClearTemporaryRecipeSettings(recipeId);
            }
            catch (IpcNotReadyError)
            {
                // Artisan 已經卸載了：它的臨時設定跟著它一起消失，沒有東西要還原。
            }
            catch (Exception e)
            {
                IceLogging.Error($"還原配方 {recipeId} 的臨時求解器時發生例外：{e.Message}", handle);
            }
        }

        IceLogging.Info($"已還原 {Applied.Count} 個配方的臨時求解器（{reason}）。", handle);
        Applied.Clear();
        appliedMissionId = 0;
    }

    /// <summary>目前有幾個配方被覆寫著（偵錯視窗／報告用）。</summary>
    internal static int AppliedCount => Applied.Count;

    private static bool ShouldLog(string key)
    {
        if (LoggedOnce.Count >= MaxTrackedMessages)
            LoggedOnce.Clear();
        return LoggedOnce.Add(key);
    }

    private static string FormatList(string[] values)
        => values.Length == 0 ? "（空）" : string.Join(", ", values);
}
