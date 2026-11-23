using System;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 限定任務模式「只刷勾選中還沒金星的任務」：挑選順序與「沒事可做」判定。
/// </summary>
/// <remarks>
/// 本檔刻意不引用 Dalamud／遊戲／設定型別：離線探針直接連結這個檔編譯。
/// </remarks>
internal static class ProvisionalGoldPlan
{
    /// <param name="Order">要依序到任務清單上找的任務（先未金星、再為了解鎖後續而保留的已金星前置）。</param>
    /// <param name="Pending">候選裡還沒金星的數量；讀不到金星狀態時＝候選數。</param>
    /// <param name="KeptPrerequisites">已金星、但後續任務也在候選裡且還沒金星而保留的前置數量。</param>
    /// <param name="GoldKnown">false＝讀不到金星狀態，<paramref name="Order"/> 就是原候選。</param>
    internal readonly record struct Result(List<uint> Order, int Pending, int KeptPrerequisites, bool GoldKnown)
    {
        /// <summary>沒有勾選的候選，或勾選的全都金星了（不需要解鎖任何後續）。</summary>
        internal bool NothingLeft => Order.Count == 0;
    }

    /// <summary>
    /// 拿掉「建設階段還沒到、遊戲不會派」的任務，原順序不變；被拿掉的依序加進 <paramref name="locked"/>。
    /// 要在 <see cref="Build"/> 之前做：拿掉的任務不算待做，也不會讓它的已金星前置被保留。
    /// </summary>
    /// <param name="isLocked">只對「確定還沒開放」回 true；判不出來要回 false。</param>
    internal static List<uint> WithoutLocked(IReadOnlyList<uint> candidates, Func<uint, bool> isLocked, List<uint> locked)
    {
        var kept = new List<uint>(candidates.Count);
        foreach (var id in candidates)
        {
            if (isLocked(id))
            {
                if (!locked.Contains(id))
                    locked.Add(id);
            }
            else
                kept.Add(id);
        }
        return kept;
    }

    /// <param name="candidates">已勾選、在本區、ICE 跑得動的限定任務，照既有優先序排好。</param>
    /// <param name="isGolded">null＝讀不到金星狀態：不過濾、原序回傳。只會被問到 <paramref name="candidates"/> 裡的任務。</param>
    /// <param name="followUpsRecursive">任務往後整條連續任務鏈（不含自己）。</param>
    internal static Result Build(IReadOnlyList<uint> candidates, Func<uint, bool>? isGolded, Func<uint, IEnumerable<uint>> followUpsRecursive)
    {
        if (isGolded == null)
            return new Result(new List<uint>(candidates), candidates.Count, 0, false);

        var inPool = new HashSet<uint>(candidates);
        var seen = new HashSet<uint>();
        var pending = new List<uint>();
        var golded = new List<uint>();

        foreach (var id in candidates)
        {
            if (!seen.Add(id))
                continue;
            if (isGolded(id))
                golded.Add(id);
            else
                pending.Add(id);
        }

        // 連續任務的後續不是永久解鎖：後續還沒金星時，已金星的前置仍要跑，但排在所有未金星任務之後。
        var order = new List<uint>(pending);
        var kept = 0;
        foreach (var id in golded)
        {
            foreach (var followUp in followUpsRecursive(id))
            {
                if (inPool.Contains(followUp) && !isGolded(followUp))
                {
                    order.Add(id);
                    kept++;
                    break;
                }
            }
        }

        return new Result(order, pending.Count, kept, true);
    }
}
