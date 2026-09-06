using Dalamud.Plugin.Ipc.Exceptions;
using ECommons.EzIpcManager;
using ICE.Utilities.Cosmic_Helper;
using SharpDX.Direct3D11;
using System.Collections.Generic;
using System.Threading.Tasks;

#nullable disable
namespace ICE.IPC;

public class NavmeshIPC
{
    public const string Name = "vnavmesh";
    public const string Repo = "https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json";
    public NavmeshIPC() => EzIPC.Init(this, Name);
    public bool Installed => Utils.HasPlugin(Name);

    [EzIPC("Nav.%m")] public readonly Func<bool> IsReady;
    [EzIPC("Nav.%m")] public readonly Func<float> BuildProgress;
    [EzIPC("Nav.%m")] public readonly Func<bool> Reload;
    [EzIPC("Nav.%m")] public readonly Func<bool> Rebuild;
    [EzIPC("Nav.%m")] public readonly Func<Vector3, Vector3, bool, Task<List<Vector3>>> Pathfind;

    [EzIPC("SimpleMove.%m")] public readonly Func<Vector3, bool, bool> PathfindAndMoveTo;
    [EzIPC("SimpleMove.%m")] public readonly Func<bool> PathfindInProgress;

    [EzIPC("Path.%m")] public readonly Action<List<Vector3>, bool> MoveTo;
    [EzIPC("Path.%m")] public readonly Action Stop;
    [EzIPC("Path.%m")] public readonly Action<bool> SetAlignCamera;
    [EzIPC("Path.%m")] public readonly Func<bool> IsRunning;
    [EzIPC("Path.%m")] public readonly Action<float> SetTolerance;

    [EzIPC("Query.Mesh.%m")] public readonly Func<Vector3, float, float, Vector3?> NearestPoint;
    [EzIPC("Query.Mesh.%m")] public readonly Func<Vector3, bool, float, Vector3?> PointOnFloor;

    // ── 路徑容許值租約（vnavmesh v7.20.0.37 起）──────────────────────────────────
    // 🔴 舊的 Path.SetTolerance 是對 vnavmesh 的**全域**欄位 FollowPath.Tolerance 單向寫入：
    //    誰寫進去就一直停在那裡，沒有任何人會還原。ICE 寫的 0.25 剛好等於 vnavmesh 的預設值，
    //    ⚠️ 但這個寫入**不是**多餘的——AutoDuty 會把同一個全域值改成別的數字
    //    （AutoDuty/Helpers/MovementHelper.cs 的 Path_SetTolerance(tollerance)），
    //    所以「ICE 開路前重設成 0.25」是有作用的，不能直接拿掉。
    // 🔑 租約是**惰性**的：Acquire 拿到憑證本身不改變任何行為，要接著呼叫 SetLeasedTolerance
    //    才生效；放約或逾時（提供端硬性上限 5 分鐘）自動還原成使用者的值。
    // 🔴 **雙軌**：這幾支端點在舊版 vnavmesh 上不存在，呼叫會擲 IpcNotReadyError
    //    （NavmeshIPC 的 EzIPC.Init 沒有帶 SafeWrapper，例外會直接傳到呼叫端）。
    //    擲了就閂住並永久退回既有的 SetTolerance 路徑，行為與這次改動前逐字相同。
    // 📌 回傳型別必須與提供端（vnavmesh/IPCProvider.cs）逐字相同：Guid 與 bool 都是
    //    **不可為 null 的值型別**。宣告成可空或別的形狀時 CallGateChannel 會走 JSON
    //    來回轉換而**不報錯**，失敗形式是靜默拿到垃圾值。
    [EzIPC("Path.%m")] public readonly Func<string, int, Guid> AcquireSuppressionFor;
    [EzIPC("Path.%m")] public readonly Func<Guid, bool> RenewSuppression;
    [EzIPC("Path.%m")] public readonly Func<Guid, bool> ReleaseSuppression;
    [EzIPC("Path.%m")] public readonly Func<Guid, float, bool> SetLeasedTolerance;

    /// <summary>要求的租期。提供端的硬性上限就是 5 分鐘，這裡直接要滿。</summary>
    /// <remarks>🔑 要滿的理由是<b>續約只當保險</b>：萬一續約整條路壞掉，仍然有滿額的緩衝時間。</remarks>
    private const int ToleranceLeaseMilliseconds = 300_000;

    /// <summary>續約間隔（提供端的建議值＝租期的十分之一）。</summary>
    /// <remarks>
    /// 🔴 <b>不能接近租期</b>：提供端 Renew 的第一件事是掃除已到期的租約，
    /// 間隔一旦接近租期，第一次心跳送到時那把已經被掃掉，續約<b>必定</b>回 false。
    /// </remarks>
    private const long ToleranceRenewIntervalMs = 30_000;

    /// <summary>取不到租約時的重試間隔（vnavmesh 還沒把 IPC 註冊好，或它的租約已達上限）。</summary>
    private const long ToleranceRetryIntervalMs = 5_000;

    /// <summary>判定「這版 vnavmesh 沒有租約端點」之後，隔多久再探一次。</summary>
    /// <remarks>
    /// 🔴 這個重試存在的理由：<see cref="IpcNotReadyError"/> <b>分不出</b>「舊版根本沒有這支端點」
    /// 與「vnavmesh 正在重新載入，端點剛好被拆掉了」。後者是暫時的，永久閂住會讓使用者
    /// 更新或重載 vnavmesh 之後<b>整個 session 都用不到租約</b>，而且完全沒有徵兆。
    /// 舊版的代價只是每分鐘多擲一次例外，而說明訊息只會寫一次。
    /// </remarks>
    private const long ToleranceUnsupportedRetryIntervalMs = 60_000;

    private const string ToleranceHandle = "[vnavmesh 容許值租約]";

    /// <summary>租用者識別字串。用 ICE 自己的 InternalName，不寫死字面值。</summary>
    private static string Owner => Svc.PluginInterface.InternalName;

    /// <summary>
    /// 保護底下那組租約狀態。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>鎖內不呼叫 ImGui、不做檔案 I/O、不寫 log</b>：訊息一律先組成字串，出了鎖才寫。
    /// 🔴 <b>絕不用 <c>EzThrottler</c> 當租約時鐘</b>：它是整個外掛共用的靜態 Dictionary 且零同步，
    /// 而且首次必放行、key 全域持久。這裡自己記下一次該續約的時刻。
    /// 📌 目前所有呼叫端都在 Framework 執行緒（<c>P.TaskManager</c> 的任務與 <c>ICE.Tick</c>），
    /// 上鎖是為了讓「哪天有人從別的執行緒進來」不會把憑證與到期時間撕裂成不一致的組合。
    /// </remarks>
    private readonly object toleranceGate = new();

    /// <summary>目前持有的容許值租約憑證；<see cref="Guid.Empty"/>＝沒有。</summary>
    private Guid toleranceLease;

    /// <summary>這把租約目前押著的值。<see cref="float.NaN"/>＝還沒押過（NaN 不等於任何值，所以下一次一定會送）。</summary>
    private float leasedTolerance = float.NaN;

    /// <summary>下一次可以做 IPC（續約或重新取得）的時刻，<see cref="Environment.TickCount64"/> 座標系。</summary>
    private long toleranceNextAttemptAt;

    /// <summary>這版 vnavmesh 沒有租約端點（擲過 <see cref="IpcNotReadyError"/>）⇒ 永久走舊路徑。</summary>
    private bool toleranceLeaseUnsupported;

    private bool loggedToleranceUnsupported;
    private bool loggedToleranceRefused;

    /// <summary>ICE 現在有沒有押著 vnavmesh 的路徑容許值（只做顯示用，不是判定依據）。</summary>
    public bool HoldingToleranceLease
    {
        get
        {
            lock (toleranceGate)
                return toleranceLease != Guid.Empty;
        }
    }

    /// <summary>
    /// 請 vnavmesh 在 ICE 導航期間使用指定的路徑容許值。<b>冪等</b>，每次開新路徑時呼叫一次。
    /// </summary>
    /// <remarks>
    /// 🔴 拿不到租約時<b>退回既有的 <c>Path.SetTolerance</c> 直接寫全域值</b>——與改動前的行為
    /// 逐字相同，絕不因為租約拿不到就不寫（那會讓 AutoDuty 留下的容許值繼續影響 ICE 的導航）。
    /// </remarks>
    public void ApplyTolerance(float tolerance)
    {
        string message;
        lock (toleranceGate)
            message = ApplyToleranceLocked(tolerance);

        if (message != null)
            IceLogging.Info(message, ToleranceHandle);
    }

    /// <summary>呼叫端必須先持有 <see cref="toleranceGate"/>。回傳要寫的訊息（出鎖之後寫）。</summary>
    private string ApplyToleranceLocked(float tolerance)
    {
        var now = Environment.TickCount64;

        // 這版 vnavmesh 沒有租約端點：走原本的全域寫入。
        // 🔴 這個閂鎖**刻意是可復原的**，理由見 ToleranceUnsupportedRetryIntervalMs。
        if (toleranceLeaseUnsupported)
        {
            if (now < toleranceNextAttemptAt)
            {
                SetTolerance(tolerance);
                return null;
            }

            toleranceLeaseUnsupported = false;
        }

        string message = null;

        try
        {
            // ① 有憑證而且到了心跳時間 → 續約。
            //    🔴 回 false＝那把已經不在了（逾時／vnavmesh 重載），**不能**繼續假設自己押著。
            if (toleranceLease != Guid.Empty && now >= toleranceNextAttemptAt)
            {
                if (RenewSuppression(toleranceLease))
                {
                    toleranceNextAttemptAt = now + ToleranceRenewIntervalMs;
                }
                else
                {
                    message = $"vnavmesh 的路徑容許值租約 {toleranceLease} 續約失敗（多半是 vnavmesh 重新載入，"
                            + "或這把已經逾時被掃掉），重新取得一把。";
                    toleranceLease = Guid.Empty;
                    leasedTolerance = float.NaN;
                }
            }

            // ② 沒有憑證就取一把。取不到就退避，退避期間走 ④ 的舊路徑。
            if (toleranceLease == Guid.Empty && now >= toleranceNextAttemptAt)
            {
                var acquired = AcquireSuppressionFor(Owner, ToleranceLeaseMilliseconds);
                if (acquired != Guid.Empty)
                {
                    toleranceLease = acquired;
                    leasedTolerance = float.NaN;
                    toleranceNextAttemptAt = now + ToleranceRenewIntervalMs;
                    loggedToleranceRefused = false;
                }
                else
                {
                    toleranceNextAttemptAt = now + ToleranceRetryIntervalMs;
                    if (!loggedToleranceRefused)
                    {
                        loggedToleranceRefused = true;
                        var refused = $"向 vnavmesh 取得路徑容許值租約失敗（{Name}.Path.AcquireSuppressionFor 回 Guid.Empty，"
                                    + "多半是它同時存在的租約已達上限）。這一輪改用舊的 Path.SetTolerance 直接寫全域值，"
                                    + $"{ToleranceRetryIntervalMs / 1000} 秒後重試。";
                        message = message == null ? refused : message + " " + refused;
                    }
                }
            }

            // ③ 有憑證就把值押上去。只在值真的變了的時候送，不是每次都送。
            if (toleranceLease != Guid.Empty)
            {
                if (leasedTolerance == tolerance)
                    return message;

                if (SetLeasedTolerance(toleranceLease, tolerance))
                {
                    leasedTolerance = tolerance;
                    var ok = $"已請 vnavmesh 在 ICE 導航期間把路徑容許值押成 {tolerance}"
                           + $"（租約 {toleranceLease}，租用者「{Owner}」）。ICE 結束導航、被卸載或當掉時都會自動還原成使用者的值。";
                    return message == null ? ok : message + " " + ok;
                }

                // 回 false＝那把不在了（傳進去的是有限數常數，不會是被拒絕的 NaN）。
                var lost = $"vnavmesh 拒絕了容許值租約 {toleranceLease} 的設值請求（那把多半已經不在了），"
                         + "這一輪改用舊的 Path.SetTolerance。";
                message = message == null ? lost : message + " " + lost;
                toleranceLease = Guid.Empty;
                leasedTolerance = float.NaN;
                toleranceNextAttemptAt = now + ToleranceRetryIntervalMs;
            }
        }
        catch (IpcNotReadyError)
        {
            // 這版 vnavmesh 根本沒有租約端點。閂住，永久走舊路徑。
            return DropToLegacyToleranceLocked(tolerance);
        }
        catch (Exception e)
        {
            // 型別不合之類。退回舊路徑但**不**閂住，退避之後照樣重試。
            // 🔴 先盡力把可能已經拿到的那把還回去：例外若發生在 Acquire **之後**
            //    （例如 SetLeasedTolerance 的簽章對不上），光清掉本地欄位會讓那把留在提供端的
            //    表上直到逾時 —— 每 5 秒漏一把，兩分半就吃光 vnavmesh 全域 32 把的上限，
            //    連**別的外掛**都拿不到租約。
            if (toleranceLease != Guid.Empty)
            {
                try
                {
                    ReleaseSuppression(toleranceLease);
                }
                catch
                {
                    // 還不回去就交給提供端逾時。這裡絕不能再擲例外。
                }
            }

            toleranceLease = Guid.Empty;
            leasedTolerance = float.NaN;
            toleranceNextAttemptAt = now + ToleranceRetryIntervalMs;
            SetTolerance(tolerance);
            return $"呼叫 vnavmesh 的容許值租約端點時發生例外：{e.GetType().Name}: {e.Message}。"
                 + $"這一輪改用舊的 Path.SetTolerance，{ToleranceRetryIntervalMs / 1000} 秒後重試。";
        }

        // ④ 雙軌退路：沒拿到租約時照改動前的行為直接寫全域值。
        SetTolerance(tolerance);
        return message;
    }

    /// <summary>
    /// 容許值租約的心跳。<b>每幀呼叫一次</b>；沒持有租約時第一行就回去，不做任何 IPC。
    /// </summary>
    /// <remarks>
    /// 🔑 <see cref="ApplyTolerance"/> 只在「開一條新路徑」時被呼叫（而且被 500 毫秒的節流擋著），
    /// 單一段導航跑超過租期時就沒有人續約了 —— 所以心跳要掛在每幀的 <c>ICE.Tick</c> 上。
    /// </remarks>
    public void RenewToleranceLease()
    {
        string message = null;

        lock (toleranceGate)
        {
            if (toleranceLease == Guid.Empty)
                return;

            var now = Environment.TickCount64;
            if (now < toleranceNextAttemptAt)
                return;

            try
            {
                if (RenewSuppression(toleranceLease))
                {
                    toleranceNextAttemptAt = now + ToleranceRenewIntervalMs;
                    return;
                }

                message = $"vnavmesh 的路徑容許值租約 {toleranceLease} 續約失敗（多半是 vnavmesh 重新載入，"
                        + "或這把已經逾時被掃掉）。下一次開新路徑時會重新取得一把；"
                        + "在那之前 vnavmesh 用的是使用者自己的容許值。";
            }
            catch (IpcNotReadyError)
            {
                // 🔴 這裡**不**閂住：能走到心跳就代表先前 Acquire 成功過，也就是這版 vnavmesh
                //    本來就有租約端點 ⇒ 這一次拿不到只可能是它正在重新載入或被卸載，是暫時的。
                //    閂住會把一次重載變成「這個 session 之後永遠用不到租約」。
                message = $"續約時 {Name} 的租約端點暫時不在（多半是它正在重新載入或被卸載）。"
                        + "下一次開新路徑時會重新取得一把；在那之前 vnavmesh 用的是使用者自己的容許值。";
            }
            catch (Exception e)
            {
                message = $"續約 vnavmesh 的容許值租約時發生例外：{e.GetType().Name}: {e.Message}。"
                        + "那把租約會在提供端逾時後自動放開，容許值屆時恢復成使用者的值。";
            }

            toleranceLease = Guid.Empty;
            leasedTolerance = float.NaN;
            toleranceNextAttemptAt = 0;
        }

        IceLogging.Info(message, ToleranceHandle);
    }

    /// <summary>把容許值租約還回去（沒持有就什麼都不做）。抵達目的地與外掛卸載都會走這裡。</summary>
    /// <remarks>
    /// 🔴 <b>IPC 呼叫刻意放在鎖外，而且整個包在 try/catch 裡</b>：這一支也會在 <c>ICE.Dispose()</c>
    /// 裡被呼叫，而那時候 vnavmesh 可能已經先被卸載了——「Dispose 裡無防護的 IPC 呼叫」是
    /// 全艦隊稽核出來的既有缺陷形狀，不要在這裡長出新的一個。
    /// 📌 就算這裡整條路壞掉也不會留下爛攤子：那把租約會在提供端逾時（上限 5 分鐘）後自動放開。
    /// </remarks>
    public void ReleaseToleranceLease(string reason)
    {
        Guid id;

        lock (toleranceGate)
        {
            id = toleranceLease;
            toleranceLease = Guid.Empty;
            leasedTolerance = float.NaN;
            toleranceNextAttemptAt = 0;
            loggedToleranceRefused = false;
            if (id == Guid.Empty)
                return;
        }

        try
        {
            ReleaseSuppression(id);
            IceLogging.Info($"已歸還 vnavmesh 的路徑容許值租約 {id}（{reason}），容許值恢復成使用者自己的設定。", ToleranceHandle);
        }
        catch (Exception e)
        {
            IceLogging.Info($"歸還 vnavmesh 的路徑容許值租約 {id} 時發生例外：{e.GetType().Name}: {e.Message}。"
                          + "那把租約會在提供端逾時後自動放開（上限 5 分鐘），容許值屆時恢復成使用者的值。", ToleranceHandle);
        }
    }

    /// <summary>
    /// 這版 vnavmesh 沒有租約端點：閂住並退回既有的 <c>Path.SetTolerance</c> 路徑。
    /// 呼叫端必須先持有 <see cref="toleranceGate"/>。
    /// </summary>
    private string DropToLegacyToleranceLocked(float tolerance)
    {
        toleranceLease = Guid.Empty;
        leasedTolerance = float.NaN;
        toleranceLeaseUnsupported = true;
        toleranceNextAttemptAt = Environment.TickCount64 + ToleranceUnsupportedRetryIntervalMs;
        SetTolerance(tolerance);

        if (loggedToleranceUnsupported)
            return null;

        loggedToleranceUnsupported = true;
        return $"這版 {Name} 沒有路徑容許值租約端點（{Name}.Path.AcquireSuppressionFor 沒有人註冊），"
             + $"ICE 改用舊的 Path.SetTolerance 直接寫它的全域值 {tolerance} —— 與這次改動前的行為完全相同。"
             + $"要讓容許值在 ICE 結束導航後自動還原，請把 {Name} 更新到有租約端點的版本。"
             + $"（每 {ToleranceUnsupportedRetryIntervalMs / 1000} 秒會再探一次，所以更新 {Name} 之後不必重載 ICE。"
             + "這行訊息只會出現一次。）";
    }
}