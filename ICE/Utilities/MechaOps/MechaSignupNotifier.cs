using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 「機甲行動的報名視窗開了」的一次性通知。
///
/// 🔴🔴 <b>純通知。</b>這個類別不呼叫任何遊戲函式、不按任何視窗、不送任何指令，
/// 也刻意<b>不提供</b>「一鍵報名」之類的入口——機甲行動零自動化是紅線。
/// 它唯一做的事情是把一行字寫進聊天視窗（走 ICE 既有的
/// <see cref="IceLogging.ChatInfo"/>，等於同時進聊天、ICE 自己的記錄檢視、
/// 以及 dalamud.log 的 Information 級）。使用者看到之後自己去點遊戲的面板。
///
/// 🔑 <b>資料源刻意用 <see cref="MechaOpsMonitor.Schedule"/> 而不是
/// <see cref="MechaOpsMonitor.EventDetail"/></b>：前者掃的是 <c>_events</c> 這個
/// <b>內嵌</b>陣列，一個指標都不用解（安全論證見 <see cref="MechaScheduleEntry"/>），
/// 而且兩格都看得到；後者要先過 <c>CurrentEvent</c> 的指標範圍驗證，
/// 「報名開放」這件事發生在事件開始之前，那條路徑不保證那時候就通得過。
///
/// 🔑 <b>開放與否照抄遊戲自己的判定</b>（<c>IsPilotRegistrationTimeframeOpen</c>，
/// 反組譯逐行貼在 <see cref="MechaEventDetail.IsRegistrationOpen"/> 上）：
/// 旗標位元 <c>PilotRegistrationOpen</c> 亮著、而且伺服器時間還沒到
/// <c>PilotRegistrationEndTimestamp</c>。遊戲自己<b>不看</b>開始時間戳，這裡也不看。
///
/// ⚠️ <b>只在宇宙區域內判定</b>：呼叫端 <c>MechaOpsMonitor.TickInner</c> 一離開
/// 宇宙區域就整段返回。這是刻意的——不在場的人收到「報名開了」也趕不上。
/// </summary>
internal static class MechaSignupNotifier
{
    /// <summary>
    /// 已經通知過的事件（key 見 <see cref="KeyOf"/>）。
    ///
    /// 🔑 <b>為什麼不是「上一輪開著沒」的一個 bool</b>：過區、登出重進、模組短暫讀不到
    /// 都會讓狀態被清成「沒開」，下一輪就再通知一次。以事件本身為鍵才不會重複洗版。
    /// </summary>
    private static readonly HashSet<long> announced = [];

    /// <summary>
    /// 事件開始時間戳比現在早這麼多之後就把記錄丟掉，讓集合不會無限成長。
    /// 一場機甲事件連同報名期遠短於這個長度，所以不會提早忘記而重複通知。
    /// </summary>
    private const long ForgetAfterSeconds = 6 * 3600;

    /// <summary>
    /// 掛在 <c>MechaOpsMonitor.TickInner</c>（Framework 執行緒）的 250ms 節流之後。
    /// 傳進來的是那一輪剛發布的排程快照，本方法自己不讀任何遊戲結構。
    /// </summary>
    public static void Tick(IReadOnlyList<MechaScheduleEntry> entries)
    {
        if (!C.NotifyMechaSignupOpen)
        {
            // 關掉時把記錄清乾淨：使用者中途關掉再打開，下一場照樣通知得到。
            if (announced.Count > 0)
                announced.Clear();
            return;
        }

        if (entries.Count == 0)
            return;

        // 🔴 一律用伺服器時間，不用本機時鐘——遊戲自己判定報名視窗時比的就是它。
        //    取不到（0）就這一輪不判定：寧可晚一點通知，也不要拿可能偏掉的
        //    本機時間去宣告「開了」或「已經截止」。
        var now = entries[0].ServerTimeNow;
        if (now <= 0)
            return;

        announced.RemoveWhere(key => now - (key >> 32) > ForgetAfterSeconds);

        foreach (var e in entries)
        {
            if (!IsRegistrationOpen(e, now))
                continue;

            // Add 回 false ＝ 這一場已經講過了。
            if (!announced.Add(KeyOf(e)))
                continue;

            Announce(e, now);
        }
    }

    /// <summary>
    /// 照抄遊戲自己的判定（見類別註解）。
    /// <c>RegistrationEnd</c> 是 0 時回 <c>false</c>＝「不知道就不通知」，
    /// 不要拿旗標單獨當成開放（那樣會在讀不到截止時刻時發出一則沒有截止時間的通知）。
    /// </summary>
    private static bool IsRegistrationOpen(MechaScheduleEntry e, long now)
    {
        if ((e.Flags & WKSMechaEventFlag.PilotRegistrationOpen) == 0)
            return false;
        if (e.RegistrationEnd <= 0)
            return false;
        return now < e.RegistrationEnd;
    }

    /// <summary>
    /// 事件識別：開始時間戳（高 32 位元）＋ 資料列 id（低 32 位元）。
    /// 刻意<b>不用</b>格位編號——同一格會輪到下一場事件，用格位會把新的一場當成講過了。
    /// </summary>
    private static long KeyOf(MechaScheduleEntry e)
        => ((long)e.EventStart << 32) | (long)(uint)e.DataRowId;

    private static void Announce(MechaScheduleEntry e, long now)
    {
        // 查不到名字就是「?」——不要拿空字串充數，那會變成一句話中間破一個洞。
        var name = MechaObjectNames.EventName(e.DataRowId) ?? "?";

        IceLogging.ChatInfo(
            ("Mecha ops sign-up is now open: ?? - closes at ?? (?? left). "
             + "ICE only tells you; sign up yourself from the game's own mecha ops panel.")
            .Loc(name, FormatClock(e.RegistrationEnd), FormatDuration(e.RegistrationEnd - now)),
            "[ICE]");
    }

    /// <summary>當地時間 HH:mm。垃圾值一律回「?」，不要猜。</summary>
    private static string FormatClock(int unixSeconds)
    {
        if (unixSeconds <= 0)
            return "?";
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("HH:mm");
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>與 <c>MechaOpsWindow.FormatDuration</c> 同格式，讓兩處的倒數長得一樣。</summary>
    private static string FormatDuration(long seconds)
    {
        if (seconds < 0)
            seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1d
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }
}
