using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using System.Runtime.InteropServices;

namespace ICE.Utilities;

[StructLayout(LayoutKind.Explicit)]
unsafe struct WKSManagerCustom
{
    [FieldOffset(0xC55)] public fixed byte MissionCompletionFlags[136];
    [FieldOffset(0xCDD)] public fixed byte MissionGoldFlags[136];

    public bool IsMissionCompleted(uint missionUnitId)
    {
        var group = (byte)(missionUnitId >> 3);
        var mask = 1 << ((int)missionUnitId & 7);
        return (mask & MissionCompletionFlags[group]) != 0;
    }

    public bool IsMissionGolded(uint missionUnitId)
    {
        var group = (byte)(missionUnitId >> 3);
        var mask = 1 << ((int)missionUnitId & 7);
        return (mask & MissionGoldFlags[group]) != 0;
    }
}

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
    /// </summary>
    internal static unsafe (bool Completed, bool Gold) GetStatus(uint missionId)
    {
        var managerPtr = WKSManager.Instance();
        if (managerPtr == null)
            return (false, false);

        var manager = (WKSManagerCustom*)managerPtr;
        return (manager->IsMissionCompleted(missionId), manager->IsMissionGolded(missionId));
    }
}
