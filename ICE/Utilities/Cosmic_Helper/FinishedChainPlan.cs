using System;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 「取得金星後自動停用」：某段連續任務拿到金星後，同一串裡還要一起停用哪些段落。
/// </summary>
/// <remarks>
/// 本檔刻意不引用 Dalamud／遊戲／設定型別：離線探針直接連結這個檔編譯。
/// 判準與 <c>MissionChain.ShouldKeepEnabledForChain</c> 互為反面：已金星、且往後每一段都已金星＝停用；
/// 往後還有任何一段沒金星＝保留（後續不是永久解鎖，要從前置重跑才接得到）。
/// </remarks>
internal static class FinishedChainPlan
{
    /// <param name="goldedMission">剛交件、且已金星的任務；它自己的停用由呼叫端處理，不會出現在回傳裡。</param>
    /// <param name="removeAfterGold">「取得金星後自動停用」開關；關著回空。</param>
    /// <param name="previous">直接前置（不含 0）。</param>
    /// <param name="next">直接後續。</param>
    /// <param name="isGolded">只會被問到同一串的任務。</param>
    /// <param name="isEnabled">已經沒勾的不再列出。</param>
    /// <param name="keepAnyway">其他例外（緊急任務）：回 true 的段落不停用。</param>
    /// <returns>要停用的段落，依鏈上由前到後。</returns>
    internal static List<uint> ToDisable(uint goldedMission, bool removeAfterGold,
                                        Func<uint, IEnumerable<uint>> previous, Func<uint, IEnumerable<uint>> next,
                                        Func<uint, bool> isGolded, Func<uint, bool> isEnabled, Func<uint, bool> keepAnyway)
    {
        var result = new List<uint>();
        if (!removeAfterGold || !isGolded(goldedMission))
            return result;

        foreach (var member in ChainMembers(goldedMission, previous, next))
        {
            if (member == goldedMission || !isEnabled(member) || !isGolded(member))
                continue;

            var finished = true;
            foreach (var followUp in Reachable(member, next))
            {
                if (!isGolded(followUp))
                {
                    finished = false;
                    break;
                }
            }

            if (finished && !keepAnyway(member))
                result.Add(member);
        }

        return result;
    }

    /// <summary>整串（含自己），鏈首在前；有環或分岔也只走訪每個任務一次。</summary>
    internal static List<uint> ChainMembers(uint missionId, Func<uint, IEnumerable<uint>> previous, Func<uint, IEnumerable<uint>> next)
    {
        var heads = new List<uint>();
        var seenUp = new HashSet<uint> { missionId };
        var stack = new Stack<uint>();
        stack.Push(missionId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var hasPrevious = false;
            foreach (var p in previous(current))
            {
                if (p == 0)
                    continue;
                hasPrevious = true;
                if (seenUp.Add(p))
                    stack.Push(p);
            }
            if (!hasPrevious)
                heads.Add(current);
        }

        // 純環（沒有鏈首）時從自己開始走。
        if (heads.Count == 0)
            heads.Add(missionId);

        var members = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var head in heads)
        {
            if (seen.Add(head))
                members.Add(head);
            foreach (var m in Reachable(head, next))
                if (seen.Add(m))
                    members.Add(m);
        }

        // 從鏈首往下走不到自己（資料異常）時，仍保證自己與往上找到的前置都在裡面。
        foreach (var m in seenUp)
            if (seen.Add(m))
                members.Add(m);

        return members;
    }

    /// <summary>往後整條鏈（不含自己），由近到遠。</summary>
    internal static List<uint> Reachable(uint missionId, Func<uint, IEnumerable<uint>> next)
    {
        var result = new List<uint>();
        var visited = new HashSet<uint> { missionId };
        var queue = new Queue<uint>();
        queue.Enqueue(missionId);
        while (queue.Count > 0)
        {
            foreach (var child in next(queue.Dequeue()))
            {
                if (child == 0 || !visited.Add(child))
                    continue;
                result.Add(child);
                queue.Enqueue(child);
            }
        }
        return result;
    }
}
