using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Utilities
{
    internal class CosmicHandler
    {
        internal static HashSet<string> commenceStrings =
        [
            "Commence selected mission?",                     // English
            "Ausgewählte Mission wird gestartet.Fortfahren?", // German
            "Commencer la mission sélectionnée ?",            // French
            "選択したミッションを開始します。よろしいですか？",      // Japanese
            "确定要开始此任务吗？",                              // Simplified Chinese
            "確定要開始此任務嗎？",                              // Traditional Chinese
            "선택한 임무를 시작하시겠습니까?",                    // Korean
        ];

        internal static HashSet<string> abandonStrings = 
        [
            "Abandon mission?",                                            // English
            "Aktuelle Mission abbrechen?",                                 // German
            "Êtes-vous sûr de vouloir abandonner la mission en cours ?",   // French - Masc
            "Êtes-vous sûre de vouloir abandonner la mission en cours ?",  // French - Fem
            "受注中のミッションを破棄します。\rよろしいですか？",                 // Japanese
            "确定要放弃已领取的任务吗？",                                      // Simplified Chinese
            "수락한 임무를 포기하시겠습니까?",                                 // Korean
            "確定要放棄已領取的任務嗎？"                                       // Traditional Chinese abandon
        ];

        internal unsafe static bool IsMissionTimedOut()
        {
            // GetAtkTextNode 取不到節點會回 null，直接解參考就是 AVE。
            // 取不到時一律回 false(視為「未逾時」)：寧可本次不動作，
            // 也不要把「不知道」當成「已確認逾時」。
            var timeoutNode = AddonHelper.GetAtkTextNode("WKSMissionInfomation", 23);
            return timeoutNode != null && timeoutNode->IsVisible();
        }
        internal unsafe static (int classScore, int cappedClassScore, int totalScores, uint classId) GetCosmicClassScores()
        {
            int classScore = 0;
            int cappedClassScore = 0;
            int totalScores = 0;

            // 🔴 WKSManager 是 [StaticAddress]：**位址**解析失敗會擲例外，但解析成功之後
            //    「槽位裡放的那個指標」在宇宙探索內容以外就是 null。直接 -> 解參考＝
            //    AccessViolationException，而 AVE 在 .NET Core 是 corrupted-state exception，
            //    try/catch 攔不到（會直接把遊戲帶走）。判空的寫法比照
            //    Task_TurninMission.ScoreCheck。
            //    讀不到就回全 0 —— 呼叫端拿到的分數是「還沒賺到」，不會誤判成已達標。
            var wksManager = WKSManager.Instance();
            uint currentMissionId = wksManager != null ? wksManager->CurrentMissionUnitRowId : 0u;

            uint classId;
            /*
             * Unsure how to handle this right now... doesn't work with dual classes. Might need to just generally go back and figure out how to view this
            if (currentMissionId > 0 &&
                CosmicHelper.SheetMissionDict.TryGetValue(currentMissionId, out var missionInfo))
                classId = missionInfo.JobId;
            else
            */
            classId = Player.JobId;

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

        internal unsafe static (int currentScore, int silverScore, int GoldScore) CurrentScore()
        {
            int currentScore = 0;
            int silverScore = 0;
            int GoldScore = 0;

            return (currentScore, silverScore, GoldScore);
        }


    }
}
