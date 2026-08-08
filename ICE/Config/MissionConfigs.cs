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
        /// 遇到「文字對不上預期」的確認框時要怎麼處理。預設 <c>AlwaysConfirm</c>＝維持現行行為。
        /// </summary>
        /// <remarks>
        /// 🔴 這是<b>另一個</b>旗標，不是把新地方掛到 <see cref="RejectUnknownYesno"/> 上：
        /// 那個旗標預設就是 <c>true</c>（既有使用者都開著），把新的檢查掛上去等於未經同意
        /// 改了所有人的行為。<br/>
        /// 涵蓋範圍與比對基準見 <c>ICE.Utilities.YesnoGuard</c>；
        /// <see cref="RejectUnknownYesno"/> 蓋的是接任務與放棄任務兩處，兩者不重疊。
        /// </remarks>
        public UnexpectedYesnoAction UnexpectedYesno { get; set; } = UnexpectedYesnoAction.AlwaysConfirm;

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

        // ---- 疊加層各區塊的顯示開關（2026-08-08 使用者要求：「預報和成果 也能加開關嗎?」）----
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

        // 機甲行動狀態改畫在 ICE 疊加層主視窗的可摺疊區塊裡（比照「宇宙工具經驗值」），
        // 而不是另外開一個獨立視窗。預設開＝2026-08-08 使用者要求的新版面。
        //
        // 🔑 關掉就回到舊的獨立視窗。兩者不會同時出現：獨立視窗的顯示條件會問
        //    「主視窗是不是真的會畫到它」（見 MechaOpsWindow.MergedIntoOverlay）。
        // ⚠️ 主視窗自己的開關 ShowOverlay 預設是**關**的——所以沒開主視窗的人
        //    仍然會拿到獨立視窗，這個合併不會讓任何人的功能憑空消失。
        public bool ShowMechaInOverlay { get; set; } = true;

        // 🔴🔴 下面兩個舊鍵自 2026-08-08（設定版本 12）起**程式不再讀取**。
        //      值已由 ConfigMigrator.MigrateMechaGlobalSlidersToPerSkill 搬進
        //      MechaShapeOverrides[42258].AngleDeg / [42150].Primary，效果值逐一相同。
        //      欄位刻意留著不刪：①舊設定檔還原得回來 ②遷移本身要讀它們。
        //      ⚠️ 新碼一律走 MechaActionShapes.ConeAngleFor()／TryResolve()，不要再讀這兩個鍵——
        //      讀了會靜默忽略 per-skill 覆蓋（失敗形式是「滑桿沒作用」）。

        // 宇宙火焰噴射器（42258）的扇形**全**角（度）。遊戲資料裡沒有（Omen=0），只能靠實機校準。
        //
        // 📌 2026-08-08 由兩場 [MechaRec] log 定錨：CAST 時刻 7.5m 內目標的相對朝向半角
        //    樣本 12 筆有 8 筆 >50°，且半角 101~152° 成群 —— 也就是實際扇形遠比原本的
        //    預設 90° 寬。240°（半角 120°）落在那一群的中位附近，所以改當預設。
        // ⚠️ 樣本含雜訊：那 12 筆是「CAST 當下在範圍內的物件」，不是「確定被打到的物件」，
        //    所以 240 是**校準起點**不是定論；滑桿上限已放到 360 讓使用者自己收斂。
        // ⚠️ 這個值是**所有**扇形機甲技能共用的（凡 CastType 13/3 者皆適用），
        //    只是目前六個已驗證的技能裡只有 42258 是扇形，所以實質上等同於 per-skill。
        // 🔴 既有使用者的 yaml 已經序列化過這一鍵 → 改預設對他們**無效**，要自己拉滑桿。
        public float MechaConeAngleDeg { get; set; } = 240f;

        // 宇宙鑽頭（42150）的矩形長度（公尺）。
        //
        // 📌 Lumina Action 表寫的 EffectRange 是 7，但那是**技能資料上的射程**，
        //    不等於實戰上真的打得到的距離。2026-08-08 由 [MechaRec] log 定錨：
        //    11 發實際 CAST 時「最近目標距離」有 10 發落在 1.75~3.65m（一發 6.74 離群），
        //    也就是使用者實際都是貼到 4m 以內才按 —— 畫 7.0 會讓範圍框遠大於有效觸發距離，
        //    造成「框到了卻打不到」的誤導。預設改 4.0。
        // ⚠️ 這是**校準**不是資料修正：真值仍未知（沒有命中/未命中的地面真相），
        //    所以做成滑桿（2~10）讓使用者自己收斂；拉回 7 就是退回 Lumina 原值。
        // 📌 半寬（XAxisModifier/2 = 2.5）沒有任何回報指出不準，維持走 Lumina 不開設定。
        public float MechaDrillLength { get; set; } = 4f;

        // ---- per-skill 形狀覆蓋（Utilities/MechaOps/MechaActionShapes.cs）----
        // 技能 id → 各維度的覆蓋值。**預設空字典＝完全沿用現行有效值**
        // （Lumina 原值，或上面兩個舊鍵已經校準過的值），所以升級不會改變任何行為。
        //
        // 🔑 讀取優先序：per-skill 覆蓋 > 內建校準值（MechaActionShapes 的常數）> Lumina 原值。
        //    ⚠️ 2026-08-08 起舊鍵不再參與這條優先序：使用者拉過的值（例如 5.0／120）
        //    已由設定版本 11→12 的遷移**搬進這個字典**，所以那些值繼續生效，
        //    只是現在看得出來是掛在哪一個技能上。
        // ⚠️ null ＝「這個維度不覆蓋」，不是 0。用 float? 而不是 0 當哨兵，
        //    否則「使用者真的想設 0」與「沒設」分不出來。
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
        // ⚠️ 2026-08-06 起這一項**不再影響任務目標**：目的指示標記對上的物件、
        //    以及跟它同 BaseId 的同型物件一律列出（見 MechaOpsMonitor.SampleTargets）。
        //    原因是使用者回報的「有害菌床」目標既沒有名字也不可選取，
        //    被這個旗標整批擋掉，而關掉它又會讓整片場景與 NPC 灌進來。
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
        // 顯示「另一個身份」的目標。**預設 false ＝分流生效**，也就是協助員不會看到
        // 駕駛員的巨型目標、反之亦然。
        //
        // 🔴 這一項改變了 2026-08-08 之前的行為，而那個行為正是使用者回報的問題本身：
        //    他以協助員身份參加「有害菌床驅除指令」，畫面上一直有駕駛員的菌床本體
        //    （did=2014722），而且勾「只顯示可選取的物件」也濾不掉——因為三層分級全都
        //    正確地把它判成「這場事件的任務目標」，缺的是「它是不是**我的**目標」。
        //
        // 🔑 分流**只影響畫不畫**：分級、標記學習、錄製器全部照舊看得到全家族，
        //    否則下一次實機錄製就分不出「另一邊發生了什麼」。
        // ⚠️ 判不出身份（上機甲前／事件外）或判不出歸屬時一律不分流（全部顯示）——
        //    這個功能的失敗方向必須是「多顯示」，不是「把使用者要打的東西藏起來」。
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

        // ---- 機甲行動區塊「逐列」開關（2026-08-08 使用者要求：「機甲ui的各項 能加開關嗎」）----
        // 🔑 全部預設開＝現行版面零改變。上面那個 ShowMechaEventProgress 仍然是整組的總開關，
        //    這幾個是它底下的細項；總開關關著時這幾個一律不生效（不是「兩個都要開」的意思，
        //    而是總開關就已經整組不畫了）。
        // ⚠️ 「目的指示那一列」與世界疊加層上的目的指示圈是**兩件事**：
        //    前者是這一列文字（ShowMechaRowObjectives），後者是 ShowMechaObjectives。
        //    把兩者綁在一起的話，想關掉視窗那一行的人會連地上的圈一起弄不見。
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
        // ✅ 「事件還沒開始就讀得到開始時間」已由 2026-08-08 的三場實機錄製證實
        //    （提前 19 分鐘就讀得到，開始時刻分秒吻合）。
        public bool ShowMechaSchedule { get; set; } = true;

        // ---- 駕駛申請書持有狀態 ----
        // 2026-08-08 使用者原話：「駕駛申請書身上只能帶一張 能偵測到有沒有嗎」。
        // 「駕駛申請書：持有／無／?」一列。
        //
        // 🔴 它不是背包道具（台服 Item／EventItem 兩張表都查無「申請書」），資料是
        //    WKSMechaEventModule 的兩個 byte（+0xA2A9 持有、+0xA2AA 資料到了沒），
        //    純量、位置在 CS 宣告的模組配置內、一個指標都不用解 ——
        //    與 ShowMechaSchedule 同一個安全等級，所以同樣預設開。
        // ⚠️ 這一列會讓機甲區塊在「現在沒有任何事件」時也有東西可畫，因此在宇宙區域裡
        //    區塊幾乎總是看得見。那是刻意的：這一列的價值就在事件開始之前。
        //    整組仍然掛在 ShowMechaAoeOverlay 底下（該項預設關），沒開機甲功能的人不受影響。
        public bool ShowMechaPilotTicket { get; set; } = true;

        // 🔴🔴 部署閘門：預設 false。
        // 開啟＝顯示緊急事件（紅色警報：磁暴／流星雨／孢子霧）的類型與剩餘時間。
        // 資料源是 AgentWKSAnnounce.Data，那是一塊**我們沒有辦法驗證大小**的堆積配置：
        // CS 宣告 Size = 0xA8 是照國際服的佈局，台服沒有離線驗證過。
        // 若台服的配置比較小，讀 +0xA0 的 State 就是越界，而 AccessViolationException
        // 是 corrupted-state exception，try/catch 與 HookSafety.ExecuteSafe 都攔不到。
        // ⚠️ 要改成預設開，必須先有實機證據（開著跑過一輪磁暴而沒有崩潰）。
        //    這與同檔的 MechaObjectiveUseMarkerVector 是同一個理由、同一個處置。
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

        // 「取得金星後排除任務」對緊急任務網開一面。
        // 🔑 預設 false ＝ 現行行為完全不變；要例外的人自己去勾。
        // 判別碼是 MissionAttributes.Critical（源自 WKSMissionUnit.IsSpecialQuest），
        // 台服 7.20 離線驗證恰為 33 個任務（列 512..544），
        // 與 WKSEmergencyMissionGroup 那條獨立資料鏈逐筆相同——見 MissionChain 的註解。
        public bool RemoveAfterGoldKeepCritical { get; set; } = false;

        /// <summary>
        /// 製作任務算出「剩下的材料已經不可能拿到金星」之後要怎麼處置。預設 <c>Off</c>＝維持現行行為。
        /// </summary>
        /// <remarks>
        /// 🔴 這是<b>破壞性</b>動作，所以預設關閉，而且只有在「這一輪除了金星以外不會交件」
        /// （<c>AutoTurnin</c> 或 <c>TurninGold</c>）時才會生效 —— 使用者本來就接受銀／銅星的話，
        /// 「拿不到金星」根本不是放棄的理由。判定本身見
        /// <see cref="ICE.Utilities.Cosmic_Helper.CraftGoldFeasibility"/>。
        /// </remarks>
        public GoldUnreachableAction CraftGoldUnreachable { get; set; } = GoldUnreachableAction.Off;

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
        /// 預設 <c>true</c>：這是在修一個使用者實測回報的問題（「明明有更近的採集點卻跑去遠的」），
        /// 預設關掉等於升級後什麼都沒變。這是<b>新增的鍵</b>，既有使用者的設定檔裡沒有它，
        /// 反序列化不會覆蓋欄位初始值，所以新預設對既有使用者一樣生效。
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
        /// <remarks>
        /// 🔴 刻意不預設開啟：兌換商店賣的樂譜／演技教材是**可交易**的
        /// （台服 Item 表核對過：48211／48213／47985 的 <c>IsUntradable</c> 都是 False），
        /// 有人就是要買已經學會的來賣。要只擋特定幾件請改用逐項的
        /// <see cref="CosmoShoppingList.SkipIfUnlocked"/>。
        /// </remarks>
        public bool HeedAlreadyLearnedPrompt { get; set; } = false;

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

    /// <summary>
    /// 單一機甲技能的範圍形狀覆蓋值。每個維度都是 <c>float?</c>：
    /// <c>null</c> ＝這個維度不覆蓋，走預設（舊鍵或 Lumina 原值）。
    ///
    /// 🔴 <b>用 <c>float?</c> 而不是拿 0 當哨兵</b>：0 是一個合法的角度／距離輸入，
    /// 拿它當「沒設定」的話，使用者把滑桿拉到底就會變成「重設」——那是靜默的行為錯誤。
    ///
    /// 各維度對應哪一種形狀見 <see cref="Utilities.MechaOps.MechaAoeShape"/>：
    /// <list type="bullet">
    ///   <item><c>Primary</c>：矩形的最遠距離／扇形的距離／圓形與射程圈的半徑</item>
    ///   <item><c>HalfWidth</c>：矩形的半寬（<b>只有矩形有意義</b>，UI 也只對矩形顯示）</item>
    ///   <item><c>AngleDeg</c>：扇形的全角（度）</item>
    /// </list>
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
        /// 🔴 為什麼需要這個：樂譜（管弦樂琴樂譜，ItemAction 2235）與演技教材
        /// （ItemAction 2709）這類道具**學會之後就從背包消失**，所以
        /// <see cref="KeepAmount"/> 永遠達不到；再配上 <see cref="KeepBuying"/>
        /// 就會一路買到宇宙信用點數見底。<br/>
        /// ⚠️ 但**不能**無條件改成「已學會就不買」—— 這些道具是可交易的，
        /// 有人買來就是要賣掉。所以做成逐項開關而不是全域行為。<br/>
        /// 判定來源是 <c>UIState.IsItemActionUnlocked</c>；只有回報「確定已學會」
        /// 才會擋，問不到答案時一律照舊購買。
        /// </remarks>
        public bool SkipIfUnlocked { get; set; } = false;
    }

    public class MissionCommand
    {
        public required string command { get; set; }
        public int Delay { get; set; } = 0;
    }
}
