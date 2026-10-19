using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Utilities;

public static partial class CosmicHelper
{
    // 🔴 刻意沒有直接索引的 CurrentMissionInfo 屬性：CurrentLunarMission 在「沒有進行中的任務」時回 0
    //    （遊戲端自己取消任務就會，例：被機甲行動抽中當駕駛員直接傳送走），而 SheetMissionDict 沒有 key 0。
    //    也不回 null：CosmicInfo 是 class，回 null 只是把 KeyNotFoundException 換成 NullReferenceException，
    //    呼叫端一樣沒有處理（任務裡丟例外 → 逾時 → 佇列被中止）。

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
    /// <summary>目前掛著的餌。<c>null</c> ＝這一輪讀不到 <c>WKSManager</c>（不在宇宙探索內容裡）。</summary>
    /// <remarks>
    /// 🔴 <c>WKSManager</c> 的 [StaticAddress] 槽位在宇宙探索內容以外是 null，直接解參考＝
    ///    攔不到的 AccessViolationException。判空理由同 <see cref="GetCosmicClassScores"/>。
    /// </remarks>
    public static unsafe uint? CurrentBait
    {
        get
        {
            var manager = WKSManager.Instance();
            return manager == null ? null : manager->FishingBait;
        }
    }

    /// <summary>目前的月面開發等級。讀不到 <c>WKSManager</c> 時回 0。</summary>
    /// <remarks>判空理由同 <see cref="CurrentBait"/>。目前全 repo 沒有呼叫端，補守衛是為了
    /// 避免下一個接上它的人中獎。</remarks>
    public static unsafe uint CurrentLunarDevelopment
    {
        get
        {
            var manager = WKSManager.Instance();
            return manager == null ? 0u : ExcelHelper.DevGrade.GetRow(manager->DevGrade).Unknown6;
        }
    }

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
        // ⚠️ 這個方法目前全 repo 零呼叫者（上游留下來的），但照樣守：
        //    任務隨時可能不存在。
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

        // 🔴 判空理由同 CosmicHandler.GetCosmicClassScores：WKSManager 的 [StaticAddress] 槽位
        //    在宇宙探索內容以外是 null，直接解參考是攔不到的 AccessViolationException。
        //    刻意只擋「讀原生記憶體」那兩段，底下決定 classId 的邏輯照跑（它只看
        //    C.SelectedJob / Player.JobId / SheetMissionDict，跟原生指標無關）——
        //    這樣呼叫端就算讀不到分數，拿到的職業 ID 仍然是對的，畫面不會跳去別的職業。
        var wksManager = WKSManager.Instance();
        uint currentMissionId = wksManager != null ? wksManager->CurrentMissionUnitRowId : 0u;

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

        if (wksManager != null && classId is >= 8 and <= 18)
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
