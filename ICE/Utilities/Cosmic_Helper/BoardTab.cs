using ICE.Enums;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>任務板（WKSMission）的三個分頁。</summary>
internal enum BoardTab : byte
{
    Basic,
    Critical,
    Provisional,
}

/// <summary>
/// 判斷任務板讀出來的某一列是否真的屬於目前分頁。
/// </summary>
/// <remarks>
/// 契約：剛切分頁時，讀出的清單最後一筆可能是上一個分頁殘留的任務（別的職業或別的分類），選了會接不到。
/// ICE 依資料表算的分類（緊急／臨時／一般）與遊戲分頁一致；一般分頁另外只列目前職業（含雙職業）。
/// 純函式、不碰遊戲狀態，任何執行緒都可呼叫。
/// </remarks>
internal static class BoardTabFilter
{
    private const MissionAttributes ProvisionalFlags =
        MissionAttributes.ProvisionalTimed | MissionAttributes.ProvisionalWeather | MissionAttributes.ProvisionalSequential;

    internal static BoardTab TabOf(MissionAttributes attributes)
    {
        if (attributes.HasFlag(MissionAttributes.Critical))
            return BoardTab.Critical;
        if ((attributes & ProvisionalFlags) != 0)
            return BoardTab.Provisional;
        return BoardTab.Basic;
    }

    /// <summary>這一列放在 <paramref name="tab"/> 分頁上是否合理；不合理＝上一個分頁殘留，不要選。</summary>
    internal static bool Belongs(BoardTab tab, MissionAttributes attributes, IReadOnlyCollection<uint> jobs, uint currentJob)
    {
        if (TabOf(attributes) != tab)
            return false;
        // 緊急與臨時分頁本來就會列出別的職業的任務，只有一般分頁依職業分。
        return tab != BoardTab.Basic || Contains(jobs, currentJob);
    }

    private static bool Contains(IReadOnlyCollection<uint> jobs, uint job)
    {
        foreach (var j in jobs)
        {
            if (j == job)
                return true;
        }
        return false;
    }
}
