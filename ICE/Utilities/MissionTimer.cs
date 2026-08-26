using ICE.Config;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Diagnostics;
using static ICE.Ui.MainUi.ModeSelect.modeSelect_TableInfo;

public class MissionTimer
{
    private Stopwatch stopwatch;
    private uint currentMission;
    private bool isRunning;

    public int TimeHistoryLimit => C.TimeHistoryLimit;

    public MissionTimer()
    {
        stopwatch = new Stopwatch();
    }

    public void StartMission(uint missionId)
    {
        // 🔴 0 不是任何任務的 id（SheetMissionDict 是 1..544）。讓它進來就會在
        //    C.MissionConfig 裡長出一個 key 0，而且是**存進設定檔的**——
        //    之後每一個「迭代 MissionConfig 再去索引 SheetMissionDict」的地方都會被它炸掉。
        if (missionId == 0)
        {
            IceLogging.Warning("忽略用任務 ID 0 啟動計時器的要求。", "[Mission Timer]");
            return;
        }

        currentMission = missionId;
        stopwatch.Restart();
        isRunning = true;
    }

    public TimeSpan CompleteMission()
    {
        if (!isRunning)
        {
            return TimeSpan.Zero;
        }

        stopwatch.Stop();
        var duration = stopwatch.Elapsed;
        isRunning = false;

        UpdateMissionStats(currentMission, duration);
        currentMission = 0;

        return duration;
    }

    private void UpdateMissionStats(uint missionId, TimeSpan duration)
    {
        // 見 StartMission 的說明：key 0 會被寫進設定檔，之後炸在別人身上。
        if (missionId == 0)
        {
            IceLogging.Warning("忽略任務 ID 0 的完成統計。", "[Mission Timer]");
            return;
        }

        if (!C.MissionConfig.ContainsKey(missionId))
        {
            C.MissionConfig[missionId] = new();
        }

        var stats = C.MissionConfig[missionId];

        // Adding the new time entry here
        stats.TurninRecords.Add(new TurninData
        {
            Time = duration.TotalSeconds,
            State = Mission_Settings.TurninState,
        });

        if (Mission_Settings.TurninState == TurninState.Bronze)
            stats.BronzeCompletion++;
        else if (Mission_Settings.TurninState == TurninState.Silver)
            stats.SilverCompletions++;
        else if (Mission_Settings.TurninState == TurninState.Gold)
            stats.GoldCompletions++;
        else if (Mission_Settings.TurninState == TurninState.Critical)
            stats.CriticalCompletions++;

        // Increment total completions (always tracks full history)
        stats.TotalCompletions++;

        // Apply time history limit per state if set
        if (TimeHistoryLimit > 0)
        {
            // Group records by state
            var groupedByState = stats.TurninRecords
                .GroupBy(t => t.State)
                .ToList();

            // Keep only the most recent TimeHistoryLimit records for each state
            var trimmedRecords = new List<TurninData>();
            foreach (var group in groupedByState)
            {
                trimmedRecords.AddRange(group.TakeLast(TimeHistoryLimit));
            }

            stats.TurninRecords = trimmedRecords;
        }

        // Calculate stats based on the (possibly limited) time history
        if (stats.TurninRecords.Any())
        {
            stats.BestTime = stats.TurninRecords.Min(t => t.Time);
            stats.AverageTime = stats.TurninRecords.Average(t => t.Time);

            // Calculate per-state averages
            var bronzeRecords = stats.TurninRecords.Where(t => t.State == TurninState.Bronze).ToList();
            var silverRecords = stats.TurninRecords.Where(t => t.State == TurninState.Silver).ToList();
            var goldRecords = stats.TurninRecords.Where(t => t.State == TurninState.Gold).ToList();
            var criticalRecords = stats.TurninRecords.Where(t => t.State == TurninState.Critical).ToList();

            stats.AverageBronzeTime = bronzeRecords.Any() ? bronzeRecords.Average(t => t.Time) : 0;
            stats.AverageSilverTime = silverRecords.Any() ? silverRecords.Average(t => t.Time) : 0;
            stats.AverageGoldTime = goldRecords.Any() ? goldRecords.Average(t => t.Time) : 0;
            stats.AverageCriticalTime = criticalRecords.Any() ? criticalRecords.Average(t => t.Time) : 0;
        }

        C.Save();
    }

    public void ResetTimers(uint missionId)
    {
        // 見 StartMission 的說明：這裡也會 new 出一筆並存檔，一樣要擋 0。
        if (missionId == 0)
            return;

        if (!C.MissionConfig.ContainsKey(missionId))
        {
            C.MissionConfig[missionId] = new();
        }

        var stats = C.MissionConfig[missionId];
        stats.TurninRecords.Clear();
        stats.BestTime = double.MaxValue;

        stats.AverageTime = 0;
        stats.AverageBronzeTime = 0;
        stats.AverageSilverTime = 0;
        stats.AverageGoldTime = 0;
        stats.AverageCriticalTime = 0;

        stats.TotalCompletions = 0;
        stats.BronzeCompletion = 0;
        stats.SilverCompletions = 0;
        stats.GoldCompletions = 0;
        stats.CriticalCompletions = 0;
        stats.FailedCounters = 0;
        // 與 FailedCounters 成對：「重設統計」要把放棄耗時一起清掉，
        // 否則清空次數卻留著時間，效率指標會被一筆對不上任何記錄的成本壓著。
        stats.AbandonedTimeSeconds = 0;

        C.Save();
    }

    public static class MissionStatsCalculator
    {
        public static double CalculateCurrencyPerMinute(double averageTimeSeconds, uint baseScore, double multiplier)
        {
            if (averageTimeSeconds <= 0) return 0;

            return (60.0 * baseScore * multiplier) / averageTimeSeconds;
        }
        public static double CalculateAverageSequenceScorePerMinute(uint id, int multiplier)
        {
            double totalTimeSeconds = 0;
            double totalScore = 0;

            List<uint> sequenceMissions = new();
            sequenceMissions = GetOnlyPreviousMissionsRecursive(id);
            sequenceMissions.Add(id);

            foreach (var missionId in sequenceMissions)
            {
                if (C.MissionConfig.TryGetValue(missionId, out var mission))
                {
                    // If any mission has invalid time, return 0
                    if (mission.AverageTime <= 0) return 0;

                    if (CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var sheetInfo))
                    {
                        totalTimeSeconds += mission.AverageTime;
                        totalScore += sheetInfo.ClassScore * multiplier;
                    }
                    else
                    {
                        return 0;
                    }
                }
            }

            if (totalTimeSeconds <= 0) return 0;

            double totalTimeMinutes = totalTimeSeconds / 60.0;
            return totalScore / totalTimeMinutes;
        }

        public static double CalculateActualScorePerMinute(List<TurninData> turninRecords, uint baseScore)
        {
            if (turninRecords.Count == 0) return 0;

            // Count each turnin type
            int bronzeCount = turninRecords.Count(t => t.State == TurninState.Bronze);
            int silverCount = turninRecords.Count(t => t.State == TurninState.Silver);
            int goldCount = turninRecords.Count(t => t.State == TurninState.Gold);

            // Calculate total score earned
            double totalScore = (bronzeCount * baseScore * 1.0) +
                               (silverCount * baseScore * 4.0) +
                               (goldCount * baseScore * 5.0);

            // Calculate total time spent (in minutes)
            double totalTimeMinutes = turninRecords.Sum(t => t.Time) / 60.0;

            if (totalTimeMinutes <= 0) return 0;

            return totalScore / totalTimeMinutes;
        }

        /// <summary>
        /// 實測「有效」每分成果：加權總分 ÷（交件總耗時 ＋ 放棄總耗時）。
        /// </summary>
        /// <remarks>
        /// 與 <see cref="CalculateActualScorePerMinute"/> 的唯一差別是分母含放棄耗時。
        /// 刷職業成果時「金星不可達就自動放棄」的耗時是迴圈裡的真實成本，不計入會系統性
        /// 高估「不穩金星」的任務。<br/>
        /// ⚠️ 這是**新方法**，不是改既有那個 —— 既有的 <c>CalculateActualScorePerMinute</c>
        /// 有顯示端呼叫者，動它等於改既有行為。<br/>
        /// 🔴 這個值只用於**排序偏好與顯示**，任何放棄／交件的決策都不可以讀它。<br/>
        /// 🔴 <paramref name="abandonedTimeSeconds"/> 是**生涯累計**，但
        /// <paramref name="turninRecords"/> 會被 <c>TimeHistoryLimit</c>（預設 100／每個星級）
        /// 裁掉舊記錄。兩者直接相除的話，分子有上限而分母無限成長，這個指標會隨時間**單向衰減**，
        /// 看起來像任務自己變差了 —— 失敗形式完全靜默。所以這裡先把放棄耗時依「保留下來的
        /// 交件比例」縮放，讓分子分母落在同一個時間窗上。沒發生裁切時比例是 1，結果不變。
        /// </remarks>
        /// <param name="totalCompletions">
        /// 生涯交件次數（<c>MissionSettings.TotalCompletions</c>，不會被裁切）。
        /// 用來推算 <paramref name="turninRecords"/> 保留了生涯的多少比例。
        /// </param>
        public static double CalculateEffectiveScorePerMinute(List<TurninData> turninRecords, uint baseScore, double abandonedTimeSeconds, int totalCompletions)
        {
            if (turninRecords == null || turninRecords.Count == 0) return 0;

            int bronzeCount = turninRecords.Count(t => t.State == TurninState.Bronze);
            int silverCount = turninRecords.Count(t => t.State == TurninState.Silver);
            int goldCount = turninRecords.Count(t => t.State == TurninState.Gold);

            // 乘數與 Task_TurninMission.UpdateScoreInfo 的實得成果模型一致：金 5／銀 4／其他 1。
            double totalScore = (bronzeCount * baseScore * 1.0) +
                               (silverCount * baseScore * 4.0) +
                               (goldCount * baseScore * 5.0);

            // 負值不可能，但設定檔是使用者可編輯的文字檔，夾一下比較安全。
            var abandoned = abandonedTimeSeconds > 0 ? abandonedTimeSeconds : 0;

            // 把生涯的放棄耗時縮放到「目前保留的交件記錄」這個時間窗。
            // totalCompletions <= 保留筆數（含舊設定檔還沒累積到、或計數對不上的情況）一律視為
            // 沒有裁切 —— 比例夾在 1 以內，絕不放大成本。
            if (totalCompletions > turninRecords.Count)
                abandoned *= (double)turninRecords.Count / totalCompletions;

            double totalTimeMinutes = (turninRecords.Sum(t => t.Time) + abandoned) / 60.0;

            if (totalTimeMinutes <= 0) return 0;

            return totalScore / totalTimeMinutes;
        }

        public static double CalculateActualScorePerHour(List<TurninData> turninRecords, uint baseScore)
        {
            if (turninRecords.Count == 0) return 0;

            // Count each turnin type
            int bronzeCount = turninRecords.Count(t => t.State == TurninState.Bronze);
            int silverCount = turninRecords.Count(t => t.State == TurninState.Silver);
            int goldCount = turninRecords.Count(t => t.State == TurninState.Gold);

            // Calculate total score earned
            double totalScore = (bronzeCount * baseScore * 1.0) +
                               (silverCount * baseScore * 4.0) +
                               (goldCount * baseScore * 5.0);

            // Calculate total time spent (in hours)
            double totalTimeHours = turninRecords.Sum(t => t.Time) / 3600.0;

            if (totalTimeHours <= 0) return 0;

            return totalScore / totalTimeHours;
        }
    }

    public void AbandonMission()
    {
        stopwatch.Stop();
        // 🔴 耗時要在 Reset() **之前**取走 —— Reset() 會把 Elapsed 歸零，
        //    原本的寫法等於把放棄花掉的時間直接丟掉。
        var abandonedSeconds = stopwatch.Elapsed.TotalSeconds;
        stopwatch.Reset();
        isRunning = false;

        // 🔴 這裡就是 key 0 的來源：任務已經結束（currentMission 早被 CompleteMission
        //    歸零）之後又走一次放棄流程，就會 new 出 C.MissionConfig[0] 並存檔。
        //    實測使用者的設定檔裡 key 0 的 failedCounters 已經累積到 118。
        if (currentMission == 0)
        {
            IceLogging.Warning("放棄任務時沒有正在計時的任務，略過失敗計數。", "[Mission Timer]");
            return;
        }

        if (!C.MissionConfig.ContainsKey(currentMission))
        {
            C.MissionConfig[currentMission] = new();
        }

        var stats = C.MissionConfig[currentMission];
        stats.FailedCounters++;
        // 放棄耗時累加進成本。key 0 的防護在上面，走到這裡的一定是真任務。
        stats.AbandonedTimeSeconds += abandonedSeconds;
        C.Save();
        currentMission = 0;
    }
}