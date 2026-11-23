namespace ICE.Utilities.Cosmic_Helper;

/// <summary>製作任務每一件成品最多能貢獻多少評價，以及出處（寫進判定的 log）。</summary>
/// <remarks>🔴 上限估低一次＝做得到金星的任務被判不可能 ⇒ 改規則前拿全部「製作計分觀測」驗每件得分 ≤ 上限零反例。</remarks>
internal static class CraftGoldCap
{
    internal const uint HardPlusRank = 6; // WKSMissionUnit.LevelGroup
    internal const uint TimedCollectableScoreText = 238; // WKSMissionText：限定時間內製作、根據收藏價值給予評價

    /// <summary>高難+ 每件＝1000 − 30×(100 − floor(100×品質÷最高品質))，低於第三門檻 300 ⇒ 滿品質 1000 為上限。</summary>
    internal const uint HardPlusCollectablePerItemMax = 1000;

    /// <returns><see langword="false"/>＝沒有可靠來源，呼叫端必須回 Unknown。</returns>
    internal static bool TryGet(uint rank, uint toDoTextId, int mainRecipes, int preRecipes,
                                uint goldScore, int totalRequired, out uint cap, out string source)
    {
        // 高難+ 兩件型（例：743、785、827、869）任務表沒寫件數，舊公式會得出「金星 ÷ 1 件」＝整個金星門檻，
        // 第一件沒滿分也判不出來。規則只套在觀測過的形狀（單一配方、沒有前置製作）。
        if (rank == HardPlusRank && toDoTextId == TimedCollectableScoreText && mainRecipes == 1 && preRecipes == 0)
        {
            cap = HardPlusCollectablePerItemMax;
            source = $"高難+ 計分規則：每件最高 {HardPlusCollectablePerItemMax}，要最終成品滿品質才拿得到；"
                   + $"依據任務等級 {HardPlusRank}、評價方式文字 {TimedCollectableScoreText}";
            return true;
        }

        if (totalRequired <= 0)
        {
            cap = 0;
            source = "任務表沒有記錄要繳交幾件（無法推算單件上限）";
            return false;
        }

        // 向上取整只是為了不因為整數除法把上限估低。
        cap = (goldScore + (uint)totalRequired - 1) / (uint)totalRequired;
        source = $"金星 {goldScore} ÷ 需繳 {totalRequired} 件";
        return true;
    }
}
