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
        /// <summary>
        /// List of all available critical missions
        /// </summary>
        private static HashSet<uint> CriticalMissions = new HashSet<uint>();
        /// <summary>
        /// List of all the available<br></br>
        /// -> Timed <br></br>
        /// -> Weather <br></br>
        /// -> Sequence <br></br>
        /// </summary>
        private static HashSet<uint> WeatherMissions = new HashSet<uint>();
        private static HashSet<uint> TimedMissions = new HashSet<uint>();
        private static HashSet<uint> SequenceMissions = new HashSet<uint>();
        private static HashSet<uint> ExARankMissions = new HashSet<uint>();
        private static HashSet<uint> ARankMissions = new HashSet<uint>();
        private static HashSet<uint> BRankMissions = new HashSet<uint>();
        private static HashSet<uint> CRankMissions = new HashSet<uint>();
        private static HashSet<uint> DRankMissions = new HashSet<uint>();

        private static int SpecialMissionCount = 0;
        private static int BasicMissionCount = 0;

        private static List<Vector3> fishingPath = new List<Vector3>();
        private static GatheringUtil.FisherSpotInfo fishingEntry = new();

        private static readonly Random _random = new Random();
        private static uint missionToAbandon = 0;

        private static int timeoutAmount = 0;
        private static int maxTimeout = 10;

        /// <summary>
        /// 排程器已經挑好、正要去領的任務 ID（尚未接下來）。0 = 目前沒有選定目標。
        /// <para>
        /// 疊加層在「目前任務：無」時用這個顯示「正要去領哪一個」。設定點一律是實際把
        /// 領取任務堆進佇列的地方，所以它代表的是<b>已決定</b>而不是<b>已接受</b>。
        /// 重擲（reroll/abandon）不會設定這個值 —— 那是要丟掉的任務，不是目標。
        /// </para>
        /// </summary>
        public static uint TargetMissionId { get; private set; }

        /// <summary>清掉選定目標。每輪重新找任務、以及任務真的接下來之後都要呼叫，避免顯示過期資訊。</summary>
        public static void ClearTargetMission() => TargetMissionId = 0;

        public static void Enqueue()
        {
            IceLogging.Info("Starting the find mission queue", "[Task Find Mission]");
            P.TaskManager.Enqueue(RefreshMissionUi, "Refreshing Mission UI");
            P.TaskManager.Enqueue(OpenMissionUi, "Opening it on proper class");
            P.TaskManager.Enqueue(RefreshSelectedMissions, "Refreshing the list of viable missions");
            if (C.XPRelicGrind)
            {
                // 宇宙工具經驗模式：預設只掃「一般任務」分頁（上游行為）。
                // 兩個開關可以額外把緊急／臨時（連續・時間・天氣）分頁也納入挑選，
                // 順序刻意跟標準模式一致：緊急 -> 臨時 -> 一般。
                // OpenTab 一開頭就會在「任務已接下」時直接返回，所以前面的分頁一旦選到，
                // 後面的分頁就不會再動作。
                if (C.XPRelicIncludeCritical)
                    P.TaskManager.Enqueue(() => OpenTab("ExpCheckCritical"), "Opening Critical tab for relic grind");
                if (C.XPRelicIncludeProvisional)
                    P.TaskManager.Enqueue(() => OpenTab("ExpCheckProvisional"), "Opening Provisional tab for relic grind");
                P.TaskManager.Enqueue(() => OpenTab("ExpCheck"), "Opening Standard tab for relic grind");
            }
            else if (C.GrindProvisionals)
            {
                P.TaskManager.Enqueue(() => OpenTab("ProvisionalGrind"), "Opening Provisional Grind Check");
            }
            else
            {
                P.TaskManager.Enqueue(TabTasksCheck, "Checking which tabs to check for missions");
            }
        }
        public static bool? RefreshMissionUi()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var hud) && !hud.IsAddonReady)
            {
                IceLogging.Info("Mission Selection Hud is no longer visible and been refreshed. Continuing on");
                return true;
            }
            else
            {
                if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud) && moonHud.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Closing the hud to make sure it's on the right class"))
                    {
                        IceLogging.Debug("Closing out the mission selection hud");
                        moonHud.Mission();
                    }
                }

            }

            return false;
        }
        public static bool? OpenMissionUi()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var hud) && hud.IsAddonReady)
            {
                IceLogging.Info("Starting the find mission queue", "[Task: Find Mission | Open Mission Ui]");
                return true;
            }
            else
            {
                if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud) && moonHud.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Opening the mission ui"))
                    {
                        IceLogging.Info("Opening the moon mission selection hud");
                        moonHud.Mission();
                    }
                }
            }

            return false;
        }
        public static bool? RefreshSelectedMissions()
        {
            // 新的一輪挑選開始，把上一輪殘留的目標清掉，免得疊加層顯示過期資訊。
            ClearTargetMission();
            CriticalMissions.Clear();
            WeatherMissions.Clear();
            TimedMissions.Clear();
            SequenceMissions.Clear();
            ExARankMissions.Clear();
            ARankMissions.Clear();
            BRankMissions.Clear();
            CRankMissions.Clear();
            DRankMissions.Clear();
            SpecialMissionCount = 0;
            BasicMissionCount = 0;

            uint currentJobId = Player.JobId;

            // 這一輪因為「ICE 跑不動」而被排除的任務。⚠️ 不能靜默跳過 —— 使用者啟用了它卻
            // 永遠不會被接，沒有訊息的話看起來就像外掛壞了。
            var skippedUnsupported = new List<(uint Id, MissionSupport.UnsupportedReason Reason)>();

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

                var missionId = mission.Key;
                HashSet<uint> missionJobs = new HashSet<uint>();
                if (CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var missionInfo))
                {
                    missionJobs = missionInfo.Jobs;
                    if (!missionJobs.Contains(currentJobId))
                        continue;

                    // Territory Check, cause people seem to also be forgetting this
                    if (missionInfo.TerritoryId != Player.Territory)
                        continue;

                    // ICE 跑不動的任務在這裡就排除掉，不要挑進候選池。
                    // 🔑 這是「自動選任務要跳過」的**唯一**攔截點：緊急／臨時／一般三個分頁挑的都是下面這幾個 HashSet，
                    //    擋在建池階段就等於三條路一起擋住。
                    // ⚠️ 刻意放在「已啟用 + 職業對 + 區域對」之後：只有使用者真的想跑的任務
                    //    才值得回報，否則每輪都會列出一整排跟他無關的任務。
                    if (MissionSupport.IsUnsupported(missionId, out var unsupportedReason))
                    {
                        skippedUnsupported.Add((missionId, unsupportedReason));
                        continue;
                    }

                    // Alright, mission was double checked to make sure it was enabled
                    // And also checked to make sure that the current job is on the mission, time to actually add it to the mission info

                    if (missionInfo.Attributes.HasFlag(MissionAttributes.Critical))
                        CriticalMissions.Add(missionId);
                    else if (missionInfo.Attributes.HasFlag(MissionAttributes.ProvisionalSequential))
                    {
                        SequenceMissions.Add(missionId);
                        SpecialMissionCount += 1;
                    }
                    else if (missionInfo.Attributes.HasFlag(MissionAttributes.ProvisionalTimed))
                    {
                        TimedMissions.Add(missionId);
                        SpecialMissionCount += 1;
                    }
                    else if (missionInfo.Attributes.HasFlag(MissionAttributes.ProvisionalWeather))
                    {
                        WeatherMissions.Add(missionId);
                        SpecialMissionCount += 1;
                    }
                    else if (missionInfo.Rank == 5)
                    {
                        ExARankMissions.Add(missionId);
                        BasicMissionCount += 1;
                    }
                    else if (missionInfo.Rank == 4)
                    {
                        ARankMissions.Add(missionId);
                        BasicMissionCount += 1;
                    }
                    else if (missionInfo.Rank == 3)
                    {
                        BRankMissions.Add(missionId);
                        BasicMissionCount += 1;
                    }
                    else if (missionInfo.Rank == 2)
                    {
                        CRankMissions.Add(missionId);
                        BasicMissionCount += 1;
                    }
                    else if (missionInfo.Rank == 1)
                    {
                        DRankMissions.Add(missionId);
                        BasicMissionCount += 1;
                    }
                }
                else
                {
                    IceLogging.Error($"We're somehow missing a mission from the sheets??? MissionID: {missionId}\n" +
                                     $"Please let me know if this happens");
                }
            }

            IceLogging.Info($"Mission count has been updated to the following for JobId {currentJobId}: \n" +
                $"Critical Count: {CriticalMissions.Count}\n" +
                $"Sequence Count: {SequenceMissions.Count}\n" +
                $"Timed Count: {TimedMissions.Count}\n" +
                $"Weather Count: {WeatherMissions.Count}\n" +
                $"A-EX Rank: {ExARankMissions.Count} \n" +
                $"A Rank: {ARankMissions.Count} \n" +
                $"B Rank: {BRankMissions.Count} \n" +
                $"C Rank: {CRankMissions.Count} \n" +
                $"D Rank: {DRankMissions.Count} \n" +
                $"Total Critical Missions: {CriticalMissions.Count} \n" +
                $"Total Special Missions: {SpecialMissionCount} \n" +
                $"Total Basic Missions: {BasicMissionCount} \n");

            ReportSkippedUnsupported(skippedUnsupported);

            return true;
        }

        private static string lastSkippedSignature = string.Empty;

        /// <summary>
        /// 把這一輪被「不支援」擋掉的任務講出來。
        /// </summary>
        /// <remarks>
        /// ⚠️ 要使用者回報的診斷。
        /// ⚠️ 節流：<c>RefreshSelectedMissions</c> 每一輪找任務都會跑，不節流會把記錄檔洗掉。
        /// </remarks>
        private static void ReportSkippedUnsupported(List<(uint Id, MissionSupport.UnsupportedReason Reason)> skipped)
        {
            if (skipped.Count == 0)
            {
                lastSkippedSignature = string.Empty;
                return;
            }

            var signature = string.Join(",", skipped.Select(x => $"{x.Id}:{x.Reason}"));
            var changed = signature != lastSkippedSignature;
            lastSkippedSignature = signature;

            var detail = string.Join("、", skipped.Select(x =>
                $"[{x.Id}]{(CosmicHelper.SheetMissionDict.TryGetValue(x.Id, out var e) ? e.Name : "?")}({x.Reason})"));

            // ⚠️ 兩個 Throttle 都要無條件呼叫（不要短路），否則名單一變就跳過計時器，
            //    下一輪沒變的時候會立刻又放行一次。
            var logDue = EzThrottler.Throttle("ICE: unsupported mission skip log", 60000);
            var chatDue = EzThrottler.Throttle("ICE: unsupported mission skip chat", 300000);

            if (changed || logDue)
                IceLogging.Info($"自動選任務跳過了 {skipped.Count} 個 ICE 跑不動的任務：{detail}。" +
                                "它們仍然可以手動接、手動完成。", "[FindMission: 未支援]");

            if (changed || chatDue)
                IceLogging.ChatInfo(
                    "Auto mission select skipped ?? enabled mission(s) ICE cannot run: ??"
                        .Loc(skipped.Count, string.Join("、", skipped.Select(x => $"[{x.Id}]"))),
                    "[ICE]");
        }
        public static bool? TabTasksCheck()
        {
            bool hasCritical = CriticalMissions.Count > 0;
            bool hasSpecial = SpecialMissionCount > 0;
            bool hasBasic = BasicMissionCount > 0;

            if (!(hasCritical || hasSpecial || hasBasic))
            {
                // ⚠️ 這裡會直接把外掛停掉，所以用 Information + 聊天視窗（要使用者回報的診斷）：
                //    排除不支援任務之後這條路徑更容易走到（整批啟用的任務可能全被跳過）。
                IceLogging.ChatInfo(
                    "No runnable mission is enabled for the current job in this zone, so ICE is stopping. (Relic XP mode is off.)".Loc()
                    + (lastSkippedSignature.Length > 0
                        ? " " + "Some enabled missions were skipped because ICE cannot run them - see the message above.".Loc()
                        : string.Empty),
                    "[ICE]");
                SchedulerMain.StopWithNotice("這個職業在這個區域沒有可跑的任務", StopReasons.NoMission, needsHelp: true);
                SchedulerMain.DisablePlugin(StopReasons.NoMission);
            }

            if (hasCritical)
                P.TaskManager.Enqueue(() => OpenTab("Critical"), "Opening the critical tab for missions");
            if (hasSpecial)
                P.TaskManager.Enqueue(() => OpenTab("Provisional"), "Opening the provisional tab for missions");
            if (hasBasic)
            {
                P.TaskManager.Enqueue(() => OpenTab("Standard"), "Opening the standard mission tab");
                P.TaskManager.Enqueue(() => FrameDelay(16), "Delaying 8 frames for tab");
                P.TaskManager.Enqueue(CheckStandard, "Checking the standard missions for any potentional missions");
            }

            return true;
        }
        public static bool? OpenTab(string type)
        {
            if (CosmicHelper.CurrentLunarMission != 0)
            {
                IceLogging.Info($"Mission has been accepted. No reason to open tab: {type}");
                return true;
            }

            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                switch (type)
                {
                    case "Critical":
                        {
                            x.CriticalMissions();
                            P.TaskManager.InsertMulti
                            (
                                new(() => FrameDelay(16), "Delaying 8 frames for tab"),
                                new(CheckCritical, "Checking to see if current missions match up with the critical")
                            );
                            break;
                        }
                    case "Provisional":
                        {
                            x.ProvisionalMissions();
                            P.TaskManager.InsertMulti
                            (
                                new(() => FrameDelay(16), "Delaying 8 frames for tab"),
                                new(CheckProvisional, "Checking to see if any provisional missions exist")
                            );
                            break;
                        }
                    case "ExpCheck":
                        {
                            x.BasicMissions();
                            P.TaskManager.InsertMulti
                            (
                                new(() => FrameDelay(16), "Delaying 8 frames for the tab"),
                                new(() => CheckExp(), "Checking Exp Missions")
                            );
                            break;
                        }
                    case "ExpCheckCritical":
                        {
                            x.CriticalMissions();
                            P.TaskManager.InsertMulti
                            (
                                new(() => FrameDelay(16), "Delaying 8 frames for the tab"),
                                new(() => CheckExp(isFallbackTab: false, requireCurrentJob: true), "Checking Exp Missions [Critical]")
                            );
                            break;
                        }
                    case "ExpCheckProvisional":
                        {
                            x.ProvisionalMissions();
                            P.TaskManager.InsertMulti
                            (
                                new(() => FrameDelay(16), "Delaying 8 frames for the tab"),
                                new(() => CheckExp(isFallbackTab: false, requireCurrentJob: true), "Checking Exp Missions [Provisional]")
                            );
                            break;
                        }
                    case "Reset":
                        {
                            x.BasicMissions();
                            P.TaskManager.InsertMulti
                            (
                                new(() => FrameDelay(16), "Throwing a delay in to make sure you're on the right tab"),
                                new(() => FindReroll(), "Finding->Accepting next reroll")
                            );
                            break;
                        }
                    case "ProvisionalGrind":
                        {
                            x.ProvisionalMissions();
                            P.TaskManager.InsertMulti
                            (
                                new(() => FrameDelay(16), "Delaying for 16 frames"),
                                new(() => CheckAllProvisional(), "Checking all provisionals")
                            );
                            break;
                        }
                    default:
                        {
                            x.BasicMissions();
                            P.TaskManager.InsertMulti
                            (
                                new(() => FrameDelay(16), "Delaying 8 frames for tab"),
                                new(CheckStandard, "Checking the standard missions for any potentional missions")
                            );
                            break;
                        }
                }

                return true;
            }
            else
            {
                if (EzThrottler.Throttle("Opening the mission ui"))
                {
                    if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud) && moonHud.IsAddonReady)
                        moonHud.Mission();
                }
            }

            return false;
        }
        public static bool? CheckCritical()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                foreach (var mission in x.StellerMissions)
                {
                    if (CriticalMissions.Contains(mission.MissionId))
                    {
                        mission.Select();
                        InsertGrabMission(mission.MissionId);
                        IceLogging.Info("Going to \" Insert Grab Mission Task\" next", "[Task: Find Mission | Check Critical]");
                        return true;
                    }
                }

                IceLogging.Info("No mission was found under the critical tab, continuing onto the next", "[Critical Mission Check]");
                return true;
            }

            return false;
        }
        private static bool? CheckProvisional()
        {
            if (CosmicHelper.CurrentLunarMission != 0)
            {
                IceLogging.Info("Mission has already been accepted, skipping Provisional Missions check");
                return true;
            }
            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                foreach (var missionType in C.MissionPrio)
                {
                    HashSet<uint> missionIds = missionType switch
                    {
                        ProvisionalTypes.ProvisionalWeather => WeatherMissions,
                        ProvisionalTypes.ProvisionalSequential => SequenceMissions,
                        ProvisionalTypes.ProvisionalTimed => TimedMissions,
                        _ => new HashSet<uint>()
                    };

                    if (missionIds.Count == 0)
                        continue;

                    foreach (var mission in x.StellerMissions)
                    {
                        if (missionIds.Contains(mission.MissionId))
                        {
                            mission.Select();
                            // Insert the task for the following:
                            // -> Check if mission is a gathering or critical
                            //   -> If yes, insert a task to check if need to pathfind to area
                            // -> Insert delay here (small one, like 500 ms)
                            // -> Insert grab mission and switch states to whichever is necessary
                            InsertGrabMission(mission.MissionId);

                            return true;
                        }
                    }
                }

                IceLogging.Debug("No mission was found under the critical tab, continuing onto the next", "[Provisional Mission Check]");
                return true;
            }

            return false;
        }
        /// <summary>
        /// 標準任務的階級挑選順序（使用者可在「任務優先度」拖曳調整）。
        /// ⚠️ 一定要把設定裡缺少的階級補在後面：舊設定檔、手動編輯、或日後新增階級時，
        ///    少掉的那一階會永遠不被挑到 —— 而且是靜默的，看起來就像「沒有可接任務」。
        /// </summary>
        private static readonly string[] DefaultRankOrder = ["ExA", "A", "B", "C", "D"];

        private static IEnumerable<string> RankOrder()
        {
            var configured = C.RankPrio ?? [];
            var seen = new HashSet<string>();

            foreach (var rank in configured)
            {
                if (DefaultRankOrder.Contains(rank) && seen.Add(rank))
                    yield return rank;
            }

            foreach (var rank in DefaultRankOrder)
            {
                if (seen.Add(rank))
                    yield return rank;
            }
        }

        public static bool? CheckStandard()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                foreach (var rankType in RankOrder())
                {
                    // Get the appropriate HashSet for this rank
                    HashSet<uint> missionHashSet = rankType switch
                    {
                        "ExA" => ExARankMissions,
                        "A" => ARankMissions,
                        "B" => BRankMissions,
                        "C" => CRankMissions,
                        "D" => DRankMissions,
                        _ => new HashSet<uint>()
                    };

                    // Skip if no missions configured for this rank
                    if (missionHashSet.Count == 0)
                        continue;

                    // Look for missions of this rank type
                    var candidates = x.StellerMissions.Where(m => missionHashSet.Contains(m.MissionId)).ToList();

                    // 「依表格排序方式挑任務」：沿用「表格設定 → 排序方式」那個下拉選單的順序
                    // （經驗 I～V／宇宙點數／月面點數／地圖位置／職業分數…），與表格顯示共用同一份邏輯。
                    if (C.UseTableSortForMissionOrder && candidates.Count > 1)
                        candidates = modeSelect_TableInfo.SortByTableOption(candidates, m => m.MissionId).ToList();

                    // 「沒金星的優先」：同一階級之內，把還沒拿到金星的任務排前面（補完成度用）。
                    // 放在表格排序「之後」：OrderBy 是穩定排序，所以未金星優先，同組之內維持表格順序。
                    // ⚠️ 先在 unsafe 區塊把「是否已金星」算成純量再排序，不要把原生指標放進 OrderBy 的 lambda。
                    // ⚠️ WKSManager.Instance() 可能是 null（還沒進入宇宙探索內容），此時不排序，行為與關閉此選項完全相同。
                    if (C.PrioritizeUngoldedMissions && candidates.Count > 1)
                    {
                        Dictionary<uint, int> goldRank = new();
                        unsafe
                        {
                            var mgr = WKSManager.Instance();
                            if (mgr != null)
                            {
                                foreach (var m in candidates)
                                    goldRank[m.MissionId] = mgr->IsMissionGolded(m.MissionId) ? 1 : 0;
                            }
                        }

                        if (goldRank.Count > 0)
                        {
                            // OrderBy 是穩定排序，所以同組之內維持原本順序，只是把已金星的往後推。
                            candidates = candidates.OrderBy(m => goldRank.GetValueOrDefault(m.MissionId)).ToList();
                            IceLogging.Debug(
                                $"沒金星優先：{goldRank.Count(kv => kv.Value == 0)}/{goldRank.Count} 個尚未金星，已排到前面",
                                "[FindMission: CheckStandard]");
                        }
                    }

                    // 「最低金星率」門檻：把金星率不達標的任務排到同階級隊尾。
                    // 🔴 是「降級」不是「過濾」—— 移除會把候選池掏空，讓 CheckReroll 連續重骰（見 MissionConfigs.MaxConsecutiveRerolls）。
                    // ⚠️ 只在「依表格排序方式挑任務」開著時生效，門檻 0 = 關閉（預設）。
                    // ⚠️ 放在最後一個排序步驟，所以它蓋過上面的「沒金星優先」：試了夠多次都沒金星的任務不該再優先塞給使用者。
                    if (C.UseTableSortForMissionOrder && C.MinGoldRatePercentForPicking > 0 && candidates.Count > 1)
                    {
                        int GoldRateRank(uint missionId)
                        {
                            if (!C.MissionConfig.TryGetValue(missionId, out var cfg)) return 0;
                            var attempts = cfg.TotalCompletions + cfg.FailedCounters;
                            // 樣本不足一律視為達標，否則第一次失敗就把任務永久打入冷宮，
                            // 連累積數據的機會都沒有。
                            // ⚠️ 一定要寫 global:: —— 在 ICE.Scheduler.Tasks 這個命名空間裡，
                            //    裸寫的 ICE 會先綁到 ICE.ICE 這個類別（global using static 帶進來的），
                            //    再取 .Config 就變成存取它的非公開成員，編譯錯誤看起來像「保護層級」問題。
                            if (attempts < global::ICE.Config.MissionConfigs.MinGoldRateSampleSize) return 0;
                            var rate = 100.0 * cfg.GoldCompletions / attempts;
                            return rate < C.MinGoldRatePercentForPicking ? 1 : 0;
                        }

                        var demoted = candidates.Count(m => GoldRateRank(m.MissionId) == 1);
                        if (demoted > 0 && demoted < candidates.Count)
                        {
                            // OrderBy 是穩定排序：達標的維持原順序，不達標的整批推到後面。
                            candidates = candidates.OrderBy(m => GoldRateRank(m.MissionId)).ToList();
                            IceLogging.Info(
                                $"最低金星率 {C.MinGoldRatePercentForPicking}%：{demoted}/{candidates.Count} 個任務金星率不足，已排到隊尾（未移除）",
                                "[FindMission: CheckStandard]");
                        }
                        else if (demoted > 0)
                        {
                            // 全部都不達標：重排等於沒排，但使用者需要知道門檻正在空轉，
                            // 否則會以為門檻沒生效。
                            IceLogging.Info(
                                $"最低金星率 {C.MinGoldRatePercentForPicking}%：本階級 {demoted} 個候選全部不達標，維持原順序",
                                "[FindMission: CheckStandard]");
                        }
                    }

                    foreach (var mission in candidates)
                    {
                        mission.Select();
                        InsertGrabMission(mission.MissionId);
                        IceLogging.Debug($"Mission was found!: {mission.MissionId}. Activating it/inserting stack to queue up mission grab");
                        return true; // Found and processed a mission
                    }
                }

                // No mission was found, time to kick in Mr. Resetti
                IceLogging.Debug("No mission was found in standard", "[FindMission: CheckStandard]");
                P.TaskManager.Insert(() => CheckReroll(), "Checking Re-Roll Status");
                IceLogging.Debug("Initating the check reroll", "[FindMission: CheckStandard]");
                return true;
            }

            return false;
        }
        /// <param name="isFallbackTab">
        /// 這個分頁是不是「最後一道防線」。只有一般任務分頁是 true —— 挑不到東西時才由它負責
        /// 走重擲流程／停止外掛。緊急與臨時分頁挑不到是正常的（那兩種任務本來就不常有），
        /// 挑不到就安靜地把控制權交給下一個分頁。
        /// </param>
        /// <param name="requireCurrentJob">
        /// 一般任務分頁本來就已依職業分頁，所以維持 false 以免動到既有行為。
        /// </param>
        private static unsafe bool? CheckExp(bool isFallbackTab = true, bool requireCurrentJob = false)
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                var bestIndex = FindBestRelicMission(requireCurrentJob, allowCoolingPick: isFallbackTab);

                if (bestIndex > 0)
                {
                    IceLogging.Debug($"A mission was found for the xp grind: {bestIndex}.");
                    var selectedMission = x.StellerMissions.Where(x => x.MissionId == bestIndex).FirstOrDefault();

                    if (selectedMission != null)
                    {
                        selectedMission.Select();
                        InsertGrabMission(selectedMission.MissionId);
                        return true;
                    }

                    // 挑到了 ID 卻在清單裡找不到（分頁在這幾幀之間被換掉之類）。
                    // 額外分頁不能卡在這裡重試，直接交給下一個分頁。
                    if (!isFallbackTab)
                    {
                        IceLogging.Debug($"選到的任務 {bestIndex} 不在目前清單裡，交給下一個分頁", "[Xp Grind]");
                        return true;
                    }
                }
                else if (!isFallbackTab)
                {
                    IceLogging.Debug("這個分頁沒有適合的宇宙工具經驗任務，交給下一個分頁", "[Xp Grind]");
                    return true;
                }
                else
                {
                    IceLogging.Debug("\n" +
                                     "Somehow, you manage to find absolutely no missions. That's actually impressive.\n" +
                                     "You might have one of the following issues:\n" +
                                     "1: Ignore Manual Mode is enabled, and you have all missions set to manual mode.\n" +
                                     "2: Only Enabled Missions is on, and you have a very limited pool of missions that somehow missed the mark\n" +
                                     "If it's 2, then this should go on to re-roll for you (assuming that you have ATLEAST 1 mission enabled somewhere...\n" +
                                     "Checking this now", "[Xp Grind]");

                    if (C.XPRelicOnlyEnabled && BasicMissionCount != 0)
                    {
                        IceLogging.Debug($"Only relic grind was enabled. Continuing to re-roll mission now");
                        HashSet<uint> EnabledMissions = new();
                        // 🔴 迭代的是 MissionConfig、查的是 SheetMissionDict，兩個字典的鍵集合不一樣，所以一定要 TryGetValue。
                        foreach (var mission in C.MissionConfig.Where(x => x.Value.Enabled
                                                                          && SheetMissionDict.TryGetValue(x.Key, out var m)
                                                                          && m.Jobs.Contains(Player.JobId)))
                        {
                            EnabledMissions.Add(mission.Key);
                        }


                        string exARankStr = string.Join(", ", ExARankMissions);
                        string aRankStr = string.Join(", ", ARankMissions);
                        string bRankStr = string.Join(", ", BRankMissions);
                        string cRankStr = string.Join(", ", CRankMissions);
                        string dRankStr = string.Join(", ", DRankMissions);
                        string enabledStr = string.Join(", ", EnabledMissions);

                        IceLogging.Debug($"ExA Rank: {exARankStr}");
                        IceLogging.Debug($"A Rank: {aRankStr}");
                        IceLogging.Debug($"B Rank: {bRankStr}");
                        IceLogging.Debug($"C Rank: {cRankStr}");
                        IceLogging.Debug($"D Rank: {dRankStr}");
                        IceLogging.Debug($"Enabled Missions: {enabledStr}");
                        IceLogging.Debug($"Planet: {Player.Territory}");
                        P.TaskManager.Insert(() => FindReroll(), "Finding Reroll mission for Relic Grind");
                        return true;
                    }
                    else if (lastRelicPick.NotReady)
                    {
                        IceLogging.Error($"Okay, something is wrong. Stopping the process", "[Relic Grind]");
                        SchedulerMain.DisablePlugin(StopReasons.Error);
                    }
                    else
                    {
                        StopRelicPoolEmpty();
                    }
                }
            }

            return false;
        }
        /// <param name="requireCurrentJob">
        /// 只保留「任務職業包含目前職業」的候選。緊急／臨時分頁會列出其他職業的任務，
        /// 而經驗是加在任務所屬職業的宇宙工具上，所以那兩個分頁必須開啟這個過濾。
        /// </param>
        /// <param name="allowCoolingPick">
        /// 候選全都在「最近已經不可能拿到金星」的冷卻中時，要不要仍從中挑一個。
        /// 只有最後一個分頁是 true，其他分頁回 null 交給下一個分頁（見 <see cref="GoldUnreachableCooldown.PickBest"/>）。
        /// </param>
        public static unsafe uint? FindBestRelicMission(bool requireCurrentJob = false, bool allowCoolingPick = true)
        {
            string tip = "[Relic XP Finder]";

            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                var maxStage = CosmicHelper.MaxRelicLevel;

                var wksManager = WKSManager.Instance();
                lastRelicPick = default;
                if (wksManager == null || wksManager->ResearchModule == null || !wksManager->ResearchModule->IsLoaded
                    || GameFunctionGuard.ResearchAnalysisMissing())
                {
                    lastRelicPick.NotReady = true;
                    return null;
                }

                var job = Player.JobId;
                var toolClassId = (byte)(job - 7);
                var stage = wksManager->ResearchModule->CurrentStages[toolClassId - 1];
                var nextstate = wksManager->ResearchModule->UnlockedStages[toolClassId - 1];

                if (Svc.Data.GetExcelSheet<WKSCosmoToolClass>().TryGetRow(toolClassId, out var row)) { }

                Dictionary<int, CosmicHelper.XPType> XPTable = new Dictionary<int, CosmicHelper.XPType>();

                if (C.UseDummyXp)
                {
                    XPTable = C.DummyXP;
                }
                else
                {
                    if (stage != maxStage)
                    {
                        for (byte type = 1; type < 6; type++)
                        {
                            if (!wksManager->ResearchModule->IsTypeAvailable(toolClassId, type))
                                break;

                            var neededXP = wksManager->ResearchModule->GetNeededAnalysis(toolClassId, type);

                            var currentXp = wksManager->ResearchModule->GetCurrentAnalysis(toolClassId, type);
                            var requiredXp = neededXP - currentXp;
                            if (!XPTable.ContainsKey(type))
                            {
                                XPTable[type] = new XPType()
                                {
                                    CurrentXP = currentXp,
                                    NeededXP = neededXP,
                                };
                            }
                        }
                    }
                    else
                    {
                        for (byte type = 1; type < 6; type++)
                        {
                            if (!wksManager->ResearchModule->IsTypeAvailable(toolClassId, type))
                                break;

                            var maxXP = wksManager->ResearchModule->GetMaxAnalysis(toolClassId, type);

                            var currentXp = wksManager->ResearchModule->GetCurrentAnalysis(toolClassId, type);
                            var requiredXp = maxXP - currentXp;
                            if (!XPTable.ContainsKey(type))
                            {
                                XPTable[type] = new XPType()
                                {
                                    CurrentXP = currentXp,
                                    NeededXP = maxXP,
                                };
                            }
                        }
                    }
                }

                var urgencies = new Dictionary<int, float>();
                for (int i = 0; i < XPTable.Count; i++)
                {
                    var bar = XPTable[i + 1];
                    urgencies[i + 1] = bar.NeededXP > 0 ? 1f - (float)bar.CurrentXP / bar.NeededXP : 0f;
                    IceLogging.Debug($"XP Type: {i+1} | Urgency: {urgencies[i + 1]}", tip);
                }

                LogRelicXpFullOnce(job, urgencies, stage == maxStage);

                Dictionary<uint, Dictionary<int, float>> rewardMissions = new();
                foreach (var availMission in x.StellerMissions)
                {
                    var id = availMission.MissionId;
                    lastRelicPick.Listed++;
                    if (CosmicHelper.SheetMissionDict.TryGetValue(id, out var mission))
                    {
                        if (requireCurrentJob && !mission.Jobs.Contains(Player.JobId))
                        {
                            IceLogging.Debug($"[Mission: {id}] 不是目前職業的任務，跳過（經驗會加到別的工具上）", tip);
                            continue;
                        }

                        // MissionConfig 與 SheetMissionDict 的鍵集合不一樣。ConfigMigrator.UpdateConfigMissionList() 啟動時會把
                        // SheetMissionDict 的每個 key 補進 MissionConfig，但那是別處建立的隱性前提，所以這裡自己守好。
                        if (!C.MissionConfig.TryGetValue(id, out var missionConfig))
                        {
                            IceLogging.Debug($"[Mission: {id}] 沒有對應的 MissionConfig，跳過。", tip);
                            continue;
                        }

                        int minLevel = 10;
                        var rank = mission.Rank;
                        switch (rank)
                        {
                            case 1:
                                minLevel = 10;
                                break;
                            case 2:
                                minLevel = 50;
                                break;
                            case 3:
                                minLevel = 90;
                                break;
                            case 4:
                            case 5:
                            case 6:
                                minLevel = 100;
                                break;
                            default:
                                minLevel = 10;
                                break;
                        }

                        bool properLevel = Player.Level >= minLevel;
                        bool IgnoreManual = C.XPRelicIgnoreManual && missionConfig.ManualMode;
                        bool IgnoreNotEnabled = C.XPRelicOnlyEnabled && !missionConfig.Enabled;
                        // 宇宙工具經驗模式自己有一套候選池（不走 RefreshSelectedMissions），
                        // 所以這裡也要問同一個判定函式，不要再直接讀黑名單。
                        bool unSupported = MissionSupport.IsUnsupported(id, out var unsupportedReason);

                        PlayerHelper.UpdateHasManip();
                        var jobId = mission.Jobs.Where(x => CosmicHelper.CrafterJobList.Contains(x)).FirstOrDefault();

                        bool manipUnlocked = PlayerHelper.ManipClassInfo.TryGetValue(jobId, out var manipInfo) && manipInfo.HasUnlocked;
                        bool isManipReq = mission.Attributes.HasFlag(MissionAttributes.ExpertCraft);

                        IceLogging.Debug($"[Mission: {id}]" +
                                         $"Is proper Level: {properLevel} | Mission Level: {minLevel} | Player Level: {Player.Level} \n" +
                                         $"Ignoring cause of manual? {IgnoreManual}\n" +
                                         $"Ignoring cuase of not enabled: {IgnoreNotEnabled}\n" +
                                         $"Ignoring because of not supported: {unSupported} ({unsupportedReason})" +
                                         $"Is Manipulation required: {isManipReq}" +
                                         $"Is Manipluation even unlocked: {manipUnlocked}", tip);

                        if (!properLevel) { lastRelicPick.Level++; continue; }
                        if (IgnoreManual) { lastRelicPick.Manual++; continue; }
                        if (IgnoreNotEnabled) { lastRelicPick.NotEnabled++; continue; }
                        if (unSupported)
                        {
                            lastRelicPick.Unsupported++;
                            // ⚠️ 不要靜默跳過。這裡跟 RefreshSelectedMissions 是兩條不同的候選池，
                            //    宇宙工具經驗模式的使用者只會走到這一條。
                            if (EzThrottler.Throttle($"ICE: relic xp skip unsupported {id}", 300000))
                                IceLogging.Info($"宇宙工具經驗挑選跳過任務 [{id}]：{unsupportedReason}"
                                                + $"（{MissionSupport.ReasonText(unsupportedReason)}）", tip);
                            continue;
                        }
                        if (isManipReq && !manipUnlocked) { lastRelicPick.Manip++; continue; }

                        Dictionary<int, float> rewardDict = new();
                        foreach (var reward in mission.RelicXpInfo.OrderBy(x => x.Key))
                        {
                            rewardDict[reward.Key] = reward.Value;
                        }
                        rewardMissions[id] = rewardDict;
                        IceLogging.Debug($"Adding {id} to the potentional missions for relic xp");
                    }
                }

                uint? bestMissionId = null;
                float bestScore = float.NegativeInfinity;
                var scored = new List<(uint Id, float Score)>(rewardMissions.Count);

                foreach (var kvp in rewardMissions)
                {
                    uint missionId = kvp.Key;
                    var reward = kvp.Value;
                    float score = 0f;
                    IceLogging.Debug($"Currently checking mission: {missionId}");

                    foreach (var rewardEntry in reward)
                    {
                        IceLogging.Debug($"Checking for value: {rewardEntry.Key}");
                        if (urgencies.TryGetValue(rewardEntry.Key, out var urgency))
                        {
                            IceLogging.Info($"Checking urgency for: {rewardEntry.Key}");
                            float contribution = urgency * rewardEntry.Value;

                            // Only add positive contributions (high urgency rewards)
                            if (contribution > 0)
                            {
                                score += contribution;
                                IceLogging.Info($"Adding positive score: {contribution}");
                            }
                            else
                            {
                                IceLogging.Debug($"Skipping negative score: {contribution}");
                            }
                        }
                    }

                    scored.Add((missionId, score));

                    // Compare this mission's score with the current best
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMissionId = missionId;
                        IceLogging.Info($"New best mission: {missionId} with score: {score}");
                    }
                }

                if (C.XPRelicAvoidGoldUnreachable && C.XPRelicAvoidGoldUnreachableMinutes > 0)
                {
                    var nowMs = Environment.TickCount64;
                    var minutes = C.XPRelicAvoidGoldUnreachableMinutes;
                    bool Cooling(uint id) => GoldUnreachableCooldown.IsCoolingDown(id, nowMs, minutes);

                    var picked = GoldUnreachableCooldown.PickBest(scored, Cooling, allowCoolingPick);
                    if (picked != bestMissionId)
                    {
                        IceLogging.Info(
                            $"任務 {bestMissionId} 分數最高，但 {minutes} 分鐘內記過「已經不可能拿到金星」，排到最後；"
                            + (picked is uint p ? $"改接 {p}。" : "這個分頁其餘候選也都在冷卻中，交給下一個分頁。"), tip);
                    }
                    else if (picked is uint p && Cooling(p))
                    {
                        IceLogging.Info($"候選全都在「最近已經不可能拿到金星」的冷卻中，照分數仍接 {p}。", tip);
                    }

                    bestMissionId = picked;
                }

                return bestMissionId;
            }
            else
            {
                return null;
            }
        }
        /// <summary>最近一次 <see cref="FindBestRelicMission"/> 把清單上的任務排除掉的原因統計。只在框架執行緒讀寫。</summary>
        private struct RelicPickStats
        {
            public bool NotReady;
            public int Listed, Level, Manual, NotEnabled, Unsupported, Manip;
        }

        private static RelicPickStats lastRelicPick;

        /// <summary>
        /// 宇宙工具經驗模式挑不到任務、也沒辦法重骰時的停止出口：這是設定造成的（多半是「僅限已勾選的任務」
        /// 開著、這個職業卻一個任務都沒勾），不是流程出錯，所以歸 <see cref="StopReasons.NoMission"/>、寫 Information。
        /// </summary>
        private static void StopRelicPoolEmpty()
        {
            var s = lastRelicPick;
            IceLogging.Info(
                $"宇宙工具經驗模式找不到可以接的任務，停止。職業 {Player.JobId}：清單上 {s.Listed} 個任務，"
                + $"未勾選 {s.NotEnabled}、等級不足 {s.Level}、手動模式 {s.Manual}、ICE 跑不動 {s.Unsupported}、需要「掌握」但沒學 {s.Manip}；"
                + $"「僅限已勾選的任務」={(C.XPRelicOnlyEnabled ? "開" : "關")}，這個職業在這個區域勾選的一般任務 {BasicMissionCount} 個"
                + "（0 個就沒辦法重骰換一批）。要繼續的話：在任務表替這個職業勾選任務，或關掉「僅限已勾選的任務」。",
                "[Relic Grind]");
            IceLogging.ChatInfo(
                (C.XPRelicOnlyEnabled && s.NotEnabled > 0
                    ? "Relic XP mode found no mission to take: 'Only selected missions' is on, but none of the listed missions are selected for this job. Select some missions for this job in the mission table, or turn that option off. ICE is stopping."
                    : "Relic XP mode found no mission it can take for this job, so ICE is stopping. See the log for why each mission was skipped.").Loc(),
                "[ICE]");
            SchedulerMain.StopWithNotice("宇宙工具經驗模式找不到可以接的任務", StopReasons.NoMission, needsHelp: true);
            SchedulerMain.DisablePlugin(StopReasons.NoMission);
        }

        /// <summary>已經為哪個職業記過「工具經驗都達標了」；0 ＝ 目前沒有。只在框架執行緒讀寫。</summary>
        private static uint relicXpFullLoggedJob;

        /// <summary>
        /// 工具經驗各類都達到需要量、而且不會自動去繳交研究時，在狀態轉換那一刻記一次 Information。
        /// 只是說明，不改挑選：這時每個任務的經驗分數都是 0，挑到的是清單第一個。
        /// </summary>
        private static void LogRelicXpFullOnce(uint job, Dictionary<int, float> urgencies, bool atMaxStage)
        {
            var allFull = urgencies.Count > 0 && urgencies.Values.All(u => u <= 0f);
            if (!allFull)
            {
                if (relicXpFullLoggedJob == job)
                    relicXpFullLoggedJob = 0;
                return;
            }

            if (relicXpFullLoggedJob == job || (C.TurninRelic && !atMaxStage))
                return;

            relicXpFullLoggedJob = job;
            IceLogging.Info(
                $"職業 {job} 的宇宙工具經驗各類都已達到{(atMaxStage ? "最終階段的上限" : "這一階的需要量")}，"
                + (atMaxStage ? "" : "但沒開「宇宙工具完成時自動回報」，")
                + "接下來挑任務沒有經驗上的依據（每個任務的經驗分數都是 0，會接清單第一個）。"
                + (atMaxStage ? "" : "可以去研究員那裡繳交升級，或開啟自動回報。"),
                "[Relic XP Finder]");
        }

        public static unsafe bool? CheckAllProvisional()
        {
            List<uint> WeatherMissions = new();
            List<uint> SequenceMissions = new();
            List<uint> TimedMissions = new();
            var checkedOtherZone = new HashSet<uint>();
            var checkedUnsupported = new HashSet<uint>();

            // Logic here to sort by the following
            // -> Job Priorty
            // -> If Enabled, throw into the proper list
            // This should make it to where the higher priorty job should be chosen first based on the priorty for the provisional. 
            foreach (var jobId in C.JobPrio)
            {
                foreach (var m in CosmicHelper.SheetMissionDict.Where(x => x.Value.Jobs.Contains(jobId)))
                {
                    bool provisional = m.Value.Attributes.HasFlag(MissionAttributes.ProvisionalSequential)
                                    || m.Value.Attributes.HasFlag(MissionAttributes.ProvisionalTimed)
                                    || m.Value.Attributes.HasFlag(MissionAttributes.ProvisionalWeather);

                    if (!provisional)
                    {
                        continue;
                    }

                    if (C.MissionConfig.TryGetValue(m.Key, out var config) && config.Enabled)
                    {
                        if (m.Value.TerritoryId != Player.Territory)
                        {
                            checkedOtherZone.Add(m.Key);
                            IceLogging.Debug($"Skipping: [{m.Key}] due to being in a different zone");
                            IceLogging.Debug($"Current Zone: {Player.Territory} | Mission Zone: {m.Value.TerritoryId}");
                            continue;
                        }

                        // 「臨時任務連刷」是第三條獨立的候選池（不走 RefreshSelectedMissions
                        // 也不走 FindBestRelicMission），所以同樣要問一次判定函式。
                        if (MissionSupport.IsUnsupported(m.Key, out var provisionalReason))
                        {
                            checkedUnsupported.Add(m.Key);
                            if (EzThrottler.Throttle($"ICE: provisional grind skip unsupported {m.Key}", 300000))
                                IceLogging.Info($"臨時任務連刷跳過任務 [{m.Key}]「{m.Value.Name}」：{provisionalReason}"
                                                + $"（{MissionSupport.ReasonText(provisionalReason)}）",
                                                "[FindMission: 未支援]");
                            continue;
                        }

                        IceLogging.Debug($"Found the provisional mission: [{m.Key}] [{m.Value.Name}].");
                        if (m.Value.Attributes.HasFlag(MissionAttributes.ProvisionalSequential) && !SequenceMissions.Contains(m.Key))
                            SequenceMissions.Add(m.Key);
                        else if (m.Value.Attributes.HasFlag(MissionAttributes.ProvisionalTimed) && !TimedMissions.Contains(m.Key))
                            TimedMissions.Add(m.Key);
                        else if (m.Value.Attributes.HasFlag(MissionAttributes.ProvisionalWeather) && !WeatherMissions.Contains(m.Key))
                            WeatherMissions.Add(m.Key);
                    }
                }
            }

            IceLogging.Debug($"Weather Mission Count: {WeatherMissions.Count}");
            IceLogging.Debug($"Sequence Mission Count: {SequenceMissions.Count}");
            IceLogging.Debug($"Timed Mission Count: {TimedMissions.Count}");

            // 攤平成單一序列：順序與原本「逐類型、類型內逐筆」的雙層迴圈逐筆相同。
            var ordered = new List<uint>();
            foreach (var missionType in C.MissionPrio)
            {
                ordered.AddRange(missionType switch
                {
                    ProvisionalTypes.ProvisionalWeather => WeatherMissions,
                    ProvisionalTypes.ProvisionalSequential => SequenceMissions,
                    ProvisionalTypes.ProvisionalTimed => TimedMissions,
                    _ => new List<uint>()
                });
            }

            if (C.ProvisionalOnlyUngolded)
            {
                var goldState = ReadGoldState(ordered.Concat(checkedOtherZone));
                Func<uint, bool>? isGolded = goldState == null ? null : id => goldState.GetValueOrDefault(id);
                var plan = ProvisionalGoldPlan.Build(ordered, isGolded, MissionChain.GetFollowUpsRecursive);

                if (plan.NothingLeft)
                {
                    StopProvisionalGoldGrind(ordered.Distinct().Count(), checkedOtherZone, checkedUnsupported.Count, goldState);
                    return true;
                }

                if (plan.Order.Count != ordered.Count || !plan.GoldKnown)
                {
                    IceLogging.Debug($"只刷未金星：候選 {ordered.Count}、未金星 {plan.Pending}、為後續保留的已金星前置 {plan.KeptPrerequisites}"
                                     + (plan.GoldKnown ? string.Empty : "（讀不到金星狀態，不過濾）"), "[ICE: Provisional Grind]");
                }
                ordered = plan.Order;
            }

            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                foreach (var id in ordered)
                {
                    var mission = x.StellerMissions.Where(x => x.MissionId == id).FirstOrDefault();
                    if (mission != null)
                    {
                        mission.Select();
                        // 這條路徑沒走 InsertGrabMission，所以要自己標記目標任務。
                        TargetMissionId = id;
                        CollectableUnlock.WarnIfMissing(id);
                        P.TaskManager.InsertMulti
                        (
                            new(() => ChangeJob(id), "Changing job if necessary"),
                            new(() => Navmesh_MoveToMission(id), "Checking if movement is necessary", Utils.TaskConfig),
                            new(() => FrameDelay(8), "Waiting 8 frames before next action"),
                            new(() => GrabMission(id), "Selecting mission for grabbing"),
                            new(() => FrameDelay(16), "Giving time before you kick in the mission")
                        );
                        return true;
                    }
                }

                IceLogging.ChatInfo("Provisional Grind has found no missions.".Loc(), "[ICE: Provisional Grind]");
                IceLogging.ChatInfo("Going to wait ~5s before checking again".Loc(), "[ICE: Provisional Grind]");
                P.TaskManager.EnqueueDelay(5000);
                return true;
            }

            return true;
        }

        /// <summary>讀這些任務的金星旗標；讀不到 <c>WKSManager</c> 時回 null（＝不知道，不是「都沒金星」）。</summary>
        private static unsafe Dictionary<uint, bool>? ReadGoldState(IEnumerable<uint> missionIds)
        {
            var manager = WKSManager.Instance();
            if (manager == null)
                return null;

            var result = new Dictionary<uint, bool>();
            foreach (var id in missionIds)
                result[id] = manager->IsMissionGolded(id);
            return result;
        }

        /// <summary>「只刷未金星」沒有可做的：勾選的全金星，或本星球沒有勾選可跑的限定任務。說明原因後停止。</summary>
        private static void StopProvisionalGoldGrind(int checkedHere, HashSet<uint> checkedOtherZone, int unsupported, Dictionary<uint, bool>? goldState)
        {
            var message = checkedHere == 0
                ? "Provisional Grind (un-golded only): no runnable provisional mission is checked on this planet. Stopping I.C.E.".Loc()
                : "Provisional Grind (un-golded only): all ?? checked provisional missions on this planet are gold. Stopping I.C.E.".Loc(checkedHere);

            var otherPending = goldState == null ? 0 : checkedOtherZone.Count(id => !goldState.GetValueOrDefault(id));
            if (otherPending > 0)
                message += " " + "?? checked provisional mission(s) on another planet are not gold yet.".Loc(otherPending);
            if (unsupported > 0)
                message += " " + "?? checked mission(s) were skipped because ICE cannot run them.".Loc(unsupported);

            IceLogging.ChatInfo(message, "[ICE: Provisional Grind]");
            IceLogging.Info($"只刷未金星的限定任務收工：本星球勾選可跑 {checkedHere}（0＝沒有勾選，否則全數金星）、其他星球未金星 {otherPending}、ICE 跑不動 {unsupported}",
                            "[ICE: Provisional Grind]");

            var reason = checkedHere == 0 ? StopReasons.NoMission : StopReasons.GoalReached;
            if (checkedHere == 0)
                SchedulerMain.StopWithNotice("本星球沒有勾選可跑的限定任務", reason, needsHelp: true);
            else
                SchedulerMain.StopWithNotice("勾選的限定任務都已金星", reason, playSound: true);
            SchedulerMain.DisablePlugin(reason);
        }
    }
}
