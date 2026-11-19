using Dalamud.Plugin.Ipc;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.IPC;

/// <summary>
/// 自動化停下來時請 NotificationMaster 跳 Windows 通知／閃工作列圖示。純通知，不影響任何流程。
/// </summary>
/// <remarks>
/// 🔴 端點名逐字照 <c>NotificationMasterAPI/NMAPINames.cs</c>、簽名照 <c>NotificationMaster/IPC.cs</c>（純字串比對，錯了只會靜默斷線）。
/// 刻意不走 <c>NotificationMaster.Notify</c> 樞紐：它的語音預設開，會和 TataruPraise 的停止播報念兩次。
/// 🔴 只在 framework 執行緒呼叫（所有呼叫點都經 <c>SchedulerMain.StopWithNotice</c>）。
/// </remarks>
internal static class NotificationMasterIPC
{
    public const string Name = "NotificationMaster";
    public const string DisplayToastNotificationIpc = "NotificationMasterAPI.DisplayToastNotification";
    public const string FlashTaskbarIconIpc = "NotificationMasterAPI.FlashTaskbarIcon";
    private const string RequesterName = "ICE";

    private static ICallGateSubscriber<string, string, string, bool>? toastSubscriber;
    private static ICallGateSubscriber<string, bool>? flashSubscriber;

    /// <summary>每幀重舉、送出即放下：同一次停止有好幾條路徑先後成立時只通知一次（與 TataruPraise 同形狀）。</summary>
    private static bool stopNoticeArmed;
    private static bool loggedUnavailable;

    /// <remarks>🔴 每幀都會跑：只做一個 bool 指派。</remarks>
    internal static void ArmStopNotice() => stopNoticeArmed = true;

    internal static void NotifyStopped(string reason, bool needsHelp)
    {
        if (!C.NotifyToastOnStop && !C.NotifyFlashTaskbarOnStop)
            return;
        if (!stopNoticeArmed)
            return;
        stopNoticeArmed = false;

        if (!Utils.HasPlugin(Name))
        {
            LogUnavailableOnce($"{Name} 沒有安裝或尚未載入");
            return;
        }

        var text = needsHelp ? $"宇宙探索自動化卡住停下了：{reason}" : $"宇宙探索自動化已停止：{reason}";
        if (C.NotifyToastOnStop)
        {
            try
            {
                toastSubscriber ??= Svc.PluginInterface.GetIpcSubscriber<string, string, string, bool>(DisplayToastNotificationIpc);
                toastSubscriber.InvokeFunc(RequesterName, "I.C.E.", text);
            }
            catch (Exception e)
            {
                LogUnavailableOnce($"呼叫「{DisplayToastNotificationIpc}」失敗：{e.Message}");
            }
        }
        if (C.NotifyFlashTaskbarOnStop)
        {
            try
            {
                flashSubscriber ??= Svc.PluginInterface.GetIpcSubscriber<string, bool>(FlashTaskbarIconIpc);
                flashSubscriber.InvokeFunc(RequesterName);
            }
            catch (Exception e)
            {
                LogUnavailableOnce($"呼叫「{FlashTaskbarIconIpc}」失敗：{e.Message}");
            }
        }
    }

    private static void LogUnavailableOnce(string why)
    {
        if (loggedUnavailable)
            return;
        loggedUnavailable = true;
        IceLogging.Debug($"停止通知沒有送出（{why}）；這個功能維持靜默，其餘流程不受影響。", "[NotificationMaster IPC]");
    }
}
