using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using ICE.Sounds;
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

        public static void Enqueue()
        {
            IceLogging.Info("Starting the find mission queue", "[Task Find Mission]");
            P.TaskManager.Enqueue(RefreshMissionUi, "Refreshing Mission UI");
            P.TaskManager.Enqueue(OpenMissionUi, "Opening it on proper class");
            P.TaskManager.Enqueue(RefreshSelectedMissions, "Refreshing the list of viable missions");
            if (C.XPRelicGrind)
            {
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

            return true;
        }
        public static bool? TabTasksCheck()
        {
            bool hasCritical = CriticalMissions.Count > 0;
            bool hasSpecial = SpecialMissionCount > 0;
            bool hasBasic = BasicMissionCount > 0;

            if (!(hasCritical || hasSpecial || hasBasic))
            {
                IceLogging.Debug("You have... no active missions and you're not on relic grinding mode. Disabling this for now");
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

                    // 「沒金星的優先」：同一階級之內，把還沒拿到金星的任務排前面（補完成度用）。
                    // ⚠️ 先在 unsafe 區塊把「是否已金星」算成純量再排序，不要把原生指標放進
                    //    OrderBy 的 lambda —— 那等於跨呼叫持有指標，正是要避免的那一類問題。
                    // ⚠️ WKSManager.Instance() 可能是 null（還沒進入宇宙探索內容），此時不排序，
                    //    行為與關閉此選項完全相同。
                    if (C.PrioritizeUngoldedMissions && candidates.Count > 1)
                    {
                        Dictionary<uint, int> goldRank = new();
                        unsafe
                        {
                            var mgr = (WKSManagerCustom*)WKSManager.Instance();
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
        private static unsafe bool? CheckExp()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                var bestIndex = FindBestRelicMission();

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
                        foreach (var mission in C.MissionConfig.Where(x => x.Value.Enabled && SheetMissionDict[x.Key].Jobs.Contains(Player.JobId)))
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
        public static unsafe uint? FindBestRelicMission()
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
                        var missionConfig = C.MissionConfig[id];

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
                        bool unSupported = UnsupportedMissions.Ids.Contains(id);

                        PlayerHelper.UpdateHasManip();
                        var jobId = mission.Jobs.Where(x => CosmicHelper.CrafterJobList.Contains(x)).FirstOrDefault();

                        bool manipUnlocked = PlayerHelper.ManipClassInfo.TryGetValue(jobId, out var manipInfo) && manipInfo.HasUnlocked;
                        bool isManipReq = mission.Attributes.HasFlag(MissionAttributes.ExpertCraft);

                        IceLogging.Debug($"[Mission: {id}]" +
                                         $"Is proper Level: {properLevel} | Mission Level: {minLevel} | Player Level: {Player.Level} \n" +
                                         $"Ignoring cause of manual? {IgnoreManual}\n" +
                                         $"Ignoring cuase of not enabled: {IgnoreNotEnabled}\n" +
                                         $"Ignoring because of not supported: {unSupported}" +
                                         $"Is Manipulation required: {isManipReq}" +
                                         $"Is Manipluation even unlocked: {manipUnlocked}", tip);

                        if (!properLevel) continue;
                        if (IgnoreManual) continue;
                        if (IgnoreNotEnabled) continue;
                        if (unSupported) continue;
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

                    var rank = CosmicHelper.SheetMissionDict[missionId].Rank;
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
                    IceLogging.Debug($"Attempting to abandon mission ID: {missionToAbandon} (Rank: {CosmicHelper.SheetMissionDict[missionToAbandon].Rank})");
                    Mission_Settings.missionAppearanceCounts[missionToAbandon] = 0;

                    return true;
                }
                else
                {
                    IceLogging.Debug("No valid missions found to abandon");
                    return false;
                }
            }

            return false;
        }
        public static void InsertGrabMission(uint missionId)
        {
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

            var missionEntry = CosmicHelper.SheetMissionDict[missionId];
            var missionConfig = C.MissionConfig[missionId];
            var currentJob = Player.JobId;

            if (!missionEntry.Jobs.Contains(currentJob))
            {
                IceLogging.Error("Somehow, we've managed to get a job that isn't suppose to be an option for our jobs?? Resetting the whole process and going to try again.\n" +
                                 "If this continues on multiple times in a row, let me know.");
                SchedulerMain.State = IceState.GrabMission;
                P.TaskManager.Tasks.Clear();
                return true;
            }

            if (missionConfig.ManualMode || UnsupportedMissions.Ids.Contains(missionId))
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

                if (gatherInfo.Count == 0)
                {
                    IceLogging.Info("HEY. This gathering location hasn't been set to gather, and should honestly be set to a manual state. Cause things are about to bug out. If it's a new area please let me know o/");
                    return true;
                }

                Vector3 closestNode = gatherInfo[0].LandZone;

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
                var fishingHole = GatheringUtil.MoonFishingLocations[territory][location];

                if (fishingHole == null || fishingHole.Count == 0)
                {
                    IceLogging.Info("We've seemed to have ran into a problem with the fishing hole... either it's missing spots, or it doesn't exist. Please report back to me on this with logs leading up to this");
                    IceLogging.Info($"Mission ID: {missionId} | Map Position: {missionEntry.MapPosition} | Moon Territory: {missionEntry.TerritoryId}");
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

        private static bool? CheckReroll()
        {
            if (SchedulerMain.State == IceState.ExecutingMission)
            {
                IceLogging.Debug("No reason to reroll, you found a proper mission");
                consecutiveRerolls = 0;
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
                    IceLogging.Info(
                        $"連續重骰 {consecutiveRerolls} 次仍找不到可接任務，停止。" +
                        "常見原因：啟用了「取得金星後自動停用該任務」，而目前可接的任務都已經拿過金星。" +
                        "可改用「沒金星的優先」（只排序不移出候選池），或放寬啟用中的任務清單。",
                        "[Check Reroll]");
                    consecutiveRerolls = 0;
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
            var jobId = CosmicHelper.SheetMissionDict[missionId].Jobs.First();
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
