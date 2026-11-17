using ICE.Utilities.Cosmic_Helper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Utilities;

public static partial class CosmicHelper
{
    // WKSHud 的 Mission 鈕開關兩用：下面兩個只給「已確認任務視窗沒開」的呼叫端。
    // 沒帶 prefix 的 log 不能搬進來（前綴取自呼叫端方法名），所以其餘按這顆鈕的地方留在原處。

    /// <summary>計分檢查、交件時任務視窗沒顯示就重開；節流 key 與 Task_Gather 分解段共用（間隔不同）。</summary>
    internal static void ReopenMissionHudForScore()
    {
        if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud))
        {
            if (EzThrottler.Throttle("Opening the moon hud", 1000))
            {
                moonHud.Mission();
                IceLogging.Info("Hud wasn't visible. Opening it", "[Score Check]");
            }
        }
    }

    /// <summary>等 Artisan 製作時把任務資訊視窗開著（看分數用）。</summary>
    internal static void OpenMissionInfoWhileCrafting()
    {
        if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud))
        {
            if (!AddonHelper.IsAddonActive("WKSMissionInfomation"))
            {
                if (EzThrottler.Throttle("Opening mission scoring info"))
                {
                    moonHud.Mission();
                }
            }
        }
    }
}
