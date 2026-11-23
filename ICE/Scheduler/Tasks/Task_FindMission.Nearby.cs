using ECommons.GameHelpers;
using ICE.Ui.MainUi.ModeSelect;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using System.Collections.Generic;

namespace ICE.Scheduler.Tasks
{
    /// <remarks>
    /// 「距離優先」（<c>C.PickNearbyMission</c>）的兩個接入點與距離估計。只在框架執行緒呼叫；
    /// 位置在挑選當下讀一次，不跨幀保存任何遊戲物件。
    /// </remarks>
    internal static partial class Task_FindMission
    {
        /// <summary>
        /// 從現在位置到任務開工點的直線距離估計，與 <see cref="Navmesh_MoveToMission"/> 的走法對齊：
        /// 採集＝路線上最近一點的落地點；釣魚＝各釣點的平均（那邊隨機挑釣點）；其他任務原地做＝0。
        /// 不在這個區域、或查不到路線／釣點資料回 null（＝不知道，不參與比較）。
        /// </summary>
        private static Func<uint, float?>? NearbyDistanceEstimator()
        {
            if (!Player.Available)
                return null;

            var from = Player.Position;
            var territory = Player.Territory;
            var cache = new Dictionary<uint, float?>();
            return id =>
            {
                if (!cache.TryGetValue(id, out var d))
                    cache[id] = d = EstimateTravelMeters(id, from, territory);
                return d;
            };
        }

        private static float? EstimateTravelMeters(uint missionId, Vector3 from, uint territory)
        {
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var mission) || mission.TerritoryId != territory)
                return null;

            if (mission.Attributes.HasFlag(MissionAttributes.Gather))
            {
                var route = GatheringRouteLoader.GetRoute(mission.TerritoryId, mission.MapPosition);
                if (route == null || route.Count == 0)
                    return null;
                var best = float.MaxValue;
                foreach (var node in route)
                    best = Math.Min(best, Vector3.Distance(from, node.LandZone));
                return best;
            }

            if (mission.Attributes.HasFlag(MissionAttributes.Fish))
            {
                if (!GatheringUtil.MoonFishingLocations.TryGetValue(mission.TerritoryId, out var spots)
                    || !spots.TryGetValue(mission.MapPosition, out var hole) || hole.Count == 0)
                    return null;
                var sum = 0f;
                foreach (var spot in hole)
                    sum += Vector3.Distance(from, spot.FishingSpot);
                return sum / hole.Count;
            }

            return 0f;
        }

        private static string MissionLabel(uint id)
            => CosmicHelper.SheetMissionDict.TryGetValue(id, out var m) ? $"{id}「{m.Name}」" : id.ToString();

        /// <summary>
        /// 宇宙工具經驗模式：在跟 <paramref name="chosen"/> 同一個冷卻組、分數差不多的候選裡改接近的。
        /// <paramref name="isCooling"/> 為 null＝沒開冷卻排序，全部同組。
        /// </summary>
        private static uint PreferNearbyRelic(uint chosen, List<(uint Id, float Score)> scored, Func<uint, bool>? isCooling, string tip)
        {
            var distance = NearbyDistanceEstimator();
            var chosenIndex = scored.FindIndex(s => s.Id == chosen);
            if (distance == null || chosenIndex < 0)
                return chosen;

            var best = scored[chosenIndex].Score;
            var tolerance = C.PickNearbyMissionScoreTolerancePercent;
            var chosenCooling = isCooling?.Invoke(chosen) ?? false;
            var equivalents = scored
                .Where(s => (isCooling?.Invoke(s.Id) ?? false) == chosenCooling && NearbyMissionPick.IsNearTie(s.Score, best, tolerance))
                .Select(s => s.Id);

            if (!NearbyMissionPick.TryPickNearer(chosen, equivalents, distance, out var nearer, out var fromMeters, out var toMeters))
                return chosen;

            var nearerScore = scored.Find(s => s.Id == nearer).Score;
            IceLogging.Info($"距離優先：分數最高的 {MissionLabel(chosen)}（分數 {best:0.###}）約 {fromMeters:N0} 米；"
                            + $"{MissionLabel(nearer)} 分數 {nearerScore:0.###}，在最高分的 {tolerance}% 以內，約 {toMeters:N0} 米"
                            + $" ⇒ 改接 {nearer}，近 {fromMeters - toMeters:N0} 米。", tip);
            return nearer;
        }

        /// <summary>
        /// 標準模式：同一階級裡，跟排第一的任務在每一道已套用的排序上都同級的候選，改接最近的放到第一個。
        /// 已套用的排序：表格排序方式（開著時鍵值要相同）、沒金星優先、最低金星率門檻；沒套用的傳 null／false。
        /// </summary>
        private static List<T> PreferNearbyStandard<T>(List<T> candidates, Func<T, uint> idOf, string rank, bool tableSortUsed,
                                                        Dictionary<uint, int>? goldRank, Func<uint, int>? goldRateRank)
        {
            var distance = NearbyDistanceEstimator();
            if (distance == null || candidates.Count < 2)
                return candidates;

            var first = idOf(candidates[0]);
            bool SameClass(uint id)
                => (!tableSortUsed || modeSelect_TableInfo.SameTableSortKey(first, id))
                   && (goldRank == null || goldRank.GetValueOrDefault(first) == goldRank.GetValueOrDefault(id))
                   && (goldRateRank == null || goldRateRank(first) == goldRateRank(id));

            var equivalents = candidates.Skip(1).Select(idOf).Where(SameClass);
            if (!NearbyMissionPick.TryPickNearer(first, equivalents, distance, out var nearer, out var fromMeters, out var toMeters))
                return candidates;

            var index = candidates.FindIndex(c => idOf(c) == nearer);
            var picked = candidates[index];
            candidates.RemoveAt(index);
            candidates.Insert(0, picked);
            IceLogging.Info($"距離優先（{rank} 級）：原本先接 {MissionLabel(first)} 約 {fromMeters:N0} 米；"
                            + $"同級（階級、金星條件{(tableSortUsed ? "、表格排序值" : "")}都相同）的 {MissionLabel(nearer)} 約 {toMeters:N0} 米"
                            + $" ⇒ 改接 {nearer}，近 {fromMeters - toMeters:N0} 米。", "[FindMission: CheckStandard]");
            return candidates;
        }
    }
}
