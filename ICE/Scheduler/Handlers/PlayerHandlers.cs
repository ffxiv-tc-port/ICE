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
        // AgentMap.Instance() 是產生器產出的兩層可空取得器（agentModule 或代理人任一為 null
        // 就回 null），裸解參考是攔不到的 AVE。唯一的呼叫端拿它當「要不要放月面衝刺」的閘門，
        // 所以讀不到就回 false ＝ 不放技能（fail-closed）。
        var agent = AgentMap.Instance();
        return agent != null && agent->IsPlayerMoving;
    }

    internal static unsafe void Tick()
    {
        if (!P.overlayWindow.IsOpen && PlayerHelper.IsInCosmicZone() && PlayerHelper.UsingSupportedJob() && C.ShowOverlay)
            P.overlayWindow.IsOpen = true;

        // 🔴🔴 機甲行動狀態視窗以前**從來沒有被打開過**。
        //    `P.mechaOpsWindow` 在 ICE.OnPluginLoad 有 `new()`（所以有進 windowSystem），
        //    但整個 repo 裡沒有任何一行寫過它的 `IsOpen`，而 Dalamud 的
        //    `Window.DrawInternal` 是**先看 IsOpen 才看 DrawConditions()**
        //    （Dalamud/Interface/Windowing/Window.cs：IsOpen 檢查在 L395、
        //     DrawConditions 在 L432）⇒ 那個視窗的 Draw() 一次都沒跑過。
        //    後果是掛在它底下的四個設定（顯示技能冷卻／proc 提示／事件狀態／事件進度）
        //    使用者勾了完全沒有反應——這正是他回報的「這些好像沒功能」。
        //
        // 🔑 修法**照抄上面 overlayWindow 那一行的既有慣例**：每個 tick 補開，
        //    真正的開關是設定而不是視窗的 X 鈕。
        // ⚠️ 這裡刻意**不要求** UsingSupportedJob()：機甲行動的協助員用的是宇宙工具，
        //    不見得掛在 ICE 認得的那幾個生產職上，要求職業會把協助員整個擋掉。
        // ⚠️ 條件只放到「總開關 + 在宇宙區域」為止；要不要真的畫、畫哪幾段，
        //    仍然完全由 MechaOpsWindow.DrawConditions() 決定（它本來就寫好了）。
        //    所以總開關 ShowMechaAoeOverlay 預設關的使用者，行為與先前完全相同。
        if (!P.mechaOpsWindow.IsOpen && PlayerHelper.IsInCosmicZone() && C.ShowMechaAoeOverlay)
            P.mechaOpsWindow.IsOpen = true;

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
    /// <returns>Hours[long], Minutes[long]；Framework 尚未就緒時回 null。</returns>
    /// <remarks>
    /// 🔴 Framework.Instance() 是 [StaticAddress(..., isPointer: true)]：產生器讀「指標的位址」
    /// 再解參考一層，遊戲尚未建立單例時回 null（非 isPointer 的那種才保證不回 null，是擲例外）。
    /// 裸解參考 null 原生指標是 AVE，屬 corrupted-state exception，try/catch 攔不到。
    /// <para>
    /// 📌 這裡回 <c>null</c> 而不是 <c>(0, 0)</c>：退回 0 點會讓呼叫端靜默排出一列
    /// 看起來完全正常、實際上是錯時段的任務，比「沒有資料」更難被察覺。
    /// 判空從呼叫端移進本函式，是為了不讓「安全」依賴「唯一呼叫端記得先擋」這個會過期的前提。
    /// </para>
    /// </remarks>
    private static (long, long)? GetEorzeaTime()
    {
        var framework = Framework.Instance();
        if (framework == null)
            return null;

        var eorzeaTime = framework->ClientTime.EorzeaTime;
        long hours = eorzeaTime / 3600 % 24;
        long minutes = eorzeaTime / 60 % 60;
        return (hours, minutes);
    }

    public static (List<TimedInfo> currentMissions, List<TimedInfo> nextMissions) GetMissionsForHour()
    {
        // 🔴 GetEorzeaTime() 會解參考 Framework.Instance()。null 解參考是 AccessViolationException，
        //    在 .NET Core 屬 corrupted-state exception，try/catch 與 HookSafety.ExecuteSafe 都攔不到
        //    —— 只能事前擋。判空已移進 GetEorzeaTime() 本身（取不到回 null），
        //    這樣防護就不再依賴「呼叫端記得先擋」這個會隨新增呼叫端而過期的前提。
        //    刻意「回空清單並記一次 Information」而不是退回 0 點：退回 0 點會靜默排出一列
        //    看起來完全正常、實際上是錯時段的任務，比空白更難被察覺。
        if (GetEorzeaTime() is not { } EzTime)
        {
            if (EzThrottler.Throttle("ICE: eorzea clock unavailable", 60000))
                IceLogging.Info(
                    "取不到艾歐澤亞時間（Framework 尚未就緒），本次略過時段任務顯示。",
                    "[時段任務]");
            return (new List<TimedInfo>(), new List<TimedInfo>());
        }

        var currentHour = (int)EzTime.Item1; // Current hour
        var territoryId = Player.Territory;

        // 依所在星球選時段表。
        // ⚠️ 上游原本的註解寫「Default to Phaenna」是**錯的** —— fallback 實際回的是 SinusMapV2。
        //    這裡保留原本的 fallback 行為（不改預設值），只補上診斷：
        //    未知星球會拿 Sinus 的時段表去排別的星球，畫面看起來正常但整列都是錯的資料。
        // 📌 目前這條 default 走不到，因為唯一的呼叫端（OverlayWindow）被
        //    PlayerHelper.IsInCosmicZone() 擋在 {1237, 1291} 之內。第三顆星開放時
        //    若有人只改了 IsInCosmicZone 卻忘了這張表，這行 log 就是唯一的痕跡。
        Dictionary<int, List<TimedInfo>> selectedMap;
        string mapName;
        switch (territoryId)
        {
            case 1291:
                selectedMap = PhaennaMapV2;
                mapName = nameof(PhaennaMapV2);
                break;
            case 1237:
                selectedMap = SinusMapV2;
                mapName = nameof(SinusMapV2);
                break;
            default:
                selectedMap = SinusMapV2;   // 行為與改動前相同，不改預設
                mapName = nameof(SinusMapV2);
                if (EzThrottler.Throttle("ICE: unknown cosmic territory", 60000))
                    IceLogging.Info(
                        $"目前區域 {territoryId} 不在已知的月面區域清單中"
                        + "（已知 1237 Sinus Ardorum、1291 Phaenna），"
                        + $"時段任務表暫時沿用 {nameof(SinusMapV2)}，畫面上的時段任務很可能是錯的。"
                        + "（若這是新開放的星球，PlayerHandlers 的時段表與 "
                        + "PlayerHelper.IsInCosmicZone 都需要補上這個區域。）",
                        "[時段任務]");
                break;
        }

        // Find which bracket the current hour falls into
        int currentBracket = (currentHour / 2) * 2;

        // Calculate next bracket (wraps around at 24)
        int nextBracket = (currentBracket + 2) % 24;

        var currentMissions = MissionsForBracket(selectedMap, mapName, territoryId, currentBracket, currentHour);
        var nextMissions = MissionsForBracket(selectedMap, mapName, territoryId, nextBracket, currentHour);

        return (KnownMissionsOnly(currentMissions, mapName, territoryId),
                KnownMissionsOnly(nextMissions, mapName, territoryId));
    }

    /// <summary>
    /// 取某個時段（雙數小時）的任務清單；缺鍵時回空清單並記一次 Information。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這裡用 <c>TryGetValue</c> 而不是索引器，所以缺鍵**不會**丟
    /// <c>KeyNotFoundException</c>。但「回空清單」在疊加層上跟「這個時段本來就沒任務」
    /// 長得一模一樣 —— 失效是完全靜默的，所以缺鍵時一定要說出來。<br/>
    /// 📌 <c>currentHour</c> 由 <c>eorzeaTime / 3600 % 24</c> 算出，正常落在 0..23，
    /// 因此 bracket 是 0..22 的雙數；<c>SinusMapV2</c> 與 <c>PhaennaMapV2</c> 目前
    /// <b>兩張表都備齊 12 個雙數時段</b>（2026-08-06 逐鍵清點），所以這條路徑現在印不出東西。
    /// 若時鐘來源異常（例如 EorzeaTime 為負，C# 的 <c>%</c> 會保留負號）導致 bracket 落在表外，
    /// 這條路徑就是那個情況唯一會留下的痕跡。
    /// </remarks>
    private static List<TimedInfo> MissionsForBracket(
        Dictionary<int, List<TimedInfo>> map, string mapName, uint territoryId, int bracket, int currentHour)
    {
        if (map.TryGetValue(bracket, out var list))
            return list;

        // 節流 key 帶上時段，避免「0 點缺」把「2 點缺」整個蓋掉。
        // 📌 EzThrottler 的 key 全域持久，但這裡的 key 集合上限是 12 個（雙數時段），不會無限膨脹。
        if (EzThrottler.Throttle($"ICE: timed mission bracket missing {bracket}", 60000))
        {
            IceLogging.Info(
                $"時段任務表 {mapName}（區域 {territoryId}）沒有「{bracket} 點」這個時段，該時段顯示為空。"
                + $"目前艾歐澤亞時間 {currentHour} 點；表裡實際有的時段："
                + string.Join(", ", map.Keys.OrderBy(x => x)) + "。",
                "[時段任務]");
        }

        return new List<TimedInfo>();
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
    private static List<TimedInfo> KnownMissionsOnly(List<TimedInfo> missions, string mapName, uint territoryId)
    {
        if (missions.Count == 0)
            return missions;

        var known = missions.Where(x => CosmicHelper.SheetMissionDict.ContainsKey(x.MissionId)).ToList();
        if (known.Count == missions.Count)
            return missions; // 常見路徑：全部都在，直接回原本那份，不要多配置一個 List

        // 節流：這個判斷每幀都會走到（疊加層一幀呼叫兩次），不節流會把 log 灌爆。
        // 📌 節流 key 帶上表名，才不會讓 Sinus 的訊息把 Phaenna 的蓋掉（反之亦然）。
        if (EzThrottler.Throttle($"ICE: timed mission table mismatch {mapName}", 60000))
        {
            var missing = missions.Where(x => !CosmicHelper.SheetMissionDict.ContainsKey(x.MissionId))
                                  .Select(x => x.MissionId)
                                  .Distinct()
                                  .OrderBy(x => x);
            IceLogging.Info(
                $"時段任務表 {mapName}（區域 {territoryId}）有 {missions.Count - known.Count}/{missions.Count} "
                + "筆任務在這個客戶端的 WKSMissionUnit 查不到資料（該列存在但整列是空的），已從顯示中略過："
                + string.Join(", ", missing)
                + $"。（{mapName} 是寫死在 PlayerHandlers 裡的國際服資料，"
                + "尚未開放的星球會整批對不上，屬於預期行為；"
                + "若是**已開放**的星球出現這行，代表上游的任務 ID 與台服對不上，需要重建這張表。）",
                "[時段任務]");
        }

        return known;
    }
}
