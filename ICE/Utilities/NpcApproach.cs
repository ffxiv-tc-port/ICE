using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.Utilities;

/// <summary>月面 NPC 接近流程（兌換、宇宙幸運、靈器、修理）共用的距離、節流 key 與走位步驟。</summary>
internal static class NpcApproach
{
    /// <summary>離 NPC 這麼近就不再新送導航，改走「到了」的分支。</summary>
    public const float ArrivedRange = 6.75f;

    /// <summary>導航中離 NPC 小於這個距離就停下。</summary>
    public const float StopRange = 5f;

    /// <summary>四個流程共用同一個節流 key（現狀如此：任一流程送出導航，其他流程同一秒內也被擋）。</summary>
    public const string PathingThrottleKey = "Pathing to repair NPC";

    /// <summary>
    /// 走向 NPC 的一步，任務每幀呼叫。回 true＝到了（含已停下自己的導航），false＝下一幀再來。
    /// 契約：只在框架執行緒呼叫；停導航只停 ICE 自己發起的路徑（<see cref="IPC.NavmeshIPC.StopOwn"/>）。
    /// </summary>
    /// <param name="requireNavmeshInstalled">vnavmesh 沒載入就原地等、不呼叫任何導航端點（兌換流程原本的做法）。</param>
    public static bool Step(NpcData.NPCInfo npc, bool requireNavmeshInstalled = false)
    {
        if (requireNavmeshInstalled && !P.Navmesh.Installed)
            return false;

        if (!P.Navmesh.IsReady())
        {
            Utils.VnavBuildInfo();
            return false;
        }

        var distance = Player.DistanceTo(npc.NpcLocation);
        if (distance <= ArrivedRange)
        {
            if (!P.Navmesh.IsRunning())
            {
                IceLogging.Debug($"Distance to the npc is correct: {npc.Name}");
                return true;
            }

            if (distance < StopRange)
            {
                var r = P.Navmesh.StopOwn();
                IceLogging.Info($"已走到 {npc.Name} 附近，停下導航：{r.Why}（取消算路={r.CancelledPathfind}、停止路徑={r.StoppedPath}）", "[NpcApproach]");
                return true;
            }

            return false;
        }

        if (!P.Navmesh.IsRunning() && EzThrottler.Throttle(PathingThrottleKey))
        {
            IceLogging.Debug($"Pathing to: {npc.Name}");
            var randomPoint = RandomUtil.GetRandomPointInBounds(npc.Corner1, npc.Corner2, npc.Corner3, npc.Corner4, npc.NpcLocation.Y);
            IceLogging.DestinationLogs.Log(randomPoint);
            P.Navmesh.PathfindAndMoveTo(randomPoint, false);
        }

        return false;
    }
}
