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
            var npcEntry = NpcData.MoonNpcs[zoneId].Where(x => x.type == NpcData.NpcType.Relic).FirstOrDefault();

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

            // 這條流程在台服會卡住不完成，而原本一行診斷都沒有：researchId 解不出來、
            // 物件表找不到 NPC、互動送出但沒有任何視窗開起來——三種情況的表徵完全一樣。
            // 用 Information 等級（使用者的記錄等級會濾掉 Debug）＋節流避免洗版。
            if (!NpcData.MoonNpcs.TryGetValue(Player.Territory, out var npcsInZone))
            {
                if (EzThrottler.Throttle("RelicTurnin: no npc table", 5000))
                    IceLogging.Info($"沒有 territory {Player.Territory} 的 NPC 資料表，無法回報宇宙工具", "[Task Relic Turnin]");
                return false;
            }

            var researchId = npcsInZone.Where(x => x.type == NpcData.NpcType.Relic).FirstOrDefault().NpcId;
            if (researchId == 0)
            {
                if (EzThrottler.Throttle("RelicTurnin: no relic npc", 5000))
                    IceLogging.Info($"territory {Player.Territory} 的 NPC 表裡沒有 Relic 類型的 NPC", "[Task Relic Turnin]");
                return false;
            }

            if (!Utils.TryGetObjectByDataId(researchId, out var researchNpc) || researchNpc == null)
            {
                if (EzThrottler.Throttle("RelicTurnin: npc not found", 5000))
                    IceLogging.Info($"物件表裡找不到 DataId {researchId} 的研究員 NPC（距離太遠或 DataId 與台服不符）", "[Task Relic Turnin]");
                return false;
            }

            if (EzThrottler.Throttle("Interacting with researchingway"))
            {
                if (EzThrottler.Throttle("RelicTurnin: interact log", 5000))
                    IceLogging.Info($"對 DataId {researchId} 送出互動，距離 {Player.DistanceTo(researchNpc.Position):F1}；等待 Talk/SelectString 開啟", "[Task Relic Turnin]");
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
                if (EzThrottler.Throttle("Selecting yes for turnin"))
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
