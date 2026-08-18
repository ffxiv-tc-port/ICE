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

namespace ICE.Scheduler.Tasks
{
    internal static class Task_FindMission
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
                    // 🔑 這是「自動選任務要跳過」的**唯一**攔截點：緊急／臨時／一般三個分頁
                    //    （CheckCritical / CheckProvisional / CheckStandard）挑的都是下面這幾個
                    //    HashSet，所以擋在建池階段就等於三條路一起擋住，不必各改一次。
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

        /// <summary>
        /// 把這一輪被「不支援」擋掉的任務講出來。
        /// </summary>
        /// <remarks>
        /// ⚠️ 一律寫 <c>Information</c>：使用者的記錄等級會濾掉 Debug/Verbose，寫 Debug 等於沒寫。<br/>
        /// ⚠️ 節流：<c>RefreshSelectedMissions</c> 每一輪找任務都會跑，不節流會把記錄檔洗掉。
        /// log 60 秒一次，聊天視窗 5 分鐘一次，而且<b>名單內容有變就立刻重印</b>
        /// （換職業／換區域會換一整批任務，那時候的舊訊息會誤導人）。<br/>
        /// 🔑 節流器的鍵在 ECommons 裡是<b>全域且跨流程持久</b>的，所以名字取得夠獨特，
        /// 不要跟別的功能共用而互相拖累。
        /// </remarks>
        private static string lastSkippedSignature = string.Empty;

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
                // ⚠️ 這裡會直接把外掛停掉。原本只寫 Debug —— 使用者的記錄等級濾掉 Debug 之後，
                //    症狀就是「ICE 自己關了、沒有任何訊息」。排除不支援任務之後這條路徑更容易走到
                //    （整批啟用的任務可能全被跳過），所以改成 Information + 聊天視窗。
                IceLogging.ChatInfo(
                    "No runnable mission is enabled for the current job in this zone, so ICE is stopping. (Relic XP mode is off.)".Loc()
                    + (lastSkippedSignature.Length > 0
                        ? " " + "Some enabled missions were skipped because ICE cannot run them - see the message above.".Loc()
                        : string.Empty),
                    "[ICE]");
                SchedulerMain.State = IceState.Idle;
                SchedulerMain.DisablePlugin();
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
                    // ⚠️ 先在 unsafe 區塊把「是否已金星」算成純量再排序，不要把原生指標放進
                    //    OrderBy 的 lambda —— 那等於跨呼叫持有指標，正是要避免的那一類問題。
                    // ⚠️ WKSManager.Instance() 可能是 null（還沒進入宇宙探索內容），此時不排序，
                    //    行為與關閉此選項完全相同。
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
        /// 是否只接受「任務職業包含目前職業」的候選。緊急／臨時分頁會列出其他職業的任務，
        /// 而宇宙工具經驗是加在<b>任務所屬職業</b>的工具上，接錯職業等於練錯工具。
        /// 一般任務分頁本來就已依職業分頁，所以維持 false 以免動到既有行為。
        /// </param>
        private static unsafe bool? CheckExp(bool isFallbackTab = true, bool requireCurrentJob = false)
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                var bestIndex = FindBestRelicMission(requireCurrentJob);

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
                        // 🔴 迭代 MissionConfig（鍵集合含 0，實測使用者設定檔就是 0..544）
                        //    卻直接索引 SheetMissionDict（鍵集合是 1..544）。
                        //    現在沒炸只是因為 key 0 的 Enabled 預設是 false，短路把它擋掉了 ——
                        //    那是預設值湊巧，不是守衛。
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
                    else
                    {
                        IceLogging.Error($"Okay, something is wrong. Stopping the process", "[Relic Grind]");
                        SchedulerMain.DisablePlugin();
                    }
                }
            }

            return false;
        }
        /// <param name="requireCurrentJob">
        /// 只保留「任務職業包含目前職業」的候選。緊急／臨時分頁會列出其他職業的任務，
        /// 而經驗是加在任務所屬職業的宇宙工具上，所以那兩個分頁必須開啟這個過濾。
        /// </param>
        public static unsafe uint? FindBestRelicMission(bool requireCurrentJob = false)
        {
            string tip = "[Relic XP Finder]";

            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                var maxStage = CosmicHelper.MaxRelicLevel;

                var wksManager = WKSManager.Instance();
                if (wksManager == null || wksManager->ResearchModule == null || !wksManager->ResearchModule->IsLoaded)
                    return null;

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

                Dictionary<uint, Dictionary<int, float>> rewardMissions = new();
                foreach (var availMission in x.StellerMissions)
                {
                    var id = availMission.MissionId;
                    if (CosmicHelper.SheetMissionDict.TryGetValue(id, out var mission))
                    {
                        if (requireCurrentJob && !mission.Jobs.Contains(Player.JobId))
                        {
                            IceLogging.Debug($"[Mission: {id}] 不是目前職業的任務，跳過（經驗會加到別的工具上）", tip);
                            continue;
                        }

                        // 同一個 key 守了 SheetMissionDict 卻直接索引 MissionConfig。
                        // 現在不會炸只是因為 ConfigMigrator.UpdateConfigMissionList() 啟動時把
                        // SheetMissionDict 的每個 key 都補進了 MissionConfig —— 那是別處建立的隱性前提，
                        // 順序一改或漏補一筆就是 KeyNotFoundException，所以這裡自己守好。
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

                        if (!properLevel) continue;
                        if (IgnoreManual) continue;
                        if (IgnoreNotEnabled) continue;
                        if (unSupported)
                        {
                            // ⚠️ 不要靜默跳過。這裡跟 RefreshSelectedMissions 是兩條不同的候選池，
                            //    宇宙工具經驗模式的使用者只會走到這一條。
                            if (EzThrottler.Throttle($"ICE: relic xp skip unsupported {id}", 300000))
                                IceLogging.Info($"宇宙工具經驗挑選跳過任務 [{id}]：{unsupportedReason}"
                                                + $"（{MissionSupport.ReasonText(unsupportedReason)}）", tip);
                            continue;
                        }
                        if (isManipReq && !manipUnlocked) continue;

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

                    // Compare this mission's score with the current best
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMissionId = missionId;
                        IceLogging.Info($"New best mission: {missionId} with score: {score}");
                    }
                }

                return bestMissionId;
            }
            else
            {
                return null;
            }
        }
        public static unsafe bool? CheckAllProvisional()
        {
            List<uint> WeatherMissions = new();
            List<uint> SequenceMissions = new();
            List<uint> TimedMissions = new();

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
                            IceLogging.Debug($"Skipping: [{m.Key}] due to being in a different zone");
                            IceLogging.Debug($"Current Zone: {Player.Territory} | Mission Zone: {m.Value.TerritoryId}");
                            continue;
                        }

                        // 「臨時任務連刷」是第三條獨立的候選池（不走 RefreshSelectedMissions
                        // 也不走 FindBestRelicMission），所以同樣要問一次判定函式。
                        if (MissionSupport.IsUnsupported(m.Key, out var provisionalReason))
                        {
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

            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                foreach (var missionType in C.MissionPrio)
                {
                    List<uint> missionIds = missionType switch
                    {
                        ProvisionalTypes.ProvisionalWeather => WeatherMissions,
                        ProvisionalTypes.ProvisionalSequential => SequenceMissions,
                        ProvisionalTypes.ProvisionalTimed => TimedMissions,
                        _ => new List<uint>()
                    };

                    if (missionIds.Count == 0)
                        continue;

                    foreach (var id in missionIds)
                    {
                        var mission = x.StellerMissions.Where(x => x.MissionId == id).FirstOrDefault();
                        if (mission != null)
                        {
                            mission.Select();
                            // 這條路徑沒走 InsertGrabMission，所以要自己標記目標任務。
                            TargetMissionId = id;
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
                }

                IceLogging.ChatInfo("Provisional Grind has found no missions.".Loc(), "[ICE: Provisional Grind]");
                IceLogging.ChatInfo("Going to wait ~5s before checking again".Loc(), "[ICE: Provisional Grind]");
                P.TaskManager.EnqueueDelay(5000);
                return true;
            }

            return true;
        }
        public static bool? FindReroll()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                List<uint> AExRank = new List<uint>();
                List<uint> ARank = new List<uint>();
                List<uint> BRank = new List<uint>();
                List<uint> CRank = new List<uint>();
                List<uint> DRank = new List<uint>();

                // Track mission appearance counts
                foreach (var mission in x.StellerMissions)
                {
                    var missionId = mission.MissionId;

                    // Increment appearance count
                    if (!Mission_Settings.missionAppearanceCounts.ContainsKey(missionId))
                        Mission_Settings.missionAppearanceCounts[missionId] = 0;
                    Mission_Settings.missionAppearanceCounts[missionId]++;

                    // 🔴 上面守的是 missionAppearanceCounts，下一行索引的卻是 SheetMissionDict ——
                    //    「守了 A 字典、直接索引 B 字典」的典型形狀。
                    //    而且這裡的 missionId 是從遊戲的 WKSMission addon 的 AtkValues 讀出來的
                    //    （ECommons 只濾掉 0），不是我們驗證過的鍵集合：只要索引偏移對不上或
                    //    未來開了第二顆星（row 545 以上在台服全是空 Name、不在 SheetMissionDict 裡），
                    //    這一行就會丟 KeyNotFoundException，而它在任務裡＝佇列卡死。
                    if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var rerollMission))
                    {
                        IceLogging.Info($"任務板上出現了不在任務表裡的任務 ID {missionId}，重骰判斷時略過它。", "[Task_FindMission: FindReroll]");
                        continue;
                    }

                    var rank = rerollMission.Rank;
                    switch (rank)
                    {
                        case 5: AExRank.Add(missionId); break;
                        case 4: ARank.Add(missionId); break;
                        case 3: BRank.Add(missionId); break;
                        case 2: CRank.Add(missionId); break;
                        case 1:
                        default: DRank.Add(missionId); break;
                    }
                }

                bool CheckARanks = (ExARankMissions.Count > 0 || ARankMissions.Count > 0) && (AExRank.Count > 0 || ARank.Count > 0);
                bool CheckBRanks = (BRankMissions.Count > 0 && BRank.Count > 0);
                bool CheckCRanks = (CRankMissions.Count > 0 && CRank.Count > 0);
                bool CheckDRanks = (DRankMissions.Count > 0 && DRank.Count > 0);

                var random = new Random();
                void ShuffleList<T>(List<T> list, Random rnd)
                {
                    for (int i = list.Count - 1; i > 0; i--)
                    {
                        int j = rnd.Next(i + 1);
                        (list[i], list[j]) = (list[j], list[i]);
                    }
                }

                ShuffleList(AExRank, random);
                ShuffleList(ARank, random);
                ShuffleList(BRank, random);
                ShuffleList(CRank, random);
                ShuffleList(DRank, random);

                // small function to find a frequent mission that might be locking us
                uint FindFrequentMission(List<uint> missionList, int threshold = 3)
                {
                    foreach (var missionId in missionList)
                    {
                        if (Mission_Settings.missionAppearanceCounts.TryGetValue(missionId, out int count) && count >= threshold)
                        {
                            return missionId;
                        }
                    }
                    return 0;
                }

                if (CheckARanks)
                {
                    // Check for frequent missions in A/AEx ranks first
                    uint frequentAEx = FindFrequentMission(AExRank, Mission_Settings.rerollThreshold);
                    uint frequentA = FindFrequentMission(ARank, Mission_Settings.rerollThreshold);

                    if (AExRank.Count > 2)
                    {
                        if (frequentAEx != 0)
                        {
                            missionToAbandon = frequentAEx;
                            IceLogging.Debug($"Abandoning frequently appearing AEX mission (appeared {Mission_Settings.missionAppearanceCounts[frequentAEx]} times)");
                            Mission_Settings.previouslyAbandoned = 5;
                        }
                        else
                        {
                            IceLogging.Debug($"Only AEX Rank missions are available. Forcing an AEX rank to be accepted");
                            missionToAbandon = AExRank.First();
                            Mission_Settings.previouslyAbandoned = 5;
                        }
                    }
                    else if (ARank.Count > 2)
                    {
                        if (frequentA != 0)
                        {
                            missionToAbandon = frequentA;
                            IceLogging.Debug($"Abandoning frequently appearing A mission (appeared {Mission_Settings.missionAppearanceCounts[frequentA]} times)");
                            Mission_Settings.previouslyAbandoned = 4;
                        }
                        else
                        {
                            IceLogging.Debug($"Only A Rank missions are available. Forcing an A rank to be accepted");
                            missionToAbandon = ARank.First();
                            Mission_Settings.previouslyAbandoned = 4;
                        }
                    }
                    else
                    {
                        if (Mission_Settings.previouslyAbandoned == 5)
                        {
                            if (frequentA != 0)
                            {
                                missionToAbandon = frequentA;
                                IceLogging.Debug($"Abandoning frequently appearing A mission (appeared {Mission_Settings.missionAppearanceCounts[frequentA]} times)");
                                Mission_Settings.previouslyAbandoned = 4;
                            }
                            else
                            {
                                missionToAbandon = ARank.First();
                                IceLogging.Debug($"Abandoning Rank 4 Mission.");
                                Mission_Settings.previouslyAbandoned = 4;
                            }
                        }
                        else if (Mission_Settings.previouslyAbandoned == 4)
                        {
                            if (frequentAEx != 0)
                            {
                                missionToAbandon = frequentAEx;
                                IceLogging.Debug($"Abandoning frequently appearing AEX mission (appeared {Mission_Settings.missionAppearanceCounts[frequentAEx]} times)");
                                Mission_Settings.previouslyAbandoned = 5;
                            }
                            else
                            {
                                missionToAbandon = AExRank.First();
                                IceLogging.Debug($"Abandoning Rank 5 Mission");
                                Mission_Settings.previouslyAbandoned = 5;
                            }
                        }
                        else
                        {
                            missionToAbandon = ARank.First();
                            IceLogging.Debug($"Starting off w/ abandoning an A rank");
                            Mission_Settings.previouslyAbandoned = 4;
                        }
                    }
                }
                else if (CheckBRanks)
                {
                    uint frequentB = FindFrequentMission(BRank, Mission_Settings.rerollThreshold);
                    if (frequentB != 0)
                    {
                        missionToAbandon = frequentB;
                        IceLogging.Debug($"Abandoning frequently appearing B mission (appeared {Mission_Settings.missionAppearanceCounts[frequentB]} times)");
                    }
                    else
                    {
                        missionToAbandon = BRank.First();
                    }
                    Mission_Settings.previouslyAbandoned = 3;
                }
                else if (CheckCRanks)
                {
                    uint frequentC = FindFrequentMission(CRank, Mission_Settings.rerollThreshold);
                    if (frequentC != 0)
                    {
                        missionToAbandon = frequentC;
                        IceLogging.Debug($"Abandoning frequently appearing C mission (appeared {Mission_Settings.missionAppearanceCounts[frequentC]} times)");
                    }
                    else
                    {
                        missionToAbandon = CRank.First();
                    }
                    Mission_Settings.previouslyAbandoned = 2;
                }
                else if (CheckDRanks)
                {
                    uint frequentD = FindFrequentMission(DRank, Mission_Settings.rerollThreshold);
                    if (frequentD != 0)
                    {
                        missionToAbandon = frequentD;
                        IceLogging.Debug($"Abandoning frequently appearing D mission (appeared {Mission_Settings.missionAppearanceCounts[frequentD]} times)");
                    }
                    else
                    {
                        missionToAbandon = DRank.First();
                    }
                    Mission_Settings.previouslyAbandoned = 1;
                }

                if (missionToAbandon != 0)
                {
                    var abandonMission = x.StellerMissions.First(m => m.MissionId == missionToAbandon);
                    abandonMission.Select();
                    P.TaskManager.Insert(() => GrabMission(missionToAbandon, true), "Going to abandon mission now");
                    // ✅ 曾經是零守衛的字典索引，已修：守衛＝下一行的 TryGetValue。
                    //    這裡只是為了印一行 log，沒有任何理由讓它有機會把佇列打斷。
                    var abandonRank = CosmicHelper.SheetMissionDict.TryGetValue(missionToAbandon, out var abandonEntry)
                        ? abandonEntry.Rank.ToString()
                        : "不在任務表裡";
                    IceLogging.Debug($"Attempting to abandon mission ID: {missionToAbandon} (Rank: {abandonRank})");
                    Mission_Settings.missionAppearanceCounts[missionToAbandon] = 0;

                    return true;
                }
                else
                {
                    // 🔴 這條路徑 `return false` ＝ NeoTaskManager **下一幀原地重跑同一個任務**
                    //    （在 NeoTaskManager 裡 false 才是「還沒好，再來一次」，null 是中止整個佇列）。
                    //    候選池空掉時條件不會自己改變，所以這行原本會一路噴到任務逾時為止。
                    //    只節流 log，控制流完全不動。EzThrottler 首次必放行 ⇒ 第一次仍看得到。
                    if (EzThrottler.Throttle("ICE: findreroll no abandon candidate", 5000))
                        IceLogging.Debug("No valid missions found to abandon");
                    return false;
                }
            }

            return false;
        }
        public static void InsertGrabMission(uint missionId)
        {
            // 這裡就是「已經決定要領哪一個」的時間點 —— 疊加層要顯示的正是這個。
            TargetMissionId = missionId;
            // 真的挑到任務了，這一輪的「換職業已試過清單」就結束了。
            // ⚠️ 不能只靠 CheckReroll 裡那個 ExecutingMission 分支清 —— 挑到任務的那一輪
            //    CheckStandard 直接 return true，根本不會再走到 CheckReroll。
            triedJobsThisSweep.Clear();
            P.TaskManager.InsertMulti(
                new(() => Navmesh_MoveToMission(missionId), "Checking if movement is necessary", Utils.TaskConfig),
                new(() => FrameDelay(8), "Waiting 8 frames before next action"),
                new(() => GrabMission(missionId), "Selecting mission for grabbing"),
                new(() => FrameDelay(16), "Giving time before you kick in the mission")
            );
        }
        private static bool? GrabMission(uint missionId, bool reroll = false)
        {
            if (CosmicHelper.CurrentLunarMission != 0)
            {
                // 任務真的接下來了，「正要去領」的目標就過期了。
                ClearTargetMission();
                Mission_Settings.ResetNodeCounter();
                SchedulerMain.State = IceState.ExecutingMission;
                timeoutAmount = 0;
                return true;
            }
            else if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
            {
                if (EzThrottler.Throttle("Selecting Yesno window"))
                {
                    if (CosmicHandler.commenceStrings.Any(s => NormalizeWhitespace(select.Text).StartsWith(NormalizeWhitespace(s), StringComparison.OrdinalIgnoreCase)) || !C.RejectUnknownYesno)
                    {
                        select.Yes();
                        if (reroll)
                            SchedulerMain.State = IceState.AbandonMission;
                        else
                            SchedulerMain.State = IceState.ExecutingMission;
                        IceLogging.Debug($"Current State upon  grabbing mission: {SchedulerMain.State}");
                        P.TaskManager.Tasks.Clear();
                        Mission_Settings.nodeTotal = 0;
                        P.TaskManager.Insert(() => CosmicHelper.CurrentLunarMission != 0);
                        IceLogging.Debug($"Are we expected to reroll? {reroll}", "[Grab Mission]");

                        return true;
                    }
                    else
                    {
                        IceLogging.Debug($"Unexpected text: '{select.Text}'", "[ICE_GrabMission]");

                        if (EzThrottler.Throttle("Unexpected Abandon Window..."))
                        {
                            select.No();
                            return false;
                        }
                    }
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var mission) && mission.IsAddonReady)
            {
                if (!reroll)
                {
                    if (FrameThrottler.Throttle("Timeout Check", 16))
                    {
                        timeoutAmount+= 1;
                    }

                    if (timeoutAmount >= maxTimeout)
                    {
                        IceLogging.Info("We've met the timeout threshold on grabbing a mission. Unsure if the timer just... ran out or something? *-shrugs-* starting the whol process again");
                        SchedulerMain.State = IceState.GrabMission;
                        P.TaskManager.Tasks.Clear();
                        timeoutAmount = 0;
                        return true;
                    }
                }

                var selectedMission = mission.StellerMissions.Where(x => x.MissionId == missionId).FirstOrDefault();
                if (selectedMission != null)
                {
                    if (EzThrottler.Throttle("Initating the quest"))
                    {
                        selectedMission.Initiate();
                    }
                    return false;
                }
                if (FrameThrottler.Throttle("Checking tab for mission", 8))
                {
                    if (FrameThrottler.Throttle("Checking Weather Tab for mission", 16))
                    {
                        mission.ProvisionalMissions();
                        return false;
                    }
                    if (FrameThrottler.Throttle("Checking Standard Tab for missions", 16))
                    {
                        mission.BasicMissions();
                        return false;
                    }
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud) && moonHud.IsAddonReady)
            {
                if (EzThrottler.Throttle("Opening the moon hud cause it somehow just slipped through/got turned off", 2000))
                {
                    moonHud.Mission();
                }
            }

            return false;
        }

        private static Vector3 fishingHoleLoc = Vector3.Zero;
        public static unsafe bool? Navmesh_MoveToMission(uint missionId)
        {
            ThrottleMessage("Currently in a navmesh movement");

            // ✅ 曾經是零守衛的字典索引 ×2，已修：兩個字典的鍵集合不同，所以分開守 ——
            //    守衛＝下一行的 SheetMissionDict.TryGetValue 與下方的 C.MissionConfig.TryGetValue。
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var missionEntry))
            {
                IceLogging.ChatError($"任務 {missionId} 不在任務表裡，無法前往任務地點。", "[ICE]");
                SchedulerMain.State = IceState.GrabMission;
                P.TaskManager.Tasks.Clear();
                return true;
            }

            if (!C.MissionConfig.TryGetValue(missionId, out var missionConfig))
            {
                IceLogging.ChatError($"任務 {missionId} 在設定檔裡沒有對應的設定，無法前往任務地點。", "[ICE]");
                SchedulerMain.State = IceState.GrabMission;
                P.TaskManager.Tasks.Clear();
                return true;
            }
            var currentJob = Player.JobId;

            if (!missionEntry.Jobs.Contains(currentJob))
            {
                IceLogging.Error("Somehow, we've managed to get a job that isn't suppose to be an option for our jobs?? Resetting the whole process and going to try again.\n" +
                                 "If this continues on multiple times in a row, let me know.");
                SchedulerMain.State = IceState.GrabMission;
                P.TaskManager.Tasks.Clear();
                return true;
            }

            if (missionConfig.ManualMode || MissionSupport.IsUnsupported(missionId))
            {
                // TODO: Remove the extra 2 here until I can fix pathfinding thing
                // This is here to make sure that you don't need to be in the area for moving. Mainly cause nodes aren't mapped out yet and it's expecting to map to that area...
                IceLogging.Info("Found a mission that is either in Manual mode, or unsupported. Continuing on", "[FindMission: NavmeshMoveTo]");
                return true;
            }
            else if (!P.Navmesh.Installed)
            {
                IceLogging.Error("HEY. YOU DIDN'T READ THE INFO PAGE DID YOU HUH. Navmesh isn't installed.... sooo... yeah this is unfort. Read the info page on the main page. I ain't going to hold your hand on this one");
                return true;
            }
            else if (missionEntry.Attributes.HasFlag(MissionAttributes.Gather)) // TODO: Fix critical thingy
            {
                // Mission was found to be a gathering or critical mission, seeing if you're within range of it
                Vector2 PlayerPos = new Vector2(Player.Position.X, Player.Position.Z);
                Vector2 MapCenter = missionEntry.MapPosition;
                var missionTerritory = missionEntry.TerritoryId;
                var mapId = missionEntry.MapPosition;
                var gatherInfo = GatheringRouteLoader.GetRoute(missionTerritory, mapId);

                // ⚠️ GetRoute 查不到路線時回 null，不是空清單 —— 原本直接 .Count 是
                //    NullReferenceException，表現成「卡住不動而且沒有訊息」。
                if (gatherInfo == null || gatherInfo.Count == 0)
                {
                    IceLogging.Info("HEY. This gathering location hasn't been set to gather, and should honestly be set to a manual state. Cause things are about to bug out. If it's a new area please let me know o/");
                    return true;
                }

                // 🔴 原本這裡寫死 `gatherInfo[0].LandZone` —— 不管人站在哪，第一次進場一律
                //    先走去「路線檔的第一個點」。路線是環狀的（PathandCheckNode 會遞增並回繞），
                //    從哪一個點起跑都合法，所以挑最近的那個；用路線檔自己的座標算，
                //    不依賴採集點有沒有載進 ObjectTable。
                var entryNode = gatherInfo.OrderBy(x => Player.DistanceTo(x.LandZone)).First();
                Vector3 closestNode = entryNode.LandZone;
                if (EzThrottler.Throttle("ICE: gather route entry node", 5000))
                    IceLogging.Info($"前往採集區域：路線共 {gatherInfo.Count} 個點，" +
                                    $"選最近的採集點 {entryNode.NodeId}（距離 {Player.DistanceTo(entryNode.LandZone):N1}）當入口。",
                                    "[FindMission: NavmeshMoveTo]");

                if (!P.Navmesh.IsRunning())
                {
                    foreach (var node in gatherInfo)
                    {
                        if (Player.DistanceTo(node.Position) < 5)
                        {
                            IceLogging.Debug("We're close to a gathering node. So continuing on.");
                            return true;
                        }
                    }
                }
                
                if (!Task_NavmeshMove.NavToDestination(closestNode))
                {
                    return false;
                }

                /*
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                    return false;
                }
                else if (!P.Navmesh.IsRunning())
                {
                    if (!Svc.Condition[ConditionFlag.Unknown101])
                    {
                        foreach (var node in gatherInfo)
                        {
                            if (Player.DistanceTo(node.Position) < 5)
                            {
                                IceLogging.Debug("We're close to a gathering node. So continuing on.");
                                return true;
                            }
                        }

                        // We ideally don't want to be trying to try and pathfind while on this. Need to wait for us to get off the hoverboard
                        if (EzThrottler.Throttle("Inializing movement for pathfinding"))
                        {
                            IceLogging.DestinationLogs.Log(closestNode);
                            P.Navmesh.PathfindAndMoveTo(closestNode, false);
                        }
                    }
                }
                else if (P.Navmesh.IsRunning())
                {
                    if (Svc.Condition[ConditionFlag.Unknown101])
                    {
                        // We're currently using the cosmoliners, telling it to stop the current navmesh in the mean time
                        if (EzThrottler.Throttle("Stopping navmesh temp"))
                        {
                            P.Navmesh.Stop();
                        }
                    }
                    if (C.UseMountOutsideMission && !Svc.Condition[ConditionFlag.Mounted] && !Player.IsBusy)
                    {
                        if (Player.DistanceTo(closestNode) > C.MountRadius)
                        {
                            if (EzThrottler.Throttle("Attemping to mount up for btn/min"))
                            {
                                Utils.MountAction();
                            }
                        }
                    }
                    else if (Svc.Condition[ConditionFlag.Mounted] && Player.DistanceTo(closestNode) < C.DismountRadius)
                    {
                        if (EzThrottler.Throttle("Dismounting from mount"))
                        {
                            Utils.Dismount();
                        }
                    }
                }
                */
            }
            else if (missionEntry.Attributes.HasFlag(MissionAttributes.Fish))
            {
                // Just a way to handle fishing missions specifically (that isn't under the critical unbrella).
                // Need to generate a path to the pre-set spot per fishing hole
                // Then need to add the last path to it once the base has been generated, to make sure that you're facing to the fishing hole properly.
                var location = missionEntry.MapPosition;
                var territory = missionEntry.TerritoryId;

                // 🔴 原本這裡是 `GatheringUtil.MoonFishingLocations[territory][location]` —— 兩層裸索引。
                //    MoonFishingLocations 是**寫死在原始碼裡的釣點座標表**（上游照國際服建的），
                //    外層鍵是區域 ID、內層鍵是任務的地圖旗標座標，而且內層是 Vector2 的
                //    **浮點數相等比對** —— 跟遊戲資料之間沒有任何共同保證。
                //
                //    台服 7.20 目前對得起來：離線重跑建表邏輯比對 exd-tc/7.20 的
                //    WKSMissionUnit／WKSMissionToDo／WKSMissionMapMarker，[1237] 有 9 個旗標，
                //    而台服 52 個釣魚任務算出來的旗標**正好就是那 9 個**，一個不多一個不少。
                //    ⚠️ 但 [1291] Phaenna 的 11 個旗標無法離線驗證（台服那些任務列目前整列是空的）。
                //    第二顆星開放當天只要有任何一個任務的旗標對不上，這行就是 KeyNotFoundException，
                //    而它跑在任務佇列裡：丟例外 → 這個任務永遠不回傳 true → 逾時 → 佇列被中止
                //    → 外掛自己停用，也就是使用者看到的「一進去就卡死」。
                //
                //    ⚠️ 下面原本那個 `fishingHole == null` 檢查是**死碼**：字典 indexer 找不到鍵是
                //    丟例外、不是回 null，所以它從來沒有機會執行。
                //    改成查得到才往下走；查不到就照本函式「採集路線缺資料」那條既有分支的作法
                //    （上方 gatherInfo.Count == 0）記一筆並 return true 放行，降級成手動處理。
                if (!GatheringUtil.MoonFishingLocations.TryGetValue(territory, out var territorySpots)
                    || !territorySpots.TryGetValue(location, out var fishingHole)
                    || fishingHole.Count == 0)
                {
                    if (EzThrottler.Throttle("ICE: fishing hole data missing", 5000))
                    {
                        IceLogging.Info(
                            $"找不到這個釣魚任務的釣點資料，略過自動前往（這個任務請改用手動模式）。" +
                            $"任務 {missionId}／區域 {territory}／地圖旗標 ({location.X}, {location.Y})。" +
                            "（釣點座標是寫死在 GatheringUtil.MoonFishingLocations 裡的，" +
                            "這個客戶端新開放的區域還沒有對應資料時就會走到這裡。）",
                            "[FindMission: 釣點]");
                    }
                    return true;
                }

                var navPos = Vector3.Zero;

                if (!P.Navmesh.IsRunning())
                {
                    foreach (var fishSpot in fishingHole)
                    {
                        if (Player.DistanceTo(fishSpot.FishingSpot) < 2)
                        {
                            IceLogging.Info("We're currently at a fishing spot! Continuing onto facing position -> grabbing the mission");
                            fishingHoleLoc = Vector3.Zero;
                            return true;
                        }
                    }
                }

                if (fishingHoleLoc == Vector3.Zero)
                {
                    var _random = new Random();
                    var randomIndex = _random.Next(fishingHole.Count);
                    if (EzThrottler.Throttle("Setting destination"))
                    {
                        IceLogging.Debug($"Random number generator said we're going to the following fishing hole #: {randomIndex}");
                        fishingHoleLoc = fishingHole[randomIndex].FishingSpot;
                    }
                }
                else
                {
                    if (!Task_NavmeshMove.NavToDestination(fishingHoleLoc))
                    {
                        if (EzThrottler.Throttle("Waitin for nav to finish"))
                        {
                            IceLogging.Debug($"Waiting for navmesh to get to: {fishingHoleLoc}");
                        }
                        return false;
                    }
                }

                /*
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                }
                if (!P.Navmesh.IsRunning())
                {
                    if (!Svc.Condition[ConditionFlag.Unknown101])
                    {
                        foreach (var fishSpot in fishingHole)
                        {
                            if (Player.DistanceTo(fishSpot.FishingSpot) < 2)
                            {
                                IceLogging.Info("We're currently at a fishing spot! Continuing onto facing position -> grabbing the mission");
                                return true;
                            }
                        }
                        if (EzThrottler.Throttle("Inializing movement for pathfinding"))
                        {
                            var _random = new Random();
                            var randomIndex = _random.Next(fishingHole.Count);
                            IceLogging.Debug($"Random number generator said we're going to the following fishing hole #: {randomIndex}");
                            if (randomIndex < fishingHole.Count)
                            {
                                Vector3 navPos = fishingHole[randomIndex].FishingSpot;
                                IceLogging.DestinationLogs.Log(navPos);
                                P.Navmesh.PathfindAndMoveTo(navPos, false);
                                fishingHoleLoc = navPos;
                                IceLogging.Debug($"Told navmesh to move to the following spot: {fishingHoleLoc}");
                            }
                        }
                    }
                }
                else if (P.Navmesh.IsRunning())
                {
                    if (Svc.Condition[ConditionFlag.Unknown101])
                    {
                        // We're currently using the cosmoliners, telling it to stop the current navmesh in the mean time
                        if (EzThrottler.Throttle("Stopping navmesh temp"))
                        {
                            IceLogging.Debug("Telling navmesh to stop cause on a cosmoliner");
                            P.Navmesh.Stop();
                            fishingPath.Clear();
                        }
                    }
                    if (C.UseMountOutsideMission && !Svc.Condition[ConditionFlag.Mounted] && !Player.IsBusy)
                    {
                        if (Player.DistanceTo(fishingHoleLoc) > C.MountRadius)
                        {
                            if (EzThrottler.Throttle("Attemping to mount up for btn/min"))
                            {
                                Utils.MountAction();
                            }
                        }
                    }
                    else if (Svc.Condition[ConditionFlag.Mounted] && Player.DistanceTo(fishingHoleLoc) < C.DismountRadius)
                    {
                        if (EzThrottler.Throttle("Dismounting from mount"))
                        {
                            Utils.Dismount();
                        }
                    }
                }
                */
            }
            else
            {
                IceLogging.Debug("Mission was not a gathering or critical mission. Navmesh moving was not necessary. Moving onto next step", "[Task_FindMission: Navmesh]");
                return true;
            }

            return false;
        }
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
                // 使用者 2026-07-31 回報的正是這個情形。
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
                    SchedulerMain.State = IceState.Idle;
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
        /// 🔑 「那個職業有沒有東西可挑」用 <see cref="CountViableMissions"/> 判斷，它的過濾條件與
        /// <see cref="RefreshSelectedMissions"/> 逐條相同 —— 兩邊只要不一致，就會換到一個同樣挑不出
        /// 任務的職業，然後在職業之間空轉。<br/>
        /// ⚠️ 只走 <c>C.JobPrio</c> 裡列出來的職業。設定介面只能拖曳排序、不能增刪，所以正常情況下
        /// 11 個職業都在；真的缺了就寫一行 Information 講出來，不要默默把使用者移掉的職業加回去。<br/>
        /// ⚠️ 這條路徑只有標準任務流程走得到（宇宙工具經驗模式與臨時任務連刷都不經過重骰上限），
        /// 所以不必再判斷那兩個模式。
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
        /// 改其中一邊記得改另一邊，不然會換到一個同樣挑不出任務的職業。<br/>
        /// 🔴 <c>WKSManager.Instance()</c> 的槽位內容在宇宙區外是 null，解參考＝AVE，
        /// 而 AVE 是 corrupted-state exception，<c>try/catch</c> 攔不到。所以先判空，
        /// 判不到就把 <c>GoldKnown</c> 回 false，讓呼叫端自己決定怎麼退。<br/>
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
        /// 🔴 <c>Mission_Settings.StartJob</c> 一定要等「換成功」之後才更新：交件流程的
        /// <c>Task_TurninMission.JobSwapCheck</c> 每次交完任務都會把職業換回 StartJob，不更新的話
        /// 下一件交完就被換回原本那個「沒任務可接」的職業，等於白換；但要是換失敗還先寫進去，
        /// JobSwapCheck 就會一直想換去一個換不過去的職業。<br/>
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
            // ✅ 曾經是零守衛的字典索引，已修：守衛＝下一行的 TryGetValue。
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
