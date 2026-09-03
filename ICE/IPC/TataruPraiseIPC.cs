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

    /// <summary><c>Func&lt;bool&gt;</c>：現在有沒有辦法出聲（總開關開著而且池裡有已合成的句子）。</summary>
    public const string IsAvailableIpc = "TataruPraise.IsAvailable";

    /// <summary><c>Func&lt;string, bool&gt;</c>：從指定情境的誇獎池挑一句念。</summary>
    public const string PraiseIpc = "TataruPraise.Praise";

    /// <summary>
    /// 送過去的情境字串。
    /// ⚠️ TataruPraise 內建的四個情境是「副本完成／升等／登入／Gil里程碑」，<b>沒有</b>這一個；
    /// 它的 <c>pool.json</c> 讀檔時不會丟掉不認得的鍵，所以使用者自己加一個「宇宙」分類就會生效。
    /// 池裡沒有這個情境時 TataruPraise 只會寫一行記錄並回 <see langword="false"/>，不會出錯。
    /// </summary>
    public const string CosmicCategory = "宇宙";

    /// <summary>同一個任務在這個時間窗內只誇一次（毫秒）。</summary>
    /// <remarks>
    /// 唯一的呼叫點（<c>MissionTimer</c> 的交件統計）本來就是每次交件只跑一次，
    /// 這個門閂是防止哪天有人把呼叫搬到輪詢路徑上——那種錯誤的表現形式是「一直念」，
    /// 而不是報錯。同一個任務連續刷金評的間隔遠大於這個窗，正常農場不會被誤擋。
    /// </remarks>
    private const long DuplicateWindowMs = 30_000;

    private static ICallGateSubscriber<bool>? isAvailableSubscriber;
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
            isAvailableSubscriber ??= Svc.PluginInterface.GetIpcSubscriber<bool>(IsAvailableIpc);

            // 先問「現在出得了聲嗎」。對方沒安裝／沒載入的話這一行就會擲 IpcNotReadyError，
            // 下面的 Praise 根本不會被呼叫到。
            if (!isAvailableSubscriber.InvokeFunc())
            {
                IceLogging.Debug($"{Name} 目前不方便出聲（總開關關著或誇獎池沒有已合成的句子），這次不誇。", "[TataruPraise IPC]");
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
                    $"想在交件金評時請 {Name} 誇獎，但它沒有安裝或尚未載入（IPC「{IsAvailableIpc}」沒有人註冊）。" +
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
}
