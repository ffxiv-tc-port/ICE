using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.MechaOps;

namespace ICE.Scheduler.Tasks
{
    /// <summary>
    /// 緊急任務結束後回基地一次（設定 <c>C.ReturnToBaseAfterCritical</c>）。
    /// 契約：<see cref="NoteActiveMission"/> 記下「ICE 正在處理的任務是不是緊急任務」；所有結束路徑最後都回到
    /// <c>Task_CheckState</c> 的「沒有任務」分支，由 <see cref="Take"/> 取用並清掉，所以只觸發一次、與結束方式無關。
    /// 走到停止條件的路徑不會到那個分支，也就不回基地。
    /// 嘗試有上限：<see cref="GiveUpMs"/> 內沒回到據點範圍就放棄，照常接下一個任務。全部只在框架執行緒呼叫。
    /// </summary>
    internal static class Task_CriticalReturn
    {
        /// <summary>任務指令1：在宇宙探索區是「宇宙返回」（傳送到中控水晶）。</summary>
        private const uint DutyActionSlot1 = 26;

        // 交件事件收尾、讀條 3 秒、冷卻 5 秒都算進去，最多約可施放三次。
        private const long GiveUpMs = 30_000;

        // 兩次施放的最短間隔：讀條＋傳送轉場還沒結束前不重送。
        private const long CastGapMs = 8_000;

        private static uint armedMissionId;

        private static uint runningMissionId;
        private static long startedAt;
        private static long lastCastAt;
        private static int casts;
        private static string lastWait = "";

        internal static void Reset() => armedMissionId = 0;

        /// <summary>ICE 開始（或接手）處理一個進行中的任務時呼叫；非緊急任務（含查不到的 ID）會清掉先前的標記。</summary>
        internal static void NoteActiveMission(uint missionId)
            => armedMissionId = CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var info)
                                && info.Attributes.HasFlag(MissionAttributes.Critical) ? missionId : 0;

        /// <returns>true＝剛結束的是緊急任務而且設定開著，這一趟要回基地。不論結果都清掉標記。</returns>
        internal static bool Take(out uint missionId)
        {
            missionId = armedMissionId;
            armedMissionId = 0;
            return missionId != 0 && C.ReturnToBaseAfterCritical;
        }

        /// <param name="alsoDoing">同一趟要在基地做的其他事（給 log 用）；空字串＝只為了回基地。</param>
        internal static bool? Begin(uint missionId, string alsoDoing)
        {
            runningMissionId = missionId;
            startedAt = Environment.TickCount64;
            lastCastAt = 0;
            casts = 0;
            lastWait = "";
            var merged = alsoDoing.Length == 0 ? "" : $"同一趟還要：{alsoDoing}；";
            IceLogging.Info($"緊急任務 {missionId} 已結束，嘗試返回基地（{merged}最多 {GiveUpMs / 1000} 秒）。", "[Critical Return]");
            return true;
        }

        internal static unsafe bool? Step()
        {
            var now = Environment.TickCount64;
            var elapsedSec = (now - startedAt) / 1000.0;

            if (!Task_Repair.TryGetHubCenter(out var center))
                return GiveUp($"目前區域 {Player.Territory} 沒有登記據點座標（可能已經不在宇宙探索區）", elapsedSec);

            if (Player.DistanceTo(new Vector2(center.X, center.Z)) < Task_Repair.HubRadius)
            {
                IceLogging.Info($"緊急任務 {runningMissionId} 結束後已回到基地（{elapsedSec:F1} 秒，施放 {casts} 次）。", "[Critical Return]");
                return true;
            }

            if (now - startedAt > GiveUpMs)
                return GiveUp(casts == 0 ? $"一直沒有機會施放：{lastWait}" : $"施放後仍未回到據點範圍；最後狀態：{lastWait}", elapsedSec);

            // 機甲行動零自動化：判斷不出是否在事件中也當成在事件中。
            if (MechaOpsMonitor.GetParticipation(out var mechaReason) != MechaParticipation.NotParticipating)
            {
                lastWait = $"機甲事件（{mechaReason}）";
                return false;
            }

            if (Player.IsBusy)
            {
                lastWait = "角色忙碌中（讀條、移動、動畫、事件或戰鬥）";
                return false;
            }

            var am = ActionManager.Instance();
            if (am == null)
            {
                lastWait = "讀不到技能管理器";
                return false;
            }

            var status = am->GetActionStatus(ActionType.GeneralAction, DutyActionSlot1);
            if (status != 0)
            {
                lastWait = $"任務指令1 不可用（狀態碼 {status}，騎乘中 {Player.Mounted}）";
                return false;
            }

            if (lastCastAt != 0 && now - lastCastAt < CastGapMs)
                return false;

            lastCastAt = now;
            casts++;
            lastWait = am->UseAction(ActionType.GeneralAction, DutyActionSlot1) ? "已施放，等待傳送" : "施放被遊戲拒絕";
            return false;
        }

        private static bool? GiveUp(string reason, double elapsedSec)
        {
            IceLogging.Info($"緊急任務 {runningMissionId} 結束後返回基地放棄（{elapsedSec:F1} 秒，施放 {casts} 次）：{reason}。照常接下一個任務。", "[Critical Return]");
            return true;
        }
    }
}
