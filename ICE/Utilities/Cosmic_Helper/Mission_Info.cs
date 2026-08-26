using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Utilities;

public static partial class CosmicHelper
{
    // 🔴 這裡原本是：
    //        public static CosmicInfo CurrentMissionInfo => SheetMissionDict[CurrentLunarMission];
    //    —— 一個零守衛的字典索引，而且被 11 個地方直接解參考。
    //
    //    為什麼直接索引一定會炸：SheetMissionDict 的鍵集合 **不是** 1..N，而是
    //    「WKSMissionUnit 裡 Name 不為空的那些 row」。對台服 7.20 實測（逐筆比對
    //    exd-tc/7.20/WKSMissionUnit.csv）＝ **只有 1..544**，被跳過的是 row 0 與
    //    row 545..1072（共 529 列，後者是第二顆星 Phaenna 的預留列，台服全部是空的）。
    //    而 CurrentLunarMission 在「沒有進行中的任務」時回 0 —— 遊戲端自己取消任務
    //    （例：被機甲行動抽中當駕駛員直接傳送走）就會走到這裡。
    //
    //    為什麼不改成回 null：CosmicInfo 是 class，回 null 只是把 KeyNotFoundException
    //    換成 NullReferenceException，呼叫端一樣沒有處理，症狀一模一樣（任務裡丟例外
    //    → 任務永遠不回傳 true → 逾時 → 佇列被中止 → 外掛自己停用）。
    //    為什麼不回一個「空任務」哨兵：那會把明確的失敗換成 **靜默的錯誤行為** ——
    //    Attributes 全 0 會讓釣魚/採集/製作流程「合法地」做出錯誤決定，比丟例外更難查。
    //
    //    所以改成只提供 Try 版本，強迫每個呼叫端表態；
    //    排程器裡的呼叫端統一用 SchedulerMain.CurrentMissionUnavailable() 收斂成
    //    「記一筆 → 清佇列 → 回到 IceState.Start 重新判斷狀態」。

    /// <summary>
    /// 取得目前進行中的任務資料。<b>任務隨時可能不存在</b>（還沒接、遊戲端自己取消了、
    /// 或是這個 row 不在 SheetMissionDict 裡），所以只有 Try 版本，沒有直接索引的屬性。
    /// </summary>
    public static bool TryGetCurrentMissionInfo([MaybeNullWhen(false)] out CosmicInfo info)
        => SheetMissionDict.TryGetValue(CurrentLunarMission, out info);

    /// <summary>目前有沒有一個「查得到資料」的進行中任務。</summary>
    public static bool HasCurrentMission => SheetMissionDict.ContainsKey(CurrentLunarMission);

    /// <summary>
    /// Gives the current mission that is active
    /// </summary>
    public static unsafe uint CurrentLunarMission
    {
        get
        {
            try
            {
                var manager = WKSManager.Instance();
                if (manager == null)
                    return 0; // or some default value

                return manager->CurrentMissionUnitRowId;
            }
            catch (AccessViolationException)
            {
                IceLogging.Error("We're currently getting access violations with this, so returning 0");
                return 0;
            }
            catch (Exception)
            {
                IceLogging.Error("Welp. Somehow not getting it still. Exception exit");
                return 0;
            }
        }
    }
    public static unsafe uint? CurrentBait => WKSManager.Instance()->FishingBait;
    public static unsafe uint CurrentLunarDevelopment => ExcelHelper.DevGrade.GetRow(WKSManager.Instance()->DevGrade).Unknown6;

    public static Dictionary<int, string> ExpDictionary = new()
    {
        { 1, "I" },
        { 2, "II" },
        { 3, "III" },
        { 4, "IV" },
        { 5, "V" }
    };

    // General use functions used across the codebase, specifically tied to cosmic related functions
    public static void OpenStellarMission()
    {
        if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var hud) && hud.IsAddonReady)
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) && missionInfo.IsAddonReady)
            {
                return;
            }
            else
            {
                if (EzThrottler.Throttle("Opening Stellar Missions"))
                {
                    IceLogging.Debug("Opening Mission Menu");
                    hud.Mission();
                }
            }
        }
    }

    public static void UpdateStateFlags()
    {
        // just a shorthand for me to be able to grab all of it, while also just snapshotting the mission we're currently running
        // ⚠️ 這個方法目前全 repo 零呼叫者（上游留下來的），但它的字典索引跟其他 11 處是同一顆雷，
        //    留著不修等於等下一個人接上它就中獎，所以照樣守。
        if (!TryGetCurrentMissionInfo(out var missionInfo))
            return;

        if (missionInfo.Attributes.HasFlag(MissionAttributes.Critical))
        {
            // TODO:
            // I really need to just add a collection point to the critical mission infomation
            // From here, grab the mission info
            // RedAlertCollectionPoint = infohere
        }
        if (missionInfo.Attributes.HasFlag(MissionAttributes.Craft))
            SchedulerMain.State |= IceState.Craft;
    }

    public unsafe static (int classScore, int cappedClassScore, int totalScores, uint classId) GetCosmicClassScores(bool useSelectedJob = false, uint jobId = 0)
    {
        int classScore = 0;
        int cappedClassScore = 0;
        int totalScores = 0;
        var wksManager = WKSManager.Instance();
        var currentMissionId = wksManager->CurrentMissionUnitRowId;

        uint classId;

        if (useSelectedJob)
        {
            // For UI that should respect C.SelectedJob
            classId = C.SelectedJob;
        }
        else
        {
            // For overlay that should show current/mission job
            if (currentMissionId > 0 && CosmicHelper.SheetMissionDict.TryGetValue(currentMissionId, out var missionInfo))
            {
                if (jobId != 0)
                    classId = jobId;
                else if (missionInfo.Jobs.Contains(Player.JobId))
                    classId = Player.JobId;
                else
                    classId = missionInfo.Jobs.First();
            }
            else if (CosmicHelper.CrafterJobList.Contains(Player.JobId) || CosmicHelper.GatheringJobList.Contains(Player.JobId))
                classId = Player.JobId;
            else
                classId = C.SelectedJob;
        }

        if (classId is >= 8 and <= 18)
        {
            var scores = wksManager->Scores;

            classScore = scores[(int)classId - 8];
            cappedClassScore = Math.Min(500_000, classScore);

            totalScores = 0;
            for (int i = 0; i < scores.Length; ++i)
                totalScores += Math.Min(500_000, scores[i]);
        }

        return (classScore, cappedClassScore, totalScores, classId);
    }
}
