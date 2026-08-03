using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using Callback = ECommons.Automation.Callback;
using Time = (int start, int end);

namespace ICE.Scheduler.Handlers;

internal static unsafe class PlayerHandlers
{
    public class TimedInfo
    {
        public uint ClassId { get; set; }
        public uint MissionId { get; set; }
    }

    public static readonly Dictionary<int, List<TimedInfo>> SinusMapV2 = new()
    {
        [0] = new()
        {
            new TimedInfo { ClassId = 8, MissionId = 40 },
            new TimedInfo { ClassId = 11, MissionId = 178 },
            new TimedInfo { ClassId = 14, MissionId = 310 }
        },
        [2] = new()
        {
            new TimedInfo { ClassId = 16, MissionId = 400 }
        },
        [4] = new()
        {
            new TimedInfo { ClassId = 9, MissionId = 85 },
            new TimedInfo { ClassId = 12, MissionId = 223 },
            new TimedInfo { ClassId = 15, MissionId = 355 }
        },
        [6] = new()
        {
            new TimedInfo { ClassId = 18, MissionId = 490 }
        },
        [8] = new()
        {
            new TimedInfo { ClassId = 10, MissionId = 130 },
            new TimedInfo { ClassId = 13, MissionId = 268 }
        },
        [10] = new()
        {
            new TimedInfo { ClassId = 17, MissionId = 445 }
        },
        [12] = new()
        {
            new TimedInfo { ClassId = 8, MissionId = 43 },
            new TimedInfo { ClassId = 11, MissionId = 175 },
            new TimedInfo { ClassId = 14, MissionId = 313 }
        },
        [14] = new()
        {
            new TimedInfo { ClassId = 16, MissionId = 403 }
        },
        [16] = new()
        {
            new TimedInfo { ClassId = 9, MissionId = 88 },
            new TimedInfo { ClassId = 12, MissionId = 220 },
            new TimedInfo { ClassId = 15, MissionId = 358 }
        },
        [18] = new()
        {
            new TimedInfo { ClassId = 18, MissionId = 493 }
        },
        [20] = new()
        {
            new TimedInfo { ClassId = 10, MissionId = 133 },
            new TimedInfo { ClassId = 13, MissionId = 265 }
        },
        [22] = new()
        {
            new TimedInfo { ClassId = 17, MissionId = 448 }
        }
    };

    public static readonly Dictionary<int, List<TimedInfo>> PhaennaMapV2 = new()
    {
        [0] = new()
        {
            new TimedInfo { ClassId = 8, MissionId = 581 },
            new TimedInfo { ClassId = 10, MissionId = 658 },
            new TimedInfo { ClassId = 12, MissionId = 752 },
            new TimedInfo { ClassId = 14, MissionId = 833 },
            new TimedInfo { ClassId = 17, MissionId = 962 }
        },
        [2] = new()
        {
            new TimedInfo { ClassId = 10, MissionId = 658 },  // Continues from 00:00
            new TimedInfo { ClassId = 16, MissionId = 910 }
        },
        [4] = new()
        {
            new TimedInfo { ClassId = 9, MissionId = 623 },
            new TimedInfo { ClassId = 11, MissionId = 700 },
            new TimedInfo { ClassId = 13, MissionId = 794 },
            new TimedInfo { ClassId = 15, MissionId = 875 },
            new TimedInfo { ClassId = 18, MissionId = 994 }
        },
        [6] = new()
        {
            new TimedInfo { ClassId = 11, MissionId = 700 },  // Continues from 04:00
            new TimedInfo { ClassId = 18, MissionId = 994 }   // Continues from 04:00
        },
        [8] = new()
        {
            new TimedInfo { ClassId = 8, MissionId = 584 },
            new TimedInfo { ClassId = 10, MissionId = 665 },
            new TimedInfo { ClassId = 12, MissionId = 742 },
            new TimedInfo { ClassId = 14, MissionId = 836 },
            new TimedInfo { ClassId = 18, MissionId = 1001 }
        },
        [10] = new()
        {
            new TimedInfo { ClassId = 12, MissionId = 742 },  // Continues from 08:00
            new TimedInfo { ClassId = 17, MissionId = 952 }
        },
        [12] = new()
        {
            new TimedInfo { ClassId = 9, MissionId = 626 },
            new TimedInfo { ClassId = 11, MissionId = 707 },
            new TimedInfo { ClassId = 13, MissionId = 784 },
            new TimedInfo { ClassId = 15, MissionId = 878 },
            new TimedInfo { ClassId = 16, MissionId = 891 }
        },
        [14] = new()
        {
            new TimedInfo { ClassId = 13, MissionId = 784 },  // Continues from 12:00
            new TimedInfo { ClassId = 16, MissionId = 891 }   // Continues from 12:00
        },
        [16] = new()
        {
            new TimedInfo { ClassId = 8, MissionId = 574 },
            new TimedInfo { ClassId = 10, MissionId = 668 },
            new TimedInfo { ClassId = 12, MissionId = 749 },
            new TimedInfo { ClassId = 14, MissionId = 826 },
            new TimedInfo { ClassId = 16, MissionId = 920 }
        },
        [18] = new()
        {
            new TimedInfo { ClassId = 8, MissionId = 574 },   // Continues from 16:00
            new TimedInfo { ClassId = 14, MissionId = 826 },  // Continues from 16:00
            new TimedInfo { ClassId = 18, MissionId = 1004 }
        },
        [20] = new()
        {
            new TimedInfo { ClassId = 9, MissionId = 616 },
            new TimedInfo { ClassId = 11, MissionId = 710 },
            new TimedInfo { ClassId = 13, MissionId = 791 },
            new TimedInfo { ClassId = 15, MissionId = 868 },
            new TimedInfo { ClassId = 17, MissionId = 933 }
        },
        [22] = new()
        {
            new TimedInfo { ClassId = 9, MissionId = 616 },   // Continues from 20:00
            new TimedInfo { ClassId = 15, MissionId = 868 },  // Continues from 20:00
            new TimedInfo { ClassId = 17, MissionId = 933 }   // Continues from 20:00
        }
    };

    private static readonly uint stellarSprintID = 4398;

    public static float Distance(this Vector3 v, Vector3 v2)
    {
        return new Vector2(v.X - v2.X, v.Z - v2.Z).Length();
    }
    public static unsafe bool IsMoving()
    {
        return AgentMap.Instance()->IsPlayerMoving;
    }

    internal static unsafe void Tick()
    {
        if (!P.overlayWindow.IsOpen && PlayerHelper.IsInCosmicZone() && PlayerHelper.UsingSupportedJob() && C.ShowOverlay)
            P.overlayWindow.IsOpen = true;

        if (C.MoonSprint 
         && PlayerHelper.IsInCosmicZone() 
         && !PlayerHelper.HasStatusId(stellarSprintID) 
         && Svc.Condition[ConditionFlag.NormalConditions] 
         && IsMoving()) 
            UseSprint();

        if ((!PlayerHelper.IsInCosmicZone() || !PlayerHelper.UsingSupportedJob()) && SchedulerMain.State != IceState.Idle)
        {
            DisablePlugin();
        }

        if (PlayerHelper.HasStatusId(4409) && C.RemoveStellarStatus)
        {
            if (EzThrottler.Throttle("Turning off Stellar Buff"))
                StatusManager.ExecuteStatusOff(4409);
        }

        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("WKSReward", out var addon) && GenericHelpers.IsAddonReady(addon))
        {
            if (EzThrottler.Throttle("Closing the reward popup"))
            {
                GenericHandlers.FireCallback("WKSReward", true, -1);
            }
        }

        WatchForUnsupportedMission();
    }

    /// <summary>上一次看到的進行中任務 ID，用來偵測「任務換了」這個邊緣事件。0 = 沒有任務。</summary>
    private static uint lastSeenMissionId;

    /// <summary>
    /// 使用者<b>自己手動接了</b>一個 ICE 跑不動的任務時，馬上在聊天視窗說明原因。
    /// </summary>
    /// <remarks>
    /// 🔑 為什麼放在 <see cref="Tick"/> 而不是排程器裡：<c>SchedulerMain.Tick()</c> 只有在
    /// ICE 執行中才會跑，但「除非手動接任務」正是<b>ICE 沒在跑</b>的情境 —— 排程器那兩個
    /// 分支（<c>Task_CheckState</c>／<c>Task_ExecuteMission</c>）在那時候一行都不會執行。
    /// 這個函式掛在 <c>Svc.Framework.Update</c> 上，不管 ICE 有沒有啟動都會執行。<br/><br/>
    /// ⚠️ 用<b>邊緣觸發</b>（任務 ID 變了才判斷）而不是每幀判斷，所以不會洗版；
    /// 就算同一個任務反覆接放，<c>IceLogging.ChatInfo</c> 還有一層以訊息全文為鍵的 60 秒節流。<br/>
    /// ⚠️ 只在宇宙探索區域內判斷：離開區域時 <c>CurrentLunarMission</c> 讀到的東西沒有意義。
    /// </remarks>
    private static void WatchForUnsupportedMission()
    {
        if (!PlayerHelper.IsInCosmicZone())
        {
            lastSeenMissionId = 0;
            return;
        }

        var currentMissionId = CosmicHelper.CurrentLunarMission;
        if (currentMissionId == lastSeenMissionId)
            return;

        lastSeenMissionId = currentMissionId;

        if (currentMissionId == 0)
            return;

        if (MissionSupport.IsUnsupported(currentMissionId, out var reason))
            MissionSupport.Notify(currentMissionId, reason);
    }

    internal static void DisablePlugin()
    {
        if (SchedulerMain.State != IceState.Idle)
        {
            P.TaskManager.Abort();
            SchedulerMain.DisablePlugin();
        }
    }

    private static void UseSprint()
    {
        var am = ActionManager.Instance();
        var isSprintReady = am->GetActionStatus(ActionType.GeneralAction, 4) == 0;

        if (isSprintReady) am->UseAction(ActionType.GeneralAction, 4);
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns>Hours[long], Minutes[long]</returns>
    private static (long, long) GetEorzeaTime()
    {
        var eorzeaTime = Framework.Instance()->ClientTime.EorzeaTime;
        long hours = eorzeaTime / 3600 % 24;
        long minutes = eorzeaTime / 60 % 60;
        return (hours, minutes);
    }

    public static (List<TimedInfo> currentMissions, List<TimedInfo> nextMissions) GetMissionsForHour()
    {
        var EzTime = GetEorzeaTime();
        var currentHour = (int)EzTime.Item1; // Current hour
        var territoryId = Player.Territory;

        // Select the appropriate map based on territoryId
        Dictionary<int, List<TimedInfo>> selectedMap = territoryId switch
        {
            // Add your actual territory IDs here
            1291 => PhaennaMapV2,
            1237 => SinusMapV2,
            _ => SinusMapV2       // Default to Phaenna
        };

        // Find which bracket the current hour falls into
        int currentBracket = (currentHour / 2) * 2;

        // Calculate next bracket (wraps around at 24)
        int nextBracket = (currentBracket + 2) % 24;

        // Get the missions for current bracket
        var currentMissions = selectedMap.ContainsKey(currentBracket)
            ? selectedMap[currentBracket]
            : new List<TimedInfo>();

        // Get the missions for next bracket
        var nextMissions = selectedMap.ContainsKey(nextBracket)
            ? selectedMap[nextBracket]
            : new List<TimedInfo>();

        return (KnownMissionsOnly(currentMissions), KnownMissionsOnly(nextMissions));
    }

    /// <summary>
    /// 濾掉「這個客戶端根本沒有的任務」。
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>SinusMapV2</c>／<c>PhaennaMapV2</c> 是上游<b>照國際服寫死</b>的時段表，
    /// 表裡的任務 ID 跟 <c>SheetMissionDict</c>（從遊戲資料建出來的）之間<b>沒有任何共同保證</b>。<br/>
    /// 台服 7.20 逐筆比對 <c>exd-tc/7.20/WKSMissionUnit.csv</c> 的結果：<br/>
    /// • <c>SinusMapV2</c> 那 22 筆 <b>全部存在</b>；<br/>
    /// • <c>PhaennaMapV2</c> 那 33 筆（<b>574..1004</b>）在表裡<b>有列、但整列是空的</b>
    ///   —— 那是第二顆星 Phaenna 的預留列，台服尚未開放。<br/>
    /// 目前走不到 Phaenna 那條分支只是因為台服沒有 territory 1291；
    /// <b>第二顆星一開放就會同時生效</b>，所以在這裡收斂成「查得到才回報」。<br/>
    /// 🔑 這也是開放當天的驗證點：如果上游的 ID 對不上台服，log 會直接說出丟了幾筆，
    /// 而不是讓疊加層排出一列 <c>???</c> 讓人以為是顯示壞了。
    /// </remarks>
    private static List<TimedInfo> KnownMissionsOnly(List<TimedInfo> missions)
    {
        if (missions.Count == 0)
            return missions;

        var known = missions.Where(x => CosmicHelper.SheetMissionDict.ContainsKey(x.MissionId)).ToList();
        if (known.Count == missions.Count)
            return missions; // 常見路徑：全部都在，直接回原本那份，不要多配置一個 List

        // 節流：這個判斷每幀都會走到（疊加層一幀呼叫兩次），不節流會把 log 灌爆。
        if (EzThrottler.Throttle("ICE: timed mission table mismatch", 60000))
        {
            var missing = missions.Where(x => !CosmicHelper.SheetMissionDict.ContainsKey(x.MissionId))
                                  .Select(x => x.MissionId)
                                  .Distinct()
                                  .OrderBy(x => x);
            IceLogging.Info(
                $"時段任務表裡有 {missions.Count - known.Count} 筆任務在這個客戶端查不到資料，已從顯示中略過："
                + string.Join(", ", missing)
                + "。（時段表是寫死在 PlayerHandlers.SinusMapV2／PhaennaMapV2 裡的國際服資料，"
                + "尚未開放的星球會整批對不上，屬於預期行為。）",
                "[時段任務]");
        }

        return known;
    }
}
