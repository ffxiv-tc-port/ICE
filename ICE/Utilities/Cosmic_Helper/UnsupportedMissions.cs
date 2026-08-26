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

            // 📌 2026-08-06 解除停用：479 / 481 / 482 / 484 / 486 / 487 / 489 / 490 / 491 / 492 /
            //    493 / 510 / 511 這 13 個的 preset 已從上游補進 FishingPresets.cs。
            //    （508、509 與 510、511 是 Dual Craft 任務——A Rank 的 508/509 本來就沒停用，
            //      Ex+ Rank 的 510/511 這次一起放行。）

            // 📌 2026-08-07 解除停用：495 / 543。
            //
            //    495：上游給的是 AHFOLDER_「整包資料夾」匯出（5 個用 PresetToSwap 互指的 preset），
            //    而 ICE 以前的匯入路徑只會呼叫單筆的 CreateAndSelectAnonymousPreset。
            //    現在 AutoHook 已開出資料夾匯入 IPC（CreateAndSelectAnonymousFolder +
            //    GetFolderImportApiVersion），ICE 端改走 Task_ExecuteMission 的 AHFOLDER 分支。
            //    🔴 它的可用性**取決於安裝的 AutoHook 版本**，所以那條判定不寫在這裡：
            //       MissionSupport.NeedsMissingFolderImport 會在 AutoHook 太舊時回報
            //       UnsupportedReason.RequiresAutoHookFolderImport（而且是資料驅動、不寫死 ID）。
            //       寫死在這份清單裡的話，使用者更新 AutoHook 之後也解不開。
            //    495 交件走分數路徑（WKSMissionText 121 → 純 Fish，銀 8000／金 20000 是真的分數），
            //    所以資料夾匯入一通它就能跑完整個流程。
            //

                        // ✅ 2026-08-07 解除停用：缺的「交件依據」補上了。
            //    使用者找到日文攻略：本任務要「用同一種餌釣到 6 種」（弱振 2＋強震 4），
            //    而資料夾 preset 的 ListOfFish 去重正好 6 筆、6 個 id 在台服也都已實裝 —— 三方印證。
            //    已在 FishingPresets.cs 補上 RequiredFish + AmountRequired=6 + UniqueFish=true。

            //    543：以前這裡寫「餌 47703／魚 47680 在台服是空名佔位列」——**那個歸因是錯的**。
            //    47703／47680 是**上游那段共用 preset**（「Red Alerts」，542／543／544 共用）
            //    鎖定的 id，跟台服 543 的實際需求無關；台服 543 要的是
            //    45939「落水的無人機」×3，餌是任務發的 45967「淡水萬能機械臂」，兩者在台服都有名字。
            //    真正的成因只是**沒有人補 543 的 preset**（它從來就是 `new FishingTools { }` 空殼）。
            //    現在已依 542／544 的同構關係補上，見 FishingPresets.cs 該筆的註解。

            1003,
            // 1004, 1005,
            1006,
            // blacklisted mission ID
        };
    }
}
