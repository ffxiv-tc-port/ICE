using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal class Task_RelicTurnin
    {
        public static void Enqueue()
        {
            // 四個都要帶 Utils.TaskConfig。NeoTaskManager 預設是 TimeLimitMS = 30000 且
            // AbortOnTimeout = true —— 實機 log 顯示這條流程每 30 秒精確逾時一次、
            // 整個佇列被中止後又從頭開始，永遠回報不了（狀態一直停在 HubReturn）。
            P.TaskManager.EnqueueMulti
            (
                new(PathToRelicNPC, "Heading to the relic NPC for turnin", Utils.TaskConfig),
                new(TalkToResearchWay, "Talk to researchway", Utils.TaskConfig),
                new(SelectReport, "Selecting Report", Utils.TaskConfig),
                new(SelectRelicClass, "Selecting the class to turnin on", Utils.TaskConfig)
            );
        }

        public static bool? PathToRelicNPC()
        {
            var zoneId = Player.Territory;
            // ✅ 這裡曾經是零守衛的字典索引，已修：守衛＝下一行的 NpcData.TryGetMoonNpc。
            // 原因留存：MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是
            // Player.Territory —— 佇列排好之後玩家還是可能被傳送走（機甲行動抽中駕駛員就會），
            // 下一個 tick 讀到的區域就不是月面了。修之前的 .First()/.FirstOrDefault() 兩種寫法
            // 都沒處理「找不到」，一個丟 InvalidOperationException、一個回 null 再 NRE。
            if (!NpcData.TryGetMoonNpc(zoneId, NpcData.NpcType.Relic, out var npcEntry))
            {
                if (EzThrottler.Throttle("ICE: moon npc missing Relic", 5000))
                    IceLogging.Info($"目前區域 {zoneId} 沒有登記研究員 NPC 的資料（可能已經被傳送離開月面），中止這一步。", "[ICE]");
                SchedulerMain.AbortToStateCheck();
                return true;
            }

            if (Player.DistanceTo(npcEntry.NpcLocation) <= 6.75f)
            {
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                }
                else if (P.Navmesh.IsRunning())
                {
                    if (Player.DistanceTo(npcEntry.NpcLocation) > C.MountRadius)
                    {

                    }

                    if (Player.DistanceTo(npcEntry.NpcLocation) < 5)
                    {
                        IceLogging.Debug("Pathing to NPC has reached the distance thresh, stopping");
                        P.Navmesh.Stop();
                        return true;
                    }
                }
                else
                {
                    IceLogging.Debug($"Distance to the npc is correct, commending repair");
                    return true;
                }
            }
            else
            {
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                }
                else if (!P.Navmesh.IsRunning())
                {
                    if (EzThrottler.Throttle("Pathing to repair NPC"))
                    {
                        IceLogging.Debug($"Pathing to: {npcEntry.Name}");

                        Vector3 randomPoint = RandomUtil.GetRandomPointInBounds(npcEntry.Corner1, npcEntry.Corner2, npcEntry.Corner3, npcEntry.Corner4, npcEntry.NpcLocation.Y);
                        IceLogging.DestinationLogs.Log(randomPoint);
                        P.Navmesh.PathfindAndMoveTo(randomPoint, false);
                    }
                }
            }

            return false;
        }

        public static bool? TalkToResearchWay()
        {
            if (GenericHelpers.TryGetAddonMaster<SelectString>("SelectString", out var selectString) && selectString.IsAddonReady)
            {
                IceLogging.Info("Talk to researchway complete");
                return true;
            }
            else if (GenericHelpers.TryGetAddonMaster<Talk>("Talk", out var talk) && talk.IsAddonReady)
            {
                if (EzThrottler.Throttle("Clicking the talk dialog", 100))
                {
                    talk.Click();
                }
            }

            // 台服的研究員 NPC DataId 與國際服不同，純靠 TryGetObjectByDataId 永遠找不到
            // （實機徵狀：人就站在 NPC 旁邊，狀態卻一直卡在 HubReturn）。
            // TryGetNpcObject 在 DataId 查不到時會退回「靠近設定座標」的判定。
            // ✅ 這裡曾經是零守衛的字典索引，已修：守衛＝下一行的 NpcData.TryGetMoonNpc。
            // 原因留存：MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是
            // Player.Territory —— 佇列排好之後玩家還是可能被傳送走（機甲行動抽中駕駛員就會），
            // 下一個 tick 讀到的區域就不是月面了。修之前的 .First()/.FirstOrDefault() 兩種寫法
            // 都沒處理「找不到」，一個丟 InvalidOperationException、一個回 null 再 NRE。
            if (!NpcData.TryGetMoonNpc(Player.Territory, NpcData.NpcType.Relic, out var npcEntry))
            {
                if (EzThrottler.Throttle("ICE: moon npc missing Relic", 5000))
                    IceLogging.Info($"目前區域 {Player.Territory} 沒有登記研究員 NPC 的資料（可能已經被傳送離開月面），中止這一步。", "[ICE]");
                SchedulerMain.AbortToStateCheck();
                return true;
            }
            if (!Utils.TryGetNpcObject(npcEntry, out var researchNpc) || researchNpc == null)
            {
                if (EzThrottler.Throttle("Researchingway object not found", 5000))
                {
                    IceLogging.Warning(
                        $"Unable to find the relic NPC near {npcEntry.NpcLocation}; " +
                        $"configured NPC IDs: {string.Join(", ", npcEntry.AlternateNpcIds.Prepend(npcEntry.NpcId))}.",
                        "[Relic Turnin]");
                }

                return false;
            }

            if (EzThrottler.Throttle("Interacting with researchingway"))
            {
                if (EzThrottler.Throttle("RelicTurnin: interact log", 5000))
                    IceLogging.Info($"對研究員 NPC 送出互動，距離 {Player.DistanceTo(researchNpc.Position):F1}；等待 Talk/SelectString 開啟", "[Task Relic Turnin]");
                Utils.TargetgameObject(researchNpc);
                Utils.InteractWithObject(researchNpc);
            }

            return false;
        }

        public static bool? SelectReport()
        {
            if (GenericHelpers.TryGetAddonMaster<SelectIconString>("SelectIconString", out var selectIconString) && selectIconString.IsAddonReady)
            {
                IceLogging.Info("We're onto selecting the class to turnin, woo!");
                return true;
            }
            else if (GenericHelpers.TryGetAddonMaster<SelectString>("SelectString", out var selectString) && selectString.IsAddonReady)
            {
                if (EzThrottler.Throttle("Selecting the research one"))
                    selectString.Entries[0].Select();
            }

            return false;
        }

        public static bool? SelectRelicClass()
        {
            Dictionary<uint, bool> jobUnlocked = new()
            {
                [8] = true,
                [9] = true,
                [10] = true,
                [11] = true,
                [12] = true,
                [13] = true,
                [14] = true,
                [15] = true,
                [16] = true,
                [17] = true,
                [18] = true,
            };
            foreach (var jobId in jobUnlocked)
            {
                if (Player.GetUnsyncedLevel((Job)jobId.Key) == 0)
                    jobUnlocked[jobId.Key] = false;
            }

            if (EzThrottler.Throttle("Throttle job unlock message", 1000))
                IceLogging.Debug($"Amount of jobs unlocked: {jobUnlocked.Where(x => x.Value).Count()}");
            uint selectedEntry = 0;
            foreach (var jobId in jobUnlocked)
            {
                if (Player.JobId == jobId.Key)
                    break;
                else
                {
                    if (jobId.Value)
                        selectedEntry += 1;
                }
            }


            if (GenericHelpers.TryGetAddonMaster<SelectIconString>("SelectIconString", out var selectIconString) && selectIconString.IsAddonReady)
            {
                if (EzThrottler.Throttle($"Selecting jobId: {Player.JobId}"))
                {
                    IceLogging.Debug($"Selecting Entry: {selectedEntry} for job: {Player.JobId} to turnin relic");
                    selectIconString.Entries[selectedEntry].Select();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var selectYesno) && selectYesno.IsAddonReady)
            {
                // ⚠️ 這個情境沒有登記比對基準（台服 Addon 表裡找不到對得上的列），
                //    所以閘門在任何檔位都只會記錄、不會擋 —— 行為在三個檔位下都不變。
                // 🔴 YesnoPressGuard 放在最後（它有副作用）：節流記的是 key 上次放行的時刻，
                //    不是「這扇窗已經按過」，擋不住同一扇窗在關閉中被重按（＝原生 AVE）。
                if (EzThrottler.Throttle("Selecting yes for turnin")
                    && YesnoGuard.ShouldConfirm(YesnoSituation.RelicTurnin)
                    && YesnoPressGuard.MayPress("研究材料繳交：繳交確認", selectYesno))
                {
                    IceLogging.Verbose("Selecting yes for the turnin");
                    selectYesno.Yes();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<Talk>("Talk", out var talk) && talk.IsAddonReady)
            {
                if (EzThrottler.Throttle("Clicking the talk dialog", 50))
                {
                    IceLogging.Verbose("Clicking the talk dialog");
                    talk.Click();
                }
            }
            else if (!Player.IsBusy)
            {
                IceLogging.Info("No longer busy talking to researchingway, to we're done");
                return true;
            }

            return false;

        }
    }
}
