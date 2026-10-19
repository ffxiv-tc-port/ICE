using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>「用剩下的材料還有沒有機會拿到金星」的判定，以及每一次製作對評價貢獻多少的觀測。</summary>
/// <remarks>
/// 製作任務的面板評價 ＝ 身上那些任務成品各自的評價值加總，<b>每一件的上限是
/// <c>GoldStarRequirement ÷ 需要繳交的總件數</c></b>。
/// 🔴 <b>所有「算不出來」的情況一律回 <see cref="CraftGoldOutlook.Unknown"/>，呼叫端不得放棄任務。</b>
/// 誤判的代價是白白丟掉一個本來做得完的任務，而使用者事後分不出來是誤判還是真的做不完。
/// </remarks>
internal static class CraftGoldFeasibility
{
    internal enum CraftGoldOutlook
    {
        /// <summary>算不出來。<b>呼叫端必須當成「還有機會」</b>。</summary>
        Unknown,
        /// <summary>剩下的材料理論上還做得到金星。</summary>
        Reachable,
        /// <summary>就算接下來每一件都做到滿分，也追不上金星門檻。</summary>
        Unreachable,
    }

    /// <summary>
    /// 這一輪還有沒有機會拿到金星。
    /// </summary>
    /// <param name="detail">給記錄檔的算式明細（三種結果都會填，方便事後判斷是不是誤判）。</param>
    internal static CraftGoldOutlook Evaluate(CosmicHelper.CosmicInfo mission, uint? currentScore, out string detail)
    {
        // 高難任務是通過／未通過，不是評價分數；時間型的門檻單位是「剩餘秒數 × 10」。
        // 兩種都不能拿分數去比大小。
        if (mission.Attributes.HasFlag(MissionAttributes.Critical))
        {
            detail = "高難任務不是用評價分數計分";
            return CraftGoldOutlook.Unknown;
        }

        if (mission.IsTimeGraded)
        {
            detail = "時間型任務的門檻單位是剩餘秒數，不是評價分數";
            return CraftGoldOutlook.Unknown;
        }

        if (mission.GoldScore == 0)
        {
            detail = "這個任務沒有金星門檻";
            return CraftGoldOutlook.Unknown;
        }

        if (mission.Crafts_Main.Count == 0)
        {
            detail = "任務表裡沒有主要製作項目";
            return CraftGoldOutlook.Unknown;
        }

        if (currentScore is not { } score)
        {
            detail = "面板讀不到目前評價";
            return CraftGoldOutlook.Unknown;
        }

        // 🔴 傳送／換區途中 InventoryManager 一律回 0 —— 那會讓「身上有材料」跟「現在讀不到」
        //    得到一模一樣的結論。
        if (!PlayerHelper.InventoryReadable())
        {
            detail = "玩家處於傳送／讀取中，道具數量讀出來會全是 0";
            return CraftGoldOutlook.Unknown;
        }

        var totalRequired = 0;
        foreach (var craft in mission.Crafts_Main.Values)
            totalRequired += craft.RequiredAmount;

        if (totalRequired <= 0)
        {
            detail = "任務表沒有記錄要繳交幾件（無法推算單件上限）";
            return CraftGoldOutlook.Unknown;
        }

        // 單件上限＝金星門檻 ÷ 總繳交件數。向上取整只是為了不因為整數除法把上限估低。
        var perItemCap = (mission.GoldScore + (uint)totalRequired - 1) / (uint)totalRequired;

        if (!TryCountRemainingCrafts(mission, out var remainingCrafts, out var why))
        {
            detail = why;
            return CraftGoldOutlook.Unknown;
        }

        // 🔴 「這一輪還沒有任何進展」時一律不判定。剛接下任務到補給材料真的進背包之間有一個空窗，
        //    那個瞬間的畫面是「評價 0、材料 0、還能做 0 件」—— 跟「材料燒光了」一模一樣，
        //    差別只在一個是還沒開始、一個是已經結束。要求「至少有一件成品或已經有評價」
        //    就能把兩者分開，代價只是最多晚一次製作才判得出來。
        //    （材料真的從頭到尾沒進來的話，既有的「材料不足」流程本來就會收尾，不會卡住。）
        var heldOutputs = 0;
        foreach (var craft in mission.Crafts_Main.Values)
        {
            PlayerHelper.GetItemCount(craft.ItemId, out var held);
            heldOutputs += Math.Max(0, held);
        }

        if (score == 0 && heldOutputs == 0)
        {
            detail = "這一輪還沒有做出任何成品、評價也還是 0（可能是補給材料還沒進背包）";
            return CraftGoldOutlook.Unknown;
        }

        // long：perItemCap 最大 2250、剩餘件數理論上不會大，但門檻與分數都是 uint，
        // 相乘後用 uint 比較是溢位的溫床，直接用 long 免得靠「應該不會那麼大」撐著。
        var maxAchievable = (long)score + (long)remainingCrafts * perItemCap;

        detail = $"目前評價 {score}｜還能再做 {remainingCrafts} 件｜單件上限 {perItemCap}"
               + $"（金星 {mission.GoldScore} ÷ 需繳 {totalRequired} 件）｜"
               + $"預估最高 {maxAchievable}｜金星門檻 {mission.GoldScore}";

        return maxAchievable >= mission.GoldScore ? CraftGoldOutlook.Reachable : CraftGoldOutlook.Unreachable;
    }

    /// <summary>
    /// 用身上的材料估「最多還能再做出幾件會計分的成品」。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這裡讀的是 <c>CosmicInfo.RequiredItems</c>（建表時就存好的「每做一個要幾個」），
    /// 與 <c>Task_Craft.HasMaterialsFor</c> 同一份資料，兩邊不會分岔。
    /// </remarks>
    private static bool TryCountRemainingCrafts(CosmicHelper.CosmicInfo mission, out int remaining, out string why)
    {
        remaining = 0;
        why = string.Empty;

        foreach (var craft in mission.Crafts_Main.Values)
        {
            if (!TryCountFor(craft, out var possible))
            {
                why = $"主要製作項目（道具 {craft.ItemId}）沒有材料資料，無法推算還能做幾件";
                return false;
            }

            remaining += possible;
        }

        foreach (var craft in mission.Crafts_Pre.Values)
        {
            if (!TryCountFor(craft, out var possible))
            {
                why = $"前置製作項目（道具 {craft.ItemId}）沒有材料資料，無法推算還能做幾件";
                return false;
            }

            // 前置成品做出來之後還要再做一次主成品，所以身上現有的前置成品本身也是「還能做的次數」。
            PlayerHelper.GetItemCount(craft.ItemId, out var held);
            remaining += possible + Math.Max(0, held);
        }

        return true;
    }

    private static bool TryCountFor(CosmicHelper.CraftingInfo craft, out int possible)
    {
        possible = int.MaxValue;

        foreach (var material in craft.RequiredItems)
        {
            // 與 HasMaterialsFor 一致：0 號道具與非正數用量都不是有效的材料需求。
            if (material.Key == 0 || material.Value <= 0)
                continue;

            PlayerHelper.GetItemCount(material.Key, out var held);
            possible = Math.Min(possible, held / material.Value);
        }

        // 一項有效材料都沒有 ＝ 建表時就沒存材料資訊（高難任務的 CraftingInfo 就是這樣），
        // 那就不是「可以做無限多件」而是「不知道」。
        if (possible == int.MaxValue)
        {
            possible = 0;
            return false;
        }

        return true;
    }

    // - - - 觀測：每一件成品實際貢獻了多少評價 - - - //
    // 🔑 這是把上面那個模型從「離線推論」升級成「這台機器上的地面真值」唯一的辦法，
    //    而且它同時也是使用者事後判斷「剛剛那次放棄是不是誤判」的依據。
    //    要使用者回報的診斷；只在數字真的變動時印，所以最多一次製作一行。

    private static uint _observedMission;
    private static int _lastItemCount = -1;
    private static uint _lastScore;

    internal static void Observe(uint missionId, CosmicHelper.CosmicInfo mission, uint? currentScore, string handle)
    {
        if (currentScore is not { } score || !PlayerHelper.InventoryReadable())
            return;

        var items = 0;
        foreach (var craft in mission.Crafts_Main.Values)
        {
            PlayerHelper.GetItemCount(craft.ItemId, out var held);
            items += Math.Max(0, held);
        }

        if (missionId != _observedMission)
        {
            _observedMission = missionId;
            _lastItemCount = items;
            _lastScore = score;
            return;
        }

        if (items == _lastItemCount && score == _lastScore)
            return;

        var itemDelta = items - _lastItemCount;
        var scoreDelta = (long)score - _lastScore;

        if (itemDelta > 0 && scoreDelta > 0)
        {
            IceLogging.Info(
                $"任務 {missionId} 製作計分觀測：成品 {_lastItemCount} → {items}（+{itemDelta}）、"
                + $"評價 {_lastScore} → {score}（+{scoreDelta}）＝每件約 {scoreDelta / itemDelta}｜"
                + $"銀 {mission.SilverScore}／金 {mission.GoldScore}。", handle);
        }
        else
        {
            IceLogging.Info(
                $"任務 {missionId} 製作計分觀測：成品 {_lastItemCount} → {items}、評價 {_lastScore} → {score}。"
                + "（件數或評價往下走通常代表任務被重接了。）", handle);
        }

        _lastItemCount = items;
        _lastScore = score;
    }

    /// <summary>
    /// 判定＋處置。<b>回傳 <see langword="true"/> 代表已經改寫排程狀態、呼叫端必須立刻 <c>return true</c>。</b>
    /// </summary>
    internal static bool HandleUnreachableGold(uint missionId, CosmicHelper.CosmicInfo mission,
                                               uint? currentScore, string handle)
    {
        if (C.CraftGoldUnreachable == GoldUnreachableAction.Off)
            return false;

        // 只有「這一輪除了金星以外不會交件」時，拿不到金星才構成收手的理由。
        // AutoTurnin 開著時 Task_CheckScore 只在金星達標時交件（銀／銅的分支被 else 擋掉），
        // TurninGold 開著時同理（TurninSilver 那一支有 !TurninGold 前置）。
        if (!C.MissionConfig.TryGetValue(missionId, out var config))
            return false;

        if (!config.AutoTurnin && !config.TurninGold)
            return false;

        var outlook = Evaluate(mission, currentScore, out var detail);
        if (outlook != CraftGoldOutlook.Unreachable)
            return false;

        if (C.CraftGoldUnreachable == GoldUnreachableAction.Notify)
        {
            if (EzThrottler.Throttle($"ICE: craft gold unreachable notify {missionId}", 60000))
            {
                IceLogging.Info(
                    $"任務 {MissionChain.DescribeMission(missionId)} 已經不可能拿到金星：{detail}。"
                    + "目前設定是「只提示」，流程不變，會照舊做到材料見底。", handle);
                IceLogging.ChatInfo(
                    $"這個任務已經不可能拿到金星了（預估最高分追不上金星門檻）。", "[ICE]");
            }

            return false;
        }

        IceLogging.Info(
            $"任務 {MissionChain.DescribeMission(missionId)} 已經不可能拿到金星，依設定收手：{detail}。"
            + "接下來走既有的收尾流程 —— ⚠️ 那條流程只會按一次「回報成果」，下一個 tick 任務還在就直接放棄，"
            + "所以已經到手的銀星／銅星有可能一起丟掉。"
            + "如果你認為這是誤判，請把這一行連同上面的「製作計分觀測」一起回報。", handle);

        SchedulerMain.State = IceState.AbandonMission;
        P.TaskManager.Tasks.Clear();
        return true;
    }
}
