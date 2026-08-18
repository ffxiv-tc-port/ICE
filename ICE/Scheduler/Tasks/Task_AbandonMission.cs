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
                            else if (SchedulerMain.State != IceState.Idle)
                            {
                                // 🔴 這裡不能無條件寫 Start：偵錯視窗的「Abandon Mission」按鈕
                                //    會在 State 還是 Idle 的時候直接 Enqueue 這串任務，放棄成功之後
                                //    無條件回 Start 等於「按一下放棄任務就把整個 ICE 啟動起來」。
                                //    排程本來就在跑時才需要回 Start 去接下一個任務。
                                //    （不走 SchedulerMain.AbortToStateCheck()：這條是正常續行，
                                //     後面排的任務還要繼續跑，不可以清佇列。）
                                SchedulerMain.State = IceState.Start;
                            }
                        }
                    }
                    else
                    {
                        // 🔴 這裡原本還有一整段作者查法文譯文用的逐字元 dump：把 select.Text 與一句
                        //    寫死的法文確認句各自跑一次 for 迴圈，每個字元印一行 IceLogging.Error。
                        //    節流器是 EzThrottler 的預設 500ms，所以那是**每半秒約 110 行 Error**
                        //    （兩句各約 50 餘字元 ＋ 4 行標頭）。IceLogging.Error 除了寫 dalamud.log
                        //    還會推進 LogSystem 那個 3000 筆的環形緩衝區 —— 也就是使用者要複製回報的
                        //    那個視窗，十幾秒就會被這段 dump 洗光，真正有用的上下文全部被擠出去。
                        //    法文比對的問題後來是靠 NormalizeWhitespace()（下面那個函式，處理 NBSP／
                        //    細空格）解掉的，這段 dump 只是當時的鷹架，留著純粹是損害。
                        // ⚠️ 只刪列印，判斷與動作完全不動：認不出來的確認框仍然按 No，
                        //    仍然留一行 Error 說明是什麼視窗。cycleapple 9d5a8f0 在同一個位置改成
                        //    「不碰這個視窗、只印 Warning 等它自己關掉」——那是行為變更（可能是別的
                        //    外掛的確認框），**這一輪刻意不採用**，維持現行按 No。
                        //
                        // 🔴 2026-08-18：同一個 else 分支上面原本還留著 4 行 `IceLogging.Debug`
                        //    （Actual text／Actual text length／Trimmed text／Trimmed length），
                        //    **完全沒有節流，這個任務回 false 就是每一幀再印一次**。
                        //    IceLogging.Debug 沒有等級閘門：它一律先 LogSystem.Log() 推進上面說的
                        //    那個 3000 筆環形緩衝區、再組字串丟給 PluginLog.Debug。使用者跑 LogLevel 2，
                        //    Dalamud 會把 PluginLog.Debug 整個丟掉 —— 也就是**這 4 行使用者永遠看不到**，
                        //    卻以每幀 4 筆的速度洗掉他要複製回報的上下文。跟上面那段法文 dump 同一種損害。
                        // 🔑 取捨：4 行裡有 3 行的內容跟下面那行已節流的 Error 重複（都印 select.Text），
                        //    唯一不重複的是長度，所以長度**折進 Error 那一行**（`[len=…]`），不另外開 log 行。
                        //    這樣既不新增任何一筆 log，又把這個訊息從使用者看不到的 Debug 升到看得到的 Error。
                        // ⚠️ 但長度是**弱訊號**：等長的異體空白（NBSP ↔ 一般空格）長度一樣、印出來也一樣，
                        //    這種只有碼位看得出來 —— 而逐字元碼位 dump 正是上面被刪掉的那段鷹架。
                        //    **不要再把它加回來**：真的遇到就去擴充 NormalizeWhitespace() 的替換表。
                        if (EzThrottler.Throttle("Unexpected Abandon Window..."))
                        {
                            IceLogging.Error($"Unexpected abandon window??? {select.Text} [len={select.Text.Length}]", "[Abandon Mission]");
                            select.No();
                        }
                    }
                }
                else if(GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var addon) && addon.IsAddonReady)
                {
                    // ⚠️ 這個 if 原本沒有大括號、下一行還是空行 —— 看起來像在 gate 下面的
                    //    Report/Abandon，實際上只 gate 到「漁夫先收竿」那一整塊。補上大括號是為了
                    //    讓讀的人看到真正的範圍，行為刻意維持完全一樣。
                    //
                    // 🔴 為什麼不把 Report/Abandon 也包進來（節流器的名字看起來就是那個意思）：
                    //    包進來會讓「放棄任務」永遠不會發生。EzThrottler 的語意是「首次必放行，
                    //    之後要 now > deadline 才放行並重設」，而下面那組是
                    //      if (Throttle("Attempt to turnin", 500)) Report();
                    //      else if (Throttle("Telling it to abandon the mission", 500)) Abandon();
                    //    —— 現在是第一個 tick 走 Report，下一個 tick「Attempt to turnin」還沒到期，
                    //    才會落到 else 去 Abandon。外面再包一層 1000ms 的話，每次外層放行時
                    //    「Attempt to turnin」的 500ms 早就過了，於是每次都走 Report，
                    //    else 那一支永遠碰不到。
                    //
                    // ⚠️ 為什麼也不直接把這個 Throttle 刪掉（讓漁夫判斷變成每個 tick 都檢查，
                    //    跟 Task_TurninMission 裡一模一樣的那個區塊一致）：那會讓「還在釣魚時」
                    //    完全不能回報／放棄，只能等 StopFishing() 生效。萬一收竿沒生效，這串任務
                    //    用的是 Utils.TaskConfig（30 分鐘、abortOnTimeout: false），失敗形式會是
                    //    「安靜地卡住半小時」。要改成那樣得先有實機證據。
                    if (EzThrottler.Throttle("Trying To Turnin/Abandon", 1000))
                    {
                        if (Player.JobId == 18 && Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Gathering])
                        {
                            if (EzThrottler.Throttle("Stop fishing so we can turn in this mission!", 2000))
                                Task_DualClass.StopFishing();

                            return false;
                        }
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
