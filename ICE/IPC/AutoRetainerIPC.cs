using ECommons.EzIpcManager;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.IPC;

#nullable disable

/// <summary>
/// 對 AutoRetainer 的<b>具名壓制租約</b>（<c>AutoRetainer.AcquireSuppression</c>／<c>ReleaseSuppression</c>）。
/// </summary>
/// <remarks>
/// 🔴 <b>要解決的問題</b>：AutoRetainer 的 MultiMode 判定 <c>!IsOccupied()</c> 成立就會去跑僱員／換角色。
/// ICE 在兩個任務之間那幾秒正好符合這個條件 —— AutoRetainer 於是在宇宙任務跑到一半時把角色登出，
/// ICE 的狀態機停在半路。<br/>
/// <br/>
/// 🔴 <b>為什麼不用舊的 <c>AutoRetainer.SetSuppressed</c></b>：那是一個<b>無主的單一布林</b>，
/// Artisan（僱員補貨時會壓制）與 GatherBuddyReborn 也在用同一個旗標，誰先結束誰就把別人的壓制一起解除。
/// 租約端點是有名字、可計數的：全部租用者都還完，壓制才真的解除。<br/>
/// <br/>
/// 🔴 <b>提供端缺席時 fail-safe</b>：AutoRetainer 沒安裝／沒載入／IPC 呼叫失敗時，一律<b>當作沒有壓制、照現況跑</b>，
/// 絕不卡住 ICE 自己的流程。續租的週期同時也是「AutoRetainer 比 ICE 晚載入」時的重試機會。<br/>
/// <br/>
/// 🔴 <b>租約會逾時</b>（提供端目前是 5 分鐘），所以要週期性續租。<see cref="Sync"/> 每幀被呼叫一次，
/// 由它自己算時間，不用 <c>EzThrottler</c>（那個的 key 是全域且首次必放行，不適合當租約時鐘）。<br/>
/// <br/>
/// 📌 <b>這不是自動接手鏈</b>：租約只叫 AutoRetainer <b>不要動</b>，不觸發任何新的自動化。
/// </remarks>
public class AutoRetainerIPC
{
    public const string Name = "AutoRetainer";

    /// <remarks>
    /// ⚠️ <c>SafeWrapper.AnyException</c> 會把呼叫失敗吞掉並回傳 <c>default</c>（也就是 <c>false</c>）——
    /// 這裡剛好就是我們要的 fail-safe 語意：「沒拿到租約」。
    /// </remarks>
    public AutoRetainerIPC() => EzIPC.Init(this, Name, SafeWrapper.AnyException);

    public bool Installed => Utils.HasPlugin(Name);

    [EzIPC] public readonly Func<string, bool> AcquireSuppression;
    [EzIPC] public readonly Func<string, bool> ReleaseSuppression;

    /// <summary>續租間隔。提供端的租約壽命是 5 分鐘，這裡留 10 倍餘裕。</summary>
    private const long RenewIntervalMs = 30_000;

    /// <summary>取不到租約時的重試間隔（AutoRetainer 還沒載入完、或那一次呼叫剛好失敗）。</summary>
    private const long RetryIntervalMs = 5_000;

    /// <summary>租用者識別字串。用 ICE 自己的 InternalName，不寫死字面值。</summary>
    private static string Owner => Svc.PluginInterface.InternalName;

    private bool holding;
    private bool loggedUnavailable;
    private long nextAttemptAt;

    /// <summary>ICE 現在有沒有壓制著 AutoRetainer（只做顯示用，不是判定依據）。</summary>
    public bool Holding => holding;

    /// <summary>
    /// 把租約狀態對齊「ICE 的自動化現在該不該壓著 AutoRetainer」。<b>每幀呼叫一次，冪等。</b>
    /// </summary>
    /// <param name="shouldHold">ICE 的排程器正在跑（<c>SchedulerMain.State != Idle</c>）。</param>
    public void Sync(bool shouldHold)
    {
        if (!shouldHold)
        {
            ReleaseNow("ICE 的自動化已停止");
            return;
        }

        if (!Installed)
        {
            // AutoRetainer 不在（或被卸載了）。它的租約表跟著它一起消失，所以這裡只要把自己的狀態歸零。
            if (holding)
            {
                holding = false;
                IceLogging.Info("AutoRetainer 已經不在了，ICE 這邊的壓制租約狀態一併歸零（AutoRetainer 卸載時租約表本來就跟著消失）。", Handle);
            }

            nextAttemptAt = 0;
            return;
        }

        var now = Environment.TickCount64;
        if (now < nextAttemptAt)
            return;

        var ok = AcquireSuppression?.Invoke(Owner) ?? false;
        if (ok)
        {
            if (!holding)
            {
                IceLogging.Info($"已請 AutoRetainer 在 ICE 跑完之前不要動（具名租約，租用者「{Owner}」）—— 避免兩個任務之間那幾秒被它拿去跑僱員或換角色。", Handle);
            }

            holding = true;
            loggedUnavailable = false;
            nextAttemptAt = now + RenewIntervalMs;
            return;
        }

        // 拿不到租約：可能是 AutoRetainer 還沒把 IPC 註冊好，也可能是舊版沒有這個端點。
        // 🔴 這裡<b>不</b>卡住 ICE 的流程，照現況跑就好；只是 AutoRetainer 可能會來搶。
        if (holding || !loggedUnavailable)
        {
            IceLogging.Info($"向 AutoRetainer 取得壓制租約失敗（端點 AutoRetainer.AcquireSuppression，租用者「{Owner}」）。ICE 照常繼續跑；若 AutoRetainer 版本太舊沒有這個端點，它仍可能在任務之間把角色拿去跑僱員。{RetryIntervalMs / 1000} 秒後重試。", Handle);
            loggedUnavailable = true;
        }

        holding = false;
        nextAttemptAt = now + RetryIntervalMs;
    }

    /// <summary>把租約還回去（沒持有就什麼都不做）。外掛卸載與自動化停止都會走這裡。</summary>
    public void ReleaseNow(string reason)
    {
        nextAttemptAt = 0;
        loggedUnavailable = false;
        if (!holding)
            return;

        holding = false;
        if (!Installed)
            return;

        ReleaseSuppression?.Invoke(Owner);
        IceLogging.Info($"已歸還 AutoRetainer 的壓制租約（{reason}）。", Handle);
    }

    private const string Handle = "[AutoRetainer 壓制]";
}
