using ECommons.EzIpcManager;
using ICE.Utilities.Cosmic_Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.IPC
{
    public class AutoHookIPC
    {
        public const string Name = "AutoHook";
        public const string Repo = "https://github.com/PunishXIV/AutoHook";
        public AutoHookIPC() => EzIPC.Init(this, Name, SafeWrapper.AnyException);
        public bool Installed => Utils.HasPlugin(Name);

        [EzIPC] public Action<bool> SetPluginState;
        [EzIPC] public Action<bool> SetAutoGigState;
        [EzIPC] public Action<string> SetPreset;
        [EzIPC] public Action<string> SetPresetAutogig;
        [EzIPC] public Action<string> CreateAndSelectAnonymousPreset;
        [EzIPC] public Action<string> ImportAndSelectPreset;
        [EzIPC] public Action DeleteSelectedPreset;
        [EzIPC] public Action DeleteAllAnonymousPresets;
        [EzIPC] public Func<uint, Task<bool>> SwapBaitById;

        /// <summary>
        /// SwapBaitById 這個 IPC 是否已被判定為不可用（不存在或簽名不符）。
        /// 判定一次之後就一直走 /ahbait 退路，不再每幀重試 IPC。
        /// </summary>
        private bool _swapBaitIpcUnavailable;

        /// <summary>
        /// 換餌。**不要直接呼叫 <see cref="SwapBaitById"/>**。
        ///
        /// 台服的 AutoHook 分岔自上游 2025-05-08，而上游是 2025-09-01（commit f98dbe5
        /// 「Added bait IPC」）才加入 <c>SwapBaitById</c>，所以我們出貨的 AutoHook
        /// **根本沒有註冊這個 IPC**（已用 AutoHook.dll 的字串表確認：其餘 8 個 IPC 名稱都在，
        /// 只有 SwapBaitById 不在）。
        ///
        /// 而 <see cref="EzIPC.Init"/> 這裡帶的是 <see cref="SafeWrapper.AnyException"/>：
        /// 呼叫不存在／簽名不符的 IPC 時例外會被吞掉、回傳 <c>default</c>（Func 就是 null），
        /// 而且沒有任何人訂閱 <c>EzIPC.OnSafeInvocationException</c> —— 也就是**完全靜默**。
        /// 釣魚流程因此會永遠卡在「等餌被裝上」的迴圈，log 一行都不會有。
        ///
        /// 退路用的是 AutoHook 自己就有的 <c>/ahbait &lt;id&gt;</c> 指令
        /// （AutoHook.cs 的 CmdAhBait/CmdBait，會以 <c>f.Id.ToString() == args</c> 比對），
        /// 且它的餌清單同樣包含 <c>WKSItemInfo.WKSItemSubCategory == 5</c> 的月面餌，
        /// 跟 ICE 的 <c>GatheringUtil.MoonBaits</c> 是同一個資料來源。
        /// ICE 本來就已經用 <c>ProcessCommand("/ahstart")</c> 驅動 AutoHook，作法一致。
        /// </summary>
        /// <returns>是否成功送出換餌請求（不代表餌已經裝上，那要等下一幀讀 WKSManager 才知道）。</returns>
        public bool TrySwapBait(uint baitId)
        {
            var logThrottle = EzThrottler.Throttle($"ICE: AutoHook bait log {baitId}", 5000);

            if (!Installed)
            {
                if (logThrottle)
                    IceLogging.Info($"想換餌（餌 ID {baitId}）但 AutoHook 沒有安裝或未啟用，釣魚流程無法繼續。", "[AutoHook IPC]");
                return false;
            }

            if (!_swapBaitIpcUnavailable && SwapBaitById != null)
            {
                // SafeWrapper.AnyException：IPC 不存在或型別不符時會吞掉例外並回傳 null。
                // 回傳 null 就是「這個 AutoHook 版本沒有提供這個 IPC」的唯一可觀察訊號。
                var pending = SwapBaitById(baitId);
                if (pending != null)
                {
                    if (logThrottle)
                        IceLogging.Info($"透過 IPC SwapBaitById 要求換餌：{baitId}", "[AutoHook IPC]");
                    return true;
                }

                _swapBaitIpcUnavailable = true;
                IceLogging.Info("AutoHook 沒有提供 SwapBaitById 這個 IPC（這是台服 AutoHook 版本較舊造成的），" +
                                "接下來一律改用 /ahbait 指令換餌。", "[AutoHook IPC]");
            }

            try
            {
                Svc.Commands.ProcessCommand($"/ahbait {baitId}");
                if (logThrottle)
                    IceLogging.Info($"已送出 /ahbait {baitId} 換餌指令。", "[AutoHook IPC]");
                return true;
            }
            catch (Exception e)
            {
                if (logThrottle)
                    IceLogging.Error($"/ahbait {baitId} 執行失敗：{e.Message}", "[AutoHook IPC]");
                return false;
            }
        }
    }
}
