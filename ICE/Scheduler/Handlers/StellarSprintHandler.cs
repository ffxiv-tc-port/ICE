using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.Scheduler.Handlers;

/// <summary>
/// 在宇宙探索區域內，自動幫生產／採集職業補上「宇宙衝刺」。
/// </summary>
/// <remarks>
/// 📌 <b>這不是新功能，是把既有功能補完。</b><br/>
/// <c>C.MoonSprint</c>（UI：「自動使用宇宙衝刺」，預設開）本來就存在，
/// 判斷式也已經在檢查「宇宙衝刺」的狀態（<see cref="StellarSprintStatusId"/> = 4398），
/// 但真正送出去的技能是 <c>GeneralAction 4</c>（一般的「衝刺」）——
/// 也就是「等的是 A、放的是 B」。
/// <para>
/// 那段碼能不能運作，完全押在一個沒有被證明過的前提上：
/// <b>遊戲會不會在宇宙區域把一般衝刺自動替換成宇宙衝刺</b>。
/// 台服 7.20 的 EXD 顯示這兩個是不同的技能、連冷卻群組都不同
/// （衝刺＝Action 3、CooldownGroup 56、Recast 60 秒；
/// 　宇宙衝刺＝Action 43357、CooldownGroup 58、Recast 1 秒、StatusGainSelf 4398），
/// 所以替換沒發生的話，失敗形式是<b>完全靜默的</b>：
/// 放出去的是一般衝刺、狀態 4398 永遠不會出現，判斷式因此永遠成立，
/// 使用者只會覺得「衝刺一直在 60 秒冷卻」而不會有任何錯誤訊息。
/// </para>
/// <para>
/// ⚠️ 這裡<b>不移除</b>原本那條路：職業不是生產／採集，或宇宙衝刺當下不可用時，
/// 一律退回 <c>GeneralAction 4</c>，行為與先前完全相同
/// （機甲行動的協助員可能掛在戰鬥職上，把它們擋掉是回退既有行為）。
/// </para>
/// </remarks>
internal static unsafe class StellarSprintHandler
{
    /// <summary>宇宙衝刺（Action 43357）。台服 7.20 EXD 實查：名稱「宇宙衝刺」、StatusGainSelf 4398。</summary>
    private const uint StellarSprintActionId = 43357;

    /// <summary>宇宙衝刺帶來的狀態（Status 4398「宇宙衝刺」／移動速度提高）。</summary>
    internal const uint StellarSprintStatusId = 4398;

    /// <summary>一般的「衝刺」（GeneralAction 4 → Action 3）。宇宙衝刺用不了時的退路。</summary>
    private const uint GeneralSprintId = 4;

    /// <summary>
    /// 上一次因為宇宙衝刺不可用而退回一般衝刺的原因碼，用來讓診斷只在「變了」的時候印。
    /// </summary>
    private static uint lastFallbackStatus = uint.MaxValue;

    /// <summary>
    /// 這個職業用得到宇宙衝刺嗎。
    /// </summary>
    /// <remarks>
    /// 📌 判準來自技能自己的 <c>ClassJobCategory</c>：台服 7.20 EXD 實查，
    /// Action 43357 的 ClassJobCategory ＝ 35 ＝「能工巧匠 大地使者」
    /// （CRP BSM ARM GSM LTW WVR ALC CUL MIN BTN FSH），
    /// 剛好等於 ICE 既有的 <c>CrafterJobList</c>(8~15) ＋ <c>GatheringJobList</c>(16~18)。<br/>
    /// 📌 <c>PlayerHelper.UsingSupportedJob()</c> 現已改用同一組清單（過去是 <c>||</c> 恆 true 的 bug，已修），
    /// 兩者語意等價；這裡仍直接用清單，不繞經那個函式。
    /// </remarks>
    private static bool IsCosmicJob(uint jobId)
        => CosmicHelper.CrafterJobList.Contains(jobId) || CosmicHelper.GatheringJobList.Contains(jobId);

    /// <summary>
    /// 每幀由 <see cref="PlayerHandlers.Tick"/> 呼叫。前面的閘門全部是純旗標讀取，
    /// 只有真的要送技能之前才會碰原生的 <c>ActionManager</c>。
    /// </summary>
    internal static void Tick()
    {
        if (!C.MoonSprint)
            return;

        if (!PlayerHelper.IsInCosmicZone())
            return;

        // 已經在衝刺狀態就什麼都不用做（這是本功能的終止條件）。
        if (PlayerHelper.HasStatusId(StellarSprintStatusId))
            return;

        // 站著不動不需要衝刺 —— 這是 ICE 原本就有的條件，保留。
        // ⚠️ IsMoving() 讀不到 AgentMap 時回 false（fail-closed），見該函式的註解。
        if (!PlayerHandlers.IsMoving())
            return;

        // 以下三個是純 ConditionFlag 讀取，用來在「明顯不該放技能」的當下
        // 連原生呼叫都省掉。真正的權威仍然是下面的 GetActionStatus。
        if (!Svc.Condition[ConditionFlag.NormalConditions])
            return;

        if (Player.Mounted || Player.Mounting)
            return;

        // 採集中／製作中／過場／換區／正在讀條，全部包含在 IsOccupied() 裡。
        if (GenericHelpers.IsOccupied() || Player.IsCasting)
            return;

        // 🔴 節流放在最後一道閘門：EzThrottler 只有在**回 true 的那一次**才重設計時器，
        //    放在前面會在「其他條件不成立」的幀把窗口白白燒掉，變成最久要等兩個週期。
        //    ⚠️ 首次呼叫必定放行、key 是全域持久的，所以 key 要帶外掛名避免撞。
        if (!EzThrottler.Throttle("ICE: Stellar Sprint", 500))
            return;

        UseSprint();
    }

    private static void UseSprint()
    {
        var am = ActionManager.Instance();
        // 📌 ActionManager.Instance() 是 [StaticAddress(sig, off)] 且**沒有** isPointer:true
        //    ⇒ 解出來的是物件本身的位址（lea），永不為 null，判空是死碼。
        //    （同一個結論已經寫在 PlayerHelper.IsManipUnlockedByUnlockLink 上。）

        if (IsCosmicJob(Player.JobId))
        {
            // GetActionStatus 回 0 ＝ 現在可以用。它是遊戲自己的答案，
            // 職業不對／冷卻中／狀態不允許都會回非 0，所以不需要另外寫一份規則去猜。
            var status = am->GetActionStatus(ActionType.Action, StellarSprintActionId);
            if (status == 0)
            {
                lastFallbackStatus = uint.MaxValue;
                am->UseAction(ActionType.Action, StellarSprintActionId);
                return;
            }

            // 宇宙衝刺用不了。可能是還在 1 秒冷卻裡（正常），
            // 也可能是台服這個技能根本拿不到（不正常，而且原本完全查不出來）。
            // 🔴 寫 Information：使用者跑 LogLevel 2，Debug／Verbose 收不到。
            //    只在狀態碼「變了」的時候印，加上 5 分鐘節流，不會洗版。
            if (status != lastFallbackStatus && EzThrottler.Throttle("ICE: Stellar Sprint unavailable", 300000))
            {
                lastFallbackStatus = status;
                IceLogging.Info(
                    $"宇宙衝刺（技能 {StellarSprintActionId}）現在不可用，狀態碼 {status}，這次改用一般衝刺。" +
                    $"如果一直是同一個狀態碼且衝刺始終沒生效，請把這一行回報給開發者。",
                    "[Stellar Sprint]");
            }
        }

        // 退路：非生產／採集職業，或宇宙衝刺當下不可用。
        // 這條就是本次改動前的原始行為，一字不改地保留。
        if (am->GetActionStatus(ActionType.GeneralAction, GeneralSprintId) == 0)
            am->UseAction(ActionType.GeneralAction, GeneralSprintId);
    }
}
