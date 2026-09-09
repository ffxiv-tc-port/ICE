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

    // 🔴 這一支從公開欄位改成「私有委派 ＋ 同名包裝方法」，理由見下面 PathfindAndMoveTo 的說明。
    // 🔴 端點名寫死成字面值，<b>不可以</b>改回 "SimpleMove.%m"：%m 展開的是<b>成員名</b>，
    //    欄位改名之後會變成 vnavmesh.SimpleMove.PathfindAndMoveToRpc —— 那個端點沒有人註冊，
    //    失敗形式是每次呼叫擲 IpcNotReadyError（不是編譯錯誤，也不是任何訊息）。
    [EzIPC("SimpleMove.PathfindAndMoveTo")] private readonly Func<Vector3, bool, bool> PathfindAndMoveToRpc;

    /// <summary>同一個端點的故障訊息重印間隔（毫秒）。</summary>
    private const long PathfindFaultLogIntervalMs = 10_000;

    /// <summary>導航故障訊息的 log 前綴。</summary>
    private const string NavFaultHandle = "[vnavmesh 導航]";

    /// <summary>上一次印過故障訊息的時刻（<see cref="System.Environment.TickCount64"/> 座標系）。</summary>
    /// <remarks>🔴 刻意不是 <c>EzThrottler</c>：它是整個外掛共用的靜態 <c>Dictionary</c> 且零同步。</remarks>
    private long pathfindFaultLoggedAt;

    /// <summary>
    /// 算路徑並開始移動。<b>vnavmesh 那一側自己擲例外時回 <see langword="false"/></b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>vnavmesh 的這支端點會擲例外，而本類的 <c>EzIPC.Init</c> 沒有帶 SafeWrapper</b>
    /// （那是刻意的，見檔案上方租約那一段的說明）⇒ 例外原樣傳到呼叫端。
    /// <c>NavmeshManager.QueryPath</c> 在 <c>_currentCTS</c> 為 null 時直接擲一個普通的
    /// <c>Exception</c>（訊息 <c>Can't initiate query - navmesh is not loaded</c>）——
    /// 那個狀態在「切圖／讀取畫面期間導航網格被卸掉」與「使用者關掉 vnavmesh 的自動載入」時都成立。
    /// Dalamud 的 <c>CallGateChannel.InvokeFunc</c> 是用 <c>Delegate.DynamicInvoke</c> 呼叫提供端的，
    /// 所以提供端擲出的東西一律被包成 <see cref="System.Reflection.TargetInvocationException"/>，
    /// 而它<b>不是</b> <c>IpcError</c> 的子型別 ⇒ 連 <c>SafeWrapper.IPCException</c> 都攔不到它。
    /// </para>
    /// <para>
    /// 補這一層之前，例外會直接逃進呼叫端。ICE 的 15 個呼叫點裡只有一部分先問過
    /// <c>IsReady()</c>（<c>Task_NavmeshMove</c>／<c>Task_Gamba</c>／<c>Task_RelicTurnin</c>／
    /// <c>Task_BuyCosmoItems</c>）；<c>Task_FindMission</c>、<c>Task_Fishing</c>、
    /// <c>Task_HubActivities</c>、<c>Task_Repair</c>、<c>Task_TurninMission</c> 只問了
    /// <c>IsRunning()</c>。那些是 <c>P.TaskManager</c> 的任務本體，例外逃出去等於
    /// <c>AbortOnError</c> ⇒ <b>整條佇列被清掉、無人值守的宇宙探索就這樣停住</b>。
    /// </para>
    /// <para>
    /// 回 <see langword="false"/> 之後那些呼叫點的行為＝「這一輪沒有開始移動」，
    /// 它們外層都有 <c>EzThrottler</c> 節流，下一輪會自己重試 —— 導航網格載回來就繼續。
    /// </para>
    /// <para>
    /// 🔴 刻意<b>只</b>攔 <see cref="System.Reflection.TargetInvocationException"/>，不是裸
    /// <c>catch (Exception)</c> —— ICE 自己這一側的程式錯誤不會被包成這個型別，照樣往上冒；
    /// <c>IpcNotReadyError</c>（vnavmesh 沒安裝／還沒註冊）的處置也一個字都沒改，仍然往上冒。
    /// </para>
    /// </remarks>
    public bool PathfindAndMoveTo(Vector3 destination, bool fly)
    {
        try
        {
            return PathfindAndMoveToRpc(destination, fly);
        }
        catch (System.Reflection.TargetInvocationException e)
        {
            var now = System.Environment.TickCount64;
            if (now - System.Threading.Volatile.Read(ref pathfindFaultLoggedAt) >= PathfindFaultLogIntervalMs)
            {
                System.Threading.Volatile.Write(ref pathfindFaultLoggedAt, now);
                var inner = e.InnerException ?? e;
                IceLogging.Info($"{Name} 的「SimpleMove.PathfindAndMoveTo」端點自己擲了例外，這一次當成「沒有開始移動」處理：{inner.GetType().Name}: {inner.Message}。這通常代表導航網格現在沒有載入（切圖中，或 vnavmesh 的自動載入被關掉）；下一輪會自己重試。持續出現請連同這一行回報。", NavFaultHandle);
            }
            return false;
        }
    }
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
    //    擲了就退回既有的 SetTolerance 路徑，行為與這次改動前逐字相同；
    //    那個閂鎖是**可復原的**（見 ToleranceUnsupportedRetryIntervalMs）。
    // 🔴🔴 **這一整組端點都在鎖外呼叫**，作法見 toleranceGate 的註解。
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
    /// 🔴🔴 <b>鎖內不做任何 IPC</b>（也不呼叫 ImGui、不做檔案 I/O、不寫 log、不配置字串）。
    /// 在鎖內打跨外掛 IPC ＝ <b>跨外掛鎖序</b>（我的鎖 → 對方的鎖）：對方日後只要長出一條
    /// 回頭呼叫的路徑就是死鎖，而那條路徑是在<b>別人的 repo</b> 裡長出來的，這邊看不到；
    /// 而且提供端常在自己的鎖外寫 Serilog ⇒ <b>檔案 I/O 會發生在我持鎖期間</b>。
    /// <br/><br/>
    /// 🔑 <b>作法＝版本序號（<see cref="toleranceOpSeq"/>）</b>：
    /// <c>lock{ 拍快照、決定下一步、記下當下的序號 }</c> → <b>鎖外</b>做 IPC →
    /// <c>lock{ 序號沒變才寫回結果，變了就丟棄 }</c>。
    /// 丟棄時若那一步<b>剛取得了一把租約</b>，會主動把它還回去（同樣在鎖外），
    /// 所以「重複 Acquire」不會漏租約，也不需要任何 busy 旗標。
    /// <br/><br/>
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

    /// <summary>這版 vnavmesh 沒有租約端點（擲過 <see cref="IpcNotReadyError"/>）⇒ 走舊路徑。</summary>
    /// <remarks>🔴 <b>可復原</b>：<see cref="ToleranceUnsupportedRetryIntervalMs"/> 到期會再探一次。</remarks>
    private bool toleranceLeaseUnsupported;

    private bool loggedToleranceUnsupported;
    private bool loggedToleranceRefused;

    /// <summary>租約狀態的版本號。<b>上面四個狀態欄位的每一次變動都必須把它往前推。</b></summary>
    /// <remarks>
    /// 🔑 這是「鎖外做 IPC」的正確性依據：規劃階段在鎖內記下當下的序號，提交階段只有在
    /// 序號沒變時才寫回 —— 序號變了就代表在我做 IPC 的期間有別人動過狀態，
    /// 我手上的結果已經過期，必須丟棄（並歸還那一步剛拿到的租約），下一圈重新規劃。
    /// <br/>⚠️ <b>漏掉任何一處 ++ 都會讓過期的結果被當成新鮮的寫回去</b>，
    /// 失敗形式是「偶爾押著一把早就該丟掉的租約」—— 所以四個狀態欄位的寫入
    /// <b>全部</b>收斂到 <see cref="MutateToleranceLocked"/> 這一個地方，沒有第二個寫入點。
    /// </remarks>
    private long toleranceOpSeq;

    /// <summary>
    /// 租約狀態的<b>唯一</b>寫入點：改四個欄位並把版本號往前推。
    /// 呼叫端必須先持有 <see cref="toleranceGate"/>。<b>不做任何 IPC。</b>
    /// </summary>
    private void MutateToleranceLocked(Guid lease, float leased, long nextAttemptAt, bool unsupported)
    {
        toleranceLease = lease;
        leasedTolerance = leased;
        toleranceNextAttemptAt = nextAttemptAt;
        toleranceLeaseUnsupported = unsupported;
        toleranceOpSeq++;
    }

    /// <summary>租約狀態機的下一步。</summary>
    private enum ToleranceStep
    {
        /// <summary>什麼都不用做（已經押著要的值）。</summary>
        None,

        /// <summary>續約（持有租約且到了心跳時間）。</summary>
        Renew,

        /// <summary>取得一把新租約。</summary>
        Acquire,

        /// <summary>把值押上去。</summary>
        PushValue,

        /// <summary>雙軌退路：直接寫 vnavmesh 的全域值（＝這次改動前的行為）。</summary>
        Legacy,
    }

    /// <summary>
    /// 決定下一步要做什麼，並拍下當下的版本號與憑證。
    /// </summary>
    /// <remarks>
    /// 🔴 呼叫端必須先持有 <see cref="toleranceGate"/>；<b>這一支不做任何 IPC、不配置任何東西</b>
    /// （它唯一呼叫的是 <see cref="MutateToleranceLocked"/>，那也是純欄位寫入）。
    /// </remarks>
    private ToleranceStep PlanToleranceLocked(float tolerance, out long seq, out Guid lease)
    {
        var now = Environment.TickCount64;

        // 這版 vnavmesh 沒有租約端點：退避期間走舊軌，退避到期就清掉閂鎖再探一次。
        if (toleranceLeaseUnsupported)
        {
            if (now < toleranceNextAttemptAt)
            {
                seq = toleranceOpSeq;
                lease = Guid.Empty;
                return ToleranceStep.Legacy;
            }

            MutateToleranceLocked(Guid.Empty, float.NaN, 0, false);
        }

        seq = toleranceOpSeq;
        lease = toleranceLease;

        // ① 持有租約而且到了心跳時間 → 先確認它還活著。
        if (lease != Guid.Empty && now >= toleranceNextAttemptAt)
            return ToleranceStep.Renew;

        // ② 沒有租約而且不在退避期間 → 取一把。
        if (lease == Guid.Empty && now >= toleranceNextAttemptAt)
            return ToleranceStep.Acquire;

        // ③ 有租約：值變了就押上去，沒變就收工。
        if (lease != Guid.Empty)
            return leasedTolerance == tolerance ? ToleranceStep.None : ToleranceStep.PushValue;

        // ④ 沒租約又還在退避期間 ⇒ 雙軌退路。
        return ToleranceStep.Legacy;
    }

    /// <summary>
    /// 請 vnavmesh 在 ICE 導航期間使用指定的路徑容許值。<b>冪等</b>，每次開新路徑時呼叫一次。
    /// </summary>
    /// <remarks>
    /// 🔴 拿不到租約時<b>退回既有的 <c>Path.SetTolerance</c> 直接寫全域值</b>——與改動前的行為
    /// 逐字相同，絕不因為租約拿不到就不寫（那會讓 AutoDuty 留下的容許值繼續影響 ICE 的導航）。
    /// 🔴 <b>所有 IPC 都在 <see cref="toleranceGate"/> 之外呼叫</b>（<c>SetTolerance</c> 本身也是 IPC）。
    /// </remarks>
    public void ApplyTolerance(float tolerance)
    {
        // 一次呼叫最多走 Renew → Acquire → PushValue 三步，第四圈必定收斂到 None/Legacy。
        // 🔴 上限是硬性的：版本號競爭在極端情況下會讓某一步重來，不能讓它在這裡無限打轉。
        //    真的用完四圈（單執行緒下不可能）就這一次不寫，下一次呼叫會再對齊一次。
        for (var round = 0; round < 4; round++)
        {
            ToleranceStep step;
            long seq;
            Guid lease;

            lock (toleranceGate)
                step = PlanToleranceLocked(tolerance, out seq, out lease);

            switch (step)
            {
                case ToleranceStep.None:
                    return;

                case ToleranceStep.Legacy:
                    SetTolerance(tolerance); // 🔴 鎖外
                    return;

                case ToleranceStep.Renew:
                    if (!RunToleranceRenewStep(seq, lease, tolerance))
                        return;
                    break;

                case ToleranceStep.Acquire:
                    if (!RunToleranceAcquireStep(seq, tolerance))
                        return;
                    break;

                case ToleranceStep.PushValue:
                    if (!RunTolerancePushStep(seq, lease, tolerance))
                        return;
                    break;
            }
        }
    }

    /// <summary>續約一步。回 <see langword="true"/>＝重新規劃並繼續，<see langword="false"/>＝收工。</summary>
    /// <remarks>🔴 IPC 在鎖外；提交階段只有版本號沒變才寫回。</remarks>
    private bool RunToleranceRenewStep(long seq, Guid lease, float tolerance)
    {
        bool renewed;
        try
        {
            renewed = RenewSuppression(lease); // 🔴 鎖外
        }
        catch (IpcNotReadyError)
        {
            DropToLegacyTolerance(seq, tolerance);
            return false;
        }
        catch (Exception e)
        {
            AbandonToleranceLease(seq, lease, tolerance, e);
            return false;
        }

        var committed = false;
        lock (toleranceGate)
        {
            if (seq == toleranceOpSeq)
            {
                committed = true;
                if (renewed)
                    MutateToleranceLocked(lease, leasedTolerance,
                        Environment.TickCount64 + ToleranceRenewIntervalMs, false);
                else
                    // 🔴 到期時刻刻意不動：讓下一圈的 Acquire 立刻執行（與改動前一致）。
                    MutateToleranceLocked(Guid.Empty, float.NaN, toleranceNextAttemptAt, false);
            }
        }

        if (committed && !renewed)
            IceLogging.Info($"vnavmesh 的路徑容許值租約 {lease} 續約失敗（多半是 vnavmesh 重新載入，"
                          + "或這把已經逾時被掃掉），重新取得一把。", ToleranceHandle);

        return true;
    }

    /// <summary>取得租約一步。</summary>
    /// <remarks>
    /// 🔴 結果過期（版本號被別人推過）而且我剛剛<b>真的拿到了一把</b>時會主動歸還：
    /// 不還的話它會壓著提供端 32 把的上限直到逾時，<b>連別的外掛都拿不到租約</b>。
    /// </remarks>
    private bool RunToleranceAcquireStep(long seq, float tolerance)
    {
        Guid acquired;
        try
        {
            acquired = AcquireSuppressionFor(Owner, ToleranceLeaseMilliseconds); // 🔴 鎖外
        }
        catch (IpcNotReadyError)
        {
            DropToLegacyTolerance(seq, tolerance);
            return false;
        }
        catch (Exception e)
        {
            AbandonToleranceLease(seq, Guid.Empty, tolerance, e);
            return false;
        }

        var stale = false;
        var reportRefused = false;

        lock (toleranceGate)
        {
            if (seq != toleranceOpSeq)
            {
                stale = true;
            }
            else if (acquired != Guid.Empty)
            {
                MutateToleranceLocked(acquired, float.NaN,
                    Environment.TickCount64 + ToleranceRenewIntervalMs, false);
                loggedToleranceRefused = false;
            }
            else
            {
                MutateToleranceLocked(Guid.Empty, float.NaN,
                    Environment.TickCount64 + ToleranceRetryIntervalMs, false);
                reportRefused = !loggedToleranceRefused;
                if (reportRefused)
                    loggedToleranceRefused = true;
            }
        }

        if (stale && acquired != Guid.Empty)
            ReleaseToleranceLeaseQuietly(acquired, "取得之後發現狀態已經被別的執行緒改過");

        if (reportRefused)
            IceLogging.Info($"向 vnavmesh 取得路徑容許值租約失敗（{Name}.Path.AcquireSuppressionFor 回 Guid.Empty，"
                          + "多半是它同時存在的租約已達上限）。這一輪改用舊的 Path.SetTolerance 直接寫全域值，"
                          + $"{ToleranceRetryIntervalMs / 1000} 秒後重試。", ToleranceHandle);

        return true;
    }

    /// <summary>把容許值押上去一步。</summary>
    private bool RunTolerancePushStep(long seq, Guid lease, float tolerance)
    {
        bool pushed;
        try
        {
            pushed = SetLeasedTolerance(lease, tolerance); // 🔴 鎖外
        }
        catch (IpcNotReadyError)
        {
            DropToLegacyTolerance(seq, tolerance);
            return false;
        }
        catch (Exception e)
        {
            AbandonToleranceLease(seq, lease, tolerance, e);
            return false;
        }

        var committed = false;
        lock (toleranceGate)
        {
            if (seq == toleranceOpSeq)
            {
                committed = true;
                if (pushed)
                    MutateToleranceLocked(lease, tolerance, toleranceNextAttemptAt, false);
                else
                    MutateToleranceLocked(Guid.Empty, float.NaN,
                        Environment.TickCount64 + ToleranceRetryIntervalMs, false);
            }
        }

        if (committed && pushed)
        {
            IceLogging.Info($"已請 vnavmesh 在 ICE 導航期間把路徑容許值押成 {tolerance}"
                          + $"（租約 {lease}，租用者「{Owner}」）。ICE 結束導航、被卸載或當掉時都會自動還原成使用者的值。",
                            ToleranceHandle);
            return false;
        }

        if (committed)
            IceLogging.Info($"vnavmesh 拒絕了容許值租約 {lease} 的設值請求（那把多半已經不在了），"
                          + "這一輪改用舊的 Path.SetTolerance。", ToleranceHandle);

        return true;
    }

    /// <summary>
    /// 容許值租約的心跳。<b>每幀呼叫一次</b>；沒持有租約（或還沒到期）時只讀兩個欄位就回去，
    /// <b>不配置任何東西、不做任何 IPC</b>。
    /// </summary>
    /// <remarks>
    /// 🔑 <see cref="ApplyTolerance"/> 只在「開一條新路徑」時被呼叫（而且被 500 毫秒的節流擋著），
    /// 單一段導航跑超過租期時就沒有人續約了 —— 所以心跳要掛在每幀的 <c>ICE.Tick</c> 上。
    /// </remarks>
    public void RenewToleranceLease()
    {
        Guid lease;
        long seq;

        // 每幀的穩態快路：兩個比較就回去。
        lock (toleranceGate)
        {
            if (toleranceLease == Guid.Empty)
                return;
            if (Environment.TickCount64 < toleranceNextAttemptAt)
                return;

            lease = toleranceLease;
            seq = toleranceOpSeq;
        }

        bool renewed;
        try
        {
            renewed = RenewSuppression(lease); // 🔴 鎖外
        }
        catch (IpcNotReadyError)
        {
            // 🔴 這裡**不**閂住：能走到心跳就代表先前 Acquire 成功過，也就是這版 vnavmesh
            //    本來就有租約端點 ⇒ 這一次拿不到只可能是它正在重新載入或被卸載，是暫時的。
            //    閂住會把一次重載變成「這個 session 之後永遠用不到租約」。
            DropHeartbeatLease(seq,
                $"續約時 {Name} 的租約端點暫時不在（多半是它正在重新載入或被卸載）。"
                + "下一次開新路徑時會重新取得一把；在那之前 vnavmesh 用的是使用者自己的容許值。");
            return;
        }
        catch (Exception e)
        {
            DropHeartbeatLease(seq,
                $"續約 vnavmesh 的容許值租約時發生例外：{e.GetType().Name}: {e.Message}。"
                + "那把租約會在提供端逾時後自動放開，容許值屆時恢復成使用者的值。");
            return;
        }

        if (renewed)
        {
            lock (toleranceGate)
            {
                if (seq == toleranceOpSeq)
                    MutateToleranceLocked(lease, leasedTolerance,
                        Environment.TickCount64 + ToleranceRenewIntervalMs, false);
            }
            return;
        }

        DropHeartbeatLease(seq,
            $"vnavmesh 的路徑容許值租約 {lease} 續約失敗（多半是 vnavmesh 重新載入，"
            + "或這把已經逾時被掃掉）。下一次開新路徑時會重新取得一把；"
            + "在那之前 vnavmesh 用的是使用者自己的容許值。");
    }

    /// <summary>心跳失敗：丟掉那把租約並寫一行說明。<b>呼叫時不可持有 <see cref="toleranceGate"/>。</b></summary>
    private void DropHeartbeatLease(long seq, string message)
    {
        lock (toleranceGate)
        {
            if (seq != toleranceOpSeq)
                return;

            // 🔴 心跳失敗不動閂鎖（沿用目前的值）：那是 ApplyTolerance 的職責。
            MutateToleranceLocked(Guid.Empty, float.NaN, 0, toleranceLeaseUnsupported);
        }

        IceLogging.Info(message, ToleranceHandle);
    }

    /// <summary>把容許值租約還回去（沒持有就什麼都不做）。抵達目的地與外掛卸載都會走這裡。</summary>
    /// <remarks>
    /// 🔴 <b>IPC 在鎖外，而且整個包在 try/catch 裡</b>：這一支也會在 <c>ICE.Dispose()</c> 裡被呼叫，
    /// 而那時候 vnavmesh 可能已經先被卸載了——「Dispose 裡無防護的 IPC 呼叫」是全艦隊稽核出來的
    /// 既有缺陷形狀，不要在這裡長出新的一個。
    /// 📌 就算這裡整條路壞掉也不會留下爛攤子：那把租約會在提供端逾時（上限 5 分鐘）後自動放開。
    /// </remarks>
    public void ReleaseToleranceLease(string reason)
    {
        Guid id;

        lock (toleranceGate)
        {
            id = toleranceLease;

            // 🔴 這也是一次狀態機轉換，一定要推版本號：否則正在鎖外做 IPC 的那一步會以為
            //    自己的結果還新鮮，把剛剛丟掉的租約又寫回來。
            MutateToleranceLocked(Guid.Empty, float.NaN, 0, toleranceLeaseUnsupported);
            loggedToleranceRefused = false;

            if (id == Guid.Empty)
                return;
        }

        try
        {
            ReleaseSuppression(id); // 🔴 鎖外
            IceLogging.Info($"已歸還 vnavmesh 的路徑容許值租約 {id}（{reason}），容許值恢復成使用者自己的設定。", ToleranceHandle);
        }
        catch (Exception e)
        {
            IceLogging.Info($"歸還 vnavmesh 的路徑容許值租約 {id} 時發生例外：{e.GetType().Name}: {e.Message}。"
                          + "那把租約會在提供端逾時後自動放開（上限 5 分鐘），容許值屆時恢復成使用者的值。", ToleranceHandle);
        }
    }

    /// <summary>盡力歸還一把租約，絕不擲例外、不碰任何共用狀態。<b>呼叫時不可持有 <see cref="toleranceGate"/>。</b></summary>
    private void ReleaseToleranceLeaseQuietly(Guid id, string reason)
    {
        try
        {
            ReleaseSuppression(id); // 🔴 鎖外
        }
        catch (Exception e)
        {
            IceLogging.Info($"歸還 vnavmesh 的路徑容許值租約 {id}（{reason}）時發生例外：{e.GetType().Name}: {e.Message}。"
                          + "那把租約會在提供端逾時後自動放開（上限 5 分鐘）。", ToleranceHandle);
        }
    }

    /// <summary>
    /// 這版 vnavmesh 沒有租約端點：閂住並退回既有的 <c>Path.SetTolerance</c> 路徑。
    /// </summary>
    /// <remarks>
    /// 🔴 <c>SetTolerance</c> 本身也是 IPC，所以它在鎖外呼叫。
    /// 🔴 閂鎖是<b>可復原</b>的（見 <see cref="ToleranceUnsupportedRetryIntervalMs"/>）。
    /// </remarks>
    private void DropToLegacyTolerance(long seq, float tolerance)
    {
        var first = false;

        lock (toleranceGate)
        {
            if (seq == toleranceOpSeq)
            {
                MutateToleranceLocked(Guid.Empty, float.NaN,
                    Environment.TickCount64 + ToleranceUnsupportedRetryIntervalMs, true);
                first = !loggedToleranceUnsupported;
                if (first)
                    loggedToleranceUnsupported = true;
            }
        }

        SetTolerance(tolerance); // 🔴 鎖外

        if (first)
            IceLogging.Info($"這版 {Name} 沒有路徑容許值租約端點（{Name}.Path.AcquireSuppressionFor 沒有人註冊），"
                          + $"ICE 改用舊的 Path.SetTolerance 直接寫它的全域值 {tolerance} —— 與這次改動前的行為完全相同。"
                          + $"要讓容許值在 ICE 結束導航後自動還原，請把 {Name} 更新到有租約端點的版本。"
                          + $"（每 {ToleranceUnsupportedRetryIntervalMs / 1000} 秒會再探一次，所以更新 {Name} 之後不必重載 ICE。"
                          + "這行訊息只會出現一次。）", ToleranceHandle);
    }

    /// <summary>
    /// 呼叫租約端點時發生非 <see cref="IpcNotReadyError"/> 的例外（型別不合之類）：
    /// 丟掉手上那把、退避之後重試，這一輪走舊軌。<b>不</b>閂住 —— 那是給「端點不存在」用的。
    /// </summary>
    private void AbandonToleranceLease(long seq, Guid lease, float tolerance, Exception e)
    {
        // 🔴 先盡力把手上那把還回去（鎖外）：例外若發生在 Acquire **之後**（例如
        //    SetLeasedTolerance 的簽章對不上），只清本地欄位會讓那把留在提供端的表上直到逾時
        //    —— 每 5 秒漏一把，兩分半就吃光 vnavmesh 全域 32 把的上限，連**別的外掛**都拿不到租約。
        if (lease != Guid.Empty)
            ReleaseToleranceLeaseQuietly(lease, "呼叫租約端點時發生例外");

        lock (toleranceGate)
        {
            if (seq == toleranceOpSeq)
                MutateToleranceLocked(Guid.Empty, float.NaN,
                    Environment.TickCount64 + ToleranceRetryIntervalMs, false);
        }

        SetTolerance(tolerance); // 🔴 鎖外

        IceLogging.Info($"呼叫 vnavmesh 的容許值租約端點時發生例外：{e.GetType().Name}: {e.Message}。"
                      + $"這一輪改用舊的 Path.SetTolerance，{ToleranceRetryIntervalMs / 1000} 秒後重試。",
                        ToleranceHandle);
    }
}