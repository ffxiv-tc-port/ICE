namespace ICE.Enums
{
    /// <summary>
    /// 宇宙製作時要不要透過 Artisan 的「臨時求解器」IPC 指定求解器。
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>Default</c> 一定要是 <b>0</b>：這個列舉會被序列化進設定檔，
    /// <c>default</c> 必須落在「不覆寫」上，而不是某個會改變製作行為的檔位。
    /// </remarks>
    public enum CosmicCraftSolver
    {
        /// <summary>不透過 IPC 覆寫，完全照 Artisan 自己對該配方的設定走。</summary>
        Default = 0,

        /// <summary>要求製作前把該配方的臨時求解器指成 Artisan 的專家求解器。</summary>
        Expert = 1,

        /// <summary>要求製作前把該配方的臨時求解器指成 Artisan 的 Raphael 求解器。</summary>
        Raphael = 2,
    }
}
