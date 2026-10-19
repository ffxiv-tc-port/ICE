using System.Collections.Generic;

namespace ICE.Utilities.Cosmic
{
    /// <summary>
    /// IDs of missions that should be disabled and shown as unsupported.
    /// </summary>
    public static class UnsupportedMissions
    {
        public static readonly HashSet<uint> Ids = new HashSet<uint>
        {
            0,

            // 📌 479 / 481 / 482 / 484 / 486 / 487 / 489 / 490 / 491 / 492 / 493 / 510 / 511 不在這份清單裡：
            //    它們的 preset 已經在 FishingPresets.cs。

            // 495 不在這份清單裡：它的 preset 是 AHFOLDER_「整包資料夾」匯出，走 Task_ExecuteMission 的 AHFOLDER 分支。
            // 🔴 它的可用性**取決於安裝的 AutoHook 版本**，所以那條判定不寫在這裡：
            //    MissionSupport.NeedsMissingFolderImport 會在 AutoHook 太舊時回報
            //    UnsupportedReason.RequiresAutoHookFolderImport（資料驅動、不寫死 ID）。
            //    寫死在這份清單裡的話，使用者更新 AutoHook 之後也解不開。


            // 543 不在這份清單裡：它的 preset 已依 542／544 的同構關係補上（見 FishingPresets.cs 該筆的註解）。

            1003,
            // 1004, 1005,
            1006,
            // blacklisted mission ID
        };
    }
}
