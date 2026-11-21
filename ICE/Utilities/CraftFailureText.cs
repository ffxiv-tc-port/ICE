using System.Collections.Generic;

namespace ICE.Utilities;

/// <summary>
/// 把 LogMessage 表裡的句型（字面片段＋巨集）比對到實際收到的訊息。
/// </summary>
/// <remarks>
/// 契約：<c>literals</c> 是表文字在每個巨集處切開的字面片段，相鄰兩段之間是一個巨集
/// （角色名、道具名…，展開後可以是任意文字、也可以是空字串）；第一段貼齊開頭、最後一段貼齊結尾。
/// 不能用表的 <c>ExtractText()</c> 整句比：巨集被攤成空字串，夾在句子中間的道具名讓整句與尾綴都比不中。
/// 本檔刻意不引用 Dalamud／Lumina：離線探針直接連結這個檔編譯。
/// </remarks>
internal static class CraftFailureText
{
    internal static bool Matches(IReadOnlyList<string> literals, string message)
    {
        if (literals.Count == 0 || string.IsNullOrEmpty(message))
            return false;

        var total = 0;
        foreach (var literal in literals)
            total += literal.Length;
        if (total == 0)
            return false;

        if (!message.StartsWith(literals[0], StringComparison.Ordinal))
            return false;
        if (literals.Count == 1)
            return message.Length == literals[0].Length;

        var pos = literals[0].Length;
        for (var i = 1; i < literals.Count - 1; i++)
        {
            if (literals[i].Length == 0)
                continue;
            var at = message.IndexOf(literals[i], pos, StringComparison.Ordinal);
            if (at < 0)
                return false;
            pos = at + literals[i].Length;
        }

        var last = literals[^1];
        return message.Length - last.Length >= pos && message.EndsWith(last, StringComparison.Ordinal);
    }
}
