using System;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 「距離優先」：在既有規則已經認定差不多的候選裡，改接離目前位置近的任務。
/// </summary>
/// <remarks>
/// 契約：只在呼叫端交進來的「同級」候選之間換，不自己判斷誰同級；距離未知（回 null）的任務不會被換上，
/// 原本那個距離未知就不換。本檔刻意不引用 Dalamud／遊戲／設定型別：離線探針直接連結這個檔編譯。
/// </remarks>
internal static class NearbyMissionPick
{
    /// <summary>近不到這麼多就不換：採集入口與釣點都是估計值，差幾米不值得改變原本的挑法。</summary>
    internal const float MinSavedMeters = 10f;

    /// <summary>
    /// 宇宙工具經驗分數算不算「差不多」：最高分 ≤ 0（各類經驗都滿了）時只認同分，
    /// 否則分數不低於最高分的 (100 − <paramref name="tolerancePercent"/>)%。
    /// </summary>
    internal static bool IsNearTie(float score, float best, int tolerancePercent)
        => best <= 0f
            ? score >= best
            : score >= best * (1f - Math.Clamp(tolerancePercent, 0, 100) / 100f);

    /// <summary>
    /// 在 <paramref name="equivalents"/> 裡找比 <paramref name="current"/> 近至少 <see cref="MinSavedMeters"/> 的最近一個；
    /// 同距離取先出現的。找不到就回 false，<paramref name="nearer"/>＝<paramref name="current"/>。
    /// </summary>
    internal static bool TryPickNearer(uint current, IEnumerable<uint> equivalents, Func<uint, float?> distance,
                                       out uint nearer, out float currentMeters, out float nearerMeters)
    {
        nearer = current;
        currentMeters = nearerMeters = 0f;
        if (distance(current) is not float from)
            return false;

        currentMeters = nearerMeters = from;
        foreach (var id in equivalents)
        {
            if (id == current || distance(id) is not float d || d >= nearerMeters)
                continue;
            nearer = id;
            nearerMeters = d;
        }

        if (nearer == current || currentMeters - nearerMeters < MinSavedMeters)
        {
            nearer = current;
            nearerMeters = currentMeters;
            return false;
        }
        return true;
    }
}
