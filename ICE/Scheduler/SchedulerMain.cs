using ECommons.Automation.NeoTaskManager;
using System.Diagnostics.CodeAnalysis;
using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using static ICE.Enums.IceState;

namespace ICE.Scheduler
{
    internal static unsafe class SchedulerMain
    {
        internal static bool EnablePlugin()
        {
            State = Start;
            IceLogging.Info($"Setting State to: {State} / Enabling Plugin");
            Mission_Settings.StartJob = Player.JobId;

            // 啟動當下就把「前置被停用、鏈上卻還有想跑的任務」修回來 —— 這是使用者正在看著畫面的
            // 時機，訊息在這裡才看得到。修不修得動由 MissionChain 自己判斷（讀不到 WKSManager
            // 就整個跳過），這裡不需要再加條件。
            MissionChain.RepairSequentialPrerequisites("[啟動檢查]");

            return true;
        }
        internal static bool DisablePlugin()
        {
            IceLogging.Debug("Stopping the plugin state", "[Schedular - Disable Plugin]");
            P.TaskManager.Abort();
            State = IceState.Idle;
            // 佇列已經被中止了，「正要去領的任務」不再成立，別讓疊加層繼續顯示。
            Task_FindMission.ClearTargetMission();
            if (P.Navmesh.Installed)
            {
                if (P.Navmesh.IsRunning())
                    P.Navmesh.Stop();
            }

            return true;
        }

        /// <summary>
        /// 排程器統一的「中止這一串任務」出口：清掉佇列，並<b>只有在排程本來就在跑的時候</b>
        /// 才回到 <see cref="Enums.IceState.Start"/> 重新判斷狀態。
        /// </summary>
        /// <remarks>
        /// 🔴 <see cref="Tick"/> 只要看到 <c>State != Idle</c> 就會開始跑<b>整套</b>任務排程。
        /// 所以中止／錯誤路徑要是無條件寫 <c>State = Start</c>，語意就變成
        /// 「出錯之後把整個 ICE 啟動起來」——而 <c>Task_Repair</c>／<c>Task_Gather</c>／
        /// <c>Task_BuyCosmoItems</c>／<c>Task_RelicTurnin</c>／<c>Task_AbandonMission</c>
        /// 都掛在偵錯視窗的單次按鈕上，按下去時 <c>State</c> 是 <c>Idle</c>：
        /// 只要中途查不到 NPC，整套 ICE 就會自己跑起來。<br/>
        /// 排程本來就在跑時回到 <c>Start</c> 重新判斷是對的，Idle 時則必須維持 Idle。
        /// 同型修正的第一例是 <c>Task_Gamba.AbortGamba()</c>（設定頁的「立即執行一次」按鈕）。
        /// </remarks>
        internal static void AbortToStateCheck()
        {
            P.TaskManager.Tasks.Clear();
            if (State != IceState.Idle)
                State = Start;
        }

        /// <summary>
        /// 排程器統一的「任務資料還在嗎」閘門。<b>回傳 true 代表任務已經不存在，呼叫端必須立刻收工</b>
        /// （任務本體 <c>return true</c>；Enqueue 方法 <c>return</c>）。
        /// </summary>
        /// <remarks>
        /// 這是 <c>CosmicHelper.CurrentMissionInfo</c>（已移除）那顆零守衛字典索引的系統性替代品。<br/>
        /// 之所以要「清佇列 + 回到 <see cref="Enums.IceState.Start"/>」而不是只回一個空值：
        /// 任務不存在時佇列裡剩下的步驟全部都是針對舊任務排的，繼續跑只會用錯的前提做決定。
        /// <c>Start</c> 會走 <c>Task_CheckState</c> 從頭重新判斷，是這個狀態機唯一的通用復原點
        /// （走 <see cref="AbortToStateCheck"/>，所以 Idle 時不會把外掛啟動起來）。<br/>
        /// 2026-08-03 實機事故就是這條路徑沒有守衛：遊戲端把探索任務取消掉 →
        /// <c>CurrentLunarMission</c> 變 0 → <c>SheetMissionDict[0]</c> 每個 tick 丟例外 37 次 →
        /// 逾時 → 佇列中止 → 外掛自己停用。
        /// </remarks>
        internal static bool CurrentMissionUnavailable(string handle, [MaybeNullWhen(true)] out CosmicHelper.CosmicInfo info)
        {
            if (CosmicHelper.TryGetCurrentMissionInfo(out info))
                return false;

            // 節流：這個狀況在最壞情況下每個 tick 都成立，不節流會把 log 灌爆
            // （正是上次事故裡「同一行噴 37 次」的形狀）。
            if (EzThrottler.Throttle("ICE: current mission unavailable", 5000))
            {
                IceLogging.Info($"查不到進行中的任務資料（任務 ID {CosmicHelper.CurrentLunarMission}），" +
                                "中止目前的流程並回到狀態判斷。" +
                                "（常見原因：遊戲端自己取消了任務，例如被機甲行動抽中當駕駛員傳送走。）", handle);
            }

            AbortToStateCheck();
            return true;
        }

        /// <summary>
        /// 同上，但取的是使用者對這個任務的設定（<c>C.MissionConfig</c>）。
        /// <b>回傳 true 代表拿不到，呼叫端必須立刻收工。</b>
        /// </summary>
        /// <remarks>
        /// 🔴 <c>C.MissionConfig</c> 與 <c>CosmicHelper.SheetMissionDict</c> 是<b>兩個鍵集合不同的字典</b>：
        /// 前者由 <c>ConfigMigrator.UpdateConfigMissionList()</c> 從後者補齊，但也會被
        /// <c>MissionTimer</c> 按需寫入（含 <c>0</c>），而且是<b>存進設定檔的</b>。
        /// 實測使用者的 <c>Mission Config.yaml</c>：<c>missionConfig</c> 的鍵是 <b>0..544</b>，
        /// 而 <c>SheetMissionDict</c> 是 <b>1..544</b> —— 所以「守了其中一個」永遠不等於
        /// 「另一個也有」。這正是 8daada5 修掉的那顆雷的形狀。
        /// </remarks>
        internal static bool CurrentMissionConfigUnavailable(uint missionId, string handle, [MaybeNullWhen(true)] out Config.MissionSettings config)
        {
            if (C.MissionConfig.TryGetValue(missionId, out config))
                return false;

            if (EzThrottler.Throttle("ICE: mission config unavailable", 5000))
            {
                IceLogging.ChatError($"任務 {missionId} 在設定檔裡沒有對應的設定，無法判斷回報條件，" +
                                     "中止目前的流程。", "[ICE]");
                IceLogging.Info($"C.MissionConfig 沒有 key {missionId}。" +
                                "（正常情況下 ConfigMigrator.UpdateConfigMissionList() 會在啟動時補齊。）", handle);
            }

            AbortToStateCheck();
            return true;
        }

        // Debug only settings
        internal static bool DebugOOMMain = false;
        internal static bool DebugOOMSub = false;

        internal static IceState State = Idle;
        internal static MissionAttributes MissionState = MissionAttributes.None;

        internal static void Tick()
        {
            if (Throttles.GenericThrottle && P.TaskManager.NumQueuedTasks == 0 && State != Idle)
            {
                switch (State)
                {
                    case Gambling: Task_Gamba.Enqueue(); break;
                    case Start: Task_CheckState.Enqueue(); break;
                    case Spiritbond: Task_Spiritbond.Enqueue(); break;
                    case Repair: Task_Repair.Enqueue(); break;
                    case HubReturn: Task_HubActivities.Enqueue(); break;
                    case GrabMission: Task_FindMission.Enqueue(); break;
                    case AbandonMission: Task_AbandonMission.Enqueue(); break;
                    case ExecutingMission: Task_ExecuteMission.Enqueue(); break;
                    case ScoreCheck: Task_CheckScore.Enqueue(); break;
                    case TurninMission: Task_TurninMission.Enqueue(); break;
                    case Craft: Task_Craft.Enqueue(); break;
                    case Gather: Task_Gather.Enqueue(); break;
                    case Fish: Task_Fishing.Enqueue(); break;
                    case DualClass: Task_DualClass.Enqueue(); break;
                    case ManualMode: Task_Manual.Enqueue(); break;
                    default: DisablePlugin(); break;
                }
            }
        }
    }
}