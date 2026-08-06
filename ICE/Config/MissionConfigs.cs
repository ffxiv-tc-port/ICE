using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace ICE.Config
{
    public class MissionConfigs : IYamlConfig
    {
        // Last edited version: 1
        public int ConfigVersion { get; set; } = 9;

        #region Safety Settings
        public bool StopOnAbort { get; set; } = true;
        public bool RejectUnknownYesno { get; set; } = true;
        public bool DelayGrabMission { get; set; } = true;
        public int DelayIncrease { get; set; } = 500;
        public bool DelayCraft { get; set; } = true;
        public int DelayCraftIncrease { get; set; } = 2500;
        public bool AnimationLockAbandon { get; set; } = true;
        public bool JumpIfStuck { get; set; } = false;

        // 任務優先度：同一階級之內優先挑「還沒拿到金星」的任務（補完成度用）。
        // 判定來源是 WKSManager.IsMissionGolded；全部拿完之後這個選項自然失去作用，
        // 排序會回到原本的順序。
        public bool PrioritizeUngoldedMissions { get; set; } = false;

        // 同一階級之內改用「表格設定 → 排序方式」的順序來挑任務（預設關閉＝維持遊戲清單順序）。
        // 與 PrioritizeUngoldedMissions 可以並用：先套表格排序，再把未金星的穩定排到前面。
        public bool UseTableSortForMissionOrder { get; set; } = false;

        // 連續重骰幾次都找不到可接任務就停下來（0 = 不限制，維持舊行為）。
        // 沒有這個上限時，只要候選池空了（例如開了「取得金星後自動停用」而目前
        // 可接的全都拿過金星），CheckReroll 就會無限重骰、卡在原地不會有任何提示。
        public int MaxConsecutiveRerolls { get; set; } = 10;

        #endregion

        #region Main Window

        public bool ShowInfoButton { get; set; } = true;
        public float MiddleColumnWidth { get; set; } = 1000f;
        public uint SelectedJob { get; set; } = 8;
        public bool XPRelicGrind { get; set; } = false;
        public bool XPRelicIgnoreManual { get; set; } = false;
        public bool XPRelicOnlyEnabled { get; set; } = false;

        /// <summary>
        /// 宇宙工具經驗模式是否也把「臨時任務」分頁（連續／時間限定／天氣限定）納入挑選。
        /// 預設關閉＝維持上游行為。資料面沒有障礙 —— 台服 7.20 的 544 個具名任務
        /// <b>全部</b>都有宇宙工具經驗獎勵（WKSMissionReward 逐筆核對過），限制純粹是
        /// 上游的挑選流程只開一般任務分頁。
        /// </summary>
        public bool XPRelicIncludeProvisional { get; set; } = false;

        /// <summary>宇宙工具經驗模式是否也把「緊急任務」分頁納入挑選。預設關閉＝維持上游行為。</summary>
        public bool XPRelicIncludeCritical { get; set; } = false;

        public bool ShowCritical { get; set; } = true;
        public bool ShowSequential { get; set; } = true;
        public bool ShowWeather { get; set; } = true;
        public bool ShowTimeRestricted { get; set; } = true;
        public bool ShowClassA { get; set; } = true;
        public bool ShowClassB { get; set; } = true;
        public bool ShowClassC { get; set; } = true;
        public bool ShowClassD { get; set; } = true;

        #endregion

        #region Overlay Settings

        public bool ShowOverlay { get; set; } = false;
        public bool ShowSeconds { get; set; } = false;
        public bool ShowTotalScore { get; set; } = true;
        public bool ShowExpBars { get; set; } = true;

        // 機甲行動技能範圍標示（Utilities/MechaOps）。預設關閉。
        public bool ShowMechaAoeOverlay { get; set; } = false;

        // 宇宙火焰噴射器（42258）的扇形角度（度）。遊戲資料裡沒有（Omen=0），
        // 預設 90°，待實機校準。
        public float MechaConeAngleDeg { get; set; } = 90f;

        // 個別技能顯示開關；沒有紀錄的 ActionId ＝ 開。
        public Dictionary<uint, bool> MechaAoeSkillToggles { get; set; } = new();

        // ---- 目標點位標示（Utilities/MechaOps/MechaTargets.cs）----
        // 形狀畫對了、卻看不到目標在哪，還是會對不準；這一組就是補這個缺口。
        // 預設開：這個功能的失敗形式是「該顯示的沒顯示」，比「多顯示」糟得多。
        public bool ShowMechaTargets { get; set; } = true;

        // 是否把每個目標的 hitbox 圈畫出來（命中判定用的是 hitbox，不是中心點）。
        public bool ShowMechaTargetHitbox { get; set; } = true;

        // 是否在目標旁邊標名字。目標一多會很吵，所以獨立開關，預設關。
        public bool ShowMechaTargetNames { get; set; } = false;

        // 涵蓋判定要不要把目標的 hitbox 半徑算進去。
        // 預設開（比照 BossmodReborn 的做法，理由寫在 MechaCoverage 的註解裡）；
        // 關掉＝退回中心點判定，也就是比較嚴格的那一邊。
        public bool MechaCoverageUseHitbox { get; set; } = true;

        // 列舉半徑（公尺）。60 是目前最長的機甲技能射程（42037 強力胡蘿蔔加農砲），
        // 所以預設值一定涵蓋得到任何打得到的東西。
        public float MechaTargetRadius { get; set; } = 60f;

        // 只列可選取（IsTargetable）的物件。關掉會連不可選取的一起畫出來——
        // 實機發現「該畫的沒畫」時的第一個排查開關。
        public bool MechaTargetsTargetableOnly { get; set; } = true;

        // 把其他玩家也畫出來。預設關（隊友不是攻擊目標，只會擋住畫面）。
        public bool MechaTargetsIncludePlayers { get; set; } = false;

        // 機甲行動狀態視窗（Ui/MechaOpsWindow）的三個子區塊。
        // 全部掛在 ShowMechaAoeOverlay 底下，總開關關著時整個視窗都不出現；
        // 子開關預設開啟，比照 MechaAoeSkillToggles「沒紀錄＝開」的風格。
        public bool ShowMechaCooldowns { get; set; } = true;
        public bool ShowMechaProcAlert { get; set; } = true;
        public bool ShowMechaEventStatus { get; set; } = true;

        // 事件進度（進度條／個人進度／貢獻／時間）。資料來自 WKSMechaEvent 的純量欄位，
        // 取樣端會先做指標範圍驗證，驗證不過就什麼都不顯示。
        public bool ShowMechaEventProgress { get; set; } = true;

        // ---- 目的指示標示（Utilities/MechaOps/MechaObjectives.cs）----
        // 把機甲事件自己的 map marker 畫成世界疊加層（有方向、有外框、不疊顏色）。
        // 比照其他子開關預設開，但整組仍然掛在 ShowMechaAoeOverlay 底下（該項預設關）。
        public bool ShowMechaObjectives { get; set; } = true;

        // 🔴 使用者裁決的前置：「先確認目標在不在 ObjectTable」。
        // 開著（預設）＝只畫在 ObjectTable 裡真的對上實體物件的標記，位置也跟著物件走。
        // 關掉＝連對不上的標記也畫在它自己的座標上（會用灰色「未確認」樣式）。
        // ⚠️ 預設不要改成 false：這個閘門同時也是「欄位偏移萬一失準」時的自動停用機制。
        public bool MechaObjectiveRequireObjectTable { get; set; } = true;

        // 標記座標要多近才算對上同一個東西（公尺，XZ 平面）。
        // 沒有官方資料，5 是猜的合理預設；做成滑桿讓實機自己調。
        public float MechaObjectiveMatchRadius { get; set; } = 5f;

        // 從玩家往目的指示畫一條方向線（NecroLens 風格：有方向、有外框、不疊顏色）。
        public bool ShowMechaObjectiveDirection { get; set; } = true;

        // 在目的指示旁邊標名稱與距離。
        public bool ShowMechaObjectiveNames { get; set; } = true;

        // 🔴🔴 部署閘門：預設 false。
        // 開啟＝改用 WKSMechaEvent.MapMarkerPtrs（一個 std::vector）來決定「哪些標記
        // 現在真的有效」。準確度較高，不會畫到上一階段留下的舊標記。
        // 代價是必須**解參考那個 vector 的後備儲存區**，而它在遊戲的堆積上——
        // First/Last 兩個欄位本身在已驗證的範圍內（讀它們沒有風險），但我們
        // **沒有任何辦法驗證 First 指向的那塊記憶體是不是還活著**。
        // 形狀檢查（null 對稱／差值是 8 的倍數／筆數 ≤ 30／對齊）是啟發式，不是範圍驗證。
        // 假設不成立的失敗形式是 AccessViolationException——那是 corrupted-state
        // exception，try/catch 與 HookSafety.ExecuteSafe 都攔不到，會直接把遊戲帶走。
        // 關著時走「掃 30 格純量」的路徑：一個指標都不解，最壞只是多畫到過期的標記，
        // 而「這個標記可能已過期」在疊加層與狀態視窗上都標示得出來。
        public bool MechaObjectiveUseMarkerVector { get; set; } = false;

        // ---- 右鍵選單（Utilities/MechaOps/MechaContextMenu.cs）----
        // 只在宇宙區域出現，且全部是純顯示項目（釘選標示／複製診斷）。
        public bool ShowMechaContextMenu { get; set; } = true;

        // ---- 隱私（Utilities/MechaOps/MechaPrivacy.cs）----
        // 其他玩家的角色名預設縮寫成「F. L.」，避免疊加層截圖與「複製記錄到剪貼簿」
        // 把別人的角色名帶出去。開啟＝顯示完整名稱。
        // ⚠️ 預設保守的那一邊是刻意的，改預設要由使用者裁決。
        public bool MechaShowFullPlayerNames { get; set; } = false;

        #endregion

        #region MissionSettings

        public bool OnlyGrabMission { get; set; } = false;
        public int TargetLevel { get; set; } = 10;
        public bool StopWhenLevel { get; set; } = false;
        public bool StopOnceHitCosmoCredits { get; set; } = false;
        public int CosmoCreditsCap { get; set; } = 30000;
        public bool StopOnceHitLunarCredits { get; set; } = false;
        public int LunarCreditsCap { get; set; } = 10000;
        public bool StopOnceHitCosmicScore { get; set; } = false;
        public int CosmicScoreCap { get; set; } = 500000;
        public bool StopOnceRelicFinished { get; set; } = false;
        public byte SequenceMissionPriority { get; set; } = 1;
        public byte WeatherMissionPriority { get; set; } = 2;
        public byte TimedMissionPriority { get; set; } = 3;
        public List<ProvisionalTypes> MissionPrio { get; set; } = new()
        {
            ProvisionalTypes.ProvisionalWeather,
            ProvisionalTypes.ProvisionalSequential,
            ProvisionalTypes.ProvisionalTimed
        };
        public bool GrindProvisionals { get; set; } = false;

        // 標準任務的階級挑選順序。原本 CheckStandard 裡是寫死的 { ExA, A, B, C, D }，
        // 拉出來讓使用者可以拖曳調整（例如想先刷低階把任務數衝上去）。
        // ⚠️ 讀取端一定要補上這裡缺少的階級，否則舊設定檔或手動編輯少了某一階，
        //    那一階的任務會永遠不被挑到 —— 靜默失效。見 Task_FindMission.RankOrder。
        public List<string> RankPrio { get; set; } = new() { "ExA", "A", "B", "C", "D" };
        public List<uint> JobPrio { get; set; } = new()
        {
            8, 9, 10, 11, 12, 13, 14, 15,  // Crafters: CRP, BSM, ARM, GSM, LTW, WVR, ALC, CUL
            16, 17, 18                     // Gatherers: MIN, BTN, FSH
        };
        public bool AutoSelectMoon { get; set; } = true;
        public bool ShowSinusMissions { get; set; } = true;
        public bool ShowPhaennaMissions { get; set; } = true;
        public bool RemoveAfterGold { get; set; } = false;
        public bool ShowExtraMissionInfo { get; set; } = true;
        public Dictionary<uint, uint> ScoreKeeper { get; set; } = new();

        #endregion

        #region Table Settings

        public int TableSortOption { get; set; } = 0;
        public bool HideUnsupportedMissions { get; set; } = false;
        public bool AutoPickCurrentJob { get; set; } = false;
        public bool ShowCompletionWindow { get; set; } = false;
        public bool ShowCompletionOnlyJob { get; set; } = false;
        public bool ShowSelectedJobOnly { get; set; } = false;
        public bool ShowCompletion_MissingGold { get; set; } = false;
        public bool ShowManualMode { get; set; } = false;
        public bool Auto_ShowTokens { get; set; } = true;

        #endregion

        #region Repair Settings

        public bool SelfRepairGather { get; set; } = true;
        public bool SelfRepairCrafter { get; set; } = false;
        public bool RepairAtVendor { get; set; } = false;
        public int RepairPercent { get; set; } = 50;
        public bool SelfSpiritbondGather { get; set; } = true;

        #endregion

        #region Gathering Settings
        public int SelectedGatherIndex { get; set; } = 0;
        public bool UseGatheringFood { get; set; } = false;
        public uint GatheringFood { get; set; } = 0;

        #region Cordial Settings

        public bool AutoCordial { get; set; } = false;
        public bool inverseCordialPrio { get; set; } = false;
        public int CordialMinGp { get; set; } = 0;
        public bool UseOnFisher { get; set; } = false;
        public bool PreventOvercap { get; set; } = false;
        public bool UseOnlyInMission { get; set; } = false;

        #endregion

        public List<GatherProfile> GatherSettings { get; set; } = new()
        {
            new GatherProfile { Id = 0, Name = "Defualt"},
        };

        public Dictionary<int, GatherProfile> GatherProfiles { get; set; } = new()
        {
            [0] = new GatherProfile() 
            { 
                Name = "Default",
            },
        };

        #endregion

        #region Gamba Settings

        // Gamba settings
        public List<Gamba> GambaItemWeights { get; set; } = new();
        public bool GambaEnabled { get; set; } = false;
        public bool GambaPreferSmallerWheel { get; set; } = false;
        public int GambaCreditsMinimum { get; set; } = 0;
        public int GambaDelay { get; set; } = 250;
        public bool GambaBetweenRuns = false;
        public int GambaAtAmount { get; set; } = 1000;

        #endregion

        #region Misc

        public bool MoonSprint { get; set; } = true;
        public uint MountId { get; set; } = 0;
        public string MountName { get; set; } = "Mount Roulette";
        public float MountRadius { get; set; } = 15.0f;
        public float DismountRadius { get; set; } = 7.0f;
        public bool UseMountOutsideMission { get; set; } = true;
        public bool UseMountInMission { get; set; } = true;
        public float LeftColumnWidth { get; set; } = 300f;
        public bool PlaySoundAlert { get; set; } = false;
        public float SoundVolume { get; set; } = 0.5f;
        public int TimeHistoryLimit { get; set; } = 100;
        public bool RemoveStellarStatus { get; set; } = false;
        public bool ShowSPM { get; set; } = false;

        #endregion

        #region Relic Settings
        
        public bool TurninRelic { get; set; } = false;
        public Dictionary<uint, bool> ClassesUnlocked { get; set; } = new()
        {
            [8] = true,
            [9] = true,
            [10] = true,
            [11] = true,
            [12] = true,
            [13] = true,
            [14] = true,
            [15] = true,
            [16] = true,
            [17] = true,
            [18] = true
        };

        #endregion

        #region Shopping List

        public Dictionary<uint, CosmoShoppingList> CosmoShopping { get; set; } = new();
        public List<uint> CosmoShoppingOrder { get; set; } = new();
        public bool BuyItems { get; set; } = false;
        public int CosmoBuyAtAmount { get; set; } = 10000;

        #endregion

        public Dictionary<uint, MissionSettings> MissionConfig { get; set; } = new();

        public List<MissionCommand> PostMissionCommands { get; set; } = new();

        #region Tab Hider

        public bool Show_StopWhen { get; set; } = true;
        public bool Show_GatheringProfile { get; set; } = true;
        public bool Show_MissionPriority { get; set; } = true;
        public bool Show_MiscSettings { get; set; } = true;
        public bool Show_HubActivities { get; set; } = true;

        #endregion

        #region Debug

        public bool FailsafeRecipeSelect { get; set; } = false;
        public bool UseDummyXp { get; set; } = false;
        public Dictionary<int, CosmicHelper.XPType> DummyXP { get; set; } = new()
        {
            { 1, new CosmicHelper.XPType { CurrentXP = 0, NeededXP = 100} },
            { 2, new CosmicHelper.XPType { CurrentXP = 50, NeededXP = 200} },
            { 3, new CosmicHelper.XPType { CurrentXP = 100, NeededXP = 300} },
            { 4, new CosmicHelper.XPType { CurrentXP = 150, NeededXP = 400} },
            { 5, new CosmicHelper.XPType { CurrentXP = 200, NeededXP = 500} },
        };
        public uint PictoColor_Circle { get; set; } = 2616716297;
        public uint PictoColor_Dot { get; set; } = 2616716297;
        public uint PictoColor_Cone { get; set; } = 0;
        public bool UseDummyRanks { get; set; } = false;
        public bool ShowDummyA { get; set; } = false;
        public bool ShowDummyB { get; set; } = false;
        public bool ShowDummyC { get; set; } = false;
        public bool ShowDummyD { get; set; } = false;

        public bool DisablePathfindingToRedAlert { get; set; } = false;
        public bool ShowDebugGatherInfo { get; set; } = false;
        public string AuthorName { get; set; } = "Puni.sh Community";
        public string CustomRoutePath { get; set; } = string.Empty;


        #endregion

        #region Yaml Save Stuff

        public static string ConfigPath => Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "Mission Config.yaml");
        private static CancellationTokenSource? _saveCts;
        private static readonly object _saveLock = new();

        // Standard save. Deliberately routed through the debounced path.
        //
        // This used to be a bare fire-and-forget Task.Run with no serialisation
        // of any kind, while its sibling SaveDebounced already had both a lock
        // and cancellation. Observed live on TC 2026-07-29: plugin startup
        // issued hundreds of Save() calls within three seconds (one per mission
        // being constructed) and every one of them raced on the same file -
        // 527 IOExceptions in 3s ("The process cannot access the file ...
        // because it is being used by another process"), i.e. 527 LOST writes,
        // not merely 527 noisy log lines.
        //
        // Serialising them would not have been enough on its own: this config
        // is ~330 KB, so 527 queued writes means re-serialising and rewriting
        // ~170 MB during startup. Debouncing collapses a burst into one write.
        // No caller can observe the difference - Save() was already
        // asynchronous and returned long before the write happened. Anything
        // that genuinely needs the bytes on disk before continuing already has
        // SaveSync().
        public void Save() => SaveDebounced();

        // Debounced save for rapid operations
        public void SaveDebounced(int delayMs = 500)
        {
            lock (_saveLock)
            {
                _saveCts?.Cancel();
                _saveCts = new CancellationTokenSource();
                var cts = _saveCts;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delayMs, cts.Token);
                        await SaveAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Newer save cancelled this one
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Failed to save MissionConfigs: {ex}");
                    }
                });
            }
        }

        // Core async implementation
        public async Task SaveAsync() => await YamlConfig.SaveAsync(this, ConfigPath);

        // Synchronous for migrations/critical paths
        public void SaveSync() => YamlConfig.SaveSync(this, ConfigPath);

        #endregion
    }

    public class MissionSettings
    {
        public bool Enabled { get; set; } = false;
        public bool ManualMode { get; set; } = false;
        public int GatherProfileId { get; set; } = 0;
        public int GProfileId { get; set; } = 0;
        public bool AutoTurnin { get; set; } = true;
        public bool TurninGold { get; set; } = false;
        public bool TurninSilver { get; set; } = false;
        public bool TurninBronze { get; set; } = false;
        public bool Use_BuildinPreset { get; set; } = false;
        public string AutoHookPresetName { get; set; } = string.Empty;
        public double BestTime { get; set; } = double.MaxValue;
        public double AverageTime { get; set; } = 0;
        public double AverageBronzeTime { get; set; } = 0;
        public double AverageSilverTime { get; set; } = 0;
        public double AverageGoldTime { get; set; } = 0;
        public double AverageCriticalTime { get; set; } = 0;
        public int TotalCompletions { get; set; } = 0;
        public int BronzeCompletion { get; set; } = 0;
        public int SilverCompletions { get; set; } = 0;
        public int GoldCompletions { get; set; } = 0;
        public int CriticalCompletions { get; set; } = 0;
        public int FailedCounters { get; set; } = 0;
        public List<TurninData> TurninRecords { get; set; } = new();
        // Old References to time below for migration
        [YamlIgnore]
        public List<double> Times { get; set; } = new();
    }

    public class TurninData
    {
        public double Time { get; set; }
        public TurninState State { get; set; }
    }

    public class GatherProfile
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int MinimumGp { get; set; } = -1;
        public int DualClassCraftAmount { get; set; } = 1;
        public GatherBuffs GatherBuffs { get; set; } = new();
    }

    public class GatherBuff
    {
        public bool Enabled { get; set; } = false;
        public int MinGp { get; set; }
        public int MaxUse { get; set; } = -1;
    }

    public class GatherBuffs
    {
        public Dictionary<string, GatherBuff> Buffs { get; set; } = new()
        {
            ["BoonIncrease2"] = new() { MinGp = 100 },
            ["BoonIncrease1"] = new() { MinGp = 50 },
            ["Tidings"] = new() { MinGp = 200 },
            ["YieldII"] = new() { MinGp = 500 },
            ["YieldI"] = new() { MinGp = 400 },
            ["BountifulYieldII"] = new() { MinGp = 100 },
            ["BonusIntegrity"] = new() { MinGp = 300 },
            ["BonusIntegrityChance"] = new() { Enabled = true, MinGp = 0 },
            ["FieldMasteryIII"] = new() { MinGp = 250 },
            ["FieldMasteryII"] = new() { MinGp = 100 },
            ["FieldMasteryI"] = new() { MinGp = 50 },
            ["FieldMasteryTemp"] = new() { MinGp = 50},
        };

        public int BountifulMinItem { get; set; } = 4;
    }

    public class Gamba
    {
        public uint ItemId { get; set; }
        public int Weight { get; set; } = 0;
        public GambaType Type { get; set; }
    }

    public class CosmoShoppingList
    {
        public int KeepAmount { get; set; } = 0;
        public int BuyAmount { get; set; } = 0;
        public bool KeepBuying { get; set; } = false;
    }

    public class MissionCommand
    {
        public required string command { get; set; }
        public int Delay { get; set; } = 0;
    }
}
