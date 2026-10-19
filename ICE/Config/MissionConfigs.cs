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

        /// <summary>
        /// 遇到「文字對不上預期」的確認框時要怎麼處理。預設 <c>Decline</c>＝比對文字、認不出來就按取消。
        /// </summary>
        /// <remarks>
        /// 🔴 這是<b>另一個</b>旗標，不是把新地方掛到 <see cref="RejectUnknownYesno"/> 上：
        /// 那個旗標預設就是 <c>true</c>（既有使用者都開著），把新的檢查掛上去等於未經同意
        /// 改了所有人的行為。<br/>
        /// 預設只對新安裝（或設定檔沒有這個鍵）生效；存過 <c>AlwaysConfirm</c> 的人刻意不遷移，要自己切。
        /// </remarks>
        public UnexpectedYesnoAction UnexpectedYesno { get; set; } = UnexpectedYesnoAction.Decline;

        // 任務優先度：同一階級之內優先挑「還沒拿到金星」的任務（補完成度用）。
        // 判定來源是 WKSManager.IsMissionGolded；全部拿完之後這個選項自然失去作用，
        // 排序會回到原本的順序。
        public bool PrioritizeUngoldedMissions { get; set; } = false;

        // 同一階級之內改用「表格設定 → 排序方式」的順序來挑任務（預設關閉＝維持遊戲清單順序）。
        // 與 PrioritizeUngoldedMissions 可以並用：先套表格排序，再把未金星的穩定排到前面。
        public bool UseTableSortForMissionOrder { get; set; } = false;

        // 挑任務時要求的最低金星率（百分比；0 = 關閉）。金星率＝GoldCompletions ÷（TotalCompletions + FailedCounters）。
        // ⚠️ 只在 UseTableSortForMissionOrder 開啟的挑任務路徑上生效，而且只是把不達標的任務排到同階級隊尾，
        //    不是把它從候選池移除（移除會把候選池掏空，觸發 CheckReroll 連續重骰）。
        // ⚠️ 嘗試次數 < MinGoldRateSampleSize 的任務一律視為達標；表格顯示不受這個門檻影響。
        public int MinGoldRatePercentForPicking { get; set; } = 0;

        // 上面那個門檻開始生效所需的最少嘗試次數。刻意不做成 UI 選項（避免再多一顆旋鈕），
        // 但留成具名常數而不是散在程式碼裡的魔術數字。
        public const int MinGoldRateSampleSize = 3;

        // 連續重骰幾次都找不到可接任務就停下來（0 = 不限制，維持舊行為）。
        // 沒有這個上限時，只要候選池空了（例如開了「取得金星後自動停用」而目前
        // 可接的全都拿過金星），CheckReroll 就會無限重骰、卡在原地不會有任何提示。
        public int MaxConsecutiveRerolls { get; set; } = 10;

        // 重骰達到上限（＝目前職業的候選池空了）時，先照「職業優先度」JobPrio 找下一個
        // 還有未金星任務的職業換過去再試，全部都試過才停止。預設關閉＝維持現行行為（直接停止）。
        // ⚠️ 自動換職業會真的動到裝備（EquipGearset），是明顯的行為改變，所以不預設開啟。
        // ⚠️ 只在標準任務流程生效：宇宙工具經驗模式與臨時任務連刷各自有獨立的候選池，
        //    根本走不到重骰上限這個判斷點（臨時任務連刷本來就自己會照 JobPrio 換職業）。
        public bool AutoSwitchJobWhenPoolEmpty { get; set; } = false;

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
        /// 預設關閉＝維持上游行為。資料面沒有障礙，限制純粹是
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

        // 🔑 三個**全部預設開**＝現行版面零改變。這一組要解決的是「我不想看這一塊」，
        //    不是要改預設長相；預設值一改就變成「未經同意動了所有人的畫面」。
        // ⚠️ 關掉的是**畫**，不是算：資料本來就是每幀現查的，不存在「關掉省了什麼」的副作用；
        //    反過來說也不會因為關掉而讓別的功能少拿到東西。

        /// <summary>疊加層的「天氣預報」那一列（目前天氣 → 下一個天氣 → 還有多久）。</summary>
        public bool ShowOverlayWeather { get; set; } = true;

        /// <summary>疊加層的「時間限定任務」那一列（這個小時／下個小時的職業圖示）。</summary>
        public bool ShowOverlayTimedMissions { get; set; } = true;

        /// <summary>
        /// 疊加層的職業成果進度條（目前任務對應職業的宇宙工具經驗條）。
        /// ⚠️ 跟 <see cref="ShowTotalScore"/>（總成果那一條）是兩件事，各自有開關。
        /// </summary>
        public bool ShowOverlayJobScore { get; set; } = true;

        // 機甲行動技能範圍標示（Utilities/MechaOps）。預設關閉。
        public bool ShowMechaAoeOverlay { get; set; } = false;

        // 🔑 關掉就回到舊的獨立視窗。兩者不會同時出現：獨立視窗的顯示條件會問
        //    「主視窗是不是真的會畫到它」（見 MechaOpsWindow.MergedIntoOverlay）。
        // ⚠️ 主視窗自己的開關 ShowOverlay 預設是**關**的——所以沒開主視窗的人
        //    仍然會拿到獨立視窗，這個合併不會讓任何人的功能憑空消失。
        public bool ShowMechaInOverlay { get; set; } = true;

        // 下面兩個是舊鍵：值已由 ConfigMigrator.MigrateMechaGlobalSlidersToPerSkill 搬進
        //      MechaShapeOverrides[42258].AngleDeg / [42150].Primary，效果值逐一相同。
        //      欄位刻意留著不刪：①舊設定檔還原得回來 ②遷移本身要讀它們。
        //      ⚠️ 新碼一律走 MechaActionShapes.ConeAngleFor()／TryResolve()，不要再讀這兩個鍵——
        //      讀了會靜默忽略 per-skill 覆蓋（失敗形式是「滑桿沒作用」）。

        // 舊鍵：宇宙火焰噴射器（42258）的扇形**全**角（度）。遊戲資料裡沒有（Omen=0），240 是實機校準的起點。
        public float MechaConeAngleDeg { get; set; } = 240f;

        // 宇宙鑽頭（42150）的矩形長度（公尺）。
        // ⚠️ 這是**校準**不是資料修正：真值仍未知（沒有命中/未命中的地面真相），
        //    所以做成滑桿（2~10）讓使用者自己收斂；拉回 7 就是退回 Lumina 原值。
        // 📌 半寬（XAxisModifier/2 = 2.5）沒有任何回報指出不準，維持走 Lumina 不開設定。
        public float MechaDrillLength { get; set; } = 4f;

        // ---- per-skill 形狀覆蓋（Utilities/MechaOps/MechaActionShapes.cs）----
        // 技能 id → 各維度的覆蓋值。**預設空字典＝完全沿用現行有效值**
        // （內建校準值或 Lumina 原值），所以升級不會改變任何行為。
        // 🔑 讀取優先序：per-skill 覆蓋 > 內建校準值（MechaActionShapes 的常數）> Lumina 原值。
        // ⚠️ null ＝「這個維度不覆蓋」，不是 0（理由見 MechaShapeOverride）。
        public Dictionary<uint, MechaShapeOverride> MechaShapeOverrides { get; set; } = new();

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
        // ⚠️ 有的目標（例：有害菌床）既沒有名字也不可選取，會被這個旗標整批擋掉；關掉之後的雜訊由下一項過濾。
        public bool MechaTargetsTargetableOnly { get; set; } = true;

        // 上面那項關掉之後，是否仍然排除**已知的**雜訊（NPC、以太之光、採集點、房屋、
        // 區域、過場、卡牌台，以及不可選取又沒有名字的場景裝飾）。
        // 🔑 判準是「已知是雜訊」而不是「不像目標」——漏掉的照樣顯示，失敗方向是安全的。
        // ⚠️ 預設 true：這一項是為了修「關掉可選取過濾就整片灌進來」而加的，
        //    預設不生效等於沒修。要看到**全部**物件（舊行為）把它關掉即可。
        public bool MechaTargetsHideSceneryAndNpcs { get; set; } = true;

        // 把其他玩家也畫出來。預設關（隊友不是攻擊目標，只會擋住畫面）。
        public bool MechaTargetsIncludePlayers { get; set; } = false;

        // ---- 身份分流（Utilities/MechaOps/MechaObjectives.cs 的 HiddenByRoleGate）----
        // 顯示「另一個身份」的目標。**預設 false ＝分流生效**（協助員不會看到駕駛員的巨型目標、反之亦然）。
        // 🔑 分流**只影響畫不畫**：分級、標記學習、錄製器全部照舊看得到全家族。
        // ⚠️ 判不出身份或判不出歸屬時一律不分流（全部顯示）——失敗方向必須是「多顯示」。
        public bool MechaShowOtherRoleTargets { get; set; } = false;

        // 機甲行動狀態視窗（Ui/MechaOpsWindow）的三個子區塊。
        // 全部掛在 ShowMechaAoeOverlay 底下，總開關關著時整個視窗都不出現；
        // 子開關預設開啟，比照 MechaAoeSkillToggles「沒紀錄＝開」的風格。
        public bool ShowMechaCooldowns { get; set; } = true;
        public bool ShowMechaProcAlert { get; set; } = true;
        public bool ShowMechaEventStatus { get; set; } = true;

        // 事件進度（進度條／個人進度／貢獻／時間）。資料來自 WKSMechaEvent 的純量欄位，
        // 取樣端會先做指標範圍驗證，驗證不過就什麼都不顯示。
        public bool ShowMechaEventProgress { get; set; } = true;

        // 🔑 全部預設開＝現行版面零改變。ShowMechaEventProgress 是整組的總開關，關著時這幾個細項一律不生效。
        // ⚠️ 「目的指示那一列」（ShowMechaRowObjectives）與世界疊加層上的目的指示圈（ShowMechaObjectives）是**兩件事**，
        //    刻意不綁在一起：想關掉視窗那一行的人不該連地上的圈一起弄不見。
        public bool ShowMechaRowEventProgress { get; set; } = true;
        public bool ShowMechaRowPersonalProgress { get; set; } = true;
        public bool ShowMechaRowContribution { get; set; } = true;
        public bool ShowMechaRowEventEnd { get; set; } = true;
        public bool ShowMechaRowSignupEnd { get; set; } = true;
        public bool ShowMechaRowTeleportEnd { get; set; } = true;
        public bool ShowMechaRowObjectives { get; set; } = true;

        // ---- 事件排程（Utilities/MechaOps/MechaSchedule.cs）----
        // 「下次機甲事件：<名稱> HH:mm（N 分後）」。
        // 資料來自 WKSMechaEventModule._events 這個**內嵌**陣列的純量欄位，
        // 一個指標都不用解（比既有的 CurrentEvent 路徑更安全），所以比照其他子開關預設開。
        public bool ShowMechaSchedule { get; set; } = true;

        // ---- 駕駛申請書持有狀態 ----
        // 「駕駛申請書：持有／無／?」一列。
        // ⚠️ 這一列會讓機甲區塊在「現在沒有任何事件」時也有東西可畫，因此在宇宙區域裡
        //    區塊幾乎總是看得見。那是刻意的：這一列的價值就在事件開始之前。
        //    整組仍然掛在 ShowMechaAoeOverlay 底下（該項預設關），沒開機甲功能的人不受影響。
        public bool ShowMechaPilotTicket { get; set; } = true;

        // ---- 報名視窗開啟通知（Utilities/MechaOps/MechaSignupNotifier.cs）----
        // 報名視窗一開就在聊天視窗講一次「報名已開放：<事件名> — 報名截止 HH:mm」。
        // 🔴🔴 純通知：不按任何視窗、不送任何指令，也刻意沒有「一鍵報名」的入口——機甲行動零自動化是紅線。
        // ⚠️ 預設 false：這是會主動在聊天視窗發言的功能，沒開口要的人不該被它打擾。
        //    設定是 YamlDotNet（缺鍵就吃這裡的初始值），所以既有使用者升級之後這一項同樣是關著的。
        public bool NotifyMechaSignupOpen { get; set; } = false;

        // 🔴🔴 部署閘門：預設 false。開啟＝顯示緊急事件（紅色警報：磁暴／流星雨／孢子霧）的類型與剩餘時間。
        // 資料源 AgentWKSAnnounce.Data 是一塊**我們沒有辦法驗證大小**的堆積配置（CS 宣告 Size = 0xA8 是照國際服的佈局）；
        // 台服的配置若比較小，讀 +0xA0 的 State 就是越界，而 AccessViolationException 攔不到。
        // ⚠️ 要改成預設開，必須先有實機證據（開著跑過一輪磁暴而沒有崩潰）。處置與 MechaObjectiveUseMarkerVector 相同。
        public bool ShowMechaEmergency { get; set; } = false;

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

        // 🔴🔴 部署閘門：預設 false。開啟＝改用 WKSMechaEvent.MapMarkerPtrs（一個 std::vector）來決定「哪些標記
        // 現在真的有效」，不會畫到上一階段留下的舊標記。
        // 形狀檢查（null 對稱／差值是 8 的倍數／筆數 ≤ 30／對齊）是啟發式，不是範圍驗證；
        // 假設不成立的失敗形式是 AccessViolationException（攔不到，會直接把遊戲帶走）。
        // 關著時走「掃 30 格純量」的路徑：一個指標都不解，最壞只是多畫到過期的標記（畫面上會標示）。
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

        // 「本區、目前所選職業的普通任務全部拿到金評就停」。判別「普通任務」＝排除 Critical
        // 與三種 Provisional（限時／天候／連續）；金評旗標一律走 MissionStatusHelper 原生讀取
        // （CustomCs.cs），不重新引入已移除的 WKSManagerCustom。預設 false＝維持現行行為。
        public bool StopOnceStandardMissionsGolded { get; set; } = false;

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

        // 標準任務的階級挑選順序（使用者可以拖曳調整，例如想先刷低階把任務數衝上去）。
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

        // 「取得金星後排除任務」對緊急任務網開一面。
        // 🔑 預設 false ＝ 現行行為完全不變；要例外的人自己去勾。
        // 判別碼是 MissionAttributes.Critical（源自 WKSMissionUnit.IsSpecialQuest）。
        public bool RemoveAfterGoldKeepCritical { get; set; } = false;

        /// <summary>
        /// 製作任務算出「剩下的材料已經不可能拿到金星」之後要怎麼處置。預設 <c>Off</c>＝維持現行行為。
        /// </summary>
        /// <remarks>🔴 這是<b>破壞性</b>動作，所以預設關閉，而且只有在「這一輪除了金星以外不會交件」
        /// （<c>AutoTurnin</c> 或 <c>TurninGold</c>）時才會生效 —— 使用者本來就接受銀／銅星的話，
        /// 「拿不到金星」根本不是放棄的理由。判定本身見
        /// <see cref="ICE.Utilities.Cosmic_Helper.CraftGoldFeasibility"/>。</remarks>
        public GoldUnreachableAction CraftGoldUnreachable { get; set; } = GoldUnreachableAction.Off;

        /// <summary>
        /// 宇宙製作時要不要透過 Artisan 的「臨時求解器」IPC 指定求解器。
        /// </summary>
        /// <remarks>
        /// 🔴 覆寫是<b>臨時的</b>：不寫進使用者的 Artisan 設定檔，而且任務結束／ICE 停止／
        /// 外掛卸載時都會還原。實作見 <see cref="ICE.Utilities.CosmicSolverOverride"/>。
        /// </remarks>
        public CosmicCraftSolver CraftSolverOverride { get; set; } = CosmicCraftSolver.Expert;

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

        /// <summary>
        /// 採集時是否每次都重新挑「離玩家最近而且還採得到」的採集點。
        /// 關掉就退回舊行為：照路線檔裡的先後順序一個接一個走。
        /// </summary>
        /// <remarks>
        /// 留這個開關是因為選點順序改變會連帶改變走位，實機萬一出現非預期的來回移動，
        /// 使用者可以自己關掉退回舊行為，不必等我們出新版。
        /// </remarks>
        public bool GatherPickClosestNode { get; set; } = true;

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

        /// <summary>
        /// 宇宙探索任務交件拿到金評時，請 TataruPraise 念一句誇獎。
        /// </summary>
        /// <remarks>
        /// ⚠️ 預設開著，但實際會不會出聲還要看 TataruPraise 自己的總開關、冷卻，
        /// 以及它的誇獎池裡「宇宙」這個情境有沒有已合成的語音。
        /// </remarks>
        public bool PraiseOnGoldTurnin { get; set; } = true;

        /// <summary>
        /// 宇宙探索的自動化<b>達成停止條件收工</b>時，請 TataruPraise 念一句。
        /// </summary>
        /// <remarks>
        /// 🔴 一輪自動化<b>最多念一句</b>——ICE 的停止路徑有十幾條，去重靠
        /// <c>ICE.IPC.TataruPraiseIPC</c> 的 <c>stopNoticeArmed</c>（每幀重舉、送出即放下）。
        /// </remarks>
        public bool PraiseOnAutomationStopped { get; set; } = true;

        /// <summary>
        /// 宇宙探索的自動化<b>卡住被迫停下</b>時，請 TataruPraise 念一句「需要幫忙」。
        /// </summary>
        /// <remarks>
        /// ⚠️ 走 TataruPraise <b>既有</b>的「需要幫忙」情境，既有使用者本來就有那個情境的語音。
        /// </remarks>
        public bool PraiseOnAutomationStuck { get; set; } = true;

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

        /// <summary>
        /// 遇到遊戲自己的「目前已經學會了該道具對應的內容」確認框時要不要放棄該件。
        /// 預設關閉＝維持上游行為（對任何 SelectYesno 一律按下確定）。
        /// </summary>
        /// <remarks>🔴 刻意不預設開啟：兌換商店賣的樂譜／演技教材是可交易的，有人就是要買已經學會的來賣。
        /// 要只擋特定幾件請改用逐項的 <see cref="CosmoShoppingList.SkipIfUnlocked"/>。</remarks>
        public bool HeedAlreadyLearnedPrompt { get; set; } = false;

        #endregion

        public Dictionary<uint, MissionSettings> MissionConfig { get; set; } = new();

        // 任務預設組（playlist）。名稱→啟用的任務 id 清單。
        // 載入時只切各任務的 Enabled，不動優先順序或個別任務設定。
        public Dictionary<string, List<uint>> MissionPlaylists { get; set; } = new();

        public List<MissionCommand> PostMissionCommands { get; set; } = new();

        #region Tab Hider

        // 📌 舊側欄的五個開關（Show_StopWhen／_GatheringProfile／_MissionPriority／_MiscSettings／_HubActivities）已不在這裡，
        //    刻意不遷移到下面的新鍵：新鍵一個管一整頁，舊鍵管的是其中一個子分頁，照搬會把沒藏過的頁一起藏掉。
        //    舊設定檔裡留著的那幾行讀取時直接略過（YamlConfig 的 IgnoreUnmatchedProperties）。

        // 一個一級項一個開關，全部預設顯示＝現行版面零改變。
        // 🔴 「設定」頁**刻意沒有**開關：這幾個開關本身就住在它的「介面與導覽」節裡，
        //    它自己也能被藏的話，使用者就沒有任何辦法把藏掉的東西叫回來。
        //    改側欄那段碼之前先想清楚這件事（SelectableSidebar.Draw 有同樣的註記）。
        public bool Show_Page_Missions { get; set; } = true;
        public bool Show_Page_Gathering { get; set; } = true;
        public bool Show_Page_HubActivities { get; set; } = true;
        public bool Show_Page_MechaOps { get; set; } = true;
        public bool Show_Page_Help { get; set; } = true;

        // 側欄下半三組**直接畫在側欄裡**的控件（月球選擇／職業選擇／宇宙工具經驗值）的顯示開關。
        // ⚠️ 前綴刻意用 Show_Side_ 而不是 Show_Page_：這三組**沒有對應的頁**，
        //    MainWindow.MainBody() 的 switch 裡找不到它們的 case。
        // 📌 預設全部 true＝現行版面零改變（舊設定檔沒有這三個鍵，反序列化時吃到欄位初始值）。
        public bool Show_Side_MoonSelection { get; set; } = true;
        public bool Show_Side_ClassSelection { get; set; } = true;
        public bool Show_Side_ToolRelicXp { get; set; } = true;

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

        /// <summary>
        /// 日誌<b>寫入端</b>門檻：低於這個等級的 log 完全不進緩衝區、不組字串、不呼叫 PluginLog。
        /// </summary>
        /// <remarks>
        /// 🔴 <c>IceLogging.MinimumLevel</c> 的 setter 會把值夾在 <c>Info</c> 以下 ——
        /// Information 是請使用者回報診斷的既定管道，關不掉。
        /// </remarks>
        public Utilities.Cosmic_Helper.IceLogging.LogLevel LogMinimumLevel { get; set; }
            = Utilities.Cosmic_Helper.IceLogging.LogLevel.Verbose;
        public string AuthorName { get; set; } = "Puni.sh Community";
        public string CustomRoutePath { get; set; } = string.Empty;


        #endregion

        #region Yaml Save Stuff

        public static string ConfigPath => Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "Mission Config.yaml");
        private static CancellationTokenSource? _saveCts;
        private static readonly object _saveLock = new();

        // 這份設定自己的存檔閘門。⚠️ 它**不是**用來擋 IOException 的（那一層是 YamlConfig 的 per-path SemaphoreSlim）；
        //    它擋的是兩個存檔先各自拍好快照、再排隊寫檔時，先拍的那份反而**後**寫，磁碟上留下比較舊的快照。
        // ⚠️ 兩層閘門的取得順序永遠是「先 _saveGate 再 YamlConfig」（SaveAsync 與 SaveSync 都是），順序一致所以不會死結。
        private static readonly SemaphoreSlim _saveGate = new(1, 1);

        // 快照序號。🔑 遞增這個動作與「序列化」在**同一個主執行緒的動作裡**完成，
        // 所以序號的大小關係就是快照的新舊關係。_writtenSeq 是已經真的寫進磁碟的最大序號。
        private static long _snapshotSeq;
        private static long _writtenSeq;

        // Standard save. Deliberately routed through the debounced path: this config
        // is ~330 KB, so a burst of saves is collapsed into one write. No caller can
        // observe the difference - Save() was already asynchronous. Anything that
        // genuinely needs the bytes on disk before continuing already has SaveSync().
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
        // 🔴🔴 快照必須在**遊戲主執行緒**上拍。序列化會走訪整個設定物件圖，而 MissionConfig 是裸 Dictionary、
        //    GambaItemWeights 之類是裸 List，改動它們的是主執行緒；在執行緒池上序列化會擲
        //    InvalidOperationException（Collection was modified），設定沒存到而畫面上什麼都看不出來。
        // 📌 交回主執行緒的只有「序列化成字串」這一步，寫檔照舊留在背景。
        // 📌 遊戲正在關閉時（IsFrameworkUnloading）RunOnFrameworkThread 就地執行，最後一次存檔不會弄丟。
        public async Task SaveAsync()
        {
            var snapshot = await Svc.Framework.RunOnFrameworkThread(
                () => (Yaml: YamlConfig.Serialize(this), Seq: Interlocked.Increment(ref _snapshotSeq)))
                .ConfigureAwait(false);

            await _saveGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // 已經有更新的快照寫下去了 ⇒ 放棄這一份，不要用舊的蓋掉新的。
                if (snapshot.Seq < Interlocked.Read(ref _writtenSeq))
                    return;

                await YamlConfig.WriteAsync(snapshot.Yaml, ConfigPath).ConfigureAwait(false);
                Interlocked.Exchange(ref _writtenSeq, snapshot.Seq);
            }
            finally
            {
                _saveGate.Release();
            }
        }

        // Synchronous for migrations/critical paths
        public void SaveSync()
        {
            _saveGate.Wait();
            try
            {
                YamlConfig.SaveSync(this, ConfigPath);
            }
            finally
            {
                _saveGate.Release();
            }
        }

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
        // 放棄任務累積耗時（秒）。刷職業成果時「金星不可達就放棄」（craftGoldUnreachable: Abandon）
        // 是迴圈裡的真實成本，但既有的每分成果只除以「交件耗時」，會系統性高估不穩金星的任務。
        // ⚠️ 舊設定檔沒有這個鍵，反序列化時吃初始值 0。
        public double AbandonedTimeSeconds { get; set; } = 0;
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

    /// <summary>
    /// 單一機甲技能的範圍形狀覆蓋值。每個維度都是 <c>float?</c>：
    /// <c>null</c> ＝這個維度不覆蓋，走預設（舊鍵或 Lumina 原值）。
    /// 🔴 <b>用 <c>float?</c> 而不是拿 0 當哨兵</b>：0 是一個合法的角度／距離輸入，
    /// 拿它當「沒設定」的話，使用者把滑桿拉到底就會變成「重設」——那是靜默的行為錯誤。
    /// </summary>
    public class MechaShapeOverride
    {
        public float? Primary { get; set; }
        public float? HalfWidth { get; set; }
        public float? AngleDeg { get; set; }

        /// <summary>三個維度都沒設＝這筆等於不存在，可以從字典裡移掉。</summary>
        public bool IsEmpty => Primary == null && HalfWidth == null && AngleDeg == null;
    }

    public class CosmoShoppingList
    {
        public int KeepAmount { get; set; } = 0;
        public int BuyAmount { get; set; } = 0;
        public bool KeepBuying { get; set; } = false;

        /// <summary>
        /// 這件道具「已經學會／已經登錄」之後就不要再買。預設關閉＝維持上游行為。
        /// </summary>
        /// <remarks>
        /// ⚠️ 刻意不做成「已學會就一律不買」：這些道具是可交易的，
        /// 有人買來就是要賣掉。所以做成逐項開關而不是全域行為。
        /// </remarks>
        public bool SkipIfUnlocked { get; set; } = false;
    }

    public class MissionCommand
    {
        public required string command { get; set; }
        public int Delay { get; set; } = 0;
    }
}
