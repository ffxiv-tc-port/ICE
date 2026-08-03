using ECommons.GameHelpers;
using ICE.Config;
using ICE.Utilities.Cosmic;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_ExecuteMission
    {
        public static void Enqueue()
        {
            P.TaskManager.Enqueue(() => ExecuteMission(), "Finding proper mission state");
        }

        private static bool? ExecuteMission()
        {
            if (CosmicHelper.CurrentLunarMission != 0)
            {
                var missionId = CosmicHelper.CurrentLunarMission;

                // 🔴「!= 0」不是字典守衛。SheetMissionDict 的鍵集合是「Name 不為空的 row」，
                //    台服 7.20 實測＝只有 1..544（row 0 與 545..1072 都被建表時跳過了）。
                //    也就是說「非 0」跟「在字典裡」是兩件不同的事，剛好目前台服的任務 ID 都落在
                //    1..544 才沒出事 —— 這是資料湊巧，不是程式有守。
                if (SchedulerMain.CurrentMissionUnavailable("[Task: Execute Mission]", out var mission))
                    return true;

                P.MissionTimer.StartMission(missionId);
                bool fishingMission = mission.Jobs.Contains(18);
                bool gatherMission = mission.Jobs.Contains(16) || mission.Jobs.Contains(17);
                bool craftMission = mission.Jobs.Overlaps(CosmicHelper.CrafterJobList);

                C.MissionConfig.TryGetValue(missionId, out var config);
                bool dualClass = (gatherMission && craftMission) || (fishingMission && craftMission);

                if (C.OnlyGrabMission || (config != null && config.ManualMode) || UnsupportedMissions.Ids.Contains(missionId))
                {
                    // 原本這條分支完全沒有 log —— 外掛就這樣安靜地切到手動模式什麼都不做。
                    var reason = UnsupportedMissions.Ids.Contains(missionId)
                        ? "這個任務在目前版本的 ICE 尚未支援（在 UnsupportedMissions 黑名單裡）"
                        : C.OnlyGrabMission
                            ? "你開了「只接任務」(Only Grab Mission)"
                            : "這個任務的設定是手動模式 (Manual Mode)";
                    IceLogging.ChatInfo($"任務 {missionId}：{reason}，所以切到手動模式，接下來要自己操作。", "[ICE]");
                    SchedulerMain.State = IceState.ManualMode;
                }
                else if (dualClass)
                {
                    IceLogging.Info("We've found a dual class mission! Kicking it off with that.", "[Task: Execute Mission]");
                    SchedulerMain.State = IceState.DualClass;
                    if (fishingMission)
                    {
                        // config 是上面 TryGetValue 拿的，可能是 null。原本直接解參考會丟 NullReferenceException，
                        // 而例外是在任務裡發生的，只會讓佇列中止，外面看起來一樣是「不動」。
                        if (config == null)
                        {
                            IceLogging.Info($"任務 {missionId} 沒有對應的 MissionConfig，無法決定要用內建還是自訂的 AutoHook preset。", "[Task: Execute Mission]");
                        }
                        else if (config.Use_BuildinPreset)
                        {
                            P.AutoHook.DeleteAllAnonymousPresets();
                            FishingTask(missionId);
                        }
                    }
                }
                else if (fishingMission)
                {
                    // Check exist twice, one here is to actually enable the fishing profile that is selected.
                    if (!C.MissionConfig.TryGetValue(missionId, out var missionConfig))
                    {
                        IceLogging.ChatError($"任務 {missionId} 沒有對應的 MissionConfig，釣魚流程無法啟動。", "[ICE]");
                        SchedulerMain.State = IceState.ManualMode;
                        return true;
                    }

                    if (missionConfig.Use_BuildinPreset)
                    {
                        // Using the build in presets that are included in the plugin.
                        P.AutoHook.DeleteAllAnonymousPresets();
                        FishingTask(missionId);
                    }
                    else
                    {
                        string presetName = missionConfig.AutoHookPresetName;
                        IceLogging.Info($"任務 {missionId} 使用自訂的 AutoHook preset：「{presetName}」。", "[Task: Execute Mission]");
                        P.AutoHook.SetPreset(presetName);
                    }

                    SchedulerMain.State = IceState.Fish;
                    IceLogging.Info($"任務 {missionId} 是釣魚任務，切到釣魚流程（使用內建 preset: {missionConfig.Use_BuildinPreset}）。", "[Task: Execute Mission]");
                }
                else if (gatherMission)
                {
                    SchedulerMain.State = IceState.Gather;
                    IceLogging.Info("Mission is a gathering mission. Need to gather inial resources. But first going to do a check to make sure where we're at.", "[Task_ExecuteMission]");
                }
                else if (craftMission)
                {
                    IceLogging.Debug("Mission is purely a crafting mission (yay), checking current state next", "[Task_ExecuteMission]");
                    SchedulerMain.State = IceState.Craft;
                }
            }
            else if (CosmicHelper.CurrentLunarMission == 0)
            {
                IceLogging.Debug("Hmm... somehow we got in this state. And we shouldn't be? Returning back to the grab mission state");
                SchedulerMain.State = IceState.GrabMission;
            }

            return true;
        }

        public static void FishingTask(uint missionId)
        {
            P.TaskManager.Enqueue(() => ClearFishingPreset(), "Clearing All Fishing Presets");
            P.TaskManager.EnqueueDelay(150);
            P.TaskManager.Enqueue(() => ImportPresetsSequentially(missionId));
        }

        private static bool? ClearFishingPreset()
        {
            if (P.AutoHook.Installed)
            {
                P.AutoHook.DeleteAllAnonymousPresets();
            }
            return true;
        }
        private static void ImportPresetsSequentially(uint missionId)
        {
            bool? ImportOtherPresets(string preset)
            {
                P.AutoHook.CreateAndSelectAnonymousPreset(preset);
                return true;
            }

            // 直接索引在任務不在表裡時會丟 KeyNotFoundException；例外發生在任務內只會讓佇列中止，
            // 外面看起來就是「不動」而且沒有訊息。
            if (!GatheringUtil.FishingPreset.TryGetValue(missionId, out var presetList))
            {
                IceLogging.ChatError($"任務 {missionId} 不在內建的釣魚 preset 表裡，AutoHook 不會被設定，釣魚無法自動進行。", "[ICE]");
                return;
            }

            var presets = presetList.FishingPreset.ToList();

            if (presets.Count == 0)
            {
                // 黑名單裡那 16 個釣魚任務在 FishingPresets.cs 就是 `new FishingTools { }` 的空殼，
                // 上游的註解直接寫「Need Info on this one」。走到這裡代表這個任務的資料還沒有人補。
                IceLogging.ChatError($"任務 {missionId} 的內建釣魚 preset 是空的（上游還沒補這筆資料），AutoHook 不會被設定，釣魚無法自動進行。", "[ICE]");
                return;
            }

            IceLogging.Info($"任務 {missionId} 匯入 {presets.Count} 筆內建 AutoHook preset。", "[Task: Execute Mission]");

            // Import first preset immediately
            P.AutoHook.CreateAndSelectAnonymousPreset(presets[0]);

            // Queue remaining presets with delays
            for (int i = 1; i < presets.Count; i++)
            {
                var preset = presets[i]; // Capture for closure

                P.TaskManager.EnqueueDelay(100);
                P.TaskManager.Enqueue(() => ImportOtherPresets(preset));
            }
        }

    }
}
