using Dalamud.Game.Addon.Lifecycle;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace ICE.Utilities
{
    /// <summary>
    /// 「同一扇視窗的同一個按法，按過就不要再按，直到它真的收掉」的共用閘門（<see cref="YesnoPressGuard"/> 的泛化版）。
    /// </summary>
    /// <remarks>
    /// 🔴🔴 <b>要防的東西</b>：<c>SelectYesno</c> 這類「按下即關」的視窗被按下之後有<b>「正在關閉中」的幾幀</b>，
    /// 這段期間 <c>GetAddonByName</c> 仍然回得到實例、<c>IsVisible</c> 與 <c>UldManager.LoadedState == Loaded</c>
    /// 也都還成立（＝<c>IsAddonReady</c> 三關全過），此時再對它送 callback／送輸入事件（<c>ReceiveEvent</c>、
    /// <c>ClickAddonButton</c>、<c>Talk.Click()</c>……）就是原生 AccessViolation（C0000005）。
    /// AVE 在 .NET Core 是 corrupted-state exception，<c>try</c>/<c>catch</c> 與任何例外隔離都攔不住 ——
    /// <b>唯一的防護是「不要送第二次」，不是「送了再接住」</b>。<br/>
    /// <br/>
    /// 🔴 <b>為什麼既有的節流不是防護</b>：<c>EzThrottler</c>／<c>FrameThrottler</c> 記的是「這把 <b>key</b>
    /// 上一次放行是什麼時候」，不是「這扇<b>窗</b>已經按過」；而且對沒見過的 key <b>第一次一律放行</b>，
    /// 所以兩個呼叫點用兩把 key 接力按同一扇還在關閉中的窗時，兩邊的節流誰都擋不住。
    /// 本守衛以<b>視窗位址</b>當索引鍵，跨呼叫點的重按正是它存在的理由。<br/>
    /// <br/>
    /// 🔑 <b>粒度＝（視窗名稱，實例位址，按法）</b>：ICE 有多處<b>刻意</b>在同一扇還開著的窗上連送不同的按法
    /// （<c>WKSMissionInfomation</c> 第一個 tick 按「回報」、下一個 tick 按「放棄」；宇宙商店對同一扇
    /// <c>ShopExchangeCurrency</c> 逐件選購），只看位址會把這些正常流程一起擋掉，所以擋的是
    /// 「同一扇窗＋同一個按法」＝真正的重按。<br/>
    /// 「回答一次就結束」的窗（<see cref="SingleAnswerAddons"/>：<c>SelectYesno</c> 族）不管哪一條路徑、
    /// 送的是什麼參數，一律併成同一個 key —— 確定／取消／<c>Callback.Fire(-1)</c> 三種按法對這種窗
    /// 都是「按下即關」，接力按就是崩潰本身。<br/>
    /// <br/>
    /// <b>解除封鎖有兩條互補的觀察點</b>（兩條都只會讓封鎖<b>提早</b>解除，不會延後）：
    /// <list type="number">
    /// <item><b>輪詢</b>：被記下的位址已經不在該名稱的 addon 清單裡（掃全索引 1..99，掃到第一個空的停）
    /// ⇒ 那扇窗真的收乾淨了。ICE 的按窗點全部由 <c>Framework.Update</c>（<c>NeoTaskManager</c> 任務／
    /// <c>PlayerHandlers.Tick</c>）驅動，每個 tick 都會再進來，所以這條在 ICE 成立；
    /// 另外 <see cref="Tick"/> 也每幀掃一次，讓「消失」那幾幀不會因為呼叫端被節流擋著而沒被觀察到。</item>
    /// <item><b>AddonLifecycle 事件</b>：<c>PreFinalize</c>（這一扇正在被銷毀）與 <c>PostSetup</c>
    /// （同一個位址被新的一扇重用）。🔴 這條是<b>必要的</b>：同名 addon 關掉再開常常<b>重用同一塊記憶體</b>
    /// （宇宙商店逐件購買連續彈出的 <c>SelectYesno</c> 正是這個形狀），只靠輪詢的話重開的那扇會被誤認成
    /// 「按過的那扇還沒收掉」而白白被擋到逃生口。⚠️ 刻意<b>不</b>把 <c>PostRefresh</c> 當解除點：
    /// 它可能在關閉中那幾幀觸發，那會把封鎖提早解除。</item>
    /// </list>
    /// 🔴 全程只做<b>位址等值比較，永遠不解參</b>——被記下的那個位址隨時可能已經失效。<br/>
    /// <br/>
    /// 🔴 <b>逃生口是刻意的</b>：萬一某扇窗既不 finalize 也不重新 setup（上一次的按壓根本沒生效、視窗就是還開著），
    /// 沒有逃生口的話呼叫端會永遠按不下去 —— ICE 這些步驟多半掛 <c>Utils.TaskConfig</c>（30 分鐘、逾時不中止），
    /// 等於半小時的無聲空轉。單答終結窗用 <see cref="DefaultEscapeFrames"/>（遠大於關閉所需的幾幀），
    /// 走到逃生口是異常，寫 Information（使用者跑 LogLevel 2）；「按一次翻一頁、窗不會因為被按而消失」的
    /// 多次互動窗（<c>Talk</c> 是代表）用 <see cref="RoutineRePressEscapeFrames"/>，走逃生口是常態，寫 Debug 不洗版。<br/>
    /// <br/>
    /// 📌 <b>正常路徑的行為沒有改變</b>：第一次看到某一扇窗的某個按法一律當場放行；被擋下來時回 <c>false</c>
    /// （<b>不是</b> <c>null</c>：NeoTaskManager 對 <c>null</c> 的語意是清掉整條佇列），對呼叫端的意義一律是
    /// 「這一輪沒按到，下一輪再來」，與「視窗還沒出現」走同一條既有路徑。<br/>
    /// ⚠️ 只在主執行緒使用（與呼叫端的節流器同一個前提）。
    /// </remarks>
    internal static unsafe class AddonPressGuard
    {
        private const string Handle = "[Addon Press Guard]";

        /// <summary>
        /// 單答終結窗：已經按過、那扇窗卻還沒消失時，最多再等這麼多幀才允許補按一次。
        /// </summary>
        /// <remarks>
        /// 🔑 這不是節流 —— 真正的防護是「同一扇窗只按一次」，這個值只是防死鎖的逃生口。
        /// 60 幀（約 0.5~1 秒）遠遠大於「關閉中的那幾幀」，補按永遠不會落在危險窗口內。
        /// 沿用 <see cref="YesnoPressGuard"/> 原本的值，既有 SelectYesno 接點的行為不變。
        /// </remarks>
        internal const int DefaultEscapeFrames = 60;

        /// <summary>
        /// 多次互動窗（<c>Talk</c> 翻頁、採集窗逐件採集、商店逐件選購……）用的短逃生口。
        /// </summary>
        /// <remarks>
        /// 這種窗整段都不關也不重建，輪詢與生命週期兩條解除點都不會觸發，走逃生口是<b>常態</b>而不是異常，
        /// 所以放行 log 寫 Debug。關閉中的危險窗口 &lt; 10 幀，15 幀不落在裡面；每頁多等 0.25 秒幾乎無感。
        /// ⚠️ 刻意<b>不</b>用「文字變了」當翻頁證據：關閉中的窗文字會讀壞（U+FFFD）。
        /// （2026-09-02 艦隊政策：Talk 類一律 15 幀。）
        /// </remarks>
        internal const int RoutineRePressEscapeFrames = 15;

        /// <summary>輪詢解除時最多掃到第幾個同名實例（掃到第一個空的就提早停）。</summary>
        private const int MaxAddonIndex = 99;

        /// <summary>萬一清理判準在某個版本失效，也不要讓這張表無限長大。</summary>
        private const int EntryCap = 32;

        /// <summary>
        /// 「一扇窗一生只回答一次」的視窗：這些名字底下的按法一律併成同一個 key。
        /// </summary>
        /// <remarks>
        /// ⚠️ 只放<b>回答一次就結束</b>的窗。<c>SelectString</c>／<c>SelectIconString</c> 刻意<b>不</b>在此：
        /// 巢狀選單常常重用同一個實例只換內容（不觸發 PostSetup），併 key 會讓下一層的選擇被擋到逃生口；
        /// 那兩個用與 <c>Entry.Select()</c> 相同的參數字串當按法即可。
        /// </remarks>
        private static readonly HashSet<string> SingleAnswerAddons = new(StringComparer.Ordinal)
        {
            "SelectYesno",
            "MaterializeDialog",
        };

        /// <summary>
        /// <c>WKSMissionInfomation</c> 的「回報結果」與「放棄任務」<b>共用</b>的按法 key。
        /// </summary>
        /// <remarks>
        /// 🔴 這兩顆都是「按下之後這扇窗就會收掉」的<b>終結性</b>動作。各用一把 key 擋不住跨鈕、
        /// 跨呼叫點的接力按：<c>Task_TurninMission</c>／<c>Task_AbandonMission</c> 任一邊按完回報、
        /// 那扇窗還沒收完的那幾幀，另一邊（或同一支的 else-if）再按放棄，就是對正在關閉的視窗
        /// 送輸入事件 ＝ 攔不到的存取違規。<b>凡是按這兩顆的呼叫點一律用這個常數，不要再寫字面值。</b><br/>
        /// ⚠️ <b>不可以</b>改成把 <c>WKSMissionInfomation</c> 放進 <see cref="SingleAnswerAddons"/>：
        /// 那會把同一扇窗的<b>所有</b>按法（「星體分解」<c>Task_Gather</c>、「宇宙背包」、「製作筆記」）
        /// 一起併成同一把 key，而那幾顆按下之後這扇窗<b>並不會關</b>（只是疊出子視窗），
        /// 併進來只會把正常流程擋到逃生口。要併的只有這兩顆終結鈕 ——
        /// 所以是在呼叫點共用這個字串，而不是在白名單裡整扇窗一起併。
        /// </remarks>
        internal const string WksMissionExitPressKey = "ReportOrAbandon";

        /// <param name="AddonName">視窗名稱（解除封鎖的監聽器與輪詢都以它為準）。</param>
        /// <param name="Address">被按的那個實例的位址，<b>只做等值比較</b>。</param>
        /// <param name="Press">按法；單答終結窗一律為空字串。</param>
        private readonly record struct PressKey(string AddonName, nint Address, string Press);

        /// <param name="Frame">按下時的繪製幀號。</param>
        /// <param name="EscapeFrames">登記當時呼叫端給的逃生口；<see cref="IsHeld"/> 判「這筆還熱著」用它。</param>
        private readonly record struct PressRecord(long Frame, int EscapeFrames);

        /// <summary>目前還開著、而且被按過的窗。每次呼叫都會把已經消失的清掉，正常情況下最多一兩筆。</summary>
        private static readonly Dictionary<PressKey, PressRecord> Pressed = [];

        private static readonly Dictionary<string, IAddonLifecycle.AddonEventDelegate> Watchers =
            new(StringComparer.Ordinal);

        /// <summary>守衛自己的時鐘：<c>Framework.Update</c> 跑過的 tick 數。</summary>
        /// <remarks>
        /// 🔴 <b>刻意不用 <c>UiBuilder.FrameCount</c></b>（本 pin 的實作證據）：那個計數器在
        /// <c>UiBuilder.OnDraw()</c> 的<b>最後</b>才遞增（<c>UiBuilder.cs:814</c>），而
        /// 「使用者隱藏 UI」「<b>過場動畫</b>」「GPose」三種情形會在 <c>:757-771</c> 直接 <c>return</c>
        /// —— 三個開關 <c>ToggleUiHide</c>／<c>ToggleUiHideDuringCutscenes</c>／
        /// <c>ToggleUiHideDuringGpose</c> 的預設值<b>全是 true</b>；連 <c>:753</c> 的
        /// <c>gameGui == null</c> 也在遞增之前。也就是<b>過場動畫期間繪製幀整段停住</b>。<br/>
        /// 拿它當時鐘的話，這裡的逃生口（<see cref="DefaultEscapeFrames"/>／
        /// <see cref="RoutineRePressEscapeFrames"/>）在過場中<b>永遠不會到期</b> —— 方向是安全的
        /// （不會去按關閉中的窗），但整個按窗流程會停擺，Talk 這種翻頁站會停在第一頁。<br/>
        /// <c>Framework.Update</c> 不受 UI 隱藏影響，所以改成自己數。正常情況下兩者 1:1，
        /// 逃生口的幀數意義不變，<b>不要因為換了時鐘去調 15／60 那兩個值</b>。
        /// </remarks>
        private static long guardTick;

        /// <summary>時鐘是否已經掛上 <c>Framework.Update</c>。</summary>
        private static int frameClockSubscribed;

        /// <summary>讀時鐘；順便確保它已經在走（第一次讀的時候才掛，不必依賴任何啟動順序）。</summary>
        private static long GuardTick
        {
            get
            {
                EnsureFrameClock();
                return guardTick;
            }
        }

        /// <summary>掛上守衛自己的時鐘（冪等；外掛建構子與每次讀時鐘都會呼叫）。</summary>
        /// <remarks>
        /// 🔴 用 <c>Interlocked.CompareExchange</c> 而不是 bool 旗標：<b>重複訂閱不是沒效果，
        /// 是計數器一個 tick 前進 2</b> ＝ 所有逃生口對半砍。<br/>
        /// 🔴 <b>刻意不掛在 <see cref="Tick"/> 裡</b>：<c>Tick()</c> 的呼叫點（<c>ICE.cs</c>）包在
        /// <c>if (Player.Available)</c> 內，登出／過場載入期間會再度停住 —— 那正是要修掉的形狀。<br/>
        /// 🔑 掛在<b>讀取端</b>而不是 <see cref="EnsureWatching"/>：<see cref="IsHeld"/> 會在還沒有
        /// 任何按壓紀錄時就先讀時鐘，掛在後者會留縫。<br/>
        /// 🔑 另外從外掛建構子先叫一次（<see cref="StartFrameClock"/>），讓這個 handler 排在 ICE 自己的
        /// <c>Tick</c> <b>前面</b>：同一個外掛內部的 <c>Framework.Update</c> 多播委派是<b>整條</b>包在
        /// 單一 try/catch 裡的（本 pin <c>Framework.cs:599-609</c> 的
        /// <c>PluginErrorHandler.InvokeAndCatch</c>），排在前面的 handler 擲例外時，後面的 handler
        /// 那一個 tick 完全不會被呼叫（不會取消訂閱，下一幀恢復）。時鐘要盡量排在最前面。
        /// </remarks>
        internal static void StartFrameClock() => EnsureFrameClock();

        private static void EnsureFrameClock()
        {
            if (Interlocked.CompareExchange(ref frameClockSubscribed, 1, 0) != 0)
                return;

            Svc.Framework.Update += AdvanceFrameClock;
        }

        /// <summary>只做一件事：把時鐘推前一格。</summary>
        /// <remarks>🔴 <b>不可以在遞增前面加任何條件或 early return</b> —— 那會把這次修掉的停擺原封不動搬回來。</remarks>
        private static void AdvanceFrameClock(IFramework _) => guardTick++;

        /// <summary>
        /// 登記「即將對這扇視窗送出這一個按法」。<b>回 <see langword="false"/> ＝這一幀絕對不能按。</b>
        /// </summary>
        /// <param name="label">記 log 用的呼叫點名稱（<b>不</b>參與判定）。</param>
        /// <param name="addonName">視窗名稱。</param>
        /// <param name="addon">那扇窗的位址（<c>AddonMaster.Base</c> 或 <c>AtkUnitBase*</c> 轉 <c>nint</c>）。<b>只當識別碼，本方法不解參。</b></param>
        /// <param name="pressKey">
        /// 這一次的「按法」（同一扇窗上不同的按法互不干擾；要擋的是<b>同一個按法重複送</b>）。
        /// <see cref="SingleAnswerAddons"/> 裡的窗會被強制併成同一個 key。
        /// </param>
        /// <param name="escapeFrames">逃生口幀數：<see cref="DefaultEscapeFrames"/> 或 <see cref="RoutineRePressEscapeFrames"/>。</param>
        /// <returns>
        /// <c>true</c>＝可以按（本方法已經把這一次記下來，呼叫端<b>必須</b>真的按下去）。<br/>
        /// <c>false</c>＝這扇窗的這個按法剛才已經按過而且還沒觀察到它收掉，這一幀不要按。
        /// </returns>
        /// <remarks>
        /// ⚠️ 一定要放在條件式的<b>最後</b>一項（其他閘門都通過、確定要按了才呼叫）——
        /// 它有副作用（記下這一次按壓）。放在前面會把「其實沒按」記成「按過了」，
        /// 讓真正該按的那一次被擋掉。位址是 0 就沒有可以識別的窗，交給呼叫端原本的守衛處理（它們都先做過 IsAddonReady）。
        /// </remarks>
        public static bool TryBeginPress(string label, string addonName, nint addon, string pressKey = "",
                                         int escapeFrames = DefaultEscapeFrames)
        {
            if (addon == 0 || string.IsNullOrEmpty(addonName))
                return true;

            if (SingleAnswerAddons.Contains(addonName))
                pressKey = string.Empty;

            ReleaseVanished();
            EnsureWatching(addonName);

            var key = new PressKey(addonName, addon, pressKey);
            var frame = GuardTick;

            if (Pressed.TryGetValue(key, out var pressed))
            {
                var elapsed = frame - pressed.Frame;

                // 這一扇的這個按法已經按過。窗還在＝可能正在關閉中，此時再送就是上面說的 AVE。
                if (elapsed < escapeFrames)
                {
                    LogHold(label, key, escapeFrames);
                    return false;
                }

                // 逃生口：等了遠超過關閉所需的時間，窗仍在。視為那次按壓沒生效（或這是另一扇窗
                // 重用了同一塊記憶體而兩條解除點都沒觀察到），放行補按一次。
                if (escapeFrames <= RoutineRePressEscapeFrames)
                {
                    // 多次互動窗走到這裡是常態（每一頁都會走到），寫 Debug 不洗版。
                    if (EzThrottler.Throttle($"AddonPressGuard-RoutineRelease-{addonName}", 10000))
                        IceLogging.Debug($"{label}：「{addonName}」（實例 0x{addon:X}，按法「{pressKey}」）按下後 {elapsed} 幀窗還在（多次互動窗的常態），放行下一次。", Handle);
                }
                else
                {
                    // 📌 寫 Information：使用者跑 LogLevel 2，Debug 收不到，
                    //    而「按了卻沒關掉」正是需要被回報的狀況。
                    IceLogging.Info($"{label}：「{addonName}」（實例 0x{addon:X}，按法「{pressKey}」）按下之後 {elapsed} 幀仍未關閉，補按一次。", Handle);
                }
            }

            if (Pressed.Count >= EntryCap)
            {
                IceLogging.Info($"{label}：待清理的視窗按壓紀錄累積到 {Pressed.Count} 筆，整批清掉重來。", Handle);
                Pressed.Clear();
            }

            Pressed[key] = new PressRecord(frame, escapeFrames);
            return true;
        }

        /// <inheritdoc cref="TryBeginPress(string, string, nint, string, int)"/>
        /// <remarks>給手上只有 <c>AtkUnitBase*</c> 的呼叫點用的多載：指標只轉成位址，不解參。</remarks>
        public static bool TryBeginPress(string label, string addonName, AtkUnitBase* addon, string pressKey = "",
                                         int escapeFrames = DefaultEscapeFrames)
            => TryBeginPress(label, addonName, (nint)addon, pressKey, escapeFrames);

        /// <inheritdoc cref="TryBeginPress(string, string, nint, string, int)"/>
        /// <remarks>
        /// 給手上是 <c>AddonMaster</c>（含 ICE 自己的 <c>ShopExchangeCurrency</c>）而且不在 <c>unsafe</c> 區塊裡的
        /// 呼叫點用的多載：指標轉型在本方法內部完成，只取位址不解參。
        /// </remarks>
        public static bool TryBeginPress(string label, string addonName, IAddonMasterBase master, string pressKey = "",
                                         int escapeFrames = DefaultEscapeFrames)
            => TryBeginPress(label, addonName, master == null ? 0 : (nint)master.Base, pressKey, escapeFrames);

        /// <summary>
        /// 只<b>看</b>不登記：這扇視窗的這個按法現在是不是被擋著。
        /// </summary>
        /// <remarks>
        /// 給「按之前要先讀窗上的文字來決定按哪個鈕」的呼叫端用，順序是
        /// <see cref="IsHeld"/> → 讀文字 → <see cref="TryBeginPress"/> → 按：
        /// 被擋的那幾幀連文字都不去讀（那正是視窗記憶體變動中的幾幀），
        /// 而讀完決定不按時也不會留下一筆「登記了卻沒按」的紀錄白白封鎖到逃生口。
        /// 判準與 <see cref="TryBeginPress"/> 完全相同，逃生口用登記當時存下來的那個值。<br/>
        /// ⚠️ 回 <see langword="true"/> ＝ 這一幀不要碰。
        /// </remarks>
        public static bool IsHeld(string addonName, nint addon, string pressKey = "")
        {
            if (addon == 0 || string.IsNullOrEmpty(addonName))
                return false;

            if (SingleAnswerAddons.Contains(addonName))
                pressKey = string.Empty;

            ReleaseVanished();

            var key = new PressKey(addonName, addon, pressKey);
            if (!Pressed.TryGetValue(key, out var pressed))
                return false;

            var held = GuardTick - pressed.Frame < pressed.EscapeFrames;
            if (held)
                LogHold("讀窗前檢查", key, pressed.EscapeFrames);

            return held;
        }

        /// <inheritdoc cref="IsHeld(string, nint, string)"/>
        public static bool IsHeld(string addonName, IAddonMasterBase master, string pressKey = "")
            => IsHeld(addonName, master == null ? 0 : (nint)master.Base, pressKey);

        /// <summary>
        /// 讀窗上的文字來做判定的站，讀到 U+FFFD 就代表視窗記憶體正在變動（多半是關閉中），<b>這一幀不碰</b>。
        /// </summary>
        /// <returns><see langword="true"/> ＝ 文字讀壞了，呼叫端這一幀什麼都不要做。</returns>
        /// <remarks>
        /// 這是崩潰的旁證而不是防護本體（防護是 <see cref="TryBeginPress"/>）：實機崩潰前 log 裡的 prompt
        /// 就是這種亂碼。寫 Information 讓使用者回報時看得到。
        /// </remarks>
        public static bool IsTextCorrupt(string addonName, string? text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains((char)0xFFFD))
                return false;

            if (EzThrottler.Throttle($"AddonPressGuard-Corrupt-{addonName}", 1000))
                IceLogging.Info($"「{addonName}」的文字讀到 U+FFFD 亂碼（視窗記憶體正在變動，多半是關閉中），這一幀不碰它。", Handle);

            return true;
        }

        /// <summary>
        /// 把 <c>Callback.Fire</c> 的參數組壓成穩定的「按法」字串（<c>"T|-1"</c>、<c>"T|12|0"</c>）。
        /// </summary>
        /// <remarks>用不變文化格式化，免得數字在別的地區設定下變成不同的字串（那會讓同一個按法被當成兩種）。</remarks>
        public static string BuildPressKey(bool updateState, params int[] values)
        {
            var sb = new StringBuilder(updateState ? "T" : "F");
            foreach (var value in values)
            {
                sb.Append('|');
                sb.Append(value.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 每幀從 <c>ICE.Tick</c> 呼叫一次：把已經從 addon 清單消失的窗解除封鎖。
        /// </summary>
        /// <remarks>
        /// 呼叫端多半被 500ms 節流擋著，光靠「呼叫時才掃」會看不到「消失」那幾幀（兩扇連續的同名窗若重用
        /// 同一位址，會被誤判成同一扇窗而多等到逃生口）。表是空的時候第一行就回去，成本可忽略。
        /// </remarks>
        public static void Tick() => ReleaseVanished();

        /// <summary>外掛卸載時硬拆所有監聽器（不留指向本組件的委派）並清表。</summary>
        public static void ForceTeardown()
        {
            foreach (var (addonName, handler) in Watchers)
            {
                Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, addonName, handler);
                Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addonName, handler);
            }

            // 時鐘也拆掉，不留指向本組件的委派；下次有人讀時鐘會自己重新掛上。
            if (Interlocked.Exchange(ref frameClockSubscribed, 0) == 1)
                Svc.Framework.Update -= AdvanceFrameClock;

            Watchers.Clear();
            Pressed.Clear();
        }

        /// <summary>被擋那一幀的診斷：單答終結窗寫 Information（使用者跑 LogLevel 2）、多次互動窗寫 Debug；每扇窗 1 秒節流免得洗版。</summary>
        private static void LogHold(string label, PressKey key, int escapeFrames)
        {
            if (!EzThrottler.Throttle($"AddonPressGuard-Hold-{key.AddonName}", 1000))
                return;

            var message = $"{label}：「{key.AddonName}」（實例 0x{key.Address:X}，按法「{key.Press}」）按過之後還沒觀察到它收掉，這一幀不再碰它 —— 對關閉中的視窗送事件是攔不到的存取違規。";
            if (escapeFrames <= RoutineRePressEscapeFrames)
                IceLogging.Debug(message, Handle);
            else
                IceLogging.Info(message, Handle);
        }

        /// <summary>
        /// 把已經從同名 addon 清單消失的窗解除封鎖。
        /// </summary>
        /// <remarks>
        /// 「不在 addon 清單裡了」是唯一能確定「上一次按下的那扇已經收乾淨」的證據。<br/>
        /// ⚠️ 判準刻意<b>不</b>用「文字還對不對」或「<c>IsVisible</c>」：窗在拆除途中可能有幾幀讀不到提示文字、
        /// 旗標也還沒翻，拿那些當「窗不見了」會在最危險的那幾幀把封鎖解除掉。<br/>
        /// 掃整串索引而不是只看第 1 個，是因為同時可能開著多扇同名窗，被記下的那扇不一定在第 1 格。<br/>
        /// 🔴 全程只做位址等值比較，<b>永遠不解參</b>。
        /// </remarks>
        private static void ReleaseVanished()
        {
            if (Pressed.Count == 0)
                return;

            // 同名的窗只掃一次；先收集再刪：不能在列舉 Dictionary 的同時改它。
            Dictionary<string, HashSet<nint>>? liveByName = null;
            List<PressKey>? gone = null;

            foreach (var key in Pressed.Keys)
            {
                liveByName ??= new(StringComparer.Ordinal);
                if (!liveByName.TryGetValue(key.AddonName, out var live))
                {
                    live = CollectLive(key.AddonName);
                    liveByName[key.AddonName] = live;
                }

                if (!live.Contains(key.Address))
                    (gone ??= []).Add(key);
            }

            if (gone == null)
                return;

            foreach (var key in gone)
                Pressed.Remove(key);
        }

        private static HashSet<nint> CollectLive(string addonName)
        {
            var live = new HashSet<nint>();
            for (var i = 1; i <= MaxAddonIndex; i++)
            {
                var addon = (nint)Svc.GameGui.GetAddonByName(addonName, i).Address;
                if (addon == 0)
                    break;
                live.Add(addon);
            }

            return live;
        }

        /// <summary>那個位址上的窗已經走完生命週期（PreFinalize）或被新的一扇重用（PostSetup）：解除它的所有紀錄。</summary>
        private static void ReleaseAddress(nint address)
        {
            if (address == 0 || Pressed.Count == 0)
                return;

            List<PressKey>? gone = null;
            foreach (var key in Pressed.Keys)
            {
                if (key.Address == address)
                    (gone ??= []).Add(key);
            }

            if (gone == null)
                return;

            foreach (var key in gone)
                Pressed.Remove(key);
        }

        /// <summary>
        /// 第一次守護某個 addon 名稱時掛上解除封鎖用的監聽器。
        /// </summary>
        /// <remarks>
        /// 掛上去之後就不再拆（只在 <see cref="ForceTeardown"/> 拆）：這兩條監聽器只做一次字典移除，
        /// 成本可忽略，而動態掛／拆比較容易留下懸空的監聽器。回呼裡只取 <c>args.Addon.Address</c>
        /// 這個位址值，不解參。
        /// </remarks>
        private static void EnsureWatching(string addonName)
        {
            if (Watchers.ContainsKey(addonName))
                return;

            IAddonLifecycle.AddonEventDelegate handler = (_, args) => ReleaseAddress(args.Addon.Address);

            Watchers[addonName] = handler;
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, addonName, handler);
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonName, handler);
        }
    }
}
