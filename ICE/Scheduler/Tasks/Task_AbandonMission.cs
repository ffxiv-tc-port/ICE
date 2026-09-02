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

        /// <summary>
        /// 「回報結果」與「放棄任務」在 <see cref="AddonPressGuard"/> 裡共用的同一把按法 key。
        /// </summary>
        /// <remarks>
        /// 🔴 這兩顆都是「按下之後這扇窗就會收掉」的<b>終結性</b>動作，各用一把 key 擋不住跨鈕接力按：
        /// 按完回報、窗還沒收完的那幾幀再按放棄，就是對正在關閉的視窗送輸入事件（原生存取違規，try/catch 攔不到）。<br/>
        /// ⚠️ 不可以改成把 <c>WKSMissionInfomation</c> 放進 <c>AddonPressGuard.SingleAnswerAddons</c>：
        /// 那會把同一扇窗的「星體分解」（<c>Task_Gather</c>）也一起併進來，而那顆按下之後窗並不會關。
        /// </remarks>
        private const string MissionEndPressKey = "ReportOrAbandon";

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
                    // 🔴 守衛順序：IsHeld → 讀文字 → MayPress → 按。按過（確定或取消）而且還沒觀察到它收掉的那幾幀
                    //    連文字都不讀；讀到 U+FFFD 也代表視窗記憶體正在變動，同樣這一幀不碰。兩種情況都只是
                    //    「這一幀什麼都不做」，方法照舊 return false 下一幀再來 —— 刻意留在這個分支裡，
                    //    不讓 else-if 鏈掉進下面 WKSMissionInfomation 那一支去按回報／放棄。
                    //    reroll 鏈的接任務確認框（文字不符 abandonStrings）正好會落進這裡：上游 GrabMission 按下確定
                    //    那一次已經被守衛記下，所以這扇窗關閉中的那幾幀 IsHeld 會回 true。
                    var promptText = YesnoPressGuard.IsHeld(select) ? null : select.Text;
                    if (promptText == null || AddonPressGuard.IsTextCorrupt("SelectYesno", promptText))
                    {
                        // 這一幀不碰。
                    }
                    else if (CosmicHandler.abandonStrings.Any(s => string.Equals(NormalizeWhitespace(promptText), NormalizeWhitespace(s), StringComparison.OrdinalIgnoreCase)) || !C.RejectUnknownYesno)
                    {
                        // 🔴 YesnoPressGuard 一定要放在條件式的最後（它有副作用：記下這一次按壓），
                        //    而且「按下去 + 狀態轉移」整組都掛在同一個條件式底下 ——
                        //    守衛擋下來時什麼都不做，不可能讓 Tasks.Clear()／State 在沒真的按下的
                        //    情況下被執行。擋下來只是這一幀不按，方法照舊 return false 下一幀再來。
                        // 🔑 這裡要防的**不是**本呼叫點自己重按（500ms 節流遠大於視窗關閉中的那幾幀），
                        //    而是下游那一步：Task_AbandonMission.Enqueue() 把
                        //    Task_TurninMission.JobSwapCheck 直接排在本任務後面，而
                        //    JobSwapCheck → GearsetHandler.TaskClassChange 是「只要 SelectYesno 開著
                        //    就按下確定」、完全不看視窗內容的按窗點，用的是另一把 key
                        //    （"Gearset"，250ms，而節流對沒見過的 key 首次一律放行）。
                        //    NeoTaskManager 一個 framework tick 只跑一個任務（TaskManager.Tick 執行
                        //    一次 CurrentTask.Function() 就 return），所以本任務一回 true，
                        //    下一幀就輪到 JobSwapCheck —— 兩次按壓最短可以只差一幀。
                        //    本任務回 true 的條件是 CurrentLunarMission（＝WKSManager->CurrentMissionUnitRowId）
                        //    變成 0，那需要幾幀**離線證明不了**，完全可能落在確認框關閉中的那幾幀裡。
                        //    守衛認的是視窗位址而不是 key，所以這裡「把這一次按壓記下來」正是
                        //    下游那道守衛唯一的判斷依據 —— 少了這一行，下游的守衛對這扇窗會直接放行。
                        if (EzThrottler.Throttle("Selecting Yes, mission is properly abandoning")
                            && YesnoPressGuard.MayPress("放棄任務：放棄確認", select))
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

                    // 🔴 守衛粒度＝（窗，位址，按法）。這扇窗的「回報」與「放棄」刻意共用**同一把 key**
                    //    （MissionEndPressKey）：兩者都是「按下之後這扇窗就會收掉」的終結性動作，
                    //    各用一把 key 擋不住跨鈕接力按 ——
                    //      tick N   ：Throttle("Attempt to turnin") 首次必放行 → 守衛首次放行 → Report()；
                    //                 任務已達可回報狀態時遊戲接受回報，這扇窗開始關閉。
                    //      tick N+1 ：本方法尾端 return false，而 NeoTaskManager 一個 framework tick 只跑一次
                    //                 CurrentTask.Function() ⇒ 下一幀原地重跑；CurrentLunarMission 還沒變 0
                    //                 （伺服器往返中），TryGetAddonMaster + IsAddonReady 在關閉中的那幾幀仍三關全過；
                    //                 "Attempt to turnin" 已消耗 → 落到 else-if，而
                    //                 "Telling it to abandon the mission" 是另一把節流（EzThrottler 首次必放行）、
                    //                 守衛又是另一把 key（首次必放行）→ 對關閉中的窗按下放棄鈕。
                    //    ⚠️ 不能改成把 WKSMissionInfomation 放進 AddonPressGuard.SingleAnswerAddons：
                    //       那會把同一扇窗的「星體分解」（Task_Gather）也併進來，而那顆按下之後窗不關。
                    //       要併的只有這兩顆終結鈕，所以在呼叫點共用同一個字串。
                    //
                    // 🔑 併 key 的代價（正常流程被延到逃生口）用「先看鈕能不能按」補掉：
                    //    Report()／Abandon() 都是 ClickButtonIfEnabled，鈕停用時本來就什麼都不做，
                    //    卻照樣會在守衛裡記下一筆「按過了」而白白封鎖對側。先判 enabled + visible
                    //    （就是 ClickButtonIfEnabled 內部同一組條件，都走 GenericHelpers 的判空版），沒按到就不登記：
                    //      ・未達回報條件（走到放棄流程的常態）：回報鈕停用 → 直接進放棄那一支，零延遲；
                    //      ・回報鈕真的可按：按下回報後，放棄要等這扇窗消失（守衛的輪詢／PreFinalize 觀察到就解除）
                    //        或 60 幀逃生口 —— 而那幾幀正是要擋掉的那幾幀。
                    // 🔴 順序：IsHeld → 讀鈕 → TryBeginPress → 按。被擋的那幾幀連 GetComponentButtonById
                    //    都不呼叫（那是去走正在關閉的視窗的節點樹），與上面 SelectYesno 那一段同一個形狀。
                    bool reportPressable = false, abandonPressable = false;
                    if (!AddonPressGuard.IsHeld("WKSMissionInfomation", addon, MissionEndPressKey))
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
                        && AddonPressGuard.TryBeginPress("放棄任務：回報結果", "WKSMissionInfomation", addon, MissionEndPressKey))
                    {
                        addon.Report();
                    }
                    else if (abandonPressable
                        && EzThrottler.Throttle("Telling it to abandon the mission", 500)
                        && AddonPressGuard.TryBeginPress("放棄任務：放棄任務", "WKSMissionInfomation", addon, MissionEndPressKey))
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
