using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;

namespace ICE.Utilities;

/// <summary>
/// 查詢型遊戲函式的「位址沒解出」閘門。CS 的包裝此時會擲 InvalidOperationException，
/// 在每幀／繪製路徑上＝每幀擲例外；呼叫端先問這裡，缺了就走自己的「不知道」分支。
/// </summary>
/// <remarks>只能在框架執行緒呼叫（記錄用的集合沒有鎖）。每個函式名只記一次 Information。</remarks>
internal static class GameFunctionGuard
{
    private static readonly HashSet<string> logged = [];

    /// <param name="pointerIsNull">呼叫端傳 <c>X.MemberFunctionPointers.Y is null</c>。</param>
    /// <param name="effect">缺了之後使用者看到的影響，寫進 log。</param>
    internal static bool Missing(bool pointerIsNull, string functionName, string effect)
    {
        if (!pointerIsNull)
            return false;

        if (logged.Add(functionName))
            IceLogging.Info($"找不到遊戲函式 {functionName}，{effect}", "[遊戲函式]");
        return true;
    }

    /// <summary>研究模組（宇宙工具經驗）的四個查詢函式缺任一個就回 true；呼叫端比照「研究模組未載入」處理。</summary>
    internal static unsafe bool ResearchAnalysisMissing()
        => Missing(WKSResearchModule.MemberFunctionPointers.IsTypeAvailable is null
                   || WKSResearchModule.MemberFunctionPointers.GetNeededAnalysis is null
                   || WKSResearchModule.MemberFunctionPointers.GetCurrentAnalysis is null
                   || WKSResearchModule.MemberFunctionPointers.GetMaxAnalysis is null,
                   "WKSResearchModule（宇宙工具經驗查詢）", "宇宙工具經驗顯示與宇宙工具任務挑選都會當成研究資料未載入而略過。");
}
