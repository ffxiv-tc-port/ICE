using ECommons.EzIpcManager;
using ICE.Utilities.Cosmic_Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.IPC;

/// <summary>ICE 對外提供的 IPC 端點。</summary>
/// <remarks>
/// 🔴 <b>凡是同步可達「原生記憶體／裸集合／別的外掛」的端點，一律經過
/// <see cref="IpcFrameworkGate"/> 交回遊戲主執行緒</b>（IPC 跑在呼叫端的執行緒上）。
/// 端點本體只留閘門呼叫，真正的實作放在同名的 <c>…Core</c> 私有方法裡，
/// 這樣「哪些端點上了閘門」用肉眼一列就看得完。
/// </remarks>
public class IceCosmicExplorationIPC
{
    public IceCosmicExplorationIPC() => EzIPC.Init(this);


    // 📌 不上閘門：純受管理的靜態列舉讀取，見類別說明。
    [EzIPC] public bool IsRunning() => SchedulerMain.State != IceState.Idle;
    [EzIPC] public void Enable() => IpcFrameworkGate.Run(nameof(Enable), EnableCore);
    private static void EnableCore() => SchedulerMain.EnablePlugin();
    [EzIPC] public void Disable() => IpcFrameworkGate.Run(nameof(Disable), DisableCore);
    private static void DisableCore() => SchedulerMain.DisablePlugin(StopReasons.IpcDisable);
    /// <summary>
    /// Adds the following missions to your mission list
    /// </summary>
    /// <param name="missionId"></param>
    [EzIPC] public void AddMissions(HashSet<uint> missionId)
        => IpcFrameworkGate.Run(nameof(AddMissions), () => AddMissionsCore(missionId));

    private static void AddMissionsCore(HashSet<uint> missionId)
    {
        // 公開的 IPC 介面，missionId 完全由別的外掛決定 ⇒ 無效的 ID 由下方的 TryGetValue 濾掉並記一筆，
        // 不丟例外回給呼叫端。
        foreach (var id in missionId)
        {
            if (C.MissionConfig.TryGetValue(id, out var config))
                config.Enabled = true;
            else
                IceLogging.Info($"IPC AddMissions 收到不存在的任務 ID {id}，已略過。", "[ICE IPC]");
        }
        C.Save();
    }

    /// <summary>
    /// Disables/Removes the missions from being selected
    /// </summary>
    /// <param name="missionIds"></param>
    [EzIPC] public void RemoveMissions(HashSet<uint> missionIds)
        => IpcFrameworkGate.Run(nameof(RemoveMissions), () => RemoveMissionsCore(missionIds));

    private static void RemoveMissionsCore(HashSet<uint> missionIds)
    {
        foreach (var id in missionIds)
        {
            if (C.MissionConfig.TryGetValue(id, out var config))
                config.Enabled = false;
            else
                IceLogging.Info($"IPC RemoveMissions 收到不存在的任務 ID {id}，已略過。", "[ICE IPC]");
        }
        C.Save();
    }

    /// <summary>
    /// Toggle the following missions states
    /// </summary>
    /// <param name="missionIds"></param>
    [EzIPC] public void ToggleMissions(HashSet<uint> missionIds)
        => IpcFrameworkGate.Run(nameof(ToggleMissions), () => ToggleMissionsCore(missionIds));

    private static void ToggleMissionsCore(HashSet<uint> missionIds)
    {
        foreach (var id in missionIds)
        {
            if (C.MissionConfig.TryGetValue(id, out var config))
                config.Enabled = !config.Enabled;
            else
                IceLogging.Info($"IPC ToggleMissions 收到不存在的任務 ID {id}，已略過。", "[ICE IPC]");
        }
        C.Save();
    }
    /// <summary>
    /// Sets only the missions in the hashset to enabled, everything else will be false
    /// </summary>
    /// <param name="missionIds"></param>
    [EzIPC] public void OnlyMissions(HashSet<uint> missionIds)
        => IpcFrameworkGate.Run(nameof(OnlyMissions), () => OnlyMissionsCore(missionIds));

    private static void OnlyMissionsCore(HashSet<uint> missionIds)
    {
        foreach (var mission in C.MissionConfig.Where(x => x.Value.Enabled))
        {
            mission.Value.Enabled = false;
        }

        foreach (var id in missionIds)
        {
            if (C.MissionConfig.TryGetValue(id, out var mission))
            {
                mission.Enabled = true;
            }
        }
        C.Save();
    }

    /// <summary>
    /// Clears all missions that you have enabled
    /// </summary>
    [EzIPC] public void ClearAllMissions()
        => IpcFrameworkGate.Run(nameof(ClearAllMissions), ClearAllMissionsCore);

    private static void ClearAllMissionsCore()
    {
        foreach (var mission in C.MissionConfig)
        {
            mission.Value.Enabled = false;
        }
        C.Save();
    }

    /// <summary>
    /// Opens the map and flags/shows a radius of where the mission's gathering point is
    /// </summary>
    /// <param name="id"></param>
    [EzIPC] public void FlagMissionArea(uint id)
        => IpcFrameworkGate.Run(nameof(FlagMissionArea), () => FlagMissionAreaCore(id));

    private static void FlagMissionAreaCore(uint id)
    {
        var info = CosmicHelper.SheetMissionDict.FirstOrDefault(x => x.Key == id);
        if (info.Value == default) return;
        if (info.Value.MarkerId == 0) return;

        Utils.SetGatheringRing(info.Value.TerritoryId, (int)info.Value.MapPosition.X, (int)info.Value.MapPosition.Y, info.Value.Radius, info.Value.Name);
    }

    /// <summary>
    /// General way of changing settings states. 
    /// </summary>
    /// <param name="config"></param>
    /// <param name="state"></param>
    [EzIPC] public void ChangeSetting(string config, bool state)
        => IpcFrameworkGate.Run(nameof(ChangeSetting), () => ChangeSettingCore(config, state));

    private static void ChangeSettingCore(string config, bool state)
    {
        IceLogging.Info($"Setting: {config}, state: {state}");
        switch (config)
        {
            case "OnlyGrabMission": C.OnlyGrabMission = state; break;
            case "StopAfterCurrent": Mission_Settings.StopAfterCurrent = state; break;
            case "StopOnceHitCosmoCredits": C.StopOnceHitCosmoCredits = state; break;
            case "StopOnceHitLunarCredits": C.StopOnceHitLunarCredits = state; break;
            case "XPRelicGrind": C.XPRelicGrind = state; break;
            default: return;
        }
        C.Save();
    }

    /// <summary>
    /// Ability to change the amounts on certain things.
    /// </summary>
    /// <param name="config"></param>
    /// <param name="amount"></param>
    [EzIPC] public void ChangeSettingAmount(string config, int amount)
        => IpcFrameworkGate.Run(nameof(ChangeSettingAmount), () => ChangeSettingAmountCore(config, amount));

    private static void ChangeSettingAmountCore(string config, int amount)
    {
        switch (config)
        {
            case "CosmoCreditsCap": C.CosmoCreditsCap = amount; break;
            case "LunarCreditsCap": C.LunarCreditsCap = amount; break;
            default: return;
        }
        C.Save();
    }

    // 📌 不上閘門：同 IsRunning，只讀 SchedulerMain.State。
    /// <summary>
    /// Returns the current state(s) that ICE is currently in a string format. 
    /// </summary>
    /// <returns></returns>
    [EzIPC] public string CurrentState()
    {
        return SchedulerMain.State.ToString();
    }

    // 📌 不上閘門：讀 volatile 字串，見 SchedulerMain.LastStopReason。
    /// <summary>最近一次停止的原因代碼；開始執行時清成空字串（執行中一律是空字串）。</summary>
    /// <remarks>
    /// tool_xp_full＝宇宙工具經驗已滿；stop_after_current＝「做完目前任務就停」；user＝ICE 介面或 /ice stop；
    /// ipc_disable＝經 IPC Disable；goal_reached＝等級／評分／月靈點／宇宙點上限或全部金評；no_mission＝沒有可跑的任務；
    /// left_zone＝離開探索區／換成不支援的職業／角色不可用；error＝流程跑不下去；gamba_done＝轉盤轉完。
    /// 🔴 代碼是對外契約，只增不改；呼叫端遇到不認得的代碼要當成「其他原因」。
    /// </remarks>
    [EzIPC] public string StopReason() => SchedulerMain.LastStopReason;

    [EzIPC] public uint CurrentMission()
        => IpcFrameworkGate.Get(nameof(CurrentMission), CurrentMissionCore, 0u);

    private static uint CurrentMissionCore()
    {
        return CosmicHelper.CurrentLunarMission;
    }
}
