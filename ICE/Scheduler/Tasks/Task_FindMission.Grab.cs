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

                    // 🔴 上面守的是 missionAppearanceCounts，下一行查的卻是 SheetMissionDict —— 兩個不同的字典，要另外守。
                    //    這裡的 missionId 是從遊戲的 WKSMission addon 的 AtkValues 讀出來的（ECommons 只濾掉 0），不是驗證過的鍵集合：
                    //    索引偏移對不上或讀到沒有名稱的任務列時，直接索引會丟 KeyNotFoundException，而它在任務裡＝佇列卡死。
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
                    // 這裡只是為了印一行 log，不能讓它有機會把佇列打斷（所以用 TryGetValue）。
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
            CollectableUnlock.WarnIfMissing(missionId);
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
                Mission_Settings.ResetSkillUse();
                SchedulerMain.State = IceState.ExecutingMission;
                timeoutAmount = 0;
                return true;
            }
            else if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
            {
                // 🔴 守衛順序：IsHeld → 讀文字 → MayPress → 按。按過（確定或取消）而且還沒觀察到它收掉的那幾幀
                //    連文字都不讀、也不消耗節流 —— 那正是視窗記憶體變動中的幾幀。
                //    被擋下＝跳過整個區塊 → 落到方法尾的 return false，與節流擋下走的是同一條既有路徑：
                //    Tasks.Clear()／State 轉移都沒執行、佇列原封不動、下一幀重跑；窗消失後由上面
                //    CurrentLunarMission != 0 那一支收工。
                if (!YesnoPressGuard.IsHeld(select) && EzThrottler.Throttle("Selecting Yesno window"))
                {
                    var promptText = select.Text;
                    if (AddonPressGuard.IsTextCorrupt("SelectYesno", promptText))
                    {
                        // 讀到 U+FFFD ＝ 視窗記憶體正在變動（多半是關閉中），這一幀不碰。
                    }
                    else if (CosmicHandler.commenceStrings.Any(s => NormalizeWhitespace(promptText).StartsWith(NormalizeWhitespace(s), StringComparison.OrdinalIgnoreCase)) || !C.RejectUnknownYesno)
                    {
                        // 🔴 守衛必須在文字比對之內另開一層 if，不能併進上面的條件式 —— 併進去被擋時會掉進 else 的取消分支。
                        //    這一次按壓被記下來，正是 reroll 鏈下游 Task_AbandonMission 對同一扇窗判「按過了」的唯一依據。
                        if (YesnoPressGuard.MayPress("接取任務：開始任務確認", select))
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
                    }
                    else
                    {
                        IceLogging.Debug($"Unexpected text: '{promptText}'", "[ICE_GrabMission]");

                        // 同一扇 SelectYesno 的第二種按法（取消）也要過同一個守衛。
                        if (EzThrottler.Throttle("Unexpected Abandon Window...")
                            && YesnoPressGuard.MayPress("接取任務：關閉未預期的確認框", select))
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
                    // WKSMission 按「開始」後本身不關（開出 SelectYesno），屬多次互動窗：逃生口 15 幀，
                    // 500ms 的刻意重送節奏不變；擋的只有「同一任務在窗走完生命週期前 15 幀內再送一次」。
                    if (EzThrottler.Throttle("Initating the quest")
                        && AddonPressGuard.TryBeginPress("接取任務：送出開始任務", "WKSMission", mission, AddonPressGuard.BuildPressKey(true, 13, (int)missionId), AddonPressGuard.RoutineRePressEscapeFrames))
                    {
                        selectedMission.Initiate();
                    }
                    return false;
                }
                if (FrameThrottler.Throttle("Checking tab for mission", 8))
                {
                    if (FrameThrottler.Throttle("Checking Weather Tab for mission", 16))
                    {
                        if (AddonPressGuard.TryBeginPress("接取任務：切到臨時任務分頁", "WKSMission", mission, "ProvisionalMissions", AddonPressGuard.RoutineRePressEscapeFrames))
                            mission.ProvisionalMissions();
                        return false;
                    }
                    if (FrameThrottler.Throttle("Checking Standard Tab for missions", 16))
                    {
                        if (AddonPressGuard.TryBeginPress("接取任務：切到基本任務分頁", "WKSMission", mission, "BasicMissions", AddonPressGuard.RoutineRePressEscapeFrames))
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
    }
}
