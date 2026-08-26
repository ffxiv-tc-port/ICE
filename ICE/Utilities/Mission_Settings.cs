using ICE.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Utilities
{
    internal static class Mission_Settings
    {
        // States that get set in the main Ui
        internal static bool StopAfterCurrent = false;
        internal static uint previouslyAbandoned = 0;

        // Gather Specifics
        internal static Vector2 previousMap = Vector2.Zero;
        internal static int nodeCounter = 0;

        // B2（cycleapple 65a5806b 機制改寫）：限量採集點的耗盡追蹤。
        // 我方採集選點走「就近挑點」(63de790)，不採用上游的環狀掃描；這裡只補上游缺的那半——
        // 精準記錄「哪些限量採集點已採光」，好在整條路線採光時提早收手（改走 ScoreCheck 驗分），
        // 而不是像原本 nodeTotal>=7（從未被遞增、恆為死碼）那樣只能靠逾時才停。
        internal static HashSet<uint> ExhaustedGatheringNodes = new();
        internal static bool GatheringNodesDepleted = false;
        private static int _nodeTotal = 0;

        // 🔑 nodeTotal 設 0 時一併清掉耗盡集合與旗標。換任務／換旗標一律走 ResetNodeCounter 或直接
        //    設 nodeTotal = 0，把「重置節點計數」與「重置耗盡狀態」綁在同一個賦值上，避免兩者漂移出
        //    不一致（節點計數歸零了、耗盡集合卻還殘留上一個任務的節點 id）。
        internal static int nodeTotal
        {
            get => _nodeTotal;
            set
            {
                _nodeTotal = value;
                if (value == 0)
                {
                    ExhaustedGatheringNodes.Clear();
                    GatheringNodesDepleted = false;
                }
            }
        }
        internal static uint item_collectableId = 0;
        internal static int CollectableStep = 0;
        internal static int NextCollectableStep = 0;
        internal static int SelectedRotation = 0;

        internal static Dictionary<string, uint> SkillUseAmount { get; set; } = new()
        {
            ["BoonIncrease2"] = 0,
            ["BoonIncrease1"] = 0,
            ["Tidings"] = 0,
            ["YieldII"] = 0,
            ["YieldI"] = 0,
            ["BountifulYieldII"] = 0,
            ["BonusIntegrityChance"] = 0,
            ["BonusIntegrity"] = 0,
            ["FieldMasteryIII"] = 0,
            ["FieldMasteryII"] = 0,
            ["FieldMasteryI"] = 0,
            ["FieldMasteryTemp"] = 0,
        };

        internal static bool Abandon = false;
        internal static bool AnimationLockAbandonState = false;
        internal static uint PossiblyStuck = 0;
        internal static uint StartJob = 0;

        internal static Vector3? NearestCollectionPoint = null;

        internal static TurninState TurninState = TurninState.None;

        internal static void ResetNodeCounter()
        {
            nodeCounter = 0;
            nodeTotal = 0;
        }
        internal static void ResetCollectableState()
        {
            CollectableStep = 0;
            NextCollectableStep = 0;
            item_collectableId = 0;
        }

        public static Dictionary<uint, int> missionAppearanceCounts = new Dictionary<uint, int>();
        public static int rerollThreshold = 3;
    }
}
