using ECommons.EzIpcManager;

namespace ICE.IPC;

#nullable disable
public class VislandIPC
{
    public const string Name = "visland";
    public VislandIPC() => EzIPC.Init(this, Name, SafeWrapper.AnyException);
    public static bool Installed => Utils.HasPlugin(Name);

    [EzIPC] public Func<bool> IsRouteRunning; // Checks to see if visland is running
    [EzIPC] public Func<bool> IsRoutePaused; // Checks to see if route is paused
    [EzIPC] public Action<bool> SetRoutePaused; // Bool to set visland route paused/not
    [EzIPC] public Action StopRoute; // Run this to stop a route from continuing
    [EzIPC] public Action<string, bool> StartRoute; // string = base64 import | bool is if you only want to run it once

    // ⚠️ 這裡原本還有一個 [EzIPC] Action<string[], bool> VIsMoveTo。已於 2026-08-03 移除，因為那是死碼：
    //    visland 從來沒有提供過任何「移動到」的 IPC（ffxiv_visland/IPC/vislandIPC.cs 只註冊
    //    IsRouteRunning / IsRoutePaused / SetRoutePaused / StopRoute / StartRoute / GatherItem 六個），
    //    而且 ICE 全 repo 零呼叫。帶 SafeWrapper.AnyException 的訂閱端呼叫下去只會被吞掉、
    //    回傳 default —— 留著只會讓下一個人以為它能用。
    //    📌 visland 另有一個我們沒宣告的 visland.GatherItem(uint)，日後真要用再加。
}
