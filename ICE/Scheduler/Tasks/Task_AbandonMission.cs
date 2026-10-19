using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;
using ICE.IPC;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_AbandonMission
    {
        public static void Enqueue()
        {
            // AbandonMission 要等遊戲跑完「回報 → 確認視窗 → 任務欄清空」，還可能卡在區域切換，
            // 30 秒的 NeoTaskManager 預設逾時 + AbortOnTimeout 會直接把整個佇列清掉。
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
                    // 🔴 兩個字典的鍵集合不一樣（MissionConfig 會被 MissionTimer 按需補上，SheetMissionDict 建表時跳過 Name 為空的列），
                    //    所以兩邊都要 TryGetValue：直接索引擲例外會讓這個任務永遠不回傳 true（逾時 → 佇列中止）。
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
                Task_CheckScore.ResetMedalCheckLog();

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
                // 下面會先試「回報結果」；交件成功時 UpdateScoreInfo 拿這裡取樣的分數當基準。
                if (EzThrottler.Throttle("Score Check Update"))
                    Task_TurninMission.RefreshPreviousScore();

                if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
                {
                    // 🔴 守衛順序：IsHeld → 讀文字 → MayPress → 按。按過（確定或取消）而且還沒觀察到它收掉的那幾幀連文字都不讀；
                    //    讀到 U+FFFD 也代表視窗記憶體正在變動，同樣這一幀不碰（方法照舊 return false 下一幀再來）。
                    //    刻意留在這個分支裡，不讓 else-if 鏈掉進下面 WKSMissionInfomation 那一支去按回報／放棄。
                    //    reroll 鏈的接任務確認框（文字不符 abandonStrings）也會落進這裡：GrabMission 按下確定那一次已經被守衛記下。
                    var promptText = YesnoPressGuard.IsHeld(select) ? null : select.Text;
                    if (promptText == null || AddonPressGuard.IsTextCorrupt("SelectYesno", promptText))
                    {
                        // 這一幀不碰。
                    }
                    else if (CosmicHandler.abandonStrings.Any(s => string.Equals(NormalizeWhitespace(promptText), NormalizeWhitespace(s), StringComparison.OrdinalIgnoreCase)) || !C.RejectUnknownYesno)
                    {
                        // 🔴 YesnoPressGuard 一定要放在條件式的最後（它有副作用：記下這一次按壓），而且「按下去 + 狀態轉移」
                        //    整組都掛在同一個條件式底下 —— 守衛擋下來時不可能讓 Tasks.Clear()／State 在沒真的按下的情況下被執行。
                        //    本任務回 true 的條件是 CurrentLunarMission 變成 0，那需要幾幀**離線證明不了**，可能落在確認框關閉中的那幾幀；
                        //    守衛認的是視窗位址而不是 key，這裡記下的按壓正是下游那道守衛唯一的判斷依據。
                        if (EzThrottler.Throttle("Selecting Yes, mission is properly abandoning")
                            && YesnoPressGuard.MayPress("放棄任務：放棄確認", select))
                        {
                            IceLogging.Debug($"Expected abandon mission text... abandoning mission", "[Abandon Mission]");
                            select.Yes();
                            if (Mission_Settings.StopAfterCurrent)
                            {
                                SchedulerMain.State = IceState.Idle;
                                TataruPraiseIPC.NotifyStopped("使用者選了跑完這輪就停（放棄任務之後）");
                                P.TaskManager.Tasks.Clear();
                            }
                            else if (SchedulerMain.State != IceState.Idle)
                            {
                                // 🔴 這裡不能無條件寫 Start：偵錯視窗的「Abandon Mission」按鈕會在 State 還是 Idle 的時候直接 Enqueue 這串任務，
                                //    無條件回 Start 等於「按一下放棄任務就把整個 ICE 啟動起來」。排程本來就在跑時才需要回 Start 去接下一個任務。
                                //    （不走 SchedulerMain.AbortToStateCheck()：這條是正常續行，後面排的任務還要繼續跑，不可以清佇列。）
                                SchedulerMain.State = IceState.Start;
                            }
                        }
                    }
                    else
                    {
                        // 🔴 認不出來的確認框維持按 No，並留一行 Error 說明是什麼視窗。刻意不採用「不碰這個視窗、等它自己關掉」的做法
                        //    （那是行為變更）。真的遇到沒涵蓋的文字就去擴充 NormalizeWhitespace() 的替換表。
                        // 同一扇 SelectYesno 的第二種按法（取消）也要過同一個守衛，才擋得住跨呼叫點接力重按。
                        if (EzThrottler.Throttle("Unexpected Abandon Window...")
                            && YesnoPressGuard.MayPress("放棄任務：關閉未預期的確認框", select))
                        {
                            IceLogging.Error($"Unexpected abandon window??? {promptText} [len={promptText.Length}]", "[Abandon Mission]");
                            select.No();
                        }
                    }
                }
                else if(GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var addon) && addon.IsAddonReady)
                {
                    // ⚠️ 這個 Throttle 只 gate「漁夫先收竿」那一整塊，不 gate 下面的 Report/Abandon（大括號標出真正的範圍）。
                    // ⚠️ 刻意不把這個 Throttle 刪掉（讓漁夫判斷變成每個 tick 都檢查，像 Task_TurninMission 裡那個區塊）：
                    //    那會讓「還在釣魚時」完全不能回報／放棄，只能等 StopFishing() 生效。萬一收竿沒生效，這串任務
                    //    用的是 Utils.TaskConfig（30 分鐘、abortOnTimeout: false），失敗形式會是「安靜地卡住半小時」。
                    //    要改成那樣得先有實機證據。
                    if (EzThrottler.Throttle("Trying To Turnin/Abandon", 1000))
                    {
                        if (Player.JobId == 18 && Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Gathering])
                        {
                            if (EzThrottler.Throttle("Stop fishing so we can turn in this mission!", 2000))
                                Task_DualClass.StopFishing();

                            return false;
                        }
                    }

                    // 回報／放棄兩顆終結鈕共用 AddonPressGuard.WksMissionExitPressKey。
                    // ⚠️ 不能改成把 WKSMissionInfomation 放進 AddonPressGuard.SingleAnswerAddons：
                    //    那會把同一扇窗的「星體分解」（Task_Gather）也併進來，而那顆按下之後窗不關。
                    // 🔴 順序：IsHeld → 讀鈕 → TryBeginPress → 按。被擋的那幾幀連 GetComponentButtonById
                    //    都不呼叫（那是去走正在關閉的視窗的節點樹），與上面 SelectYesno 那一段同一個形狀。
                    bool reportPressable = false, abandonPressable = false;
                    if (!AddonPressGuard.IsHeld("WKSMissionInfomation", addon, AddonPressGuard.WksMissionExitPressKey))
                    {
                        unsafe
                        {
                            var reportButton = addon.ReportResultsButton;
                            reportPressable = GenericHelpers.IsComponentEnabled(reportButton)
                                              && GenericHelpers.IsComponentVisible(&reportButton->AtkComponentBase);

                            var abandonButton = addon.AbandonMissionButton;
                            abandonPressable = GenericHelpers.IsComponentEnabled(abandonButton)
                                               && GenericHelpers.IsComponentVisible(&abandonButton->AtkComponentBase);
                        }
                    }

                    if (reportPressable
                        && EzThrottler.Throttle("Attempt to turnin", 500)
                        && AddonPressGuard.TryBeginPress("放棄任務：回報結果", "WKSMissionInfomation", addon, AddonPressGuard.WksMissionExitPressKey))
                    {
                        addon.Report();
                    }
                    else if (abandonPressable
                        && EzThrottler.Throttle("Telling it to abandon the mission", 500)
                        && AddonPressGuard.TryBeginPress("放棄任務：放棄任務", "WKSMissionInfomation", addon, AddonPressGuard.WksMissionExitPressKey))
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
