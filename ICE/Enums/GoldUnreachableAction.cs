namespace ICE.Enums
{
    /// <summary>
    /// 製作任務判定出「這一輪已經不可能拿到金星」之後要怎麼處置。
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>Off</c> 一定要是 <b>0</b>：這個列舉會被序列化進設定檔，而 <c>default</c> 必須落在
    /// 「什麼都不做」上。沒有零值的列舉會讓 <c>default</c> 停在一個有副作用的檔位。
    /// </remarks>
    public enum GoldUnreachableAction
    {
        /// <summary>維持現行行為：一路做到材料見底，再由既有的「材料不足」流程收尾。</summary>
        Off = 0,

        /// <summary>只在記錄檔與聊天視窗說明，流程完全不變。</summary>
        Notify = 1,

        /// <summary>
        /// 立刻收手，走既有的 <c>IceState.AbandonMission</c>
        /// （那條路本來就會先試著回報、回報不成才真的放棄）。
        /// </summary>
        Abandon = 2,
    }
}
