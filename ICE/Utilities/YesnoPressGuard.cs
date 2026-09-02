using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Utilities
{
    /// <summary>
    /// 「同一扇 <c>SelectYesno</c> 在它消失之前只按一次」的守衛 —— 現在是 <see cref="AddonPressGuard"/> 的
    /// <c>SelectYesno</c> 專用薄包裝，既有接點的簽章與行為維持不變。
    /// </summary>
    /// <remarks>
    /// 🔴 要防的東西（按下後「正在關閉中」的幾幀 IsAddonReady 三關全過、再送一次就是攔不到的 AccessViolation）、
    /// 為什麼節流不是防護（記的是 key 上次放行的時刻而不是「這扇窗按過了」，跨呼叫點首次一律放行）、
    /// 為什麼以視窗位址當索引鍵、為什麼「只做位址等值比較永遠不解參」，全部見 <see cref="AddonPressGuard"/>。
    /// 本類別只負責把 SelectYesno 的呼叫點對到那邊：視窗名稱固定為 <c>"SelectYesno"</c>、所有按法併成同一個 key
    /// （確定／取消／<c>Callback.Fire(0)</c>／<c>Callback.Fire(-1)</c> 對這種「回答一次即終結」的窗都是同一次按壓）、
    /// 逃生口沿用原本的 60 幀（<see cref="AddonPressGuard.DefaultEscapeFrames"/>）。<br/>
    /// 📌 相較於舊版（自己一張 SelectYesno 專用的表），多了兩件事：解除封鎖除了輪詢還接了
    /// <c>PreFinalize</c>／<c>PostSetup</c>（同位址被新窗重用時不會被誤擋到逃生口），以及與非 SelectYesno
    /// 視窗共用同一張表。<br/>
    /// 📌 正常路徑的行為沒有改變：第一次看到某一扇窗一律當場放行，被擋下來的只有「同一扇窗還開著時的第二次按壓」。
    /// </remarks>
    internal static class YesnoPressGuard
    {
        private const string AddonName = "SelectYesno";

        /// <summary>
        /// 這一次可不可以按下這扇 <c>SelectYesno</c>。
        /// </summary>
        /// <param name="label">記 log 用的呼叫點名稱（<b>不</b>參與判定）。</param>
        /// <param name="addon">那扇窗的位址（<c>AddonMaster.Base</c> 或 <c>AtkUnitBase*</c> 轉 <c>nint</c>）。</param>
        /// <returns>
        /// <c>true</c>＝可以按（本方法已經把這一次記下來，呼叫端<b>必須</b>真的按下去）。<br/>
        /// <c>false</c>＝這扇窗剛才已經按過而且還沒消失，這一次不要按。
        /// </returns>
        /// <remarks>
        /// ⚠️ 一定要放在條件式的<b>最後</b>一項（其他閘門都通過、確定要按了才呼叫）——
        /// 它有副作用（記下這一次按壓）。放在前面會把「其實沒按」記成「按過了」，
        /// 讓真正該按的那一次被擋掉。
        /// </remarks>
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
        /// 給「按之前要先讀確認框文字來決定按確定還是取消」的呼叫端用，順序是
        /// <see cref="IsHeld(SelectYesno)"/> → 讀文字 → <see cref="MayPress(string, SelectYesno)"/> → 按：
        /// 被擋的那幾幀連文字都不去讀（那正是視窗記憶體變動中的幾幀），
        /// 而讀完決定不按時也不會留下一筆「登記了卻沒按」的紀錄白白封鎖到逃生口。<br/>
        /// ⚠️ 回 <see langword="true"/> ＝ 這一幀不要碰。
        /// </remarks>
        public static unsafe bool IsHeld(SelectYesno master)
            => AddonPressGuard.IsHeld(AddonName, (nint)master.Base);
    }
}
