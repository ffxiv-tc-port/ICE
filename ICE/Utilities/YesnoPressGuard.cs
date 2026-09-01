using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Utilities
{
    /// <summary>
    /// 「同一扇 <c>SelectYesno</c> 在它消失之前只按一次」的共用守衛。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>要防的東西</b>：<c>SelectYesno</c> 被按下之後有「正在關閉中」的幾幀，
    /// <c>GetAddonByName</c> 仍然拿得到實例，<c>IsVisible</c> 與
    /// <c>UldManager.LoadedState == Loaded</c> 也都還成立（＝<c>IsAddonReady</c> 三關全過），
    /// 此時再送一次 callback 就是原生 AccessViolation（C0000005）。
    /// AVE 在 .NET Core 是 corrupted-state exception，<c>try</c>/<c>catch</c> 與任何例外隔離
    /// 都攔不住 —— 唯一的防護是「不要按第二次」，不是「按了再接住」。<br/>
    /// <br/>
    /// 🔴 <b>為什麼既有的節流不是防護</b>：<c>FrameThrottler</c>／<c>EzThrottler</c> 記的是
    /// 「這個 <b>key</b> 上一次放行是哪一幀／哪個時刻」，不是「這扇 <b>窗</b> 已經按過」。
    /// 兩者的差別在 ICE 是會踩到的，不是理論問題：<c>Task_Repair</c> 的三個按窗點各自用
    /// <b>不同的 key</b>（<c>"SelectYesnoThrottle"</c>／<c>"Saying yes to the gil"</c>／
    /// <c>"Closing surprise repair window"</c>），而 <c>FrameThrottler</c> 對<b>沒見過的 key
    /// 第一次呼叫一律放行</b>（見 <c>FrameThrottler{T}.Throttle</c>：<c>!ContainsKey</c> ⇒ <c>return true</c>）。
    /// 所以「<c>SelfRepair</c> 按下確定 → 同一扇窗還在關閉中 → 下一個步驟 <c>CloseRepair</c>
    /// 用另一把 key 立刻再送一次 callback」這條路徑，<b>兩個節流誰都擋不住</b>。
    /// 這也是為什麼本守衛刻意用<b>視窗位址</b>當索引鍵而不是用呼叫點的標籤 ——
    /// 跨呼叫點的重按正是節流的盲區。<br/>
    /// <br/>
    /// 🔴 <b>全程只做位址等值比較，永遠不解參</b>。這裡存的 <c>nint</c> 只被拿來和「現在還在
    /// addon 清單裡的位址」比大小，從來不會被當成指標讀取，所以即使那塊記憶體已經被回收
    /// 也不會有事（這正是「絕不跨幀保存原生指標」那條規則允許的用法：存的是識別碼，不是指標）。<br/>
    /// <br/>
    /// 📌 <b>正常路徑的行為沒有改變</b>：第一次看到某一扇窗一律當場放行，
    /// 被擋下來的只有「同一扇窗還開著時的第二次按壓」——也就是崩潰本身。
    /// </remarks>
    internal static class YesnoPressGuard
    {
        private const string Handle = "[Yesno Press Guard]";

        /// <summary>
        /// 已經按過、那扇窗卻還沒消失時，最多再等這麼多幀才允許補按一次。
        /// </summary>
        /// <remarks>
        /// 🔑 這不是節流 —— 真正的防護是「同一扇窗只按一次」，這個值只是防死鎖的逃生口：
        /// 永久封鎖會讓呼叫端的步驟一路卡到逾時，而 ICE 這幾步掛的是 <c>Utils.TaskConfig</c>
        /// （30 分鐘、逾時不中止），等於三十分鐘的無聲空轉。
        /// 取 60 幀（約 0.5~1 秒）是為了遠遠大於「關閉中的那幾幀」，
        /// 補按永遠不會落在危險窗口內。
        /// </remarks>
        private const int RePressEscapeFrames = 60;

        /// <summary>視窗位址 → 上一次按下它的幀數。</summary>
        /// <remarks>
        /// 只會裝著「目前還開著、而且被按過」的窗，每次呼叫都會把已經消失的清掉，
        /// 所以正常情況下最多一兩筆。<see cref="EntryCap"/> 只是最後一道保險。
        /// </remarks>
        private static readonly Dictionary<nint, long> PressedWindows = [];

        /// <summary>
        /// 萬一清理判準在某個版本失效，也不要讓這張表無限長大。
        /// </summary>
        private const int EntryCap = 32;

        private static long CurrentFrame => (long)Svc.PluginInterface.UiBuilder.FrameCount;

        /// <summary>
        /// 這一次可不可以按下這扇 <c>SelectYesno</c>。
        /// </summary>
        /// <param name="label">記 log 用的呼叫點名稱（<b>不</b>參與判定）。</param>
        /// <param name="addon">那扇窗的位址（<c>AddonMaster.Base</c> 或 <c>AtkUnitBase*</c> 轉 <c>nint</c>）。</param>
        /// <returns>
        /// <c>true</c>＝可以按（本方法已經把這一次記下來，呼叫端<b>必須</b>真的按下去）。<br/>
        /// <c>false</c>＝這扇窗剛才已經按過而且還沒消失，這一次不要按。
        /// </returns>
        /// <remarks>
        /// ⚠️ 一定要放在條件式的<b>最後</b>一項（其他閘門都通過、確定要按了才呼叫）——
        /// 它有副作用（記下這一次按壓）。放在前面會把「其實沒按」記成「按過了」，
        /// 讓真正該按的那一次被擋掉。
        /// </remarks>
        public static bool MayPress(string label, nint addon)
        {
            // 位址是 0 就沒有可以識別的窗，交給呼叫端原本的守衛處理（它們都先做過 IsAddonReady）。
            if (addon == 0)
                return true;

            PruneClosedWindows();

            var frame = CurrentFrame;
            if (PressedWindows.TryGetValue(addon, out var pressedAt))
            {
                var elapsed = frame - pressedAt;

                // 這一扇已經按過。窗還在＝可能正在關閉中，此時再送 callback 就是上面說的 AVE。
                if (elapsed < RePressEscapeFrames)
                    return false;

                // 逃生口：等了遠超過關閉所需的時間，窗仍在。視為那次按壓沒生效
                // （或這是另一扇窗重用了同一塊記憶體），放行補按一次。
                // 📌 寫 Information：使用者跑 LogLevel 2，Debug 收不到，
                //    而「按了卻沒關掉」正是需要被回報的狀況。
                IceLogging.Info($"{label}：按下之後 {elapsed} 幀仍未關閉，補按一次。", Handle);
            }

            if (PressedWindows.Count >= EntryCap)
            {
                IceLogging.Info($"{label}：待清理的確認框累積到 {PressedWindows.Count} 筆，整批清掉重來。", Handle);
                PressedWindows.Clear();
            }

            PressedWindows[addon] = frame;
            return true;
        }

        /// <inheritdoc cref="MayPress(string, nint)"/>
        /// <remarks>
        /// 給<b>不在 <c>unsafe</c> 區塊裡</b>的呼叫點用的多載（例如
        /// <c>Task_RelicTurnin.TurninRelic</c>）：指標轉型在本方法內部完成，
        /// 呼叫端不必為了取一個位址就把整個方法標成 <c>unsafe</c>。
        /// </remarks>
        public static unsafe bool MayPress(string label, SelectYesno master)
            => MayPress(label, (nint)master.Base);

        /// <summary>
        /// 把已經從 <c>SelectYesno</c> 清單消失的窗解除封鎖。
        /// </summary>
        /// <remarks>
        /// 「不在 addon 清單裡了」是唯一能確定「上一次按下的那扇已經收乾淨」的證據。<br/>
        /// ⚠️ 判準刻意<b>不</b>用「文字還對不對」或「<c>IsVisible</c>」：窗在拆除途中
        /// 可能有幾幀讀不到提示文字、旗標也還沒翻，拿那些當「窗不見了」會在最危險的
        /// 那幾幀把封鎖解除掉。<br/>
        /// 掃整串索引而不是只看第 1 個，是因為同時可能開著多扇 <c>SelectYesno</c>，
        /// 被記下的那扇不一定在第 1 格；掃到第一個空的就停。<br/>
        /// 🔴 全程只做位址等值比較，<b>永遠不解參</b>。
        /// </remarks>
        private static void PruneClosedWindows()
        {
            if (PressedWindows.Count == 0)
                return;

            var live = new HashSet<nint>();
            for (var i = 1; i < 100; i++)
            {
                var addon = (nint)Svc.GameGui.GetAddonByName("SelectYesno", i).Address;
                if (addon == 0)
                    break;
                live.Add(addon);
            }

            if (live.Count == 0)
            {
                PressedWindows.Clear();
                return;
            }

            // 先收集再刪：不能在列舉 Dictionary 的同時改它。
            List<nint>? gone = null;
            foreach (var key in PressedWindows.Keys)
            {
                if (!live.Contains(key))
                    (gone ??= []).Add(key);
            }

            if (gone == null)
                return;

            foreach (var key in gone)
                PressedWindows.Remove(key);
        }
    }
}
