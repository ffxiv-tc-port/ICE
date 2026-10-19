using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Utilities
{
    /// <summary>
    /// 「同一扇 <c>SelectYesno</c> 在它消失之前只按一次」的守衛 —— <see cref="AddonPressGuard"/> 的
    /// <c>SelectYesno</c> 專用薄包裝。
    /// </summary>
    /// <remarks>🔴 要防的東西：按下後「正在關閉中」的幾幀 IsAddonReady 三關全過，再送一次就是攔不到的 AccessViolation。
    /// 節流不是防護（記的是 key 上次放行的時刻而不是「這扇窗按過了」，跨呼叫點首次一律放行）；
    /// 守衛以視窗位址當索引鍵、只做位址等值比較永遠不解參（見 <see cref="AddonPressGuard"/>）。
    /// 📌 第一次看到某一扇窗一律當場放行，被擋下來的只有「同一扇窗還開著時的第二次按壓」。</remarks>
    internal static class YesnoPressGuard
    {
        private const string AddonName = "SelectYesno";

        /// <summary>
        /// 這一次可不可以按下這扇 <c>SelectYesno</c>。
        /// </summary>
        /// <param name="label">記 log 用的呼叫點名稱（<b>不</b>參與判定）。</param>
        /// <param name="addon">那扇窗的位址（<c>AddonMaster.Base</c> 或 <c>AtkUnitBase*</c> 轉 <c>nint</c>）。</param>
        /// <returns>
        /// <c>true</c>＝可以按（本方法已經把這一次記下來，呼叫端<b>必須</b>真的按下去）。
        /// </returns>
        public static bool MayPress(string label, nint addon)
            => AddonPressGuard.TryBeginPress(label, AddonName, addon);

        /// <inheritdoc cref="MayPress(string, nint)"/>
        /// <remarks>
        /// 給<b>不在 <c>unsafe</c> 區塊裡</b>的呼叫點用的多載（例如
        /// <c>Task_RelicTurnin.TurninRelic</c>）：指標轉型在本方法內部完成，
        /// 呼叫端不必為了取一個位址就把整個方法標成 <c>unsafe</c>。
        /// </remarks>
        public static unsafe bool MayPress(string label, SelectYesno master)
            => MayPress(label, (nint)master.Base);

        /// <summary>
        /// 只<b>看</b>不登記：這扇 <c>SelectYesno</c> 是不是「按過而且還沒觀察到它收掉」。
        /// </summary>
        /// <remarks>
        /// ⚠️ 回 <see langword="true"/> ＝ 這一幀不要碰。
        /// </remarks>
        public static unsafe bool IsHeld(SelectYesno master)
            => AddonPressGuard.IsHeld(AddonName, (nint)master.Base);
    }
}
