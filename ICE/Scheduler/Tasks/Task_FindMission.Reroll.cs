using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using ICE.Sounds;
using ICE.Ui.MainUi.ModeSelect;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ICE.Utilities.Cosmic;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentWKSMission;
using static ICE.Utilities.CosmicHelper;
using ICE.IPC;

namespace ICE.Scheduler.Tasks
{
    internal static partial class Task_FindMission
    {
        // 連續重骰但一直沒找到任務的次數。找到任務時歸零。
        private static int consecutiveRerolls = 0;

        /// <summary>
        /// 這一輪「候選池空了 → 換職業」已經試過的職業。真的領到任務、或全部職業都試完之後清空。
        /// </summary>
        /// <remarks>
        /// 🔴 沒有這個集合就會無限迴圈：換過去的職業如果同樣挑不到任務，重骰上限會再次觸發，
        /// 然後又從 <c>JobPrio</c> 的第一個重新挑 —— 在兩個職業之間來回跳、永遠不收斂。
        /// </remarks>
        private static readonly HashSet<uint> triedJobsThisSweep = new();

        /// <summary>換職業這件事本身的期限（<c>Environment.TickCount64</c>）。0 ＝ 目前沒有在換。</summary>
        /// <remarks>
        /// ⚠️ <see cref="SwitchJobStep"/> 只有真的換成功才回 true。沒有期限的話，換不過去
        /// （主手缺裝、角色一直忙碌…）就會一路回 false，失敗形式是「安靜地卡住」而不是報錯。
        /// </remarks>
        private static long jobSwitchDeadline = 0;

        /// <summary>換職業最多等多久（毫秒）。</summary>
        private const int JobSwitchTimeoutMs = 15000;

        private static bool? CheckReroll()
        {
            if (SchedulerMain.State == IceState.ExecutingMission)
            {
                IceLogging.Debug("No reason to reroll, you found a proper mission");
                consecutiveRerolls = 0;
                triedJobsThisSweep.Clear();
            }
            else
            {
                consecutiveRerolls++;

                // 沒有上限時，只要候選池空了（例如開了「取得金星後自動停用」而目前可接的
                // 全都拿過金星），這裡就會無限重骰、卡在原地而且完全沒有提示。
                var limit = C.MaxConsecutiveRerolls;
                if (limit > 0 && consecutiveRerolls >= limit)
                {
                    // 停止是最後手段：先看看有沒有別的職業還有未金星的任務可以接。
                    // 預設關閉，使用者開了才會走這條（見 C.AutoSwitchJobWhenPoolEmpty）。
                    if (TrySwitchToJobWithMissions())
                    {
                        consecutiveRerolls = 0;
                        return true;
                    }

                    IceLogging.Info(
                        $"連續重骰 {consecutiveRerolls} 次仍找不到可接任務，停止。" +
                        "常見原因：啟用了「取得金星後自動停用該任務」，而目前可接的任務都已經拿過金星。" +
                        "可改用「沒金星的優先」（只排序不移出候選池），或放寬啟用中的任務清單。",
                        "[Check Reroll]");
                    consecutiveRerolls = 0;
                    triedJobsThisSweep.Clear();
                    SchedulerMain.StopWithNotice("連續重骰仍然找不到可接的任務", needsHelp: true);
                    P.TaskManager.Tasks.Clear();

                    if (C.PlaySoundAlert)
                        _ = SoundPlayer.PlaySoundAsync();

                    return true;
                }

                IceLogging.Debug($"No mission was found, time for rerolling! ({consecutiveRerolls})", "[Check Reroll]");

                P.TaskManager.Insert(() => OpenTab("Reset"), "Opening tab to the reset mission");
                IceLogging.Debug("Task for re-roll thrown in", "[Check Reroll]");
            }
            return true;
        }
        /// <summary>
        /// 候選池空了：照「職業優先度」（<c>C.JobPrio</c>）找下一個還有未金星任務、而且真的換得
        /// 過去的職業，把換職業的步驟排進佇列。<b>回傳 true 代表已經接手，呼叫端不要再走停止流程。</b>
        /// </summary>
        /// <remarks>
        /// ⚠️ 只走 <c>C.JobPrio</c> 裡列出來的職業。設定介面只能拖曳排序、不能增刪，所以正常情況下
        /// 11 個職業都在；真的缺了就寫一行 Information 講出來，不要默默把使用者移掉的職業加回去。
        /// </remarks>
        private static bool TrySwitchToJobWithMissions()
        {
            if (!C.AutoSwitchJobWhenPoolEmpty)
                return false;

            var currentJob = Player.JobId;
            triedJobsThisSweep.Add(currentJob);

            var allJobCount = CosmicHelper.CrafterJobList.Count + CosmicHelper.GatheringJobList.Count;
            if (C.JobPrio.Count < allJobCount)
            {
                IceLogging.Info(
                    $"職業優先度清單裡只有 {C.JobPrio.Count} 個職業（完整是 {allJobCount} 個），" +
                    "沒列出來的職業不會被自動換過去。要全部納入請到「優先度設定」按重設。",
                    "[Check Reroll: 換職業]");
            }

            foreach (var jobId in C.JobPrio)
            {
                if (jobId == currentJob || triedJobsThisSweep.Contains(jobId))
                    continue;

                var (viable, ungolded, goldKnown) = CountViableMissions(jobId);
                var ungoldedText = goldKnown ? ungolded.ToString() : "?";

                // 使用者要的條件是「還沒全金星的職業」。讀不到 WKSManager 時退回「還有任務可接」——
                // 那時每個任務的金星狀態都是不知道的，拿「不知道」去否決一個職業會更糟。
                var wanted = goldKnown ? ungolded : viable;

                // 不管選不選它，這一輪都算試過了：否則下一次觸發重骰上限又會把同一批職業重新
                // 評估一遍，同樣的訊息會一直重印。
                triedJobsThisSweep.Add(jobId);

                if (wanted <= 0)
                {
                    IceLogging.Debug(
                        $"職業 {(Job)jobId}：可接 {viable} 個、未金星 {ungoldedText} 個，跳過",
                        "[Check Reroll: 換職業]");
                    continue;
                }

                // ⚠️ 不要靜默跳過：使用者看到的會是「明明還有任務卻換不過去」，而且沒有任何線索。
                if (!GearsetHandler.HasUsableGearset((Job)jobId))
                {
                    IceLogging.Info(
                        $"{(Job)jobId} 還有 {viable} 個可接的任務（未金星 {ungoldedText} 個），" +
                        "但找不到可以直接換過去的套裝 —— 沒有這個職業的套裝，或套裝的主手武器不在身上。跳過它。",
                        "[Check Reroll: 換職業]");
                    continue;
                }

                IceLogging.Info(
                    $"目前職業（{(Job)currentJob}）已經沒有可接的任務了，換到 {(Job)jobId}：" +
                    $"可接 {viable} 個，其中 {ungoldedText} 個還沒拿到金星。",
                    "[Check Reroll: 換職業]");

                jobSwitchDeadline = Environment.TickCount64 + JobSwitchTimeoutMs;
                P.TaskManager.Tasks.Clear();
                P.TaskManager.InsertMulti(
                    new(() => RefreshMissionUi(), "Closing the mission board before changing job"),
                    new(() => SwitchJobStep(jobId), "Switching to a job that still has missions")
                );

                // 換完之後從狀態判斷重跑：Task_CheckState 會用新職業重新決定要修理／萃取／接任務，
                // 那是這個狀態機唯一的通用復原點。
                SchedulerMain.State = IceState.Start;
                return true;
            }

            IceLogging.Info(
                "職業優先度裡的每一個職業都沒有可接的未金星任務，沒有可以換過去的對象。",
                "[Check Reroll: 換職業]");
            triedJobsThisSweep.Clear();
            return false;
        }

        /// <summary>
        /// 算出「如果現在是 <paramref name="jobId"/>，候選池裡會有幾個任務、其中幾個還沒金星」。
        /// </summary>
        /// <remarks>
        /// 🔑 過濾條件與 <see cref="RefreshSelectedMissions"/> <b>逐條相同</b>：
        /// 已啟用 → 在任務表裡 → 職業對得上 → 區域對得上 → ICE 跑得動。
        /// ⚠️ 指標只在這個呼叫的堆疊框內使用，不跨幀保存。
        /// </remarks>
        private static unsafe (int Viable, int Ungolded, bool GoldKnown) CountViableMissions(uint jobId)
        {
            var viable = 0;
            var ungolded = 0;

            var manager = WKSManager.Instance();
            var goldKnown = manager != null;

            foreach (var mission in C.MissionConfig)
            {
                var enabled = mission.Value.Enabled;

                if (C.XPRelicGrind)
                {
                    if (!enabled && C.XPRelicOnlyEnabled)
                        continue;
                }
                else if (!enabled)
                    continue;

                if (!CosmicHelper.SheetMissionDict.TryGetValue(mission.Key, out var missionInfo))
                    continue;
                if (!missionInfo.Jobs.Contains(jobId))
                    continue;
                if (missionInfo.TerritoryId != Player.Territory)
                    continue;
                if (MissionSupport.IsUnsupported(mission.Key))
                    continue;

                viable++;
                if (goldKnown && !manager->IsMissionGolded(mission.Key))
                    ungolded++;
            }

            return (viable, ungolded, goldKnown);
        }

        /// <summary>
        /// 真的把職業換過去。換到了回 true；等過頭了也回 true（帶一行 Information），
        /// 讓流程繼續往下跑而不是安靜地卡住。
        /// </summary>
        /// <remarks>
        /// ⚠️ <c>GearsetHandler.TaskClassChange</c> 自帶 250ms 節流、而且玩家忙碌時直接返回，
        /// 所以這裡不必再包一層節流。
        /// </remarks>
        private static bool? SwitchJobStep(uint jobId)
        {
            if (Player.JobId == jobId)
            {
                // 換成功了才把「ICE 認定的主職業」換過來，交件之後才不會被換回去。
                Mission_Settings.StartJob = jobId;
                jobSwitchDeadline = 0;
                IceLogging.Info($"已經換到 {(Job)jobId}，重新開始找任務。", "[Check Reroll: 換職業]");
                return true;
            }

            if (jobSwitchDeadline != 0 && Environment.TickCount64 > jobSwitchDeadline)
            {
                jobSwitchDeadline = 0;
                IceLogging.Info(
                    $"等了 {JobSwitchTimeoutMs / 1000} 秒還是沒能換到 {(Job)jobId}，放棄這次換職業。" +
                    "（常見原因：那個職業的套裝主手武器不在身上，或角色一直處於忙碌狀態。）",
                    "[Check Reroll: 換職業]");
                return true;
            }

            GearsetHandler.TaskClassChange((Job)jobId);
            return false;
        }
        public static bool? FrameDelay(int amount)
        {
            P.TaskManager.InsertDelay(amount, true);
            return true;

        }
        public static bool? TimeDelay(int amount)
        {
            P.TaskManager.InsertDelay(amount);
            return true;
        }
        private static bool? ChangeJob(uint missionId)
        {
            // 查不到就當成「不用換職業」直接放行，讓後面的步驟去處理，
            // 總比在這裡丟例外把整個佇列卡住好。
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var jobMission))
            {
                IceLogging.Info($"任務 {missionId} 不在任務表裡，跳過換職業判斷。", "[Task_FindMission: ChangeJob]");
                return true;
            }

            var jobId = jobMission.Jobs.First();
            if (Player.JobId == jobId)
                return true;
            else
            {
                if (EzThrottler.Throttle("Swapping to job for mission"))
                    GearsetHandler.TaskClassChange((Job)jobId);
                return false;
            }
        }

        private static string NormalizeWhitespace(string text)
        {
            return text.Trim()
                       .Replace('\u00A0', ' ')  // Non-breaking space to regular space
                       .Replace('\u2009', ' ')  // Thin space to regular space
                       .Replace('\u202F', ' ')  // Narrow no-break space to regular space
                       .Replace('\u3000', ' '); // Ideographic space to regular space
        }
        private static void ThrottleMessage(string s)
        {
            if (EzThrottler.Throttle($"{s} _ message", 2000))
            {
                IceLogging.Debug(s);
            }
        }
    }
}
