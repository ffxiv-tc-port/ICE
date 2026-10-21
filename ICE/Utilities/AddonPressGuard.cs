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
    /// 本守衛以<b>視窗位址</b>當索引鍵，跨呼叫點的重按正是它存在的理由。<br/>
    /// 🔴 全程只做<b>位址等值比較，永遠不解參</b>——被記下的那個位址隨時可能已經失效。<br/>
    /// ⚠️ 只在主執行緒使用（與呼叫端的節流器同一個前提）。
    /// </remarks>
    internal static unsafe class AddonPressGuard
    {
        private const string Handle = "[Addon Press Guard]";

        /// <summary>按窗診斷那一行的前綴。<b>全艦隊逐字統一，不要改。</b></summary>
        private const string PressDiagnosticHandle = "[按窗診斷]";

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
        /// </remarks>
        internal const int RoutineRePressEscapeFrames = 15;

        /// <summary>輪詢解除時最多掃到第幾個同名實例（掃到第一個空的就提早停；掃滿仍沒停＝查詢不成立、不解除）。</summary>
        private const int MaxAddonIndex = 256;

        /// <summary>常駐窗按下後，要觀察到「還在清單裡、但已隱藏」持續這麼多幀才解除。</summary>
        /// <remarks>不承重：真正的防護是送出前要求常駐窗可見（<see cref="PersistentAddons"/>），這個值只決定要等多久。</remarks>
        internal const int HiddenReleaseFrames = 20;

        /// <summary>
        /// 實機證實是「常駐」的窗：關閉只是隱藏，位址一直留在清單裡，不會 PreFinalize 也不會重新 PostSetup。
        /// </summary>
        /// <remarks>🔴 這份名單同時是「送出前必須可見」的名單（與「隱藏夠久才解除」互為反面）；把會被銷毀的窗放進來＝拆除途中提早解除。</remarks>
        private static readonly HashSet<string> PersistentAddons = new(StringComparer.Ordinal)
        {
            "ContextMenu",
            "ContextIconMenu",
            "Talk",
        };

        /// <summary>萬一清理判準在某個版本失效，也不要讓這張表無限長大。到頂時只淘汰冷紀錄（<see cref="EvictCold"/>）。</summary>
        private const int EntryCap = 256;

        private static readonly List<PressKey> evictScratch = [];

        /// <summary>移除所有已過自己逃生口的紀錄；回 true＝至少騰出一格。</summary>
        private static bool EvictCold(long frame)
        {
            evictScratch.Clear();
            foreach (var (k, rec) in Pressed)
            {
                if (frame - rec.Frame >= rec.EscapeFrames)
                    evictScratch.Add(k);
            }
            foreach (var k in evictScratch)
                Pressed.Remove(k);
            return evictScratch.Count > 0;
        }

        /// <summary>
        /// 「一扇窗一生只回答一次」的視窗：這些名字底下的按法一律併成同一個 key。
        /// </summary>
        /// <remarks>
        /// ⚠️ 只放<b>回答一次就結束</b>的窗。<c>SelectString</c>／<c>SelectIconString</c> 選一項就關，按索引記帳時
        /// 換一個索引就會打在關閉中的窗上，所以併 key。巢狀選單若真的重用同一實例，代價只是等到逃生口（會寫 Information）。
        /// </remarks>
        private static readonly HashSet<string> SingleAnswerAddons = new(StringComparer.Ordinal)
        {
            "SelectYesno",
            "MaterializeDialog",
            "SelectString",
            "SelectIconString",
        };

        /// <summary>
        /// <c>WKSMissionInfomation</c> 的「回報結果」與「放棄任務」<b>共用</b>的按法 key。
        /// </summary>
        internal const string WksMissionExitPressKey = "ReportOrAbandon";

        /// <param name="AddonName">視窗名稱（解除封鎖的監聽器與輪詢都以它為準）。</param>
        /// <param name="Address">被按的那個實例的位址，<b>只做等值比較</b>。</param>
        /// <param name="Press">按法；單答終結窗一律為空字串。</param>
        private readonly record struct PressKey(string AddonName, nint Address, string Press);

        /// <param name="Frame">按下時的守衛時鐘讀數（<see cref="GuardTick"/>）。</param>
        /// <param name="EscapeFrames">登記當時呼叫端給的逃生口；<see cref="IsHeld"/> 判「這筆還熱著」用它。</param>
        /// <param name="Listed">登記時在同名清單裡找得到這個位址；只有它成立才准用「從清單消失」解除。</param>
        private readonly record struct PressRecord(long Frame, int EscapeFrames, bool Listed);

        /// <summary>一扇窗的身分（窗名＋位址）。位址只做等值比較。</summary>
        private readonly record struct PressSlot(string AddonName, nint Address);

        /// <summary>目前還開著、而且被按過的窗。每次呼叫都會把已經消失的清掉，正常情況下最多一兩筆。</summary>
        private static readonly Dictionary<PressKey, PressRecord> Pressed = [];

        /// <summary>看過 PreFinalize 的實例：鎖到位址從同名清單消失或同位址重建，期間任何按法都擋，沒有逃生口。</summary>
        private static readonly HashSet<PressSlot> Finalizing = [];

        /// <summary>常駐窗第一次被觀察到「在清單裡但隱藏」時的守衛幀；看得見或重新按下就移除。</summary>
        private static readonly Dictionary<PressSlot, long> HiddenSince = [];

        /// <summary>「這個窗名第一次走隱藏解除」只寫一行 Information。</summary>
        private static readonly HashSet<string> HiddenReleaseReported = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, IAddonLifecycle.AddonEventDelegate> Watchers =
            new(StringComparer.Ordinal);

        /// <summary>守衛自己的時鐘：<c>Framework.Update</c> 跑過的 tick 數。</summary>
        /// <remarks>
        /// 不用繪製幀號當時鐘：UI 隱藏（過場）時它不前進，這裡的逃生口（<see cref="DefaultEscapeFrames"/>／
        /// <see cref="RoutineRePressEscapeFrames"/>）就<b>永遠不會到期</b> —— 方向是安全的
        /// （不會去按關閉中的窗），但整個按窗流程會停擺，Talk 這種翻頁站會停在第一頁。<br/>
        /// <c>Framework.Update</c> 不受 UI 隱藏影響，所以自己數。正常情況下兩者 1:1，
        /// 逃生口的幀數意義不變，<b>不要因為時鐘不同去調 15／60 那兩個值</b>。
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
        /// <c>if (Player.Available)</c> 內，登出／過場載入期間會停住。
        /// </remarks>
        internal static void StartFrameClock() => EnsureFrameClock();

        private static void EnsureFrameClock()
        {
            if (Interlocked.CompareExchange(ref frameClockSubscribed, 1, 0) != 0)
                return;

            Svc.Framework.Update += AdvanceFrameClock;
        }

        /// <summary>只做一件事：把時鐘推前一格。</summary>
        /// <remarks>🔴 <b>不可以在遞增前面加任何條件或 early return</b> —— 時鐘一停，所有逃生口就永遠不到期。</remarks>
        private static void AdvanceFrameClock(IFramework _) => guardTick++;

        /// <summary>
        /// 登記「即將對這扇視窗送出這一個按法」。<b>回 <see langword="false"/> ＝這一幀絕對不能按。</b>
        /// </summary>
        /// <param name="label">記 log 用的呼叫點名稱（<b>不</b>參與判定）。</param>
        public static bool TryBeginPress(string label, string addonName, nint addon, string pressKey = "",
                                         int escapeFrames = DefaultEscapeFrames)
        {
            // 不確定一律不放行：拿不到位址或名稱就無從記帳，放行等於沒有守衛。
            if (addon == 0 || string.IsNullOrEmpty(addonName))
                return false;

            if (SingleAnswerAddons.Contains(addonName))
                pressKey = string.Empty;

            ReleaseVanished();
            EnsureWatching(addonName);

            var key = new PressKey(addonName, addon, pressKey);
            var slot = new PressSlot(addonName, addon);
            if (Finalizing.Contains(slot))
            {
                LogHold(label, key with { Press = pressKey + "←PreFinalize" }, DefaultEscapeFrames);
                return false;
            }

            // 🔴 常駐窗送出前必須可見：與「隱藏夠久才解除」互為反面，兩者一組才是防護。
            if (PersistentAddons.Contains(addonName) && !PersistentAddonVisible(addonName, addon))
            {
                LogPersistentHidden(label, addonName, addon);
                return false;
            }

            var frame = GuardTick;
            var terminating = escapeFrames > RoutineRePressEscapeFrames;

            if (TryFindHotSibling(key, frame, terminating, out var sibling, out var siblingRec))
            {
                LogSiblingHold(label, key, sibling, siblingRec);
                return false;
            }

            if (Pressed.TryGetValue(key, out var pressed))
            {
                var elapsed = frame - pressed.Frame;

                // 這一扇的這個按法已經按過。窗還在＝可能正在關閉中，此時再送就是攔不到的 AVE。
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
                    // 📌 要使用者回報的診斷：「按了卻沒關掉」正是需要被回報的狀況。
                    IceLogging.Info($"{label}：「{addonName}」（實例 0x{addon:X}，按法「{pressKey}」）按下之後 {elapsed} 幀仍未關閉，補按一次。", Handle);
                }
            }

            // 滿了只淘汰已過逃生口的冷紀錄（它們本來就會被放行，刪掉不改判定）；還熱著的一筆都不動，淘汰不到就不送。
            if (!Pressed.ContainsKey(key) && Pressed.Count >= EntryCap && !EvictCold(frame))
            {
                if (EzThrottler.Throttle("AddonPressGuard-Full", 10000))
                    IceLogging.Info($"{label}：視窗按壓紀錄 {Pressed.Count} 筆全都還在逃生口內，這一發不送。", Handle);
                return false;
            }

            Pressed[key] = new PressRecord(frame, escapeFrames, IsListed(addonName, addon));
            HiddenSince.Remove(slot);

            // ─── 按窗診斷（全艦隊統一格式）───
            // 🔴 格式逐字統一，各外掛各自的 AddonPressGuard 才能互相比對：
            //    [按窗診斷] plugin=<外掛名> addon=<addon名> addr=0x<位址16進位大寫> key=<參數鍵>
            // 🔴 只在「這一幀真的要送出按壓」時寫一行；不節流（節流會讓「兩個外掛在同一毫秒按同一個位址」看不見）；
            //    只印位址數值，絕不解參。Verbose 級。
            IceLogging.Verbose($"plugin=ICE addon={addonName} addr=0x{addon:X} key={pressKey}", PressDiagnosticHandle);

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
        /// 判準與 <see cref="TryBeginPress"/> 完全相同，逃生口用登記當時存下來的那個值。<br/>
        /// ⚠️ 回 <see langword="true"/> ＝ 這一幀不要碰。
        /// </remarks>
        public static bool IsHeld(string addonName, nint addon, string pressKey = "")
        {
            // 不確定一律當作擋著。
            if (addon == 0 || string.IsNullOrEmpty(addonName))
                return true;

            if (SingleAnswerAddons.Contains(addonName))
                pressKey = string.Empty;

            ReleaseVanished();

            var key = new PressKey(addonName, addon, pressKey);
            var frame = GuardTick;

            if (Finalizing.Contains(new PressSlot(addonName, addon)))
                return true;

            // 同一扇窗有別的終結按法還熱著＝那扇窗可能正在關閉，連讀都不讀。
            if (TryFindHotSibling(key, frame, false, out var sibling, out var siblingRec))
            {
                LogSiblingHold("讀窗前檢查", key, sibling, siblingRec);
                return true;
            }

            if (!Pressed.TryGetValue(key, out var pressed))
                return false;

            var held = frame - pressed.Frame < pressed.EscapeFrames;
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
            Finalizing.Clear();
            HiddenSince.Clear();
            HiddenReleaseReported.Clear();
        }

        /// <summary>被擋那一幀的診斷：單答終結窗寫 Information（要使用者回報的診斷）、多次互動窗寫 Debug；每扇窗 1 秒節流免得洗版。</summary>
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
        /// 同一扇窗（名稱＋位址）上<b>別的按法</b>是否擋住這一發：終結按法還熱著時任何按法都擋；
        /// 這一發本身是終結按法（<paramref name="terminating"/>）時，任何還熱著的紀錄都擋。
        /// </summary>
        /// <remarks>「終結」＝逃生口長於多次互動那一級，也就是可能讓窗關掉的按法。</remarks>
        private static bool TryFindHotSibling(PressKey key, long frame, bool terminating, out PressKey sibling, out PressRecord siblingRec)
        {
            foreach (var (k, rec) in Pressed)
            {
                if (k.Address != key.Address || k.Press == key.Press || !string.Equals(k.AddonName, key.AddonName, StringComparison.Ordinal))
                    continue;
                if (frame - rec.Frame >= rec.EscapeFrames)
                    continue;
                if (terminating || rec.EscapeFrames > RoutineRePressEscapeFrames)
                {
                    sibling = k;
                    siblingRec = rec;
                    return true;
                }
            }

            sibling = default;
            siblingRec = default;
            return false;
        }

        /// <summary>被同窗別的按法擋下：擋人的是終結按法時寫 Information，只是多次互動那一級的短等待寫 Debug。</summary>
        private static void LogSiblingHold(string label, PressKey key, PressKey sibling, PressRecord siblingRec)
        {
            if (!EzThrottler.Throttle($"AddonPressGuard-Hold-{key.AddonName}", 1000))
                return;

            var message = $"{label}：「{key.AddonName}」（實例 0x{key.Address:X}）的按法「{sibling.Press}」按過之後還沒觀察到它收掉，這一幀不送按法「{key.Press}」。";
            if (siblingRec.EscapeFrames <= RoutineRePressEscapeFrames)
                IceLogging.Debug(message, Handle);
            else
                IceLogging.Info(message, Handle);
        }

        /// <summary>
        /// 每次呼叫的解除路徑：①登記時找得到、現在從同名清單消失 ⇒ 解除（含 PreFinalize 鎖）；
        /// ②常駐窗隱藏滿 <see cref="HiddenReleaseFrames"/> 幀 ⇒ 解除。
        /// </summary>
        /// <remarks>
        /// 「不在 addon 清單裡了」是唯一能確定「上一次按下的那扇已經收乾淨」的證據；同名可能多扇並存，所以掃整串索引。<br/>
        /// 🔴 全程只做位址等值比較，<b>永遠不解參</b>；同名實例掃滿上限＝查詢不成立，一律不解除。
        /// </remarks>
        private static void ReleaseVanished()
        {
            if (Pressed.Count == 0 && Finalizing.Count == 0)
                return;

            // 同名的窗只掃一次；先收集再刪：不能在列舉集合的同時改它。
            liveScanGeneration++;
            goneScratch.Clear();

            foreach (var (key, rec) in Pressed)
            {
                var live = LiveAddresses(key.AddonName);
                if (live != null && rec.Listed && !live.Contains(key.Address))
                    goneScratch.Add(key);
            }

            foreach (var key in goneScratch)
                Pressed.Remove(key);

            unlockedScratch.Clear();
            foreach (var slot in Finalizing)
            {
                var live = LiveAddresses(slot.AddonName);
                if (live != null && !live.Contains(slot.Address))
                    unlockedScratch.Add(slot);
            }

            foreach (var slot in unlockedScratch)
                Finalizing.Remove(slot);

            ReleaseHiddenPersistent();
        }

        /// <summary>某窗名這一輪掃到的位址；只在框架執行緒用。Generation 對不上＝這一輪還沒掃。</summary>
        private sealed class LiveScan
        {
            public readonly HashSet<nint> Addresses = [];
            public long Generation = -1;
            public bool Known;
        }

        // 每次呼叫重用的暫存（同 evictScratch）：表裡有紀錄時這條路每幀都走。
        private static readonly Dictionary<string, LiveScan> liveScratch = new(StringComparer.Ordinal);
        private static long liveScanGeneration;
        private static readonly List<PressKey> goneScratch = [];
        private static readonly List<PressSlot> unlockedScratch = [];
        private static readonly List<PressSlot> slotScratch = [];
        private static readonly List<PressSlot> staleScratch = [];

        /// <returns>同名清單上的全部位址；掃滿 <see cref="MaxAddonIndex"/> 仍沒掃到空格時回 <see langword="null"/>（＝不知道）。</returns>
        private static HashSet<nint>? LiveAddresses(string addonName)
        {
            if (!liveScratch.TryGetValue(addonName, out var scan))
            {
                scan = new LiveScan();
                liveScratch[addonName] = scan;
            }

            if (scan.Generation != liveScanGeneration)
            {
                scan.Generation = liveScanGeneration;
                scan.Addresses.Clear();
                scan.Known = false;
                for (var i = 1; i <= MaxAddonIndex; i++)
                {
                    var addon = (nint)Svc.GameGui.GetAddonByName(addonName, i).Address;
                    if (addon == 0)
                    {
                        scan.Known = true;
                        break;
                    }
                    scan.Addresses.Add(addon);
                }
            }

            return scan.Known ? scan.Addresses : null;
        }

        /// <summary>同名清單裡找得到這個位址；掃滿 <see cref="MaxAddonIndex"/> 仍沒掃到空格＝不知道，回 false。</summary>
        private static bool IsListed(string addonName, nint address)
        {
            var found = false;
            for (var i = 1; i <= MaxAddonIndex; i++)
            {
                var addon = (nint)Svc.GameGui.GetAddonByName(addonName, i).Address;
                if (addon == 0)
                    return found;
                if (addon == address)
                    found = true;
            }

            return false;
        }

        /// <summary>這一幀在同名清單裡對某個位址的觀察。</summary>
        /// <remarks>🔴 零值必須是「不解除、不放行」的答案。</remarks>
        private enum AddonPresence
        {
            Unknown = 0,
            NotListed = 1,
            Visible = 2,
            Hidden = 3,
        }

        /// <remarks>解參考的是 <c>GetAddonByName</c> 這一幀交回來的指標，記下的位址只做等值比較。</remarks>
        private static AddonPresence LookUpVisibility(string addonName, nint address)
        {
            if (string.IsNullOrEmpty(addonName) || address == 0)
                return AddonPresence.Unknown;

            for (var i = 1; i <= MaxAddonIndex; i++)
            {
                var unit = (AtkUnitBase*)Svc.GameGui.GetAddonByName(addonName, i).Address;
                if (unit == null)
                    return AddonPresence.NotListed;
                if ((nint)unit != address)
                    continue;
                return unit->IsVisible ? AddonPresence.Visible : AddonPresence.Hidden;
            }

            return AddonPresence.Unknown;
        }

        /// <summary>找不到或查詢不成立都回 <see langword="false"/>（＝不准送）。</summary>
        private static bool PersistentAddonVisible(string addonName, nint address)
            => LookUpVisibility(addonName, address) == AddonPresence.Visible;

        private static void LogPersistentHidden(string label, string addonName, nint address)
        {
            if (EzThrottler.Throttle($"AddonPressGuard-PersistentHidden-{addonName}", 1000))
                IceLogging.Info($"{label}：「{addonName}」是常駐窗，實例 0x{address:X} 這一幀不可見（或不在同名清單裡），不送。", Handle);
        }

        /// <summary>常駐窗（不會被銷毀、只會隱藏）的解除點：隱藏滿 <see cref="HiddenReleaseFrames"/> 幀就放掉該實例的全部紀錄。</summary>
        private static void ReleaseHiddenPersistent()
        {
            if (Pressed.Count == 0 && HiddenSince.Count == 0)
                return;

            var slots = slotScratch;
            slots.Clear();
            foreach (var key in Pressed.Keys)
            {
                if (!PersistentAddons.Contains(key.AddonName))
                    continue;
                var slot = new PressSlot(key.AddonName, key.Address);
                if (!slots.Contains(slot))
                    slots.Add(slot);
            }

            if (HiddenSince.Count > 0)
            {
                staleScratch.Clear();
                foreach (var slot in HiddenSince.Keys)
                {
                    if (!slots.Contains(slot))
                        staleScratch.Add(slot);
                }

                foreach (var slot in staleScratch)
                    HiddenSince.Remove(slot);
            }

            if (slots.Count == 0)
                return;

            var frame = GuardTick;
            foreach (var slot in slots)
            {
                if (LookUpVisibility(slot.AddonName, slot.Address) != AddonPresence.Hidden)
                {
                    HiddenSince.Remove(slot);
                    continue;
                }

                if (!HiddenSince.TryGetValue(slot, out var since))
                {
                    HiddenSince[slot] = frame;
                    continue;
                }

                if (frame - since < HiddenReleaseFrames)
                    continue;

                HiddenSince.Remove(slot);
                RemoveSlotRecords(slot);
                if (HiddenReleaseReported.Add(slot.AddonName))
                    IceLogging.Info($"「{slot.AddonName}」是常駐視窗（隱藏而不銷毀），按下紀錄改由「隱藏滿 {HiddenReleaseFrames} 幀」解除。這一行每個視窗名每次遊戲只寫一次。", Handle);
            }
        }

        /// <summary>移除某實例（窗名＋位址）底下的全部紀錄。</summary>
        private static void RemoveSlotRecords(PressSlot slot)
        {
            List<PressKey>? gone = null;
            foreach (var key in Pressed.Keys)
            {
                if (key.Address == slot.Address && string.Equals(key.AddonName, slot.AddonName, StringComparison.Ordinal))
                    (gone ??= []).Add(key);
            }

            if (gone == null)
                return;

            foreach (var key in gone)
                Pressed.Remove(key);
        }

        /// <summary>同位址開出新的一扇（PostSetup）：解鎖，並清掉「不是這一幀才登記的」紀錄。</summary>
        /// <remarks>🔴 同幀豁免：監聽器之間的呼叫順序不保證，在 PostSetup 處理常式裡剛按下的紀錄不能被當場清掉。</remarks>
        private static void ReleaseRebuilt(PressSlot slot)
        {
            Finalizing.Remove(slot);
            HiddenSince.Remove(slot);
            if (Pressed.Count == 0)
                return;

            var frame = GuardTick;
            List<PressKey>? gone = null;
            foreach (var (key, rec) in Pressed)
            {
                if (key.Address != slot.Address || !string.Equals(key.AddonName, slot.AddonName, StringComparison.Ordinal))
                    continue;
                if (rec.Frame == frame)
                    continue;
                (gone ??= []).Add(key);
            }

            if (gone == null)
                return;

            foreach (var key in gone)
                Pressed.Remove(key);
        }

        /// <summary>該實例進入銷毀：鎖到它從清單消失或同位址重建。守護過的窗名即使沒按過也鎖。</summary>
        private static void LockFinalizing(PressSlot slot) => Finalizing.Add(slot);

        /// <summary>
        /// 第一次守護某個 addon 名稱時掛上監聽器：PostSetup 清該位址「不是這一幀才登記的」紀錄，PreFinalize 把該位址上鎖。
        /// </summary>
        /// <remarks>
        /// 掛上去之後就不再拆（只在 <see cref="ForceTeardown"/> 拆）：動態掛／拆比較容易留下懸空的監聽器。
        /// 🔴 粒度是位址不是名字：#A 關閉中的同一幀第二扇同名窗 #B 被建立時，不能連坐清掉 #A 的紀錄。
        /// 回呼裡只取 <c>args.Addon.Address</c> 這個位址值，不解參。
        /// </remarks>
        private static void EnsureWatching(string addonName)
        {
            if (Watchers.ContainsKey(addonName))
                return;

            IAddonLifecycle.AddonEventDelegate handler = (eventType, args) =>
            {
                var address = args.Addon.Address;
                if (address == 0)
                    return;
                var slot = new PressSlot(addonName, address);

                if (eventType == AddonEvent.PreFinalize)
                {
                    LockFinalizing(slot);
                    return;
                }

                ReleaseRebuilt(slot);
            };

            Watchers[addonName] = handler;
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, addonName, handler);
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonName, handler);
        }
    }
}
