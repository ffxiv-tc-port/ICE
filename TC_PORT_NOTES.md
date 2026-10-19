# ICE 台服移植修補清單

改基底（rebase 到上游、重新 fork、或整檔照搬上游檔案）之前逐條核對：新基底有沒有等價的東西，沒有就要重搬。這份只列「我方有、漏掉會壞」的修改；純介面調整與註解整理不列。

- **上游**：`LeontopodiumNivale14/Ices-Cosmic-Exploration`（remote `upstream`），分岔點 `f3b7a69`（台灣時間 2025-12-16，國際服 7.4 前最後一顆 API13）。上游之後換成 net10／SDK15，不能直接合併。台服社群版 `cycleapple/Ices-Cosmic-Exploration`（remote `upstream-cycleapple`，分支 `api13-tw`）是另一個來源，下面標「cycleapple」的是從那裡移植的。
- **我方**：分支 `tc-7.20`，改動範圍 `f3b7a69..HEAD`。2026-08-26 全庫壓縮之前的原始 commit 在 tag `pre-squash-20260826` 的歷史裡（`git log pre-squash-20260826`），考古（`git log -S` 之類）要同時對那個 tag 跑。下面引用的我方 SHA 在本 repo 都解得開；標「上游」「cycleapple」的要先 `git fetch --no-tags upstream`／`upstream-cycleapple`，4liang0121 的 `1f2028c` 不在本 repo。
- **格式**：做了什麼 — `位置`（檔案／符號，不記行號）｜漏掉時的樣子｜驗：怎麼確認｜來源 commit。
- **漏掉時的樣子**：「靜默」＝不報錯也不崩潰，只是行為錯（卡住、按錯、數字錯、字變回英文）；「崩潰」＝原生存取違規，`try/catch` 攔不到；「看得見」＝有例外或明顯症狀。

## 改基底時最容易靜默出錯的前 10 條

1. **AutoHook IPC 的型別照本艦隊 AutoHook 的提供端宣告**：`SwapBaitById` 是 `Func<uint, bool>`、`CreateAndSelectAnonymousFolder` 是 `Func<string, List<string>>`（上游分別寫 `Func<uint, Task<bool>>`、`Action<string>`），另有上游沒有的 `GetFolderImportApiVersion`、`GetPluginState`。— `IPC/AutoHookIPC.cs`｜靜默：`EzIPC.Init` 帶 `SafeWrapper.AnyException`，型別不合被吞成 default ⇒ 換餌永遠退回 `/ahbait`、資料夾匯入拿到 null｜驗：逐一對 `AutoHook/AutoHook/IPC/AutoHookIPC.cs` 的 `public` 簽章；實機不該出現指名 AutoHook 的「[EzIPC] 跨外掛 IPC 呼叫失敗」｜`94ad344` `02f5909` `bee4831` `0f417ea`
2. **設定檔是 YAML `Mission Config.yaml`（`MissionConfigs`，世代 12），上游已改成 EzConfig `Config`（`ConfigFiles/`，世代從 1 起）**：改基底到上游要先寫 YAML→EzConfig 遷移器；我方 `ConfigVersion` 不可沿用上游的編號（會把沒跑過的遷移宣告成跑過）。11→12（機甲滑桿併進逐技能覆蓋）是我方加的。舊 EzConfig `Configuration`（`DefaultConfig.json`）只剩 `ConfigMigrator.MigrateConfigv1` 在讀。— `Config/MissionConfigs.cs`、`Config/ConfigMigrator.cs`｜靜默：使用者設定整份歸零｜驗：使用者設定檔 `configVersion: 12`，遷移鏈連續到 12｜`16d7388` `161185a`
3. **台服月面 NPC 的 DataId 與國際服不同**：`NpcData.MoonNpcs[1237]` 的修理／兌換／研究員／轉盤各帶 3 個台服變體（`AlternateNpcIds`），`Utils.TryGetNpcObject` 先試全部 ID，再退回「設定座標 5 米內、可選取的 EventNpc／BattleNpc」；查表一律走 `NpcData.TryGetMoonNpc`。— `Utilities/NpcInfo.cs`、`Utilities/Utils.cs`｜靜默：人站在 NPC 旁邊卡在 HubReturn（宇宙工具回報、修理、點數購物、轉盤）｜驗：實機 log 沒有 `[NPC Resolver]` 的退路 Warning＝ID 直接命中｜`dfac70d` `3cec4b0`（cycleapple）`8f283dd`
4. **台服任務表只有 1..544 有內容**（545..1072 是空列）：`DictionaryCreation` 跳過空名，所以 `SheetMissionDict` 的鍵就是 1..544，而 `C.MissionConfig` 曾多出鍵 0。所有「守 A 字典、索引 B 字典」都改成各自 `TryGetValue`；目前任務一律 `CosmicHelper.TryGetCurrentMissionInfo`／`SchedulerMain.CurrentMissionUnavailable`（查不到＝清佇列回狀態判斷）；`MissionTimer` 在寫入端擋掉鍵 0。— `ICEDictornaryCreation.cs`、`Utilities/Cosmic_Helper/Mission_Info.cs`、`Scheduler/SchedulerMain.cs`、`Utilities/MissionTimer.cs`｜看得見但像「不動」：`KeyNotFoundException` 讓佇列中止、外掛自己停用；在 ImGui 迴圈裡是整個視窗畫不出來｜驗：新搬進來的碼不可直接 `SheetMissionDict[id]`／`C.MissionConfig[id]`（除非 id 就是從那個字典列舉出來的）；載入時的 `[資料盤點]` 幾行｜`8daada5` `8f283dd` `4a00652` `009a761`
5. **長等待的任務要帶 `Utils.TaskConfig`**（30 分鐘、逾時不中止）：NeoTaskManager 預設是 30 秒逾時＋`AbortOnTimeout`，等 Artisan、走路、等餌、區域切換都會超過 ⇒ 整條佇列被清。— `Utilities/Utils.cs`（`TaskConfig`）、各 `Scheduler/Tasks/Task_*.cs` 的 `Enqueue`｜靜默：宇宙製作每次 32.5 秒必中止、宇宙工具回報每 30 秒從頭再來（上游的 `TaskTimeoutException` 訊息是空的）｜驗：從上游搬來的 `Enqueue` 逐一看有沒有帶設定；實機 Error「任務逾時：」（`ICE.cs` 的 `OnTaskTimeout`）｜`751b6aa` `31a94af` `94ad344` `8daada5` `8f283dd` `726806c`
6. **宇宙點數商店讀遊戲資料，台服兌換 NPC 直接開商店**：商品清單執行期掃 `SpecialShop`，判準「付款道具＝宇宙點數 45690」（取代 49 筆國際服寫死清單）；台服兌換 NPC 互動後直接開 `ShopExchangeCurrency`、不出 `SelectIconString`；選單出現時以商店名稱比對，不寫死 `Entries[1]`（台服那一項是空商店）。— `Utilities/Shop_Cosmocredits.cs`、`Scheduler/Tasks/Task_BuyCosmoItems.cs`（`TalkToCreditNPC`、`SelectShop`）｜靜默：清單對不上實際商店；站在 NPC 前每 500ms 重送互動的無限迴圈｜驗：購物流程走得到 `BuyItems`；兩步各自有 20 秒放棄閘門，走到會留 Information｜`174f11c` `b1a62bb`
7. **時間型任務的銀／金門檻是「剩餘秒數 × 10」，不是分數**（台服 34 個，`WKSMissionToDo.WKSMissionText` 104／110／113／114／115）：交件閘門統一走 `Task_CheckScore.EvaluateMedalGoals`，時間型釣魚走 `TimeGradedFishRequirementsMet`（資料表數量 → preset `RequiredFish` → 都沒有就不宣稱達標），獎章判定對門檻 0 不判金；任務表與疊加層改畫 mm:ss。— `Scheduler/Tasks/Task_CheckScore.cs`、`Utilities/Cosmic_Helper/CosmicHelper.cs`（`CosmicInfo.IsTimeGraded`）｜靜默：面板評價是 Undefined、銅門檻 0 ⇒ `0 >= 0` 恆真，身上一條魚都沒有就交件｜驗：Information「交件門檻判定〔時間型〕」｜`8cf569c` `358e7aa` `c8f75b4` `cbdcabc`
8. **在地化的 key 就是程式碼裡的英文原文**：`.Loc()`（約 1100 處）查 `ICE/LanguageChineseTraditional.ini`（約 1040 條）。改英文字串＝那條翻譯消失、退回英文；標籤加 `###ID` 後綴避免譯後撞名；會被翻譯的字串不可放在比 `Localization.Init("ChineseTraditional")` 更早初始化的 `static` 欄位（改成屬性）；ini 一條一行、值裡不可有 `==`、多行寫成 `\n`。— `ICE.cs`（`Localization.Init`）、`ICE/ICE.csproj`（ini 以 Content 帶進輸出）｜靜默：畫面變回英文｜驗：`python ~/.claude/tools/loc/loc_verify.py D:/ffxiv-tc-port/ICE`（目前缺漏只剩 ECommons 子模組 7 條，ICE 本體 0）｜`a8ebd6f` `e17dc78` `c444122`
9. **按窗一律先過 `AddonPressGuard`**：同一扇視窗同一按法在它從 addon 清單消失前只按一次（`SelectYesno` 走 `YesnoPressGuard`），`WKSMissionInfomation` 的回報／放棄共用 `AddonPressGuard.WksMissionExitPressKey`，時鐘在 `Framework.Update` 自己數（過場中繪製幀會停）。上游的按窗碼都沒有這層。— `Utilities/AddonPressGuard.cs`、`Utilities/YesnoPressGuard.cs`，約 50 個 `TryBeginPress`／`MayPress` 呼叫點｜崩潰：對關閉中的視窗再送一次 callback；漏掉時沒有任何編譯警告｜驗：`git grep -n "TryBeginPress\|MayPress" -- ICE`；搬進來的每個 `Callback.Fire`／`.Yes()`／`.Report()`／按鈕點擊前面都要有它｜`d6c498e` `d68a579` `7cdeb57` `86453c9` `3d974da` `3571e29`
10. **外掛庫網址指本艦隊 feed**：必要外掛頁的「加入外掛庫／安裝外掛」、`NavmeshIPC.Repo`、`LifestreamIPC.Repo` 都是 `ffxiv-tc-port/DalamudPluginsTC` 的 `repo.json`（`PandoraIPC.Repo` 刻意不動：台服沒有移植版）。— `Ui/MainUi/HelpFolder/helpSelect_Required.cs`（`TcRepoUrl`）、`IPC/NavmeshIPC.cs`、`IPC/LifestreamIPC.cs`｜靜默：按下去裝進國際服 API15 版，和台服版撞同一個 InternalName｜驗：`git grep -n "pluginmaster\|repo.json" -- ICE`｜`6d511a9`

### 搬任何一個上游檔案時的五個檢查

1. 它宣告的 IPC 型別是否逐字等於本艦隊提供端（見第 5 節）。
2. 它的按窗點是否接上 `AddonPressGuard`（第 9 條）。
3. 它的 `Enqueue` 是否帶 `Utils.TaskConfig`（第 5 條）。
4. 它有沒有直接索引 `SheetMissionDict`／`C.MissionConfig`／`NpcData.MoonNpcs`（第 4 條、第 3 條）。
5. 它改了哪些英文字串（要同步 ini，第 8 條），以及有沒有新設定鍵（第 6 節）。

## 1. 台服資料與遊戲行為

- **內建分數表補台服缺的 10 個任務**（203、330、380、425、433、436、444、446、447、454），台服已開放的 544 個任務現在全有非 0 分數 — `Resources/MissionScores.csv`｜靜默：完成一次前分數 0、排序墊底｜驗：`[資料盤點]` 裡內建分數表那行｜`4831993`
- **交件後不用記錄值蓋掉內建分數**：`UpdateScoreInfo` 只在表上沒有（或為 0）時寫 `ClassScore`，與載入順序「表 → ScoreKeeper → 0」一致；放棄流程先按「回報結果」交件時也有正確的分數基準 — `Scheduler/Tasks/Task_TurninMission.cs`（`UpdateScoreInfo`、`RefreshPreviousScore`）、`Task_AbandonMission.cs`｜靜默：記錄值錯（實例 5 倍）就蓋掉表值直到下次載入｜驗：記錄值與表值不同時有 Information `[Score Keeper]`｜`262f63d`
- **釣魚任務 preset**：479／481／482／484／486／487／489～493／510／511 從上游搬 `AH6_`；474 改單 stage（原本最後一筆開耐心）；483 的兩筆互指、要保留兩筆；494／495 是 `AHFOLDER_` 整包；543 以 542 為模子自組；486 要 5 種、494 要 6 種。`AH6_` 要本艦隊同波的 AutoHook 才匯得進 — `Utilities/GatheringHelper/FishingPresets.cs`（含 `BackfillAmountRequiredFromSheet`）、`Utilities/Cosmic_Helper/UnsupportedMissions.cs`｜靜默：preset 是空殼時任務被跳過或站著釣到逾時｜驗：啟動時那行 Information「時間型釣魚任務的交件依據」｜`c8f75b4` `d1f8f2b` `c0cbdac` `bee4831` `5ad9dc3`
- **任務支不支援只問一個函式**：`MissionSupport.GetReason`（缺內建釣魚設定／這個客戶端沒有任務資料／需要 AutoHook 資料夾匯入／黑名單）；自動挑任務時跳過並說明，手動接到也說原因 — `Utilities/Cosmic_Helper/MissionSupport.cs`｜靜默：接了跑不動的任務卻不做事｜驗：任務名稱前的【未支援】標記｜`a4e90cc` `bee4831`
- **宇宙衝刺**：`C.MoonSprint` 放 Action 43357（判斷式等的是狀態 4398），宇宙衝刺不可用或不是生產／採集職業時退回一般衝刺 — `Scheduler/Handlers/StellarSprintHandler.cs`｜靜默：一直放 60 秒冷卻的一般衝刺（上游現在仍是 GeneralAction 4）｜驗：不可用時有 Information｜`f645aed`
- **據點範圍逐區域查表**（1237 中心約 (2.84, -0.06)、水平距離），查不到就當成在據點內 — `Scheduler/Tasks/Task_Repair.cs`（`HubCheck`）｜靜默：離開月面時一直重施放返回｜驗：查不到區域時有 Information｜`438547f`（cycleapple）
- **緊急任務繳交點**：過濾 `IsTargetable`，並以任務中心為圓心限制半徑（任務半徑＋25，下限 100）— `Utilities/Utils.cs`（`TryGetObjectCollectionPoint`）、`Task_TurninMission.TurninMission`｜靜默：跑去別區或點不動的繳交點｜驗：Information「已選定緊急任務繳交點」｜`8f9fc7b`（cycleapple）
- **確認框的比對基準取自台服 Addon 表**（修理 860～863、套裝主手 4384／4385／4388、收藏品 1463／3815、宇宙好運道 16915；「已經學會」4937／11501／11506），不寫死字串 — `Utilities/YesnoGuard.cs`、`Scheduler/Tasks/Task_BuyCosmoItems.cs`｜靜默：比對不到時「只記錄／按取消」檔位沒作用｜驗：設定頁用橘字列出沒有比對基準的情境；「只記錄」檔位會把認不出的文字寫進 log｜`8c50dc8` `f60a991`
- **精選萃取前先驗解鎖任務 638** — `Scheduler/Tasks/Task_Spiritbond.cs`（`IsSpiritbondReadyAny`）｜靜默：卡在萃取那一步｜驗：`IsSpiritbondReadyAny` 開頭的 `QuestManager.IsQuestComplete(638)`｜`a2d5273`（上游 `4b8d3a7` 的一段）
- **尚未開放的道具名稱是空字串**：台服 Item 表保留列但 Name 空，轉盤設定改顯示「（此版本尚未開放）」— `Ui/MainUi/Settings/Settings_Table/GambaWheel.cs`｜看得見：一片空白｜驗：轉盤設定頁｜`6703c66`
- **天氣預報整段沒變時只有一筆資料**，讀 `[1]` 前先判長度 — `Scheduler/Handlers/WeatherForecastHandler.cs`｜看得見：越界例外｜驗：`GetNextWeather` 在不足兩筆時回 default｜`54d3880`
- **工程管理（掌握）解鎖判定**：並跑 `UnlockLink` 判定、不一致時印 Information；權威仍是國際服寫死的狀態碼 574／586 — `Utilities/PlayerHelper.cs`｜靜默（舊判定在台服若不對）：製作少用一個技能｜驗：不一致時的 Information｜`e96b83e`（cycleapple）
- **本艦隊 Dalamud pin 的差異**：`TerritoryChanged` 委派是 `Action<ushort>`（上游 `uint`）；`DataId`→`BaseId`、`ClientState.LocalPlayer`→`IObjectTable.LocalPlayer` 是純改名 — `ICE.cs`（`OnTerritoryChange`）｜看得見：編譯錯誤｜驗：用本艦隊 pin 建置｜`a2d5273` `6330590`

## 2. 排程與任務流程

- **錯誤出口不再把整個 ICE 叫起來**：中止路徑走 `SchedulerMain.AbortToStateCheck()`（排程本來在跑才回 Start）— `Scheduler/SchedulerMain.cs`｜靜默：偵錯按鈕或轉盤中途失敗 ⇒ 整套自動化自己跑起來｜驗：`git grep -n AbortToStateCheck -- ICE` 涵蓋各中止出口｜`da73860` `e29675c`
- **修理的兩個死結**：`CloseRepair` 收尾回 true；「離開修理狀態」移到 `if (需要修理)` 外面 — `Scheduler/Tasks/Task_Repair.cs`｜靜默：佇列卡 30 分鐘／永遠停在修理狀態｜驗：別的插件先修好裝備時狀態會離開修理｜`f186bc7` `c639c7f`（cycleapple `9d5a8f0`）
- **宇宙製作**：拿掉多送一次的 `CraftItem`；材料檢查改成全材料×每次用量；等 Artisan 加斷路器（看背包成品數、退避 3／6／12／24 秒、連 5 次沒進展就停並說明）— `Scheduler/Tasks/Task_Craft.cs`、`Task_DualClass.cs`、`Utilities/CraftProgressGuard.cs`｜靜默：同配方送兩次、高估做得起、每 1.35 秒重下指令｜驗：停下時聊天視窗指名道具與各素材持有／需要數｜`c0fe03c` `ee830fc`
- **換區中背包讀成 0 時不做破壞性判斷**：`PlayerHelper.InventoryReadable()` 擋在「沒餌就放棄」「材料不夠就放棄」這類判斷前 — `Utilities/PlayerHelper.cs`｜靜默：身上 999 個餌卻判成沒餌而放棄任務｜驗：真的判定沒餌時 log 會列出每種餌的數量｜`8daada5` `8f283dd`
- **重骰上限與自動換職業**：`MaxConsecutiveRerolls`（預設 10，0＝不限）；候選池空時照職業優先度換到還有未金星任務的職業（`AutoSwitchJobWhenPoolEmpty`，預設關；換成功才更新 `Mission_Settings.StartJob`）— `Scheduler/Tasks/Task_FindMission.cs`｜靜默：無限重骰卡原地｜驗：達上限時停止並在聊天視窗說明｜`40173f0` `a6cbbc4`
- **「取得金星後停用」不鎖死連續任務鏈**（`MissionChain.ShouldKeepEnabledForChain`、`RepairSequentialPrerequisites`），另有緊急任務例外 `RemoveAfterGoldKeepCritical` — `Utilities/Cosmic_Helper/MissionChain.cs`、`Task_TurninMission.GoldCheck`｜靜默：後續任務再也接不到｜驗：Information「保留任務 …」｜`3f31cf3` `aaea6be`
- **逾時先驗分再決定交件或放棄**（`ApplyTimeoutDecision`）；限量採集點採光就收手（`Mission_Settings.ExhaustedGatheringNodes`，取代從未遞增的 `nodeTotal`）— `Scheduler/Tasks/Task_CheckScore.cs`、`Task_Gather.cs`｜靜默：做完的任務連成品一起放棄／乾等逾時｜驗：Information「任務逾時：已達交件門檻，改為交件」｜`9cd521a` `2d95ea6`（cycleapple `65a5806`）
- **採集挑點**：三個挑點時機都走就近挑點 `Task_Gather.TrySelectClosestNode`（`GatherPickClosestNode` 預設開；取法來自 4liang0121 `api13-tw` 的 `1f2028c`）；自訂路線讀設定資料夾的 `CustomRoutes`（以區域＋標記座標覆寫內建）；接近角 `GetGatherApproachPosition` 吸附導航網格失敗就退回 `LandZone` — `Scheduler/Tasks/Task_Gather.cs`、`Task_FindMission.cs`、`Utilities/GatheringHelper/GatheringRouteLoader.cs`｜靜默：照路線檔順序跑去遠的點｜驗：挑點各路徑都有 Information；設定頁「採集路線」列出內建／自訂與載入失敗數｜`deb6b9e` `63de790` `fcf1359`（cycleapple）
- **三個上游缺陷**：騎乘判定 `inMission` 反了、雙職任務拿不到 Dual Class 採集設定檔（`GatherSettings.SetupAllProfiles` 的順序）、`PlayerHelper.UsingSupportedJob` 的 `||` 恆真。上游現況：`inMission` 與 `UsingSupportedJob` 已修 — `Scheduler/Tasks/Task_NavmeshMove.cs` 等｜靜默｜驗：讀碼（`inMission` 要是 `CurrentLunarMission != 0`）｜`395fcf1` `b9ece96`（cycleapple）`ee89151`
- **任務挑選與統計（功能，預設維持原行為）**：沒金星的優先、階級順序可調（設定缺的階級補在後面）、依表格排序挑任務、金星率門檻與「有效每分成果」（放棄耗時計入）、任務預設組、本區本職全金就停、宇宙工具經驗可涵蓋緊急／臨時任務 — `Task_FindMission.cs`、`Utilities/MissionTimer.cs`、`Config/MissionConfigs.cs`｜驗：設定頁「任務優先度」「停止條件」｜`6703c66` `692cf9c` `840a040` `42f4841` `b9f9f45` `19b1732` `8ee5eb2`
- **可選的安全與收手（功能，預設關或原行為）**：其餘自動按下的確認框三檔 `UnexpectedYesno`（AlwaysConfirm＝原行為）、點數購物「已學會就不買」與「尊重遊戲的已經學會提示」、材料算不到金星時收手 `CraftGoldUnreachable`（Off／Notify／Abandon；Abandon 實際上多半連銀星一起丟掉）、宇宙轉盤手動執行一次 — `Utilities/YesnoGuard.cs`、`Task_BuyCosmoItems.cs`、`Utilities/Cosmic_Helper/CraftGoldFeasibility.cs`、`Task_Gamba.cs`｜驗：預設值見 `Config/MissionConfigs.cs`｜`8c50dc8` `f60a991` `093927a` `51ad14e` `13e62a0` `e29675c`

## 3. 原生層安全

- **`WKSManager.Instance()` 在宇宙區外回 null**：解參考前一律判空，讀不到回「不知道」（分數不畫、不觸發停手）— `Utilities/Cosmic_Helper/Mission_Info.cs`、`Utilities/CosmicHandler.cs`、`Ui/Relic_XP.cs`、`Scheduler/Tasks/Task_CheckState.cs`、偵錯分頁｜崩潰｜驗：`git grep -n "WKSManager.Instance()" -- ICE` 每一處之後都有判空｜`98b26ae` `c8f75b4`
- **UI 節點鏈逐層判空**：按鈕啟用判斷用 `GenericHelpers.IsComponentEnabled`（`IsEnabled` 解的是 `OwnerNode`）；`AddonHelper.GetAtkTextNode`、`GetNodeByIDChain`、`Task_Spiritbond.TryGetSpiritbondTextNode` — `Utilities/AddonHelper.cs` 等｜崩潰｜驗：`git grep -n IsEnabled -- ICE` 只剩註解｜`b0465d4`
- **Agent／Framework／模組取得器判空**：三個插旗函式（`Utils.SetFlagForNPC`、`SetRawFlagforNpc`、`SetGatheringRing`）第一件事是寫入代理人欄位；另有 `PlayerHandlers.GetEorzeaTime`、`GearsetHandler.TaskClassChange`、`AddonHelper.IsAddonActive`、`Task_Fishing.FacePosition` — `Utilities/Utils.cs` 等｜崩潰｜驗：取不到代理人時插旗函式寫 Information 並返回｜`43aeacb` `ac35e66` `f4d7156` `ac4394a`
- **轉盤**：信用點道具陣列有上界（`Task_Gamba.TryGetCosmoCreditItemId`），兩顆輪盤按鈕都判空、值相同就不重寫（`TrySelectWheel`）— `Scheduler/Tasks/Task_Gamba.cs`、`Task_CheckState.cs`｜看得見：越界／往位址 0xE8 寫入｜驗：只找得到一顆輪盤按鈕時寫 Information、不寫入｜`e29675c` `c8f75b4` `86453c9`
- **原生文字先剝 payload 再判「視窗記憶體在變動」**：守衛前讀的文字用 `GetText()`（`ReadSeString`），不用 `Utf8String.ToString()`（含道具連結會解出 U+FFFD，`AddonPressGuard.IsTextCorrupt` 就永遠成立）— `Scheduler/Tasks/Task_Spiritbond.cs`｜靜默：精製永遠不動作｜驗：`Task_Spiritbond` 讀鍊性值走 `GetText()`｜`83475ba`
- **繪製路徑不擲例外**：`GetRow`→`TryGetRow`、字典索引→`TryGetValue`、學會狀態查詢第一次失敗就閂成「不知道」— `Ui/DebugWindowTabs/Ui_MapTesting.cs`、`ShoppingTab.cs`、`Hud_ItemExchange.cs`、`Task_BuyCosmoItems.cs`｜看得見：視窗整個壞掉｜驗：這幾處用 `TryGetRow`｜`fbd9c44` `b1a62bb` `093927a`
- **搬進 ICE 的 AddonMaster**（上游 ECommons 已刪的 `Shop`／`ShopExchangeCurrency`／`InclusionShop`）：寫死的 AtkValue 索引都補了 `AtkValuesCount` 邊界；索引值本身照國際服，台服沒證明過相同 — `Utilities/AddonMasters/*.cs`｜崩潰：`NumEntries` 讀到垃圾時越界｜驗：兌換視窗那行 Information（點數／AtkValue 總數／解析到幾件）｜`b7b354b` `b1a62bb`
- **任務面板分數**：ECommons fork 的 `WKSMissionInfomation` 分數改 `uint?`、做型別／邊界／null 三重檢查、時間型門檻不再被壓成假分數、多一個 `CriticalScoreRaw`；ICE 端讀不到畫「?」不畫 0 — 子模組 `ECommons/.../WKSMissionInfomation.cs`、`Ui/OverlayWindow.cs`｜崩潰：接高難任務時面板一載入就閃退｜驗：疊加層讀不到分數時畫黃色「?」｜`4cf9b28` `45b2a9d` `71fc248` `7b8caba`
- **任務目標讀取器不寫死索引、不跨幀存節點指標**：走訪節點樹，只取「說明＋n/m」形狀，可見性讀 `NodeFlags` — `Ui/MissionObjectiveReader.cs`｜崩潰／顯示錯｜驗：面板就緒卻抓不到目標列時有 Information｜`8ee5eb2` `c51adfb`
- **偵錯按鈕 `MoveItemSlot` 帶 `a6: true`**（false＝只改本機、不送伺服器）— `Ui/DebugWindowTabs/Ui_TestButtons.cs`｜靜默：道具看起來動了、下一次同步又彈回去｜驗：呼叫處第 6 個參數｜`6614e1c`
- 機甲行動的指標驗證見第 8 節；IPC 端點回主執行緒見第 4 節。

## 4. 艦隊整合（跨插件）

- **AutoRetainer 壓制租約**：排程啟動時 `AcquireSuppressionFor`（Guid 憑證），`ICE.Tick` 每幀 `Sync`、30 秒續約，續約回 false 就重取，停止／卸載時歸還；舊版沒這組端點時照常跑 — `IPC/AutoRetainerIPC.cs`｜看得見：任務之間角色被登出去跑僱員｜驗：Information「已請 AutoRetainer 在 ICE 跑完之前不要動」｜`3014815` `bc821da`
- **vnavmesh 路徑容許值租約**：取代對全域 `Path.SetTolerance` 的單向寫入；IPC 全在鎖外、用版本序號守衛；舊版 vnavmesh 退回 `SetTolerance` — `IPC/NavmeshIPC.cs`（`RenewToleranceLease`、`MutateToleranceLocked`）｜靜默：永久壓著別人的全域值｜驗：Information「已請 vnavmesh 在 ICE 導航期間把路徑容許值押成」｜`92aa064` `51ac345`
- **AutoHook 啟用開關用借的**：第一次進釣魚段先拍使用者的值再打開，回 Idle／卸載時「本來關著」就關回去；端點存不存在用 `HasFunction` 判，不看回傳值 — `IPC/AutoHookIPC.cs`（`SyncBorrow`、`ReturnEnabled`）｜靜默：跑完一趟 AutoHook 一直開著｜驗：舊版沒有 `GetPluginState` 時有 Information 說明不會幫忙關回去｜`0f417ea`
- **Artisan 臨時求解器**：送 `CraftItem` 前先 `GetAvailableSolverTypes` 確認、再 `SetTemporarySolverByType`；任務結束、停止、卸載一定還；不再去改使用者的 Artisan 設定檔。型別全名用定義類別，`RaphaelSolverDefintion` 的拼字是 Artisan 原樣、不可訂正 — `Utilities/CosmicSolverOverride.cs`、`IPC/ArtisanIPC.cs`、設定 `CraftSolverOverride`（預設 Expert）｜靜默：專家配方被一般求解器接手｜驗：型別不在清單時有 Information｜`1cff636`
- **TataruPraise**：金評交件誇一句（接在 `MissionTimer.UpdateMissionStats` 的金評分支），自動化停下來／卡住各說一句（一輪最多一句）；前置問 `IsAvailableFor(情境)`；用 Dalamud 原生 `GetIpcSubscriber`，不用 EzIPC — `IPC/TataruPraiseIPC.cs`｜純通知｜驗：對方沒裝時只寫一次 Information｜`6c6609d` `48e50c8` `a55cb83`
- **ICE 自己的 IPC 端點回主執行緒執行**：10 支包 `IpcFrameworkGate`（在主執行緒就地執行／否則等 5 秒／逾時回「不可用」；卸載中且不在主執行緒時直接回「不可用」），`IsRunning`、`CurrentState` 刻意不包 — `IPC/IpcFrameworkGate.cs`、`IPC/IceCosmicExplorationIPC.cs`｜崩潰：別的插件從背景執行緒呼叫時讀寫原生記憶體與裸集合｜驗：逾時時有 Information｜`2519ef3` `0a6e3ad` `98b7c4c`
- **vnavmesh 提供端擲的例外攔得住**：`PathfindAndMoveTo` 包一層接 `TargetInvocationException`，端點名寫字面值 `"SimpleMove.PathfindAndMoveTo"`（不可用 `%m`，欄位改名就變成不存在的端點）— `IPC/NavmeshIPC.cs`｜看得見：導航網格沒載入時整條佇列被清｜驗：Information「端點自己擲了例外」｜`df973d3`
- **IPC 失敗不再完全靜默**：`EzIpcFailureLog` 訂閱 `EzIPC.OnSafeInvocationException` 印 Information；拿掉兩個永遠失敗的死訂閱端（Artisan `AssignRecipie`、visland `VIsMoveTo`）— `EzIpcFailureLog.cs`｜驗：Information「[EzIPC] 跨外掛 IPC 呼叫失敗」｜`6aeaa18`
- **Lifestream**：只改了 `Repo` 常數，ICE 裡沒有任何呼叫點 — `IPC/LifestreamIPC.cs`｜驗：`git grep -n "Lifestream\." -- ICE` 為 0｜`6d511a9`

## 5. IPC 型別對齊（照搬上游 IPC 檔之前先對提供端）

- 型別不合時 Dalamud 的 CallGate 不一定報錯：`IpcTypeMismatchError` 會被 `SafeWrapper.AnyException` 吞掉；部分組合會走 JSON 來回轉換、靜默拿到垃圾。要改端點形狀，正解是提供端換新名字，不是同名改型別。驗法一律是逐字對提供端 repo 裡註冊的簽章。
- **AutoHook**：見前 10 條第 1 條；舊版 AutoHook 沒有 `SwapBaitById` 時走 `/ahbait <id>` 退路（`AutoHookIPC.TrySwapBait`，有 60 秒可復原的閂鎖）— `94ad344` `02f5909`
- **AutoRetainer 租約是 Guid 形狀**：`Func<string, int, Guid>`／`Func<Guid, bool>`（原本宣告 `Func<string, bool>`，歸還租約變成回傳 true 的空操作）— `IPC/AutoRetainerIPC.cs`｜`bc821da`
- **vnavmesh**：租約端點回傳 `Guid`、`bool`（不可為 null 的值型別）；`Query.Mesh.NearestPoint`／`PointOnFloor` 回 `Vector3?`（上游原本就對，保持）— `IPC/NavmeshIPC.cs`｜`92aa064`
- **Artisan**：`GetAvailableSolverTypes` 回 null＝舊版沒端點、回空陣列＝這個配方沒這個求解器，兩者分開處理 — `IPC/ArtisanIPC.cs`、`Utilities/CosmicSolverOverride.cs`｜`1cff636`
- **TataruPraise**：訂閱端 `ICallGateSubscriber<string, bool>`，舊版沒 `IsAvailableFor` 時擲 `IpcNotReadyError` ⇒ 安靜不出聲 — `IPC/TataruPraiseIPC.cs`｜`48e50c8`

## 6. 設定檔

- **兩套並存**：YAML `Mission Config.yaml`（`MissionConfigs`，所有現役設定）＋EzConfig `DefaultConfig.json`（`Configuration`，舊版設定，只給遷移用）。上游現在只剩 EzConfig `Config`（前 10 條第 2 條）｜驗：設定資料夾裡兩個檔都在
- **世代**：`ConfigVersion` 新檔預設 9，遷移鏈跑到 12；11→12 是我方的 `MigrateMechaGlobalSlidersToPerSkill`。新的遷移從 13 起，不要沿用上游編號 — `Config/ConfigMigrator.cs`｜驗：使用者檔的 `configVersion`｜`16d7388` `161185a`
- **存檔**：`Save()` 走去抖動＋每個路徑一把互斥鎖（啟動時 527 次並行寫入失敗）；序列化在主執行緒拍快照、寫檔在背景，靠序號守衛不寫回舊快照（並行修改時 YamlDotNet 擲的例外原本被吞成「存檔失敗」）— `Config/MissionConfigs.cs`、`Config/YamlConfigManager.cs`｜靜默：設定沒存到｜驗：log 沒有「Failed to save MissionConfigs」｜`e02f893` `161185a` `5571b0a`
- **新鍵**：YamlDotNet 缺鍵吃欄位初始值 ⇒ 新鍵的預設對既有使用者也生效；新設定一律以原行為當預設（刻意的例外：`GatherPickClosestNode` 預設開）— `Config/MissionConfigs.cs`｜驗：欄位初始值
- **舊鍵不刪**：舊的分頁顯示開關 `Show_*`、機甲舊滑桿 `MechaConeAngleDeg`／`MechaDrillLength` 停止讀取但欄位留著（拿掉欄位＝使用者檔裡那些值再也讀不回來）｜驗：欄位還在 `MissionConfigs`｜`8810296` `16d7388`
- **任務鍵 0**：寫入端擋掉，既有的由 `ConfigMigrator.UpdateConfigMissionList` 清掉（冪等）｜驗：使用者檔的 `missionConfig` 沒有鍵 0｜`009a761`

## 7. 在地化

- **機制**（前 10 條第 8 條）：`ECommonsMain.Init` 之後、指令與視窗建構之前呼叫 `Localization.Init("ChineseTraditional")`；ini 以 `Content`＋`PreserveNewest` 帶進輸出與發版 zip｜驗：`ICE` 建構子的呼叫順序；發版 zip 裡有 ini｜`a8ebd6f`
- **ini 格式**：多行 tooltip 寫成一行、用 `\n` 接（實體換行會被當成獨立的壞行丟掉）；多行 `.Loc()` key 的 CRLF 正規化在 ECommons fork 裡（`.cs` 以 CRLF 存時原始字串字面值帶 CR，ini 的 key 是 LF）— `ICE/LanguageChineseTraditional.ini`｜靜默：那幾段顯示英文｜驗：log 沒有 `[Localization] Invalid entry`｜`e17dc78` `c444122`
- **官方譯名**：以台服 sqpack／EXD 為準（例：宇宙衝刺、貢獻之星、套裝、乙太之光、宇宙工具），「外掛」一律改「插件」— `ICE/LanguageChineseTraditional.ini`｜驗：對台服 EXD 的 Action／Status／ItemUICategory 名稱｜`9d4d782` `5a4eb4a` `9c1c570` `f645aed` `e0804f1`
- **範圍**：主介面、設定、疊加層、機甲行動與偵錯視窗都已包 `.Loc()`；不翻的是 ImGui ID、字典 key、設定序列化值（顯示端翻譯）｜驗：`loc_verify.py`（見前 10 條第 8 條）｜`21da10d` `0de9b73` `91a469f` `11e673c`

## 8. 機甲行動（我方新增，上游沒有）

- **範圍與紅線**：`Utilities/MechaOps/*.cs`（11 檔）、`Ui/MechaOpsWindow.cs`、`Ui/DebugWindowTabs/Ui_MechaRecorder.cs`。純顯示與通知，不施放、不走位、不報名，也沒有留自動化入口；總開關 `ShowMechaAoeOverlay` 預設關｜驗：`git grep -n "UseAction\|PathfindAndMoveTo\|InteractWithObject" -- ICE/Utilities/MechaOps` 只有錄製器的說明文字｜`6c2c7a7` 起
- **指標驗證**：`CurrentEvent` 解參考前要在模組自己的 `_events` 陣列內且對齊到格首（`IsInsideEventArray`），驗不過就當讀不到；只讀純量，不碰 `MapMarkerPtrs`／`CurrentStateHandler`／`DataRowPtr` — `Utilities/MechaOps/MechaOpsMonitor.cs`、`MechaObjectives.cs`｜崩潰（少了這層）｜驗：驗不過時有節流過的 Information｜`9fbc9ee` `116f119`
- **部署閘門（預設關）**：`MechaObjectiveUseMarkerVector`（要解參考遊戲堆積上的 vector，活不活驗不了）、`ShowMechaEmergency`（讀 `AgentWKSAnnounce` 的資料，台服配置大小沒驗證）；UI 用紅字寫明開了可能崩 — `Config/MissionConfigs.cs`｜驗：兩個鍵的初始值是 false｜`ea565c5` `52a761b`
- **時間與偏移**：事件時間戳基準是伺服器時間（`MechaOpsMonitor.TryGetServerTime` → `Framework.GetServerTime`），傳送視窗照抄遊戲自己的判定函式。CS 結構偏移（`_events` @ +0x30、步長 0x5130、`CurrentEvent` @ +0xA290、`Flags` @ +0xA2A4）對台服 7.20 執行檔離線反組譯驗過；CS 沒有的駕駛申請書兩個 byte（7.25＝持有 +0xA2A9、資料已到 +0xA2AA）由 `MechaPilotTicketLayout` 在插件建構子裡從遊戲碼特徵碼讀出位移，並驗證落在 CS 的 `Flags` 之後、結構結尾之前，推不出或對不上就讓插件載入失敗— `Utilities/MechaOps/MechaOpsMonitor.cs`、`MechaSchedule.cs`｜驗：改版後對新執行檔重跑離線反組譯｜`116f119` `cf12643` `7b16d34`
- **目標分身份**：身份看 PetHotbar 上的機甲技能；目標白名單由 `WKSMechaEventData` 的群組欄→`WKSMechaEventObjectGroup`→`WKSMechaEventObject` 分成駕駛員／協助員，家族內再用 ObjectKind 補（EventObj＝駕駛員、CardStand＝協助員）；顯示端依身份分流 — `Utilities/MechaOps/MechaObjectNames.cs`、`MechaTargets.cs`、`MechaAoeOverlay.cs`｜驗：錄製器 `[MechaRec]` 的 `roleGate` 欄｜`3ce41ce` `26e64e2` `91307cb` `7365539`
- **隱私**：他人角色名在寫進 log 之前縮寫成「F. L.」（`MechaShowFullPlayerNames` 預設 false）— `Utilities/MechaOps/MechaPrivacy.cs`｜驗：log 裡他人名字是縮寫｜`cf12643`
- **其他顯示**：技能範圍（形狀取自 Action 表；鑽頭 4 米、火焰噴射器 240 度是實測校準值）與逐技能覆蓋、冷卻、proc、事件狀態與進度、下次事件時間、駕駛申請書持有／無／?、報名開放時聊天通知一次（`NotifyMechaSignupOpen` 預設關）、併進疊加層主視窗、事件錄製（偵錯用、不存檔）｜驗：設定頁「機甲行動」｜`491c594` `152cbd0` `9ce082a` `8ad751d` `20ac4b3` `52a761b` `7b16d34` `52b949b` `43cf512` `7a46dff` `cd1312a`

## 9. 記錄與診斷

- **`IceLogging`**：`MinimumLevel`（設定 `LogMinimumLevel`，預設 Verbose＝全寫；Verbose／Debug 用內插字串處理器，閘門關著時洞不求值；Information 以上關不掉）；呼叫點前綴以 `[CallerFilePath]`＋`[CallerLineNumber]` 快取（`GetCallerPrefix` 靠 `StackFrame(3)`，不可再包一層）；環形緩衝改 `Queue` — `Utilities/Cosmic_Helper/IceLogging.cs`｜驗：`MinimumLevel` 的 setter 把值夾在 Info 以下｜`c5b4bdb` `2a63730` `bf5f858`
- **任務逾時指名是哪一步**：`TaskManagerConfiguration.OnTaskTimeout` 印 Error，事件要在 `new TaskManager(...)` 之前掛 — `ICE.cs`｜驗：Error「任務逾時：」｜`726806c`
- **載入時資料盤點**：`[資料盤點]` 報各寫死表對不上目前客戶端的筆數、寫死的 545 分界落到 1237／1291 各幾筆 — `ICEDictornaryCreation.cs`（`ReportHardcodedTableCoverage`，一定要在 `ICE` 建構子最後呼叫）｜驗：載入後的 log｜`4a00652` `ca6820b`
- **鎖內不寫 log**：採集路線載入改成收集後出鎖再寫（`PendingLogs`）；租約 IPC 移到鎖外 — `Utilities/GatheringHelper/GatheringRouteLoader.cs`、`IPC/NavmeshIPC.cs`｜驗：`~/.claude/tools/fleet/lock_io_scan.py`｜`2d87432` `51ac345`
- **降噪**：`[按窗診斷]` 已降到 Verbose；建表逐筆補給品記錄降到 Verbose；認不出的確認框不再逐字元 dump｜驗：一般等級的 log 看不到這些行｜`54786f3` `184c8ca` `ac24979` `48b10ad`

## 10. 建置、子模組、CI

- **子模組指 org fork**：ECommons → `ffxiv-tc-port/ECommons`（分支 `pin-wrathcombo-tc-api13`）、Pictomancy → `ffxiv-tc-port/ffxiv_pictomancy-sourpuh`（分支 `tc-7.20`；是 sourpuh 原版，不是 Splatoon 用的那支）。fork 裡帶著本清單用到的修正（`WKSMissionInfomation` 分數、`.Loc()` CRLF key、`FrameThrottler` 改用 `Framework.Update` 計時、原生取得器判空、台服世界 IsPublic）。換回上游子模組＝這些一起消失 — `.gitmodules`｜驗：`git submodule status` 與 `.gitmodules` 的網址｜`b7b354b` `774accb` `952b394` `c444122` `744c10e`
- **版本與 manifest**：`ICE.csproj` 用艦隊的 `BuildNumber.txt` 版號機制（發版 tag 紀元 `v7.25.x.y`）；`ICE.json` 補 `InternalName`／`RepoUrl`／`IconUrl`、繁中說明；`Data/Icon.png` 轉 RGBA（RGB 在遊戲內顯示成問號）｜驗：feed 上那顆 zip 的 manifest｜`896c497`
- **CI**：自架 runner、pinned Dalamud 從 runner 本機取並明傳 `DalamudLibPath`、子模組用本機私鑰換的 App 權杖 checkout — `.github/workflows/`｜驗：workflow 的 `runs-on` 與 checkout 步驟｜`d0148dc` `6302cb9` `4f1ee12`
- **遊戲 ESC 不被擋**：四個視窗 `RespectCloseHotkey = false`（上游只有疊加層）— `Ui/MainWindow.cs`、`OverlayWindow.cs`、`InfoWindow.cs`、`DebugWindow.cs`｜看得見：ICE 視窗有焦點時 ESC 失效｜驗：四個建構子｜`a314862`

## 看起來像 bug、其實是刻意的（不要順手改）

- `Task_BuyCosmoItems.TryPurchaseItem` 被節流擋下時回 true：三個呼叫端共用同一把節流 key，改成 false 會把沒買完的採購提早結束｜`a691b9f`
- `Task_AbandonMission` 第一個 tick 按回報、下一個 tick 才按放棄（靠兩把節流交替）；外層 1000ms 節流只管到「漁夫先收竿」那一塊｜`e1100c0` `13e62a0`
- 認不出的放棄確認框仍按「否」（cycleapple 改成不碰，那是行為變更，沒採用）｜`ac24979`
- `IsRunning`／`CurrentState` 兩個 IPC 端點不上主執行緒閘門（逾時回 false 會被讀成「ICE 沒在跑」）｜`0a6e3ad`
- `InfoWindow` 的 `HasGatheringSetup` 恆為 false，連同 else 分支原樣保留｜`d98e94b`
- `ImGuiEx.Icon` 會留一個待處理的 `SameLine`，疊加層在呼叫端補 `ImGui.NewLine()`，不改 ECommons｜`6865668`

## 已知缺口（改基底或台服開新星時要一起處理）

- `NpcData.MoonNpcs[1291]` 沒有 `AlternateNpcIds` ⇒ 台服開 Phaenna 時會重演第 3 條｜`4a00652`
- 1291 的寫死資料都沒有台服驗證：`DictionaryCreation` 的 `keyId < 545` 分界、`PhaennaMapV2`、`HubCheck` 的 1291 中心座標；Oizys（1040..1369）與 Auxesia（1370..1702）我方完全沒有｜`ca6820b` `ac4394a` `438547f`
- 時間型任務的獎章判定疑似在台服恆判銅：`Task_CheckScore.ParseRequirementTime` 取空白切開的第一段當時間，而台服面板條件字串是「剩餘時間 25:10以上」（時間在第二段）。使用者設定裡 4 個時間型任務的交件紀錄全是 Bronze、分數增量恰為表值 5 倍。未實機驗證；修它會改變時間型任務的交件時機。證據看 Information `[Medal Check]`（每個時間型任務記一行：三個節點的原文、解析秒數、資料表門檻、判定獎章）｜`262f63d` 的內文
- 工程管理解鎖的權威判定仍是國際服寫死的狀態碼 574／586｜`e96b83e`
- 機甲行動的模組偏移是對台服 7.20 驗的，每次改版要重驗｜`116f119` `7b16d34`
- 我方 AutoHook 沒有 `AH6_` 的 `ConditionSet`／`Triggers` 模型 ⇒ 494／495 的階段切換不會發生（全程用第一階段；交件判定在 ICE，不受影響）｜`c8f75b4` `bee4831`
