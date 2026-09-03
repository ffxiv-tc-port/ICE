using ICE.Utilities.Cosmic_Helper;
using System.Globalization;

namespace ICE.Utilities;

/// <summary>
/// 「這個任務剛剛跨過金星門檻」的一次性通知（記錄檔 <c>Information</c> ＋聊天列各一次）。
/// </summary>
/// <remarks>
/// 🔑 <b>沒有自己的計時器、也沒有新的每幀輪詢</b>：完全掛在疊加層既有的分數讀取路徑上
/// （<see cref="Ui.OverlayWindow"/> 的評價型任務那一行，本來就每幀從面板讀一次目前評價與門檻），
/// 這裡只做兩次比較加一個門閂。<br/><br/>
/// 🔴 <b>純通知，不改任何行為</b>：要不要停手、要不要交件，仍然完全由 <c>Task_CheckScore</c>
/// 既有的設定決定。這個類別不碰 <c>SchedulerMain.State</c>、不碰 <c>Mission_Settings</c>、
/// 也不碰任何設定值。<br/><br/>
/// ⚠️ 節流用自己的門閂而不是 <c>EzThrottler</c>：<c>EzThrottler</c> 的 key 是全域且跨任務持久的，
/// 而且首次一定放行——拿它做「每個任務一次」會在 key 撞到時靜默漏掉通知。
/// 這裡的門閂只認任務 ID，任務換掉（含中途放棄後重接同一個）就重新武裝。
/// </remarks>
internal static class MedalNotifier
{
    /// <summary>已經為哪個任務發過金星達標通知。0＝這一趟還沒發過。</summary>
    private static uint goldAnnouncedFor;

    /// <summary>目前盯著的任務 ID。換任務就把門閂放掉，下一趟同樣任務照樣會通知。</summary>
    private static uint watchedMission;

    /// <summary>
    /// 餵一次「目前評價 / 金星門檻」。跨過門檻的那一刻發一次通知，之後同一個任務不再重複。
    /// </summary>
    /// <param name="missionId">目前進行中的任務 ID。0＝沒有任務，會把門閂重置。</param>
    /// <param name="currentScore">面板讀到的目前評價。讀不到請傳 <see langword="null"/>，<b>不要傳 0</b>。</param>
    /// <param name="goldThreshold">金星門檻。讀不到請傳 <see langword="null"/>。</param>
    internal static void ObserveScore(uint missionId, uint? currentScore, uint? goldThreshold)
    {
        if (missionId == 0)
        {
            watchedMission = 0;
            goldAnnouncedFor = 0;
            return;
        }

        if (missionId != watchedMission)
        {
            watchedMission = missionId;
            goldAnnouncedFor = 0;
        }

        if (goldAnnouncedFor == missionId)
            return;

        // 🔴 任一邊「不知道」就什麼都不做。把 null 當 0 會在面板還沒讀好的那一瞬間
        //    做出錯誤判斷，而且是靜默的。
        // 🔴 門檻 0 一律視為沒有資料：0 會讓「分數 >= 門檻」恆真，開場就噴一則假通知。
        if (currentScore is not uint current || goldThreshold is not uint gold || gold == 0)
            return;

        if (current < gold)
            return;

        goldAnnouncedFor = missionId;

        // 任務名查不到就寫 "???" —— 不要拿 ID 充當名字。
        var name = CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var info)
            ? GameTextUtil.StripGameIcons(info.Name)
            : "???";

        var message = "Gold threshold reached on [??] ?? (rating ?? / gold ??)".Loc(
            missionId,
            name,
            current.ToString("N0", CultureInfo.InvariantCulture),
            gold.ToString("N0", CultureInfo.InvariantCulture));

        // 📌 使用者跑 LogLevel 1，盲區只有 Verbose,Debug 收得到但單檔數十萬行會淹沒 —— 要人回報得到的診斷一律 Information。
        IceLogging.Info(message, "[Medal Notifier]");

        // Svc.Chat.Print 只是把訊息塞進 Dalamud 自己的佇列（下一個 framework update 才真的印），
        // 所以在繪製路徑上呼叫是安全的。
        Svc.Chat.Print($"[I.C.E.] {message}");
    }
}
