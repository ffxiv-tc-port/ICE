using System;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 宇宙工具經驗模式的「最近已經不可能拿到金星的任務先排到最後」：記錄與挑選。
/// </summary>
/// <remarks>
/// 契約：記錄只放記憶體、不寫設定檔，插件重載就清空；只在框架執行緒讀寫。
/// 本檔刻意不引用 Dalamud／遊戲／設定型別：離線探針直接連結這個檔編譯。
/// </remarks>
internal static class GoldUnreachableCooldown
{
    private static readonly Dictionary<uint, long> MarkedAtMs = new();

    /// <summary>記下「這個任務這一輪已經不可能拿到金星」。</summary>
    /// <returns>這次是不是新記上的（記之前不在冷卻中）——呼叫端只在新記上時寫記錄。</returns>
    internal static bool Mark(uint missionId, long nowMs, int minutes)
    {
        var wasCooling = IsCoolingDown(missionId, nowMs, minutes);
        MarkedAtMs[missionId] = nowMs;
        return !wasCooling;
    }

    /// <summary>這一輪拿到金星了：之前的記錄作廢。</summary>
    internal static bool Clear(uint missionId) => MarkedAtMs.Remove(missionId);

    internal static bool IsCoolingDown(uint missionId, long nowMs, int minutes)
        => minutes > 0
           && MarkedAtMs.TryGetValue(missionId, out var at)
           && nowMs - at < minutes * 60_000L;

    /// <summary>
    /// 依分數挑一個任務：先在不在冷卻中的候選裡挑；全部都在冷卻中時，
    /// <paramref name="allowCoolingPick"/> 為 true 才從冷卻中的挑（排到最後，不是移除），否則回 null 交給下一個分頁。
    /// </summary>
    /// <remarks>
    /// 同分取先出現的（嚴格大於才換），與原本的挑法相同；
    /// <paramref name="isCoolingDown"/> 恆回 false 時，結果與原本的挑法逐筆相同。
    /// </remarks>
    internal static uint? PickBest(IReadOnlyList<(uint Id, float Score)> candidates, Func<uint, bool> isCoolingDown, bool allowCoolingPick)
    {
        uint? best = null;
        uint? bestCooling = null;
        var bestScore = float.NegativeInfinity;
        var bestCoolingScore = float.NegativeInfinity;

        foreach (var (id, score) in candidates)
        {
            if (isCoolingDown(id))
            {
                if (score > bestCoolingScore)
                {
                    bestCoolingScore = score;
                    bestCooling = id;
                }
            }
            else if (score > bestScore)
            {
                bestScore = score;
                best = id;
            }
        }

        return best ?? (allowCoolingPick ? bestCooling : null);
    }
}
