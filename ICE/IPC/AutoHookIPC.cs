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
        /// AutoHook 端資料夾匯入 IPC 的合約版本。<b>0 ＝ 這版 AutoHook 沒有這個功能</b>
        /// （IPC 不存在時例外被 <see cref="SafeWrapper.AnyException"/> 吞掉，回傳 <c>default(int)</c>），
        /// 所以 AutoHook 那邊的版本號刻意從 1 起跳。
        /// </summary>
        [EzIPC] public Func<int> GetFolderImportApiVersion;

        /// <summary>
        /// 匯入一整包 <c>AHFOLDER_</c> 資料夾。回傳實際掛上去的 preset 名稱，第一筆＝被選取的進入點。
        /// <br/>📌 <b>回傳 <c>null</c> 與回傳空清單意義不同</b>：<c>null</c> ＝這版 AutoHook 沒有這個 IPC
        /// （SafeWrapper 吞掉例外後的 default），空清單＝有這個 IPC 但匯入失敗。
        /// </summary>
        [EzIPC] public Func<string, List<string>> CreateAndSelectAnonymousFolder;

        /// <summary>ICE 需要的最低資料夾匯入合約版本。</summary>
        public const int RequiredFolderImportApiVersion = 1;

        /// <summary>探測結果快取。null ＝ 還沒探測過。</summary>
        private int? _folderImportApiVersion;

        /// <summary>
        /// 這版 AutoHook 支不支援整包資料夾匯入。
        /// </summary>
        /// <remarks>
        /// 🔴 <b>這是出貨順序的相容性閘門。</b>使用者可能只更新 ICE 沒更新 AutoHook，
        /// 那些需要資料夾匯入的任務（494／495）必須<b>維持停用</b>而不是跑到一半才發現匯不進去——
        /// 後者的失敗形式是「站在釣點不動直到逾時」，log 一行都沒有。
        /// <br/><br/>
        /// 📌 探測結果會快取，並且<b>只在第一次判定時寫一行 Information</b>
        /// （使用者跑 LogLevel 2，Debug／Verbose 收不到；而這是「為什麼這個任務被跳過」唯一的線索）。
        /// </remarks>
        public bool SupportsFolderImport()
        {
            if (!Installed)
                return false;

            if (_folderImportApiVersion is { } cached)
                return cached >= RequiredFolderImportApiVersion;

            // IPC 不存在／簽名不符時，SafeWrapper.AnyException 會吞掉例外並回傳 default(int) = 0。
            // 🔑 這正是「回 0 比報錯常見」的實例：0 要當成「沒有這個功能」，不能當成「版本 0」。
            var version = 0;
            if (GetFolderImportApiVersion != null)
                version = GetFolderImportApiVersion();

            _folderImportApiVersion = version;

            if (version >= RequiredFolderImportApiVersion)
            {
                IceLogging.Info(
                    $"AutoHook 支援整包資料夾匯入（API 版本 {version}），需要資料夾 preset 的任務可以執行。",
                    "[AutoHook IPC]");
                return true;
            }

            IceLogging.Info(
                "這版 AutoHook 沒有提供資料夾匯入 IPC（CreateAndSelectAnonymousFolder / " +
                $"GetFolderImportApiVersion 回報 {version}，需要 {RequiredFolderImportApiVersion} 以上）。" +
                "需要整包資料夾 preset 的宇宙釣魚任務會維持停用—— " +
                "請把 AutoHook 更新到與這版 ICE 同一波出貨的版本。",
                "[AutoHook IPC]");
            return false;
        }

        /// <summary>
        /// 匯入整包資料夾 preset。
        /// </summary>
        /// <returns>實際匯入的 preset 筆數；0 代表沒有匯入任何東西（呼叫端應視為失敗）。</returns>
        public int TryImportFolder(string folderExport)
        {
            if (!SupportsFolderImport())
                return 0;

            if (CreateAndSelectAnonymousFolder == null)
                return 0;

            var names = CreateAndSelectAnonymousFolder(folderExport);

            // null ＝ IPC 呼叫本身失敗（被 SafeWrapper 吞掉）；空清單 ＝ AutoHook 收到了但匯不進去。
            // 兩者對使用者的意義不同，分開講。
            if (names == null)
            {
                IceLogging.Info(
                    "呼叫 AutoHook 的 CreateAndSelectAnonymousFolder 失敗（例外被吞掉）。" +
                    "AutoHook 版本可能在探測之後被換掉了，釣魚無法自動進行。", "[AutoHook IPC]");
                _folderImportApiVersion = null; // 讓下一次重新探測，不要一路錯下去
                return 0;
            }

            if (names.Count == 0)
            {
                IceLogging.Info(
                    "AutoHook 收到了資料夾 preset 但一筆都沒匯進去（字串格式不對或解不開），釣魚無法自動進行。",
                    "[AutoHook IPC]");
                return 0;
            }

            IceLogging.Info(
                $"已匯入 {names.Count} 筆資料夾 preset，AutoHook 選取的進入點是「{names[0]}」。"
                + (names.Count > 1
                    ? $"其餘 {names.Count - 1} 筆是同一台狀態機的後續階段，由 AutoHook 自己依 PresetToSwap 條件切換。"
                    : string.Empty),
                "[AutoHook IPC]");

            return names.Count;
        }

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
