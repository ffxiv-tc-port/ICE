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

            // 494、495：上游對這兩個任務給的是 AHFOLDER_ 前綴的「整包資料夾」匯出，
            // 不是單一 preset。匯入資料夾走的是 AutoHook 的 ImportPresetFolder（會建資料夾、
            // 重新配發 GUID、逐筆掛進去），而 ICE 目前的匯入路徑只呼叫
            // CreateAndSelectAnonymousPreset（單筆 preset）。要支援得先做整套資料夾匯入基建，
            // 本輪不做，所以維持停用。
            494, 495,

            // 543：preset 本身有（上游用的就是 542／544 那一段，三個任務共用同一串），
            // 但**台服的道具資料還沒上**：
            //   餌 47703、魚 47680 在 exd-tc/7.20 的 Item.csv 裡**列是存在的，Name 欄位是空字串**
            //   （＝保留給尚未實裝內容的佔位列，不是「查無此列」）。
            // 名稱是空的表示這兩個道具在台服還拿不到，換餌與魚種比對都會落空，所以維持停用。
            543,

            1003,
            // 1004, 1005,
            1006,
            // blacklisted mission ID
        };
    }
}
