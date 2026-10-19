using Dalamud.Plugin.Ipc;
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

        /// <summary>
        /// 借走 AutoHook 的「啟用」開關（<b>只改執行期的值</b>，提供端不會寫進使用者的設定檔）。
        /// </summary>
        /// <remarks>
        /// 🔴 <b>不要直接呼叫</b>，走 <see cref="BorrowEnabled"/>／<see cref="ReturnEnabled"/>：
        /// 裸呼叫是單向寫入，ICE 跑完之後使用者的開關會一直停在 ICE 設的值上。
        /// </remarks>
        [EzIPC] public Action<bool> SetPluginState;

        /// <summary>讀 AutoHook 目前的啟用開關。<b>借用前的快照就靠它</b>。</summary>
        /// <remarks>
        /// ⚠️ 提供端回的是<b>欄位本身</b>（可能已經被別人借走了）⇒ 只在<b>第一次</b>借用時拍快照，
        /// 重複借用時再拍會把 ICE 自己設進去的 <see langword="true"/> 當成使用者的值。
        /// </remarks>
        [EzIPC] public Func<bool> GetPluginState;
        [EzIPC] public Action<bool> SetAutoGigState;
        [EzIPC] public Action<string> SetPreset;
        [EzIPC] public Action<string> SetPresetAutogig;
        [EzIPC] public Action<string> CreateAndSelectAnonymousPreset;
        [EzIPC] public Action<string> ImportAndSelectPreset;
        [EzIPC] public Action DeleteSelectedPreset;
        [EzIPC] public Action DeleteAllAnonymousPresets;
        /// <summary>
        /// 換餌（依道具 ID）。<b>提供端的簽章是 <c>bool SwapBaitById(uint)</c></b>
        /// （<c>AutoHook/IPC/AutoHookIPC.cs</c>），所以這裡宣告成 <c>Func&lt;uint, bool&gt;</c>。
        /// </summary>
        /// <remarks>
        /// 🔴 <b>不要直接呼叫這個欄位</b>，走 <see cref="TrySwapBait"/>（它帶端點探測與 /ahbait 退路）。
        /// </remarks>
        [EzIPC] public Func<uint, bool> SwapBaitById;

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

        // Installed walks the installed-plugin list. Plugins load / unload between frames, so the answer is
        // asked once per frame and shared by every caller in that frame (the mission table asks once per row).
        // Framework thread only: these memo fields are not synchronized.
        private DateTime _installedFrame;
        private bool _installedKnown;
        private bool _installedThisFrame;

        private bool InstalledThisFrame()
        {
            var frame = ECommons.DalamudServices.Svc.Framework.LastUpdate;
            if (!_installedKnown || frame != _installedFrame)
            {
                _installedThisFrame = Installed;
                _installedFrame = frame;
                _installedKnown = true;
            }
            return _installedThisFrame;
        }

        /// <summary>
        /// 這版 AutoHook 支不支援整包資料夾匯入。
        /// </summary>
        /// <remarks>
        /// 🔴 <b>這是出貨順序的相容性閘門。</b>使用者可能只更新 ICE 沒更新 AutoHook，
        /// 那些需要資料夾匯入的任務（494／495）必須<b>維持停用</b>而不是跑到一半才發現匯不進去——
        /// 後者的失敗形式是「站在釣點不動直到逾時」，log 一行都沒有。
        /// </remarks>
        public bool SupportsFolderImport()
        {
            if (!InstalledThisFrame())
                return false;

            if (_folderImportApiVersion is { } cached)
                return cached >= RequiredFolderImportApiVersion;

            // IPC 不存在／簽名不符時，SafeWrapper.AnyException 會吞掉例外並回傳 default(int) = 0。
            // 🔑 0 要當成「沒有這個功能」，不能當成「版本 0」。
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

        /// <summary>AutoHook 端 <c>SwapBaitById</c> 的完整 IPC 契約名。</summary>
        /// <remarks>🔑 用 <see cref="Name"/> ＋ <c>nameof</c> 組出來，保證與 EzIPC 訂閱的名字不可能漂移。</remarks>
        private const string SwapBaitByIdIpc = $"{Name}.{nameof(SwapBaitById)}";

        /// <summary>判定不可用之後的重探間隔。</summary>
        /// <remarks>
        /// 🔴 刻意<b>不</b>用 <c>EzThrottler</c> 當計時器：它是整個外掛共用的靜態 Dictionary、
        /// 零同步、首次必放行、key 全域持久。自己記下一次可以重探的時刻就好。
        /// </remarks>
        private const long SwapBaitRecheckIntervalMs = 60_000;

        /// <summary>
        /// <see cref="SwapBaitById"/> 這個 IPC 是否已被判定為不可用（AutoHook 沒有註冊它）。
        /// </summary>
        /// <remarks>
        /// 🔴 <b>這個判定刻意是可復原的</b>（<see cref="SwapBaitRecheckIntervalMs"/> 到期就再探一次）：
        /// 使用者很可能只更新了 ICE 還沒更新 AutoHook，永久閂住會讓他更新 AutoHook 之後
        /// <b>整個 session 都用不到這支 IPC</b>，而且完全沒有徵兆。
        /// </remarks>
        private bool _swapBaitIpcUnavailable;

        /// <summary>下一次可以重新探測端點的時刻（<see cref="Environment.TickCount64"/> 座標系）。</summary>
        private long _swapBaitIpcRecheckAt;

        /// <summary>「AutoHook 沒有這支 IPC」只寫一次記錄，不要每次重探都刷一行。</summary>
        private bool _loggedSwapBaitUnavailable;

        /// <summary>探測用的訂閱端。<c>GetIpcSubscriber</c> 拿到的東西不需要退訂，所以沒有 Dispose 的問題。</summary>
        private ICallGateSubscriber<uint, bool> swapBaitProbe;

        /// <summary>AutoHook 到底有沒有註冊 <see cref="SwapBaitById"/> 這支端點。</summary>
        /// <remarks>🔴 <b>為什麼需要這支探測</b>：端點是 <c>Func&lt;uint, bool&gt;</c>，回傳的
        /// <see langword="false"/> 有三種完全不同的意思——
        /// ①AutoHook 根本沒註冊這支端點（<c>IpcNotReadyError</c> 被
        /// <see cref="SafeWrapper.AnyException"/> 吞掉，回 <c>default(bool)</c>）
        /// ②AutoHook 註冊了，但它判定呼叫端不在 Framework 執行緒上而拒絕
        /// ③餌真的沒換成（身上沒有、或 ID 不在它的清單裡）。</remarks>
        private bool SwapBaitEndpointRegistered()
        {
            try
            {
                swapBaitProbe ??= Svc.PluginInterface.GetIpcSubscriber<uint, bool>(SwapBaitByIdIpc);
                return swapBaitProbe.HasFunction;
            }
            catch (Exception e)
            {
                IceLogging.Info($"探測 {SwapBaitByIdIpc} 有沒有註冊時發生例外：{e.GetType().Name}: {e.Message}。" +
                                "當成沒有註冊處理，改用 /ahbait 換餌。", "[AutoHook IPC]");
                return false;
            }
        }

        /// <summary>
        /// 換餌。<b>不要直接呼叫 <see cref="SwapBaitById"/></b>，走這一支。
        /// </summary>
        /// <remarks>
        /// 退路是 <c>ProcessCommand("/ahbait")</c>，與 ICE 既有用 <c>/ahstart</c> 驅動 AutoHook 的作法一致。
        /// 目前三個呼叫點都掛在 <c>P.TaskManager</c> 的任務上，本來就跑在 Framework 執行緒。
        /// </remarks>
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

            var now = Environment.TickCount64;
            if (_swapBaitIpcUnavailable && now >= _swapBaitIpcRecheckAt)
                _swapBaitIpcUnavailable = false; // 重探期到了，再問一次端點在不在

            if (!_swapBaitIpcUnavailable && SwapBaitById != null)
            {
                if (!SwapBaitEndpointRegistered())
                {
                    _swapBaitIpcUnavailable = true;
                    _swapBaitIpcRecheckAt = now + SwapBaitRecheckIntervalMs;

                    if (!_loggedSwapBaitUnavailable)
                    {
                        _loggedSwapBaitUnavailable = true;
                        IceLogging.Info($"這版 AutoHook 沒有註冊 {SwapBaitByIdIpc} 這個 IPC（版本比 ICE 舊），" +
                                        "接下來改用 /ahbait 指令換餌。" +
                                        $"每 {SwapBaitRecheckIntervalMs / 1000} 秒會再探一次，所以更新 AutoHook 之後不必重載 ICE。" +
                                        "這行訊息只會出現一次。", "[AutoHook IPC]");
                    }
                }
                else if (SwapBaitById(baitId))
                {
                    _loggedSwapBaitUnavailable = false;
                    if (logThrottle)
                        IceLogging.Info($"透過 IPC {SwapBaitByIdIpc} 要求換餌：{baitId}", "[AutoHook IPC]");
                    return true;
                }
                else
                {
                    // 🔴 端點在、也真的被呼叫到了，只是這一次沒換成 —— **不能**拿這個 false 去閂住 IPC。
                    //    可能的原因：身上沒有這個餌、餌 ID 不在 AutoHook 的清單裡，
                    //    或它判定呼叫端不在 Framework 執行緒上而拒絕。
                    //    往下再送一次 /ahbait 當第二次嘗試。
                    if (logThrottle)
                        IceLogging.Info($"AutoHook 收到換餌請求（餌 ID {baitId}）但回報沒有換成，" +
                                        "接著再送一次 /ahbait 當第二次嘗試。", "[AutoHook IPC]");
                }
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

        // ── 啟用開關的借用與還原 ────────────────────────────────────────────
        // 🔴 契約：只在釣魚段借，跑完必須還；狀態只從 Framework 執行緒讀寫。
        // ⚠️ AutoHook 的暫停租約只能往「停手」壓（ResolveEnabled＝使用者的值 && 沒人壓制），
        //    表達不了「暫時幫忙打開」⇒ 這裡走快照／還原而不是租約。
        // 📌 提供端沒有 GetPluginState 時不借用，退回原本的單向 SetPluginState(true)。

        /// <summary>AutoHook 端 <c>GetPluginState</c> 的完整 IPC 契約名。</summary>
        private const string GetPluginStateIpc = $"{Name}.{nameof(GetPluginState)}";

        private ICallGateSubscriber<bool> pluginStateProbe;

        private bool borrowedPluginState;

        /// <summary>借用前使用者自己的值（只在借用生效時有意義）。</summary>
        private bool pluginStateBeforeBorrow;

        private bool loggedNoPluginStateGetter;

        /// <summary>ICE 現在有沒有借著（偵錯用，不是判定依據）。</summary>
        public bool BorrowingPluginState => borrowedPluginState;

        /// <summary>釣魚段開始前呼叫：確保 AutoHook 是開的。<b>冪等</b>，同一輪只拍一次快照。</summary>
        public void BorrowEnabled(string handle)
        {
            if (!Installed)
                return;

            if (!borrowedPluginState && TryReadPluginState(out var userValue))
            {
                borrowedPluginState = true;
                pluginStateBeforeBorrow = userValue;
                if (!userValue)
                    IceLogging.Info("AutoHook 本來是關著的，ICE 先幫忙打開；這一輪跑完（或 ICE 卸載）會關回去。", handle);
            }

            SetPluginState?.Invoke(true);
        }

        /// <summary>把啟用開關還給使用者。<b>冪等</b>；沒借就什麼都不做。</summary>
        public void ReturnEnabled(string reason)
        {
            if (!borrowedPluginState)
                return;

            borrowedPluginState = false;
            if (!pluginStateBeforeBorrow && Installed)
            {
                SetPluginState?.Invoke(false);
                IceLogging.Info($"已把 AutoHook 的啟用開關關回使用者原本的值（{reason}）。", "[AutoHook IPC]");
            }
        }

        /// <summary>每幀對齊：ICE 的自動化停了就把開關還回去。</summary>
        /// <remarks>
        /// 📌 冪等且沒借用時第一行就回去，不做任何 IPC。掛在 <c>SchedulerMain.State</c> 而不是
        /// 逐一列舉釣魚的離開路徑——ICE 的停止路徑有十幾條，列舉必漏。
        /// </remarks>
        public void SyncBorrow(bool shouldHold)
        {
            if (!shouldHold)
                ReturnEnabled("ICE 的自動化已停止");
        }

        /// <summary>讀使用者目前的啟用開關。端點沒註冊時回 <see langword="false"/> 並<b>不</b>輸出值。</summary>
        /// <remarks>
        /// 🔴 一定要先問 <c>HasFunction</c>：端點缺席時 <see cref="SafeWrapper.AnyException"/>
        /// 會把 <c>IpcNotReadyError</c> 吞成 <c>default(bool)</c>＝<see langword="false"/>，
        /// 與「使用者真的把它關著」分不出來 —— 猜錯的代價是 ICE 跑完把使用者開著的 AutoHook 關掉。
        /// </remarks>
        private bool TryReadPluginState(out bool state)
        {
            state = false;
            try
            {
                pluginStateProbe ??= Svc.PluginInterface.GetIpcSubscriber<bool>(GetPluginStateIpc);
                if (!pluginStateProbe.HasFunction)
                {
                    if (!loggedNoPluginStateGetter)
                    {
                        loggedNoPluginStateGetter = true;
                        IceLogging.Info($"這版 AutoHook 沒有註冊 {GetPluginStateIpc}，ICE 無法得知使用者原本的設定，" +
                                        "所以只會把它打開、不會幫忙關回去（與更新前的行為相同）。", "[AutoHook IPC]");
                    }
                    return false;
                }
            }
            catch (Exception e)
            {
                IceLogging.Info($"探測 {GetPluginStateIpc} 時發生例外：{e.GetType().Name}: {e.Message}。當成沒有註冊處理。",
                                "[AutoHook IPC]");
                return false;
            }

            state = GetPluginState?.Invoke() ?? false;
            return true;
        }
    }
}
