namespace ICE.Utilities;

/// <summary>寫死在多處的月面區域 ID（TerritoryType）。型別刻意是 int：與原本的整數字面值完全同義，換掉不改任何轉型。</summary>
internal static class CosmicZones
{
    public const int SinusArdorum = 1237;
    public const int Phaenna = 1291;
}

/// <summary>月面 NPC 接近流程（兌換、宇宙幸運、靈器、修理）共用的距離與節流 key。</summary>
internal static class NpcApproach
{
    /// <summary>離 NPC 這麼近就不再新送導航，改走「到了」的分支。</summary>
    public const float ArrivedRange = 6.75f;

    /// <summary>導航中離 NPC 小於這個距離就停下。</summary>
    public const float StopRange = 5f;

    /// <summary>四個流程共用同一個節流 key（現狀如此：任一流程送出導航，其他流程同一秒內也被擋）。</summary>
    public const string PathingThrottleKey = "Pathing to repair NPC";
}
