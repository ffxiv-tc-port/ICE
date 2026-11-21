using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace ICE.Utilities;

/// <summary>
/// 從遊戲已解析好的聊天訊息認出「製作失敗」，交給 <see cref="CraftProgressGuard"/>
/// 分辨「Artisan 做完一場但失敗」與「Artisan 根本沒動」。
/// </summary>
/// <remarks>
/// 契約：只讀訊息，不改內容、不動 isHandled；由遊戲的聊天處理在框架執行緒呼叫。
/// LogKind 不是製作（66）的訊息第一行就回去；ICE 沒有在等製作結果時也不比對文字。
/// 句型取自 LogMessage 表本身（台服繁中），不寫死字串；比對規則見 <see cref="CraftFailureText"/>。
/// </remarks>
internal static class CraftFailureWatcher
{
    private const int CraftingLogKind = 66;

    // 5914／5915＝未滿足需要的品質，製作（道具）失敗；1160／1177＝製作失敗。
    private static readonly uint[] FailureLogMessages = [5915, 5914, 1160, 1177];

    private static (uint Id, List<string> Literals)[]? patterns;
    private static bool loggedError;

    internal static void Enable() => Svc.Chat.ChatMessage += OnChatMessage;

    internal static void Disable() => Svc.Chat.ChatMessage -= OnChatMessage;

    private static void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        // 聊天類型的低 7 位元是 LogKind，上面幾位元是發話者／對象關係。
        if (((int)type & 0x7F) != CraftingLogKind || !CraftProgressGuard.IsArmed)
            return;

        try
        {
            // 另一端是 Lumina 的字面片段 ⇒ 這一端也走 Lumina 解析器，連字符等 payload 才攤得一致。
            var text = new ReadOnlySeStringSpan(message.Encode()).ExtractText();
            if (string.IsNullOrEmpty(text))
                return;

            foreach (var (id, literals) in patterns ??= LoadPatterns())
            {
                if (CraftFailureText.Matches(literals, text))
                {
                    CraftProgressGuard.NoteSynthesisFailed(id, (int)type, text);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            if (!loggedError)
            {
                loggedError = true;
                IceLogging.Warning($"判讀製作失敗訊息時發生例外（之後不再重複記錄）：{ex}", "[Craft Failure]");
            }
        }
    }

    private static (uint, List<string>)[] LoadPatterns()
    {
        var sheet = Svc.Data.GetExcelSheet<LogMessage>();
        var list = new List<(uint, List<string>)>();
        foreach (var id in FailureLogMessages)
            if (sheet.TryGetRow(id, out var row))
                list.Add((id, Literals(row.Text)));

        // 要使用者回報的診斷：樣本少於預期＝那幾則製作失敗認不出來，退回舊行為（算「沒有進展」）。
        IceLogging.Info(
            $"製作失敗訊息比對樣本 {list.Count}/{FailureLogMessages.Length} 則（｛…｝＝巨集）："
            + string.Join(" ‖ ", list.Select(p => $"{p.Item1}={string.Join("｛…｝", p.Item2)}")), "[Craft Failure]");
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
