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
            // 第一步之前就掛上 YesAlready／TextAdvance 的暫停，不等下一幀的 Tick。
            Handlers.YesAlreadyManager.Tick();
            Handlers.TextAdvancedManager.Tick();
            IceLogging.Info($"Setting State to: {State} / Enabling Plugin");
            Mission_Settings.StartJob = Player.JobId;
            Task_CheckScore.ResetMedalCheckLog();
            Task_Repair.ResetSelfRepairState();

            // 啟動當下就把「前置被停用、鏈上卻還有想跑的任務」修回來 —— 這是使用者正在看著畫面的
            // 時機，訊息在這裡才看得到。修不修得動由 MissionChain 自己判斷（讀不到 WKSManager
            // 就整個跳過），這裡不需要再加條件。
            MissionChain.RepairSequentialPrerequisites("[啟動檢查]");

            // 具名壓制租約：請 AutoRetainer 在 ICE 跑完之前不要去跑僱員／換角色。
            // 🔴 fail-safe：拿不到（沒安裝／舊版沒有這個端點）就照現況跑，絕不卡住這裡。
            // 📌 續租與「狀態被別的路徑改掉」的對齊由 ICE.Tick 每幀的 Sync 負責，這裡只是讓它立刻生效。
            P.AutoRetainer?.Sync(true);

            return true;
        }
        internal static bool DisablePlugin()
        {
            IceLogging.Debug("Stopping the plugin state", "[Schedular - Disable Plugin]");
            P.TaskManager.Abort();
            State = IceState.Idle;
            Handlers.YesAlreadyManager.Tick();
            Handlers.TextAdvancedManager.Tick();
            // 佇列已經被中止了，「正要去領的任務」不再成立，別讓疊加層繼續顯示。
            Task_FindMission.ClearTargetMission();
            if (P.Navmesh.Installed)
            {
                P.Navmesh.CancelOwnPathfind();
                if (P.Navmesh.IsRunning())
                    P.Navmesh.Stop();
            }

            // 具名壓制租約還回去：ICE 停了就沒有理由再擋著 AutoRetainer。
            P.AutoRetainer?.ReleaseNow("ICE 的自動化被停用");

            // 宇宙製作的臨時求解器也一併還給 Artisan：ICE 停了就沒有理由再影響
            // 使用者自己手動製作那些配方。沒有覆寫在身上時這一支第一行就回去。
            CosmicSolverOverride.ClearAll("ICE 的自動化被停用");

            return true;
        }

        /// <summary>
        /// 排程器統一的「中止這一串任務」出口：清掉佇列，並<b>只有在排程本來就在跑的時候</b>
        /// 才回到 <see cref="Enums.IceState.Start"/> 重新判斷狀態。
        /// </summary>
        /// <remarks>
        /// 🔴 <see cref="Tick"/> 只要看到 <c>State != Idle</c> 就會開始跑<b>整套</b>任務排程。
        /// 排程本來就在跑時回到 <c>Start</c> 重新判斷是對的，Idle 時則必須維持 Idle。
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
        /// 任務 ID 在 <c>SheetMissionDict</c> 查不到時不擲例外，由呼叫端收工。
        /// </remarks>
        internal static bool CurrentMissionUnavailable(string handle, [MaybeNullWhen(true)] out CosmicHelper.CosmicInfo info)
        {
            if (CosmicHelper.TryGetCurrentMissionInfo(out info))
                return false;

            // 節流：這個狀況在最壞情況下每個 tick 都成立，不節流會把 log 灌爆。
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
        /// </summary>
        /// <remarks>
        /// 🔴 <c>C.MissionConfig</c> 與 <c>CosmicHelper.SheetMissionDict</c> 是<b>兩個鍵集合不同的字典</b>：
        /// 前者由 <c>ConfigMigrator.UpdateConfigMissionList()</c> 從後者補齊，但也會被
        /// <c>MissionTimer</c> 按需寫入，而且是<b>存進設定檔的</b>。
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