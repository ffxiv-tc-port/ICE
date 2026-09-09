using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.IPC;

/// <summary>
/// 「宇宙探索任務交件拿到金評」時請 TataruPraise 念一句誇獎。純通知，不影響任何流程。
/// </summary>
/// <remarks>
/// 🔴 <b>刻意不用 ECommons 的 <c>EzIPC</c>。</b>ICE 其餘的 IPC 包裝都帶
/// <c>SafeWrapper.AnyException</c>，那會把「對方沒安裝」「簽名不符」一律吞成 <c>default</c>
/// ——就是 <c>AutoHookIPC</c> 註解裡記過的那個完全靜默的失敗形式。
/// 這裡改用 Dalamud 原生的 <c>GetIpcSubscriber</c> ＋ 明確的 try/catch，
/// 「對方不在」會走 <see cref="IpcNotReadyError"/> 這條看得見的路。<br/><br/>
///
/// 🔴 <b>契約名是逐字常數，不要「順手改成好看一點」。</b>Dalamud 的 CallGate 是純字串比對，
/// 名字錯了不會有任何錯誤訊息，只會永遠拿到「沒有人註冊」——靜默斷線。
/// 三個名字的權威定義在 <c>TataruPraise/TataruPraise/IpcContract.cs</c>。<br/><br/>
///
/// 📌 <b>沒有需要 Dispose 的東西。</b><c>GetIpcSubscriber</c> 拿到的訂閱端不需要退訂，
/// 所以這個類別不會出現在 <c>ICE.Dispose()</c> 裡——也就沒有「Dispose 裡無防護的 IPC 呼叫」那個雷。
/// </remarks>
internal static class TataruPraiseIPC
{
    /// <summary>對方外掛的內部名稱（只用在記錄檔的措辭上，判斷在不在一律靠 IPC 本身）。</summary>
    public const string Name = "TataruPraise";

    /// <summary><c>Func&lt;string, bool&gt;</c>：<b>這一個情境</b>現在出不出得了聲（總開關＋這個情境的開關＋這個情境有已合成的語音）。</summary>
    /// <remarks>📌 刻意<b>不</b>看冷卻：冷卻是「這一次剛好不出聲」，不是「不能出聲」。</remarks>
    public const string IsAvailableForIpc = "TataruPraise.IsAvailableFor";

    /// <summary><c>Func&lt;string, bool&gt;</c>：從指定情境的誇獎池挑一句念。</summary>
    public const string PraiseIpc = "TataruPraise.Praise";

    /// <summary>
    /// 送過去的情境字串。
    /// ⚠️ TataruPraise 內建的四個情境是「副本完成／升等／登入／Gil里程碑」，<b>沒有</b>這一個；
    /// 它的 <c>pool.json</c> 讀檔時不會丟掉不認得的鍵，所以使用者自己加一個「宇宙」分類就會生效。
    /// 池裡沒有這個情境時 TataruPraise 只會寫一行記錄並回 <see langword="false"/>，不會出錯。
    /// </summary>
    public const string CosmicCategory = "宇宙";

    /// <summary>
    /// 「宇宙探索自動化整段收工」的情境字串。
    /// </summary>
    /// <remarks>
    /// ⚠️ 跟 <see cref="CosmicCategory"/> 是<b>兩個不同的鍵</b>：那個是<b>單一任務</b>拿到金評
    ///（一輪農場會響很多次），這個是<b>整段自動化停下來</b>，一輪只響一次。
    /// 📌 這是 TataruPraise 7.20 之後才有的內建情境，既有使用者的池裡會自動補上句子，
    /// 但<b>語音還沒合成</b> ⇒ <c>IsAvailableFor</c> 回 false ⇒ 靜默，不會突然多出聲音。
    /// </remarks>
    public const string StoppedCategory = "宇宙停止";

    /// <summary>
    /// 「自動化卡住了，需要人來看一下」的情境字串（TataruPraise 的既有內建情境）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 只給<b>不是使用者想要的停止</b>用：連續重骰找不到可接任務、目前職業在這個區域
    /// 沒有可跑的任務、任務表對不上的錯誤路徑。達成停止條件而收工走 <see cref="StoppedCategory"/>
    /// ——「跑完了」跟「卡住了」念同一句話等於沒講。
    /// </remarks>
    public const string NeedHelpCategory = "需要幫忙";

    /// <summary>同一個任務在這個時間窗內只誇一次（毫秒）。</summary>
    /// <remarks>
    /// 唯一的呼叫點（<c>MissionTimer</c> 的交件統計）本來就是每次交件只跑一次，
    /// 這個門閂是防止哪天有人把呼叫搬到輪詢路徑上——那種錯誤的表現形式是「一直念」，
    /// 而不是報錯。同一個任務連續刷金評的間隔遠大於這個窗，正常農場不會被誤擋。
    /// </remarks>
    private const long DuplicateWindowMs = 30_000;

    private static ICallGateSubscriber<string, bool>? isAvailableForSubscriber;
    private static ICallGateSubscriber<string, bool>? praiseSubscriber;

    /// <summary>上一次真的送出誇獎的任務 ID 與當下的 tick。</summary>
    private static uint lastPraisedMission;
    private static long lastPraisedTick = long.MinValue;

    /// <summary>「對方不在」只寫一次記錄，不要每次交件都刷一行。</summary>
    private static bool loggedNotInstalled;

    /// <summary>
    /// 交件結果已判定為金評時呼叫一次。
    /// </summary>
    /// <param name="missionId">剛交件的任務 ID，只用來做重複呼叫的門閂。</param>
    /// <remarks>
    /// 🔴 <b>必須在主執行緒（framework update）上呼叫。</b>目前唯一的呼叫點是
    /// <c>MissionTimer.UpdateMissionStats</c>，它掛在 NeoTaskManager 的任務上，本來就跑在主執行緒。
    /// </remarks>
    internal static void PraiseGoldTurnin(uint missionId)
    {
        if (!C.PraiseOnGoldTurnin)
            return;

        var now = Environment.TickCount64;
        if (missionId != 0 && missionId == lastPraisedMission && now - lastPraisedTick < DuplicateWindowMs)
        {
            IceLogging.Debug($"任務 [{missionId}] 的金評誇獎剛送過（{now - lastPraisedTick} ms 前），這次跳過。", "[TataruPraise IPC]");
            return;
        }

        try
        {
            isAvailableForSubscriber ??= Svc.PluginInterface.GetIpcSubscriber<string, bool>(IsAvailableForIpc);

            // 先問「這一個情境現在出得了聲嗎」。對方沒安裝／沒載入的話這一行就會擲 IpcNotReadyError，
            // 下面的 Praise 根本不會被呼叫到。
            // 🔴 不要退回去問 IsAvailable：那個問的是「整池」，於是「別的情境有句子、
            //    宇宙情境一句都沒有」時它照樣回 true，這道閘門等於白做。
            if (!isAvailableForSubscriber.InvokeFunc(CosmicCategory))
            {
                IceLogging.Debug($"{Name} 現在念不了「{CosmicCategory}」（總開關關著、這個情境被關掉、或它一句已合成的都沒有），這次不誇。", "[TataruPraise IPC]");
                return;
            }

            praiseSubscriber ??= Svc.PluginInterface.GetIpcSubscriber<string, bool>(PraiseIpc);
            var queued = praiseSubscriber.InvokeFunc(CosmicCategory);

            lastPraisedMission = missionId;
            lastPraisedTick = now;
            loggedNotInstalled = false;

            // 📌 使用者跑 LogLevel 1，盲區只有 Verbose,Debug 收得到但單檔數十萬行會淹沒 —— 這是「誇獎到底有沒有送出去」唯一的線索。
            // ⚠️ 回傳 false 不是錯誤：可能是冷卻還沒過，也可能是「宇宙」這個情境在池裡一句都沒有。
            IceLogging.Info(
                queued
                    ? $"任務 [{missionId}] 交件金評，已請 {Name} 念一句「{CosmicCategory}」情境的誇獎。"
                    : $"任務 [{missionId}] 交件金評，{Name} 收到了但沒有播出（冷卻未過，或誇獎池裡沒有「{CosmicCategory}」這個情境的句子）。",
                "[TataruPraise IPC]");
        }
        catch (IpcNotReadyError)
        {
            // 對方沒安裝或還沒載入。這是預期內的狀態，不是錯誤。
            if (!loggedNotInstalled)
            {
                loggedNotInstalled = true;
                IceLogging.Info(
                    $"想在交件金評時請 {Name} 誇獎，但它沒有安裝或尚未載入（IPC「{IsAvailableForIpc}」沒有人註冊）。" +
                    "這個功能會維持靜默，其餘流程完全不受影響。",
                    "[TataruPraise IPC]");
            }
        }
        catch (Exception e)
        {
            // 對方版本不合、簽名對不上之類。同樣不要影響交件流程。
            IceLogging.Info($"呼叫 {Name} 的誇獎 IPC 失敗：{e.Message}", "[TataruPraise IPC]");
        }
    }

    /// <summary>這一輪自動化<b>還沒</b>念過「停下來了」。</summary>
    /// <remarks>
    /// 🔴 <b>這是防「連續念好幾次」唯一的機制，不要拿掉。</b>ICE 的停止路徑不只一條：
    /// <c>Task_CheckState</c> 有七個停止條件、<c>Task_FindMission</c> 兩條，
    /// 而「跑完這輪就停」在四個檔案各有一份，其中好幾條會在同一次停止裡先後成立。
    /// <para>
    /// 這個旗標由 <see cref="ArmStopNotice"/> 在<b>排程還在跑的每一幀</b>重新舉起，
    /// 送出一次就放下 ⇒ <b>一輪自動化最多念一句</b>，要等下一次啟動才會再念。
    /// </para>
    /// <para>
    /// 🔴 刻意<b>不</b>用 <c>EzThrottler</c>：那是整個外掛共用的靜態字典而且零同步，
    /// 而且它的 key 全域持久、首次必放行——兩個性質都跟這裡要的相反。
    /// 這個 bool 只被 framework 執行緒碰（<c>ICE.Tick</c> 與 NeoTaskManager 的任務都是），
    /// 所以不需要鎖。
    /// </para>
    /// </remarks>
    private static bool stopNoticeArmed;

    /// <summary>
    /// 排程還在跑（<c>State != Idle</c>）時<b>每一幀</b>呼叫一次，把「停下來要念一句」重新舉起。
    /// </summary>
    /// <remarks>🔴 刻意只做一個 bool 指派、不加任何條件——這一支每幀都會跑。</remarks>
    internal static void ArmStopNotice() => stopNoticeArmed = true;

    /// <summary>自動化<b>達成停止條件收工</b>了，請塔塔露念一句。</summary>
    /// <param name="reason">寫進記錄檔的原因，不會唸出來。</param>
    /// <remarks>🔴 必須在 framework 執行緒上呼叫（所有呼叫點都是排程器或 NeoTaskManager 的任務）。</remarks>
    internal static void NotifyStopped(string reason)
        => SendStopNotice(StoppedCategory, C.PraiseOnAutomationStopped, reason);

    /// <summary>自動化<b>卡住被迫停下</b>了，請塔塔露念一句「需要幫忙」。</summary>
    /// <param name="reason">寫進記錄檔的原因，不會唸出來。</param>
    /// <remarks>🔴 必須在 framework 執行緒上呼叫（所有呼叫點都是排程器或 NeoTaskManager 的任務）。</remarks>
    internal static void NotifyNeedsHelp(string reason)
        => SendStopNotice(NeedHelpCategory, C.PraiseOnAutomationStuck, reason);

    private static void SendStopNotice(string category, bool enabled, string reason)
    {
        if (!enabled)
            return;

        if (!stopNoticeArmed)
        {
            IceLogging.Debug($"這一輪停下來的通知已經送過了，「{reason}」這次不重複。", "[TataruPraise IPC]");
            return;
        }

        // 🔴 先放下旗標再送：下面任何一步擲例外都不該讓後面每一幀重試。
        stopNoticeArmed = false;

        try
        {
            isAvailableForSubscriber ??= Svc.PluginInterface.GetIpcSubscriber<string, bool>(IsAvailableForIpc);

            // 🔴 不要退回去問 IsAvailable：那個問的是「整池」，於是「別的情境有句子、
            //    這個情境一句都沒有」時它照樣回 true，這道閘門等於白做。
            if (!isAvailableForSubscriber.InvokeFunc(category))
            {
                IceLogging.Debug($"{Name} 現在念不了「{category}」（總開關關著、這個情境被關掉、或它一句已合成的都沒有），這次不念。", "[TataruPraise IPC]");
                return;
            }

            praiseSubscriber ??= Svc.PluginInterface.GetIpcSubscriber<string, bool>(PraiseIpc);
            var queued = praiseSubscriber.InvokeFunc(category);
            loggedNotInstalled = false;

            // 📌 使用者跑 LogLevel 1，盲區只有 Verbose —— 這是「到底有沒有送出去」唯一的線索。
            IceLogging.Info(
                queued
                    ? $"自動化停下來了（{reason}），已請 {Name} 念一句「{category}」情境的話。"
                    : $"自動化停下來了（{reason}），{Name} 收到了但沒有播出（冷卻未過，或誇獎池裡沒有「{category}」這個情境的句子）。",
                "[TataruPraise IPC]");
        }
        catch (IpcNotReadyError)
        {
            // 對方沒安裝或還沒載入。這是預期內的狀態，不是錯誤。
            if (!loggedNotInstalled)
            {
                loggedNotInstalled = true;
                IceLogging.Info(
                    $"想在自動化停下來時請 {Name} 念一句，但它沒有安裝或尚未載入（IPC「{IsAvailableForIpc}」沒有人註冊）。" +
                    "這個功能會維持靜默，其餘流程完全不受影響。",
                    "[TataruPraise IPC]");
            }
        }
        catch (Exception e)
        {
            // ⚠️ 提供端自己擲的例外會被 CallGate 包成 TargetInvocationException，
            //    catch (IpcNotReadyError) 攔不到——所以這一層一定要在。絕不要讓它往上冒。
            IceLogging.Info($"呼叫 {Name} 的通知 IPC 失敗：{e.Message}", "[TataruPraise IPC]");
        }
    }
}
