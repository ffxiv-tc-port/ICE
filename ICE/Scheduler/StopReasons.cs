namespace ICE.Scheduler;

/// <summary>IPC <c>StopReason()</c> 回傳的停止原因代碼。</summary>
/// <remarks>🔴 對外契約（別的插件與巨集會比對字串）：既有代碼不改名、不改語意；新增停止路徑先歸進既有代碼。</remarks>
internal static class StopReasons
{
    /// <summary>宇宙工具這一階段的經驗都滿了，且開著「工具經驗滿了就停」（含已做到最終階段）。</summary>
    internal const string ToolXpFull = "tool_xp_full";
    /// <summary>「做完目前任務就停」生效（交件、放棄任務或手動模式之後）。</summary>
    internal const string StopAfterCurrent = "stop_after_current";
    /// <summary>使用者在 ICE 介面按停止或打 <c>/ice stop</c>。</summary>
    internal const string User = "user";
    /// <summary>別的插件或巨集經 IPC <c>Disable</c> 叫停。</summary>
    internal const string IpcDisable = "ipc_disable";
    /// <summary>達到使用者設的目標：等級、宇宙評分、月靈點、宇宙點上限，或標準任務全部金評。</summary>
    internal const string GoalReached = "goal_reached";
    /// <summary>這個職業在這個區域沒有可跑的任務（含連續重骰仍找不到）。</summary>
    internal const string NoMission = "no_mission";
    /// <summary>離開宇宙探索區（含被機甲行動傳送走）、換成 ICE 不支援的職業，或角色暫時不可用（登出、讀取中）。</summary>
    internal const string LeftZone = "left_zone";
    /// <summary>流程自己判定跑不下去：任務資料對不上、製作連續沒進展、未知的排程狀態。</summary>
    internal const string Error = "error";
    /// <summary>轉盤（WKSLottery）轉完。</summary>
    internal const string GambaDone = "gamba_done";
}
