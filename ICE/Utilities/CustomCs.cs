using FFXIVClientStructs.FFXIV.Client.Game.WKS;

namespace ICE.Utilities;

/// <summary>
/// 任務完成／金章旗標的共用讀取點。主視窗任務清單（<c>modeSelect_TableInfo</c> 的「✓」欄）
/// 與疊加層（<c>OverlayWindow</c>）要顯示同一組狀態，資料只在這裡讀一次，避免兩邊的指標
/// 運算各自維護一份、日後漂移出不一致的結果。
/// </summary>
internal static class MissionStatusHelper
{
    /// <summary>
    /// 讀取任務的完成／金章旗標。<c>WKSManager</c> 拿不到指標時視為「未完成、非金章」
    /// 而不是丟例外——這與呼叫端原本各自的 <c>managerPtr == null</c> 提前 return 行為一致。
    /// 完成／金章旗標讀取直接用 CS pin 原生的 <c>WKSManager.IsMissionCompleted</c>／
    /// <c>IsMissionGolded</c>（偏移 0xC55／0xCDD，與舊版自疊的 WKSManagerCustom 完全相同）。
    /// </summary>
    internal static unsafe (bool Completed, bool Gold) GetStatus(uint missionId)
    {
        var managerPtr = WKSManager.Instance();
        if (managerPtr == null)
            return (false, false);

        return (managerPtr->IsMissionCompleted(missionId), managerPtr->IsMissionGolded(missionId));
    }
}
