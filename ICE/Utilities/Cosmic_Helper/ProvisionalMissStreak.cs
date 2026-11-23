using System;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 限定任務「只刷未金星」：同一任務本場連續沒拿到金星的次數。
/// </summary>
/// <remarks>
/// 契約：只放記憶體、不寫設定檔，插件重載就清空；只在框架執行緒讀寫。
/// 本檔刻意不引用 Dalamud／遊戲／設定型別：離線探針直接連結這個檔編譯。
/// </remarks>
internal static class ProvisionalMissStreak
{
    private static readonly Dictionary<uint, int> Misses = new();
    private static string? _artisanVersion;

    internal static int Count => Misses.Count;

    /// <returns>這次之後的連續次數。</returns>
    internal static int RecordMiss(uint missionId)
    {
        var n = Misses.GetValueOrDefault(missionId) + 1;
        Misses[missionId] = n;
        return n;
    }

    /// <returns>之前有沒有連續記錄（呼叫端只在有時寫記錄）。</returns>
    internal static bool RecordGold(uint missionId) => Misses.Remove(missionId);

    internal static int Get(uint missionId) => Misses.GetValueOrDefault(missionId);

    /// <summary>
    /// 記下目前 Artisan 版本；和上次記的不同就清空全部計數（製作能力變了，舊的失手不再算數）。
    /// </summary>
    /// <param name="version">null＝讀不到（沒載入）：不清、也不更新記下的版本。</param>
    /// <returns>被清掉的任務數；沒換版回 0。</returns>
    internal static int SyncArtisanVersion(string? version, out string? previous)
    {
        previous = _artisanVersion;
        if (version == null)
            return 0;
        if (_artisanVersion == null || _artisanVersion == version)
        {
            _artisanVersion = version;
            return 0;
        }

        _artisanVersion = version;
        var cleared = Misses.Count;
        Misses.Clear();
        return cleared;
    }

    /// <summary>測試用：回到剛載入的狀態。</summary>
    internal static void ResetAll()
    {
        Misses.Clear();
        _artisanVersion = null;
    }
}
