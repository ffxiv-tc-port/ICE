using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ICE.Ui.DebugWindowTabs;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;
using static ICE.Utilities.GatheringHelper.GatheringUtil;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_Fishing
    {
        // Something to note. 42, 43, 85 are the conditions that you get while you're fishing
        // 43 and 85 are active while you're fishing
        // 42 is active when reeling in a fish
        // Something to consider, start fishing... (that's condition 42 when you start)
        // Whenever all the conditions are cleared, check the inventory for the frame, see if you have enough/meet the score

        private static FishingDebug _fishingDebug = null;

        public static void Enqueue()
        {
            // think the process should be:
            // check score
            // if score not complete, check if can craft
            // if craft not required, fish
            // wait for fishing to be done

            LogFishingEntryState();

            // 這兩個都必須帶 Utils.TaskConfig。NeoTaskManager 的預設是 TimeLimitMS = 30000
            // 且 AbortOnTimeout = true，而 FishingCheck 在「走去釣點 / 等餌裝上 / 等咬鉤」時
            // 本來就會連續回傳 false 好幾分鐘 —— 用預設值會每 30 秒把整個佇列 Abort 一次，
            // 表現出來就是「動一下又重來」而完全沒有錯誤訊息。
            // （同樣的坑在 Task_Craft.Enqueue 已經修過一次，見該處註解。）
            P.TaskManager.Enqueue(() => Task_CheckScore.Fish(), "Checking fishing score", Utils.TaskConfig);
            P.TaskManager.Enqueue(() => FishingCheck(), "Standard fishing check", Utils.TaskConfig);
        }

        /// <summary>
        /// 進入釣魚流程時印一次現況。釣魚卡住的回報幾乎都缺這幾個欄位，
        /// 沒有它們就只能靠猜「停在哪一步」，所以刻意寫 Information 等級
        /// （使用者的記錄等級只會濾掉 Verbose、Debug 收得到但單檔數十萬行會淹沒）。
        /// </summary>
        private static void LogFishingEntryState()
        {
            if (!EzThrottler.Throttle("ICE: fishing entry state log", 10000))
                return;

            var missionId = CosmicHelper.CurrentLunarMission;
            var missionName = CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var sheetInfo) ? sheetInfo.Name : "(不在任務表裡)";
            var presetCount = GatheringUtil.FishingPreset.TryGetValue(missionId, out var preset) ? preset.FishingPreset.Count : -1;
            var baitCount = GatheringUtil.MoonBaits.Sum(x => x.Value.Count);

            IceLogging.Info($"進入釣魚流程。任務 {missionId}「{missionName}」｜AutoHook 已安裝: {P.AutoHook.Installed}" +
                            $"｜內建 preset 筆數: {(presetCount < 0 ? "任務不在 preset 表裡" : presetCount.ToString())}" +
                            $"｜已知月面餌種類: {baitCount}｜目前掛的餌: {CosmicHelper.CurrentBait?.ToString() ?? "null(不在任務中)"}" +
                            $"｜目前職業: {Player.JobId}", "[Task_Fishing]");
        }

        private static int BaitCounter = 0;

        // ⚠️ 這裡原本有一份 private PlayerIsUsable()。同樣的守衛在 Task_Craft / Task_DualClass /
        //    Task_CheckScore 也需要，所以提升成 PlayerHelper.InventoryReadable() 共用一份 ——
        //    「換區期間讀到 0 就做破壞性決定」這個 bug class 只有集中在一個地方才守得住。

        /// <summary>
        /// 「沒餌了」是會直接放棄任務的破壞性判斷，所以把判定當下每一種餌的實際數量都印出來。
        /// 2026-08-03 實機出現過假陽性：使用者身上有 999 個宇宙蛾蛹，卻在被機甲行動傳送走的
        /// 那一瞬間被判定成沒餌並放棄了任務 —— 沒有這份清單就只能用猜的。
        /// </summary>
        private static void LogOutOfBait(string reason, string handle)
        {
            var detail = string.Join("、", GatheringUtil.MoonBaits
                .SelectMany(b => b.Value.Select(id => new { Name = b.Key, Id = id }))
                .Select(x => $"{x.Name}({x.Id})={(PlayerHelper.GetItemCount(x.Id, out var c) ? c.ToString() : "讀取失敗")}"));

            IceLogging.ChatError($"{reason}，準備回報／放棄任務。" +
                                 $"（掛著的餌: {CosmicHelper.CurrentBait?.ToString() ?? "null"}，" +
                                 $"玩家可用: {PlayerHelper.InventoryReadable()}）", "[ICE]");
            IceLogging.Info($"放棄任務前的餌存量明細：{detail}", handle);
        }

        private static unsafe bool? FishingCheck()
        {
            if (_fishingDebug == null)
            {
                _fishingDebug = new FishingDebug();
            }

            string handle = "[Standard Fishing: Fishing Check]";

            // 遊戲端可能在我們釣魚的中途把任務取消掉（實例：被機甲行動抽中當駕駛員直接傳送走，
            // 系統訊息「放棄了探索任務」）。這時 CurrentLunarMission 會變 0，而 SheetMissionDict
            // 沒有 key 0（建表時 Name 為空的 row 被跳過）→ 直接索引就是 KeyNotFoundException。
            // 例外發生在任務裡只會表現成「卡住不動」，所以在碰任何任務資料之前先退回重新判斷狀態。
            // （原本這裡是自己寫一次 ContainsKey + 清佇列 + 回 Start；現在收斂成全排程器共用的閘門。）
            if (SchedulerMain.CurrentMissionUnavailable(handle, out var currentMission))
                return true;

            // 傳送 / 讀取地圖途中 InventoryManager 讀不到東西，所有 GetItemCount 都會回 0。
            // 底下「沒餌了 → 放棄任務」是破壞性判斷，在這種瞬間做會直接誤殺一個好好的任務，
            // 所以整個檢查在玩家不可用時一律先等。
            if (!PlayerHelper.InventoryReadable())
            {
                if (EzThrottler.Throttle("ICE: fishing player unavailable log", 5000))
                    IceLogging.Info("玩家目前處於傳送／讀取中，暫停釣魚判斷（此時道具數量讀出來會全是 0）。", handle);
                return false;
            }

            if (Player.Mounted)
            {
                Utils.Dismount();
                return false;
            }

            if (EzThrottler.Throttle("Throttling intro message", 1000))
            {
                IceLogging.Debug("Checking to see where we need to be here", handle);
            }
            bool hasBait = false;

            if (CosmicHelper.CurrentBait == 0)
            {
                if (EzThrottler.Throttle("Equipping bait"))
                {
                    foreach (var bait in GatheringUtil.MoonBaits)
                    {
                        foreach (var baitId in bait.Value)
                        {
                            if (PlayerHelper.GetItemCount(baitId, out var count) && count > 0)
                            {
                                // TrySwapBait 會在 IPC 不可用時退回 /ahbait 指令，並且把結果寫進 log。
                                // 直接呼叫 P.AutoHook.SwapBaitById 會在台服的 AutoHook 上靜默失敗。
                                P.AutoHook.TrySwapBait(baitId);
                                if (EzThrottler.Throttle("ICE: fishing bait equip log", 5000))
                                    IceLogging.Info($"目前沒有掛餌，要求裝上餌 ID {baitId}（{bait.Key}）。", handle);
                                return false;
                            }
                        }
                    }

                    LogOutOfBait("沒有掛餌，而且身上找不到任何可用的月面餌", handle);
                    SchedulerMain.State = IceState.AbandonMission;
                    P.TaskManager.Tasks.Clear();
                    return true;
                }
                return false;
            }

            // little check here for seeing if we have any baits
            foreach (var bait in GatheringUtil.MoonBaits)
            {
                foreach (var baitId in bait.Value)
                {
                    if (PlayerHelper.GetItemCount(baitId, out var count) && count > 0)
                    {
                        if (EzThrottler.Throttle("Throttling bait message", 1000))
                            IceLogging.Debug("We have the bait! Continuing onwards");
                        hasBait = true;
                        break;
                    }
                }
            }

            if (!hasBait)
            {
                LogOutOfBait("身上找不到任何可用的月面餌", handle);
                SchedulerMain.State = IceState.AbandonMission;
                P.TaskManager.Tasks.Clear();
                return true;
            }
            // 用方法開頭那次守衛拿到的 currentMission，不要再讀一次 CurrentMissionInfo ——
            // 少一次遊戲讀取，也保證整個方法看到的是同一筆任務資料。
            else if (currentMission.Attributes.HasFlag(MissionAttributes.Collectables) && !PlayerHelper.HasStatusId(805))
            {
                if (EzThrottler.Throttle("Log Throttle for fishing"))
                    IceLogging.Debug("We need to apply collector's glove", "Task_Start Fishing");

                if (!Player.IsBusy)
                {
                    if (EzThrottler.Throttle("Attempting to turn on collectability"))
                        ActionManager.Instance()->UseAction(ActionType.Action, 4101);
                }
                return false;
            }
            else if (!Svc.Condition[ConditionFlag.Gathering])
            {
                if (!_fishingDebug.IsFishable())
                {
                    if (_fishingDebug.FindFishableLocation(out var fishablePos, searchSteps:64))
                    {
                        IceLogging.Info("We're not in a fishable angle, so going to face one", handle);
                        P.TaskManager.Tasks.Clear();
                        P.TaskManager.Enqueue(() => FacePosition(fishablePos.Value));
                        return true;
                    }
                    else
                    {
                        IceLogging.Debug("Our current fishing position isn't viable. So going to move to the next fishing spot");
                        var flag = currentMission.MapPosition;
                        var territoryId = currentMission.TerritoryId;

                        var nextFishingSpot = GetNextFishingSpot(territoryId, flag, Player.Position);
                        if (nextFishingSpot != null)
                        {
                            IceLogging.Info($"We found another fishing spot to move to! {nextFishingSpot.FishingSpot} | moving to it");
                            P.TaskManager.Tasks.Clear();
                            // InitiateMoving 在 navmesh 還沒建好時會一直回 false（建圖是分鐘級），
                            // 用預設 30 秒逾時 + AbortOnTimeout 會直接把佇列砍掉。
                            P.TaskManager.Enqueue(() => InitiateMoving(nextFishingSpot.FishingSpot), "Vnav moving to fishing", Utils.TaskConfig);
                            return true;
                        }

                        // 這裡是原本會「靜默永遠不動」的死路：站的位置不能釣、又查不到任何備用釣點，
                        // 就一路 return false 下去，而唯一的線索是上面那行 Debug（使用者的記錄等級看不到）。
                        if (EzThrottler.Throttle("ICE: no fishing spot data log", 10000))
                        {
                            var hasZone = MoonFishingLocations.ContainsKey(territoryId);
                            var spotCount = hasZone && MoonFishingLocations[territoryId].TryGetValue(flag, out var zoneSpots) ? zoneSpots.Count : 0;
                            IceLogging.Info($"目前位置不能釣魚，而且找不到可以移動過去的釣點：" +
                                            $"地區 {territoryId} 是否有釣點資料 = {hasZone}，任務標記 {flag} 下的釣點筆數 = {spotCount}。" +
                                            $"（釣點資料是寫死在 GatheringUtil.MoonFishingLocations 裡的，這筆缺了就只能手動釣。）", handle);
                        }
                    }
                }
                else if (EzThrottler.Throttle("Starting to fish", 1000))
                {
                    IceLogging.Info("已站在可釣位置，送出 /ahstart 開始釣魚。", handle);
                    // ActionManager.Instance()->UseAction(ActionType.Action, 289);
                    Svc.Commands.ProcessCommand("/ahstart");
                }
                // ⚠️ BaitCounter 不是「重試次數」。它掛在「Starting to fish」那個 1 秒節流的 else 上，
                // 所以在兩次 /ahstart 之間本來就會被加個 1~2 次，跟餌一點關係都沒有。
                // 訊息照這個實際語意寫，不要再講成「重試 N 次」誤導判讀。
                else if (EzThrottler.Throttle("Adding counter for bait not equipped"))
                {
                    BaitCounter++;
                    IceLogging.Debug($"Adding 1 to the counter. Counter is at: {BaitCounter}");
                    if (BaitCounter >= 2)
                    {
                        foreach (var bait in GatheringUtil.MoonBaits)
                        {
                            foreach (var baitId in bait.Value)
                            {
                                if (PlayerHelper.GetItemCount(baitId, out var count) && count > 0)
                                {
                                    // 已經掛著同一種餌就不必再送指令，否則會在等待甩竿的空檔一直重送。
                                    if (CosmicHelper.CurrentBait == baitId)
                                        return false;

                                    P.AutoHook.TrySwapBait(baitId);
                                    if (EzThrottler.Throttle("ICE: fishing bait recheck log", 5000))
                                        IceLogging.Info($"還沒開始釣魚，且掛著的餌（{CosmicHelper.CurrentBait?.ToString() ?? "無"}）" +
                                                        $"不是預期的餌，要求改掛 {baitId}（{bait.Key}）。", handle);
                                    return false;
                                }
                            }
                        }
                    }
                }
                return false;
            }
            else
            {
                // Means we are fishing, all we need to do is enable autohook then wait for us to get the amount of fish we need
                P.AutoHook.SetPluginState(true);
                IceLogging.Info("We're starting to fish. So kicking it over to checking the fish items", handle);
                P.TaskManager.Insert(() => WaitToStartFishing(), "Waiting till we actually start fishing", Utils.TaskConfig);
                BaitCounter = 0;
                return true;
            }
        }

        /// <summary>
        /// ConditionFlag.Fishing 轉為 true 的時刻，用來量「這次到底有沒有真的甩竿」。
        /// AutoHook 在甩竿前會先放大物狙擊之類的動作，那會讓這個旗標短暫翻起再落下（實測約 130ms），
        /// 而 ICE 目前只要看到旗標落下就當成「釣完了」→ 回去查分 → 重新送一次 /ahstart。
        /// 先量測、不改行為：把每一次的持續時間寫進 log，才有依據決定要不要加最小持續時間門檻。
        /// </summary>
        private static DateTime _fishingStartedAt = DateTime.MinValue;

        private static unsafe bool? WaitToStartFishing()
        {
            if (Svc.Condition[ConditionFlag.Fishing])
            {
                _fishingStartedAt = DateTime.Now;
                IceLogging.Info("We've started fishing, just going to wait for it to finish", "[Fishing: Waiting]");
                P.TaskManager.Insert(() => FinishFishing(), "Waiting for fishing to finish", Utils.TaskConfig);
                return true;
            }
            if (!Svc.Condition[ConditionFlag.Gathering])
            {
                IceLogging.Info("Somehow, we've gotten here and we're not fishing?? Going to check the score for a timer check", "[Fishing: Waiting]");
                P.TaskManager.Tasks.Clear();
                return true;
            }

            return false;
        }

        private static int collectableCounter = 0;
        private static unsafe bool? FinishFishing()
        {
            if (!Svc.Condition[ConditionFlag.Fishing])
            {
                var held = _fishingStartedAt == DateTime.MinValue ? TimeSpan.Zero : DateTime.Now - _fishingStartedAt;
                _fishingStartedAt = DateTime.MinValue;

                if (held > TimeSpan.Zero && held < TimeSpan.FromSeconds(1))
                {
                    // 這種「不到一秒就結束」的幾乎都不是真的釣完，而是 AutoHook 放輔助技能造成的旗標閃爍。
                    // 目前刻意不擋（擋錯會讓真的釣完被漏掉），只標記出來讓 log 看得出比例。
                    IceLogging.Info($"釣魚狀態只維持了 {held.TotalMilliseconds:F0} ms 就結束，" +
                                    "這通常不是真的釣完（多半是 AutoHook 施放輔助技能造成的狀態閃爍）。" +
                                    "接下來會回去查分並重新送一次 /ahstart。", "[Fishing: Finished]");
                }
                else
                {
                    IceLogging.Info($"We're done fishing, time to go back to the score check（本次持續 {held.TotalSeconds:F1} 秒）", "[Fishing: Finished]");
                }
                return true;
            }
            else
            {
                if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var yesNo) && yesNo.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Adding +1 to counter", 250))
                    {
                        collectableCounter += 1;
                    }
                    if (collectableCounter >= 2)
                    {
                        // 閘門預設是「一律按下確定」＝與原本完全相同（見 YesnoGuard）。
                        // 🔴 YesnoPressGuard 放在最後（它有副作用）：節流記的是 key 上次放行的時刻，
                        //    不是「這扇窗已經按過」，擋不住同一扇窗在關閉中被重按（＝原生 AVE）。
                        if (EzThrottler.Throttle("Selecting yes to collectables")
                            && YesnoGuard.ShouldConfirm(YesnoSituation.FishingCollect)
                            && YesnoPressGuard.MayPress("釣魚：收為收藏品確認", (nint)yesNo.Base))
                        {
                            yesNo.Yes();
                        }
                        return false;
                    }
                    return false;
                }
            }
            if (collectableCounter != 0)
                collectableCounter = 0;

            return false;
        }

        public static unsafe bool? FacePosition(Vector3 pos, float tolerance = 0.1f)
        {
            float currentRotation = Player.Rotation;

            // If rotation is still changing, wait for it to stabilize
            if (Math.Abs(Player.Rotation - currentRotation) > tolerance)
            {
                return false;
            }

            Vector3 direction = pos - Player.Position;
            float targetRotation = (float)Math.Atan2(direction.X, direction.Z);

            float angleDifference = GetShortestAngleDifference(Player.Rotation, targetRotation);

            if (Math.Abs(angleDifference) < tolerance)
            {
                return true;
            }

            if (EzThrottler.Throttle("Facing toward the fishing hole"))
            {
                // 🔴 Framework.Instance() 是 [StaticAddress(..., isPointer: true)]，合法回 null；
                //    GetConfigOption 找不到選項時也回 null。原本兩層都沒判，而下面是**寫入**
                //    （把自動面向暫時開起來再還原）—— 裸寫等於往位址 0 寫，是攔不到的 AVE。
                //    拿不到就不動設定、也不轉向，回 false 讓這個任務下一輪重試。
                var fwk = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
                if (fwk == null)
                    return false;

                var autoRotateConfig = fwk->SystemConfig.GetConfigOption((uint)ConfigOption.AutoFaceTargetOnAction);
                if (autoRotateConfig == null)
                {
                    IceLogging.Info("讀不到「自動面向目標」設定，這一輪不轉向。", "[Task_Fishing]");
                    return false;
                }

                var autoRotateOriginal = autoRotateConfig->Value.UInt;

                autoRotateConfig->Value.UInt = 1;

                IceLogging.Debug($"Telling the game to face you to: {pos}");
                Vector3 temp = pos;
                ActionManager.Instance()->AutoFaceTargetPosition(&temp);

                autoRotateConfig->Value.UInt = autoRotateOriginal;
            }

            return false;
        }
        public static float GetShortestAngleDifference(float currentAngle, float targetAngle)
        {
            float difference = targetAngle - currentAngle;

            // Normalize to [-π, π] for shortest path
            while (difference > Math.PI) difference -= (float)(2 * Math.PI);
            while (difference < -Math.PI) difference += (float)(2 * Math.PI);

            return difference;
        }
        public static FisherSpotInfo? GetNextFishingSpot(uint zone, Vector2 flag, Vector3 playerPosition)
        {
            // Check if the zone and flag exist
            if (!MoonFishingLocations.TryGetValue(zone, out var zoneData) ||
                !zoneData.TryGetValue(flag, out var spots) ||
                spots.Count == 0)
            {
                return null;
            }

            // Find the index of the spot close to the player (within distance of 2)
            int currentIndex = -1;
            for (int i = 0; i < spots.Count; i++)
            {
                float distance = Vector3.Distance(playerPosition, spots[i].FacePosition);
                if (distance < 2f)
                {
                    currentIndex = i;
                    break;
                }
            }

            // If a close spot was found, return the next one (cycling back to 0 if at the end)
            if (currentIndex != -1)
            {
                int nextIndex = (currentIndex + 1) % spots.Count;
                return spots[nextIndex];
            }

            // If no close spot found, return the first entry
            return spots[0];
        }
        public static bool? InitiateMoving(Vector3 fishingPos)
        {
            if (!P.Navmesh.IsReady())
            {
                Utils.VnavBuildInfo();
                return false;
            }
            else if (P.Navmesh.IsRunning())
            {
                // 🔴 這就是「等 vnav 走完」那一步，而且是整個釣魚流程裡最長的一段。
                //    NeoTaskManager 預設 TimeLimitMS = 30000 + AbortOnTimeout = true ——
                //    在月面走遠一點就一定超過 30 秒，一超過就把整個佇列清掉，
                //    表現出來是「走到一半突然重來」而且完全沒有訊息。
                P.TaskManager.Enqueue(() => !P.Navmesh.IsRunning(), "Waiting for vnav movement to finish", Utils.TaskConfig);
                return true;
            }
            else
            {
                if (EzThrottler.Throttle("Navmesh movement"))
                {
                    IceLogging.DestinationLogs.Log(fishingPos);
                    P.Navmesh.PathfindAndMoveTo(fishingPos, false);
                }
                return false;
            }
        }
    }
}
