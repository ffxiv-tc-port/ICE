namespace ICE.Enums
{
    /// <summary>
    /// 遇到「文字對不上預期」的 <c>SelectYesno</c> 確認框時要怎麼處理。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這個列舉一定要有零值，而且零值必須是「維持現行行為」：
    /// YamlDotNet 反序列化時舊設定檔沒有這個鍵，欄位就會停在 <c>default</c>。
    /// </remarks>
    public enum UnexpectedYesnoAction
    {
        /// <summary>維持現行行為：不讀文字、一律按下確定。</summary>
        AlwaysConfirm = 0,

        /// <summary>照常按下確定，只是把對不上的確認框文字寫進記錄檔。行為與現行完全相同。</summary>
        LogOnly = 1,

        /// <summary>對不上就按取消，並寫進記錄檔。</summary>
        Decline = 2,
    }
}
