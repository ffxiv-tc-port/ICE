using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ICE.Scheduler.Tasks;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace ICE.Utilities;

/// <summary>
/// 從遊戲系統訊息認出宇宙探索任務的結束狀態（評價已定、宣告的獎章、拒絕垂釣、回報結果），記進 <see cref="Signals"/>。
/// </summary>
/// <remarks>
/// 契約：只讀訊息，不改內容、不動 isHandled；由遊戲的聊天處理在框架執行緒呼叫。ICE 沒在跑時第一行就回去。
/// 比對依 LogMessage 表本身的 LogKind（聊天類型低 7 位元）與句型（<see cref="CraftFailureText"/>），不寫死字串。
/// </remarks>
internal static class MissionEndWatcher
{
    internal static readonly MissionEndSignals Signals = new();

    private static (uint Id, int LogKind, List<string> Literals)[]? patterns;
    private static HashSet<int>? watchedKinds;
    private static bool loggedError;

    // 最近一次在任務結束匯流點記下的獎章；回報訊息比匯流點晚到時拿來對照。
    private static uint recordedMission;
    private static TurninState recordedMedal = TurninState.None;

    internal static void Enable() => Svc.Chat.ChatMessage += OnChatMessage;

    internal static void Disable() => Svc.Chat.ChatMessage -= OnChatMessage;

    /// <summary>ICE 接下任務那一刻：新的一次執行，上一次的評價已定／回報獎章都不帶過來。</summary>
    internal static void NoteMissionStarted(uint missionId)
    {
        Signals.BeginRun(missionId);
        recordedMission = 0;
    }

    private static void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (SchedulerMain.State == IceState.Idle)
            return;

        try
        {
            var list = patterns ??= LoadPatterns();
            var kind = (int)type & 0x7F;
            if (watchedKinds == null || !watchedKinds.Contains(kind))
                return;

            var text = new ReadOnlySeStringSpan(message.Encode()).ExtractText();
            if (string.IsNullOrEmpty(text))
                return;

            foreach (var (id, logKind, literals) in list)
            {
                if (logKind == kind && CraftFailureText.Matches(literals, text))
                {
                    Handle(id);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            if (!loggedError)
            {
                loggedError = true;
                IceLogging.Warning($"判讀任務結束訊息時發生例外（之後不再重複記錄）：{ex}", "[Mission End]");
            }
        }
    }

    private static void Handle(uint id)
    {
        var mission = CosmicHelper.CurrentLunarMission;
        var now = DateTime.UtcNow;
        var run = Signals.Run;
        Signals.Observe(id, mission, now);

        switch (id)
        {
            case MissionEndSignals.MissionStartedMessage:
                recordedMission = 0;
                break;
            case MissionEndSignals.EvaluationFixedMessage:
                IceLogging.Info($"遊戲宣告「探索任務的評價已定」：任務 {mission}｜遊戲宣告過的獎章＝{Signals.AnnouncedMedal(mission)}。"
                                + "（釣魚任務：不再開始釣魚，直接交件。）", "[Mission End]");
                break;
            case MissionEndSignals.FishingRefusedMessage:
                var refusals = Signals.Refusals(mission);
                if (refusals == 1 || refusals == 3)
                    IceLogging.Info($"遊戲拒絕垂釣（「目前該釣場無法進行垂釣。」第 {refusals} 次）：任務 {mission}｜"
                                    + $"評價已定＝{Signals.IsEvaluationFixed(mission, now)}。"
                                    + "暫停送出開始釣魚、回去重判任務狀態。", "[Mission End]");
                break;
            case MissionEndSignals.ReportedBronzeMessage:
            case MissionEndSignals.ReportedSilverMessage:
            case MissionEndSignals.ReportedGoldMessage:
                var reported = Signals.ReportedMedal;
                if (run != 0 && recordedMission == run)
                {
                    // 匯流點已經記過了：只留對照，不回頭改統計。
                    IceLogging.Info($"遊戲回報任務 {run} 的獎章＝{reported}｜ICE 在任務結束時已記＝{recordedMedal}"
                                    + $"{(reported == recordedMedal ? "（一致）" : "（不一致，回報訊息比任務結束晚到）")}。", "[Mission End]");
                    Signals.TryTakeReportedMedal(run, out _);
                    recordedMission = 0;
                }
                break;
        }
    }

    /// <summary>
    /// 任務結束的匯流點（交件／放棄流程看到任務 ID 變 0 那一刻）呼叫：遊戲的回報訊息已經到了就改記遊戲說的獎章。
    /// 緊急任務（Critical）照舊記 Critical。
    /// </summary>
    internal static void ReconcileReportedMedal(uint missionId, string handle)
    {
        if (Mission_Settings.TurninState != TurninState.Critical
            && Signals.TryTakeReportedMedal(missionId, out var reported))
        {
            IceLogging.Info($"任務 {missionId}：遊戲回報的獎章＝{reported}｜ICE 判定＝{Mission_Settings.TurninState}"
                            + $"{(reported == Mission_Settings.TurninState ? "（一致）" : "，改記遊戲回報的獎章")}。", handle);
            Mission_Settings.TurninState = reported;
            recordedMission = 0;
            return;
        }

        recordedMission = missionId;
        recordedMedal = Mission_Settings.TurninState;
    }

    private static (uint, int, List<string>)[] LoadPatterns()
    {
        var sheet = Svc.Data.GetExcelSheet<LogMessage>();
        var list = new List<(uint, int, List<string>)>();
        foreach (var id in MissionEndSignals.WatchedMessages)
            if (sheet.TryGetRow(id, out var row))
                list.Add((id, (int)row.LogKind.RowId, Literals(row.Text)));

        watchedKinds = list.Select(p => p.Item2).ToHashSet();

        // 要使用者回報的診斷：樣本少於預期＝那幾則認不出來，該訊號退回舊行為（只靠 ICE 自己數）。
        IceLogging.Info(
            $"任務結束訊息比對樣本 {list.Count}/{MissionEndSignals.WatchedMessages.Length} 則（｛…｝＝巨集）："
            + string.Join(" ‖ ", list.Select(p => $"{p.Item1}[{p.Item2}]={string.Join("｛…｝", p.Item3)}")), "[Mission End]");
        return list.ToArray();
    }

    private static List<string> Literals(ReadOnlySeString text)
    {
        var literals = new List<string>();
        var current = new StringBuilder();
        foreach (var payload in text)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                current.Append(Encoding.UTF8.GetString(payload.Body.Span));
                continue;
            }
            literals.Add(current.ToString());
            current.Clear();
        }
        literals.Add(current.ToString());
        return literals;
    }
}
