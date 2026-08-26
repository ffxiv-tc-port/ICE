using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_AbandonMission
    {
        public static void Enqueue()
        {
            // AbandonMission 要等遊戲跑完「回報 → 確認視窗 → 任務欄清空」，還可能卡在區域切換，
            // 30 秒的 NeoTaskManager 預設逾時 + AbortOnTimeout 會直接把整個佇列清掉。
            // 2026-08-03 實機就是這樣：AbandonMission 每個 tick 丟例外 37 次後逾時 → 佇列被中止 → 外掛自己停用。
            P.TaskManager.Enqueue(() => AbandonMission(), "Abandoning the current mission", Utils.TaskConfig);
            P.TaskManager.Enqueue(() => Task_TurninMission.JobSwapCheck(), "Checking to see if we need to swap jobs", Utils.TaskConfig);
            P.TaskManager.Enqueue(() => Task_TurninMission.GoldCheck(), "Checking post mission state + gold state condition", Utils.TaskConfig);
            P.TaskManager.Enqueue(() => Task_TurninMission.CommandCheck(), "Checking for post mission commands", Utils.TaskConfig);
            if (C.DelayGrabMission)
                P.TaskManager.EnqueueDelay(C.DelayIncrease);
        }

        public static bool WasAbandoned = false;

        public static bool? AbandonMission()
        {
            string tag = "Abandon Mission";

            if (CosmicHelper.CurrentLunarMission == 0)
            {
                if (WasAbandoned)
                {
                    IceLogging.Debug("Mission was abandoned");
                    P.MissionTimer.AbandonMission();
                }
                else
                {
                    Task_TurninMission.UpdateScoreInfo();
                    var duration = P.MissionTimer.CompleteMission();
                    Mission_Settings.TurninState = TurninState.None;

                    // Log the results
                    // 🔴 這裡原本只用 TryGetValue 守住 MissionConfig，同一行的 SheetMissionDict 卻是直接索引。
                    // 兩個字典的鍵集合不一樣：MissionConfig 會被 MissionTimer 按需補上（含 0），
                    // 而 SheetMissionDict 在建表時就把 Name 為空的 row 0 跳過了 —— 所以
                    // PreviousMissionId 還是初始值 0 時，TryGetValue 會過、SheetMissionDict[0] 直接炸。
                    // 例外讓這個任務永遠不回傳 true，接著就是逾時 → 佇列中止 → 外掛停用。
                    var prevId = Task_TurninMission.PreviousMissionId;
                    if (C.MissionConfig.TryGetValue(prevId, out var config) &&
                        CosmicHelper.SheetMissionDict.TryGetValue(prevId, out var prevMission))
                    {
                        IceLogging.Info($"Mission [{prevId}] [{prevMission.Name}] completed in {duration:mm\\:ss\\.ff} | Best: {TimeSpan.FromSeconds(config.BestTime):mm\\:ss\\.ff} | Avg: {TimeSpan.FromSeconds(config.AverageTime):mm\\:ss\\.ff}", $"{tag} [Mission Timer]");
                    }
                    else
                    {
                        IceLogging.Info($"任務結束但沒有可用的前一個任務資訊（PreviousMissionId = {prevId}），跳過計時統計。" +
                                        "（常見原因：遊戲端自己把任務取消了，例如被機甲行動傳送走。）", $"{tag} [Mission Timer]");
                    }
                }

                WasAbandoned = false;

                if (P.AutoHook.Installed)
                {
                    P.AutoHook.DeleteAllAnonymousPresets();
                }

                IceLogging.Info("Current mission is 0, checking to see where we need to be now", "[Abandon Mission]");
                return true;
            }
            else
            {
                Task_TurninMission.PreviousMissionId = CosmicHelper.CurrentLunarMission;
                if (EzThrottler.Throttle("Score Check Update"))
                    Task_TurninMission.ScoreCheck();

                if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
                {
                    if (CosmicHandler.abandonStrings.Any(s => string.Equals(NormalizeWhitespace(select.Text), NormalizeWhitespace(s), StringComparison.OrdinalIgnoreCase)) || !C.RejectUnknownYesno)
                    {
                        if (EzThrottler.Throttle("Selecting Yes, mission is properly abandoning"))
                        {
                            IceLogging.Debug($"Expected abandon mission text... abandoning mission", "[Abandon Mission]");
                            select.Yes();
                            if (Mission_Settings.StopAfterCurrent)
                            {
                                SchedulerMain.State = IceState.Idle;
                                P.TaskManager.Tasks.Clear();
                            }
                            else
                            {
                                SchedulerMain.State = IceState.Start;
                            }
                        }
                    }
                    else
                    {
                        IceLogging.Debug($"Actual text: '{select.Text}'");
                        IceLogging.Debug($"Actual text length: {select.Text.Length}");
                        IceLogging.Debug($"Trimmed text: '{select.Text.Trim()}'");
                        IceLogging.Debug($"Trimmed length: {select.Text.Trim().Length}");

                        if (EzThrottler.Throttle("Unexpected Abandon Window..."))
                        {
                            var actualText = select.Text.Trim();
                            var expectedFrench = "Êtes-vous sûre de vouloir abandonner la mission en cours ?";

                            // Debug the ACTUAL text character by character
                            IceLogging.Error("=== ACTUAL TEXT BREAKDOWN ===");
                            for (int i = 0; i < actualText.Length; i++)
                            {
                                IceLogging.Error($"Actual char {i}: '{actualText[i]}' (Unicode: {(int)actualText[i]})");
                            }

                            // Debug the EXPECTED text character by character
                            IceLogging.Error("=== EXPECTED TEXT BREAKDOWN ===");
                            IceLogging.Error($"Expected: '{expectedFrench}'");
                            IceLogging.Error($"Expected length: {expectedFrench.Length}");
                            for (int i = 0; i < expectedFrench.Length; i++)
                            {
                                IceLogging.Error($"Expected char {i}: '{expectedFrench[i]}' (Unicode: {(int)expectedFrench[i]})");
                            }

                            IceLogging.Error($"Unexpected abandon window??? {select.Text}", "[Abandon Mission]");
                            select.No();
                        }
                    }
                }
                else if(GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var addon) && addon.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Trying To Turnin/Abandon", 1000))

                    if (Player.JobId == 18 && Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Gathering])
                    {
                        if (EzThrottler.Throttle("Stop fishing so we can turn in this mission!", 2000))
                            Task_DualClass.StopFishing();

                        return false;
                    }

                    if (EzThrottler.Throttle("Attempt to turnin", 500))
                    {
                        addon.Report();
                    }
                    else if (EzThrottler.Throttle("Telling it to abandon the mission", 500))
                    {
                        IceLogging.Debug("Attempting to abandon.", "[Abandoning Mission]");
                        addon.Abandon();
                        WasAbandoned = true;
                    }
                }
                else if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var SpaceHud) && SpaceHud.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Opening the current mission info Ui"))
                    {
                        IceLogging.Debug("WKSMissionInformation missing. Attempting opening.", "[Abandoning Mission]");
                        SpaceHud.Mission();
                    }
                }
            }

            return false;
        }

        private static string NormalizeWhitespace(string text)
        {
            return text.Trim()
                       .Replace('\u00A0', ' ')  // Non-breaking space to regular space
                       .Replace('\u2009', ' ')  // Thin space to regular space
                       .Replace('\u202F', ' ')  // Narrow no-break space to regular space
                       .Replace('\u3000', ' '); // Ideographic space to regular space
        }
    }
}
