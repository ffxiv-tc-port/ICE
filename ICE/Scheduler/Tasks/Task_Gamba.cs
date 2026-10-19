using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using YamlDotNet.Core.Tokens;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_Gamba
    {
        public static readonly List<Config.Gamba> DefaultGambaItems = new()
        {
            // Mounts
            new Config.Gamba { ItemId = 44505, Weight = 200, Type = GambaType.Mount }, // Vacuum Suit Identification Key
            new Config.Gamba { ItemId = 47973, Weight = 200, Type= GambaType.Mount }, // Warp Loader Identification Key

            // Emotes
            new Config.Gamba { ItemId = 44509, Weight = 25, Type = GambaType.Emote }, // Ballroom Etiquette - Personal Perfection
            new Config.Gamba { ItemId = 46795, Weight = 25, Type = GambaType.Emote }, // Ballroom Etiquette - Anticipating Exertion

            // Outfits
            new Config.Gamba { ItemId = 47937, Weight = 50, Type = GambaType.Outfit }, // Cosmosuit Coffer
            new Config.Gamba { ItemId = 47095, Weight = 50, Type = GambaType.Outfit }, // Star Pilot Attire Coffer

            // Minions
            new Config.Gamba { ItemId = 47966, Weight = 25, Type = GambaType.Minion }, // Micro Rover
            new Config.Gamba { ItemId = 46782, Weight = 25, Type = GambaType.Minion }, // Model Suit

            // Accessories
            new Config.Gamba { ItemId = 48154, Weight = 5, Type = GambaType.Accessory }, // The Faces We Wear - Tinted Sunglasses
            new Config.Gamba { ItemId = 48160, Weight = 5, Type = GambaType.Accessory }, // Loparasol
            new Config.Gamba { ItemId = 46840, Weight = 5, Type = GambaType.Accessory }, // The Faces We Wear - Scaevan Headgear

            // Orchestration
            new Config.Gamba { ItemId = 48210, Weight = 0, Type = GambaType.Orchestrion }, // Stargazers Orchestrion Roll
            new Config.Gamba { ItemId = 48220, Weight = 0, Type = GambaType.Orchestrion }, // Echoes in the Distance Orchestrion Roll
            new Config.Gamba { ItemId = 48221, Weight = 0, Type = GambaType.Orchestrion }, // Close in the Distance (Instrumental) Orchestrion Roll
            new Config.Gamba { ItemId = 46155, Weight = 0, Type = GambaType.Orchestrion }, // Kaleidoscope Orchestrion Roll

            // Housing Items
            new Config.Gamba { ItemId = 23892, Weight = 0, Type = GambaType.Housing }, // Verdant Partition
            new Config.Gamba { ItemId = 48733, Weight = 0, Type = GambaType.Housing }, // Cosmotable
            new Config.Gamba { ItemId = 48734, Weight = 0, Type = GambaType.Housing }, // Cosmolamp
            new Config.Gamba { ItemId = 48136, Weight = 0, Type = GambaType.Housing }, // Drafting Table
            new Config.Gamba { ItemId = 32215, Weight = 0, Type = GambaType.Housing }, // Spring Meadow Partition
            new Config.Gamba { ItemId = 46175, Weight = 0, Type = GambaType.Housing }, // Portable Exoterminal
            new Config.Gamba { ItemId = 46174, Weight = 0, Type = GambaType.Housing }, // Cosmokitchen Partition
            new Config.Gamba { ItemId = 46173, Weight = 0, Type = GambaType.Housing }, // Cosmoseat


            // Dyes
            new Config.Gamba { ItemId = 48169, Weight = 0, Type = GambaType.Dye }, // Metallic Pink Dye
            new Config.Gamba { ItemId = 48170, Weight = 0, Type = GambaType.Dye }, // Metallic Ruby Red Dye
            new Config.Gamba { ItemId = 48171, Weight = 0, Type = GambaType.Dye }, // Metallic Cobalt Green Dye
            new Config.Gamba { ItemId = 48172, Weight = 0, Type = GambaType.Dye }, // Metallic Dark Blue Dye

            // Materia
            new Config.Gamba { ItemId = 41762, Weight = 0, Type = GambaType.Materia }, // Gatherer's Guerdon Materia XI
            new Config.Gamba { ItemId = 41763, Weight = 0, Type = GambaType.Materia }, // Gatherer's Guile Materia XI
            new Config.Gamba { ItemId = 41764, Weight = 0, Type = GambaType.Materia }, // Gatherer's Grasp Materia XI
            new Config.Gamba { ItemId = 41765, Weight = 0, Type = GambaType.Materia }, // Craftsman's Competence Materia XI
            new Config.Gamba { ItemId = 41766, Weight = 0, Type = GambaType.Materia }, // Craftsman's Cunning Materia XI
            new Config.Gamba { ItemId = 41767, Weight = 0, Type = GambaType.Materia }, // Craftsman's Command Materia XI
            new Config.Gamba { ItemId = 41775, Weight = 0, Type = GambaType.Materia }, // Gatherer's Guerdon Materia XII
            new Config.Gamba { ItemId = 41776, Weight = 0, Type = GambaType.Materia }, // Gatherer's Guile Materia XII
            new Config.Gamba { ItemId = 41777, Weight = 0, Type = GambaType.Materia }, // Gatherer's Grasp Materia XII
            new Config.Gamba { ItemId = 41778, Weight = 0, Type = GambaType.Materia }, // Craftsman's Competence Materia XII
            new Config.Gamba { ItemId = 41779, Weight = 0, Type = GambaType.Materia }, // Craftsman's Cunning Materia XII
            new Config.Gamba { ItemId = 41780, Weight = 0, Type = GambaType.Materia }, // Craftsman's Command Materia XII

            // Other
            new Config.Gamba { ItemId = 43943, Weight = 0, Type = GambaType.Other }, // Cracked Prismaticrystal
            new Config.Gamba { ItemId = 43944, Weight = 0, Type = GambaType.Other }, // Cracked Novacrystal
            new Config.Gamba { ItemId = 28724, Weight = 0, Type = GambaType.Other }, // Crafter's Delineation
            new Config.Gamba { ItemId = 6141,  Weight = 0, Type = GambaType.Other }, // Cordial HQ
            new Config.Gamba { ItemId = 48158, Weight = 0, Type = GambaType.Other }, // Magicked Prism (Cosmic Exploration)
        };

        public static void EnsureGambaWeightsInitialized(bool force = false)
        {
            bool changed = false;
            if (force)
                C.GambaItemWeights.Clear();
            foreach (var item in DefaultGambaItems)
            {
                if (C.GambaItemWeights.Any(x => x.ItemId == item.ItemId))
                    continue;
                C.GambaItemWeights.Add(new Config.Gamba { ItemId = item.ItemId, Weight = item.Weight, Type = item.Type });
                changed = true;
            }
            if (changed)
                C.Save();
        }

        /// <summary>
        /// 目前這個區域／狀態能不能手動跑一次轉盤。給設定頁的「立即執行一次」按鈕當閘門用——
        /// 轉盤視窗已經開著（直接開賭），或人在有登記轉盤 NPC 的宇宙探索區（先走過去）。
        /// </summary>
        public static bool CanRunManually()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSLottery>("WKSLottery", out var lottery) && lottery.IsAddonReady)
                return true;

            return NpcData.TryGetMoonNpc(Player.Territory, NpcData.NpcType.Gamba, out _);
        }

        /// <summary>
        /// 中止這一串轉盤流程。
        /// </summary>
        /// <remarks>
        /// 🔴 不可以一律寫 <c>State = Start</c>：<c>SchedulerMain.Tick()</c> 只要看到
        /// <c>State != Idle</c> 就會開始跑整套任務排程——也就是說，使用者只是在設定頁按了
        /// 「立即執行一次」（此時 State 是 Idle），一旦中途查不到 NPC 就會**整套 ICE 自己跑起來**。
        /// </remarks>
        private static void AbortGamba()
        {
            P.TaskManager.Tasks.Clear();
            if (SchedulerMain.State != IceState.Idle)
                SchedulerMain.State = IceState.Start;
        }

        // 區域 → 信用點道具。刻意不讀 WKSManager 的未具名欄位：那個偏移換版就會移動。
        private static readonly Dictionary<uint, uint> CreditItemByTerritory = new()
        {
            [1237] = 45691, // Sinus Ardorum
            [1291] = 48146, // Phaenna
            [1310] = 48147, // Oizys
            [1319] = 48148, // Auxesia
        };

        /// <summary>目前區域的信用點道具 ID；不在表內的區域回 false。</summary>
        internal static bool TryGetCosmoCreditItemId(out uint itemId)
            => CreditItemByTerritory.TryGetValue(Player.Territory, out itemId);

        public static void Enqueue()
        {
            EnsureGambaWeightsInitialized();
            if (GenericHelpers.TryGetAddonMaster<WKSLottery>("WKSLottery", out var gamba) && gamba.IsAddonReady)
            {
                P.TaskManager.EnqueueMulti
                    (
                        new(GamblingTime, "Time to go gambling!", Utils.TaskConfig),
                        new(CloseTalk, "Closing the talk window"),
                        new(() => SchedulerMain.State = IceState.Idle)
                    );
            }
            else
            {
                // If this is the case, then we're here to initalize the gamba
                P.TaskManager.EnqueueMulti
                    (
                        new(PathToGambaNpc, "Pathing to the gamba NPC"),
                        new(TalkToGambaNpc, "Talk to the Gamba NPC"),
                        new(SelectGamba, "Selecting the options to go to gamba"),
                        new(GamblingTime, "Time to go gambling!", Utils.TaskConfig),
                        new(CloseTalk, "Closing the talk window")
                    );
            }
        }

        private static bool? PathToGambaNpc()
        {
            var zoneId = Player.Territory;
            // MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是 Player.Territory —— 佇列排好之後玩家還是可能
            // 被傳送走（機甲行動抽中駕駛員就會），所以一定要走 NpcData.TryGetMoonNpc，不可以直接索引。
            if (!NpcData.TryGetMoonNpc(zoneId, NpcData.NpcType.Gamba, out var npcEntry))
            {
                if (EzThrottler.Throttle("ICE: moon npc missing Gamba", 5000))
                    IceLogging.Info($"目前區域 {zoneId} 沒有登記轉盤 NPC 的資料（可能已經被傳送離開月面），中止這一步。", "[ICE]");
                AbortGamba();
                return true;
            }

            if (Player.DistanceTo(npcEntry.NpcLocation) <= 6.75f)
            {
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                }
                else if (P.Navmesh.IsRunning())
                {
                    if (Player.DistanceTo(npcEntry.NpcLocation) < 5)
                    {
                        IceLogging.Debug("Pathing to NPC has reached the distance thresh, stopping");
                        P.Navmesh.Stop();
                        return true;
                    }
                }
                else
                {
                    IceLogging.Debug($"Distance to the npc is correct, commending gamba");
                    return true;
                }
            }
            else
            {
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                }
                else if (!P.Navmesh.IsRunning())
                {
                    if (EzThrottler.Throttle("Pathing to repair NPC"))
                    {
                        IceLogging.Debug($"Pathing to: {npcEntry.Name}");

                        Vector3 randomPoint = RandomUtil.GetRandomPointInBounds(npcEntry.Corner1, npcEntry.Corner2, npcEntry.Corner3, npcEntry.Corner4, npcEntry.NpcLocation.Y);
                        IceLogging.DestinationLogs.Log(randomPoint);
                        P.Navmesh.PathfindAndMoveTo(randomPoint, false);
                    }
                }
            }

            return false;
        }
        private static bool? TalkToGambaNpc()
        {
            if (GenericHelpers.TryGetAddonMaster<SelectString>("SelectString", out var selectString) && selectString.IsAddonReady)
            {
                IceLogging.Info("We've gotten to selecting the npc dialog (woo!). Selecting gamba");
                return true;
            }
            else if (GenericHelpers.TryGetAddonMaster<Talk>("Talk", out var talk) && talk.IsAddonReady)
            {
                // Talk 類（按一次翻一頁、窗不會因為被按而消失）：守衛逃生口 15 幀，走到是常態、寫 Debug。
                if (EzThrottler.Throttle("Closing Talk Window", 250)
                    && AddonPressGuard.TryBeginPress("宇宙好運道：NPC 對話翻頁", "Talk", talk, "Click", AddonPressGuard.RoutineRePressEscapeFrames))
                    talk.Click();
            }
            else
            {
                // MoonNpcs 只有月面兩個 key（1237／1291），而這裡的 key 是 Player.Territory —— 佇列排好之後玩家還是可能
                // 被傳送走（機甲行動抽中駕駛員就會），所以一定要走 NpcData.TryGetMoonNpc，不可以直接索引。
                if (!NpcData.TryGetMoonNpc(Player.Territory, NpcData.NpcType.Gamba, out var npcEntry))
                {
                    if (EzThrottler.Throttle("ICE: moon npc missing Gamba", 5000))
                        IceLogging.Info($"目前區域 {Player.Territory} 沒有登記轉盤 NPC 的資料（可能已經被傳送離開月面），中止這一步。", "[ICE]");
                    AbortGamba();
                    return true;
                }
                Utils.TryGetNpcObject(npcEntry, out var researchNpc);
                if (EzThrottler.Throttle("Interacting with gambaNpc!"))
                {
                    Utils.TargetgameObject(researchNpc);
                    Utils.InteractWithObject(researchNpc);
                }
            }

            return false;
        }
        private static bool? SelectGamba()
        {
            if (GenericHelpers.TryGetAddonMaster<SelectIconString>("SelectIconString", out var iconString) && iconString.IsAddonReady)
            {
                // 選單一選即關：守衛擋下時這一幀不選，下一輪節流再來。
                if (EzThrottler.Throttle("Selecting Materia Selection")
                    && AddonPressGuard.TryBeginPress("宇宙好運道：選擇轉盤選項", "SelectIconString", iconString, AddonPressGuard.BuildPressKey(true, 0)))
                {
                    var select = iconString.Entries[0];
                    IceLogging.Debug($"Selecting: {select.Text}");
                    select.Select();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<SelectString>("SelectString", out var selectString) && selectString.IsAddonReady)
            {
                if (EzThrottler.Throttle("Selecting yes to gamba")
                    && AddonPressGuard.TryBeginPress("宇宙好運道：確認參加", "SelectString", selectString, AddonPressGuard.BuildPressKey(true, 0)))
                {
                    selectString.Entries[0].Select();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<WKSLottery>("WKSLottery", out var gamba) && gamba.IsAddonReady)
            {
                return true;
            }

            return false;
        }
        // ────────────────────────────────────────────────────────────────
        // 輪盤選擇 log 的變化偵測
        // ────────────────────────────────────────────────────────────────
        // 🔑 用變化偵測而不是純時間節流：同一個決策只在**內容真的變了**時才印，決策一改變下一幀立刻看得到。
        // ⚠️ 下面四個欄位只有 log 讀寫。控制流、選輪盤的 Flags 寫入完全不看它們。
        private const int WheelPureStellarLeft = 1;
        private const int WheelPureStellarRight = 2;
        private const int WheelLeftBetter = 3;
        private const int WheelRightBetter = 4;
        private const int WheelBothEqual = 5;

        // 0 ＝ 還沒印過任何一次。五個決策碼都 >= 1，所以第一次呼叫必定印得出來，
        // 不必依賴 lastWheelDecisionTick 的初始值（那是 0，開機後不久理論上可能 < 500）。
        private static int lastWheelDecision;
        private static float lastWheelDecisionLeft;
        private static float lastWheelDecisionRight;
        private static long lastWheelDecisionTick;

        /// <summary>這一幀的輪盤決策要不要印出來（同一個決策只在改變時印一次）。</summary>
        private static bool WheelDecisionChanged(int decision, float leftWeight, float rightWeight)
        {
            var now = Environment.TickCount64;
            var returnedAfterLeaving = now - lastWheelDecisionTick > 500;
            lastWheelDecisionTick = now;

            // Equals 而不是 == ：NaN.Equals(NaN) 為 true，設定壞掉導致權重變 NaN 時
            // 才不會每幀都判成「變了」而重新開始洗版。
            if (!returnedAfterLeaving
                && decision == lastWheelDecision
                && leftWeight.Equals(lastWheelDecisionLeft)
                && rightWeight.Equals(lastWheelDecisionRight))
                return false;

            lastWheelDecision = decision;
            lastWheelDecisionLeft = leftWeight;
            lastWheelDecisionRight = rightWeight;
            return true;
        }
        private static unsafe bool? GamblingTime()
        {
            string tag = "Gambling Time Task";

            if (GenericHelpers.TryGetAddonMaster<WKSLottery>("WKSLottery", out var gamba) && gamba.IsAddonReady)
            {
                if (!TryGetCosmoCreditItemId(out var itemId))
                {
                    if (EzThrottler.Throttle("ICE: gamba currency unknown", 5000))
                        IceLogging.Info("讀不到目前宇宙探索區域對應的信用點道具，中止轉盤流程。", tag);
                    AbortGamba();
                    return true;
                }

                PlayerHelper.GetItemCount(itemId, out var credits);

                bool confirmEnabled, leftWheelEnabled, rightWheelEnabled;
                bool leftWheelPresent, rightWheelPresent;
                unsafe
                {
                    // 這三個屬性都是 Addon->GetComponentButtonById(id)，找不到節點會回 null；
                    // 且 IsEnabled 解的是 OwnerNode 而非 AtkResNode，兩層都要擋才不會 AVE。
                    // 任一層為 null 一律當成「按鈕不可按」→ 本次不動作。
                    confirmEnabled = GenericHelpers.IsComponentEnabled(gamba.SpinWheelButton);

                    // 🔴 「節點在不在」與「按鈕能不能按」是兩件事，要分開存：
                    //    下面的輪盤分支呼叫的 SelectWheelLeft/Right 一定會同時寫**兩顆**輪盤鈕的 Flags，
                    //    所以那個分支真正的前提是「兩顆節點都在」（判空），
                    //    而不是「兩顆都 enabled」—— 選輪盤的常態就是一邊 enabled、另一邊不是，
                    //    拿 enabled 當前提會把正常流程整個關掉。
                    var leftWheel = gamba.WheelLeftButton;
                    var rightWheel = gamba.WheelRightButton;
                    leftWheelPresent = leftWheel != null;
                    rightWheelPresent = rightWheel != null;
                    leftWheelEnabled = GenericHelpers.IsComponentEnabled(leftWheel);
                    rightWheelEnabled = GenericHelpers.IsComponentEnabled(rightWheel);
                }

                if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
                {
                    // 🔴 GamblingTime 是每個 tick 都會跑的輪詢步驟：沒有守衛的話，按下之後的下一幀會對同一扇
                    //    「正在關閉中」的窗再按一次 —— 那就是原生 AccessViolation。YesnoPressGuard 認的是視窗位址，
                    //    第一次按一樣立刻放行，只擋掉「同一扇窗還開著時的重按」。
                    if (credits >= 1000 + C.GambaCreditsMinimum)
                    {
                        if (YesnoGuard.ShouldConfirm(YesnoSituation.Lottery)
                            && YesnoPressGuard.MayPress("宇宙好運道：消耗點數確認", (nint)select.Base))
                            select.Yes();
                    }
                    else if (YesnoPressGuard.MayPress("宇宙好運道：點數不足取消", (nint)select.Base))
                        select.No();
                }
                else if (confirmEnabled)
                {
                    // 零節流的每幀輪詢：按下之後鈕會停用，但「鈕停用所以不會重按」不算守衛。
                    // 轉盤按下確認後窗不關（動畫完回到同一扇），屬多次互動窗：逃生口 15 幀、走到寫 Debug。
                    if (AddonPressGuard.TryBeginPress("宇宙好運道：轉動轉盤", "WKSLottery", gamba, "Confirm", AddonPressGuard.RoutineRePressEscapeFrames))
                        gamba.ConfirmButton();
                }
                else if (leftWheelPresent && rightWheelPresent && (leftWheelEnabled || rightWheelEnabled))
                {
                    float leftWeight = gamba.LeftWheelItems.Sum(item => C.GambaItemWeights.FirstOrDefault(x => x.ItemId == item.itemId)?.Weight ?? 0);
                    float rightWeight = gamba.RightWheelItems.Sum(item => C.GambaItemWeights.FirstOrDefault(x => x.ItemId == item.itemId)?.Weight ?? 0);

                    if (C.GambaPreferSmallerWheel)
                    {
                        leftWeight = gamba.LeftWheelItems.Length > 0 ? leftWeight / gamba.LeftWheelItems.Length : 0;
                        rightWeight = gamba.RightWheelItems.Length > 0 ? rightWeight / gamba.RightWheelItems.Length : 0;

                        if (leftWeight == rightWeight && leftWeight > 0)
                        {
                            leftWeight += 1.0f / Math.Max(1, gamba.LeftWheelItems.Length);
                            rightWeight += 1.0f / Math.Max(1, gamba.RightWheelItems.Length);
                        }
                    }

                    // ⚠️ 只有 log 被包起來。判斷式、SelectWheel* 的呼叫、隨機選邊
                    //    全部原封不動 —— 那是行為，不是診斷。
                    if (gamba.LeftWheelItems.Length == 0)
                    {
                        if (WheelDecisionChanged(WheelPureStellarLeft, leftWeight, rightWeight))
                            IceLogging.Info($"Found a pure stellar mission gamba. Choosing left wheel", tag);
                        SelectWheelLeft(gamba);
                    }
                    else if (gamba.RightWheelItems.Length == 0)
                    {
                        if (WheelDecisionChanged(WheelPureStellarRight, leftWeight, rightWeight))
                            IceLogging.Info($"Found a pure stellar mission gamba. Choosing right wheel", tag);
                        SelectWheelRight(gamba);
                    }
                    else if (leftWeight > rightWeight)
                    {
                        if (WheelDecisionChanged(WheelLeftBetter, leftWeight, rightWeight))
                            IceLogging.Info($"[Gamba] First wheel is better with total weight: {leftWeight}");
                        SelectWheelLeft(gamba);
                    }
                    else if (rightWeight > leftWeight)
                    {
                        if (WheelDecisionChanged(WheelRightBetter, leftWeight, rightWeight))
                            IceLogging.Info($"[Gamba] Second wheel is better with total weight: {rightWeight}");
                        SelectWheelRight(gamba);
                    }
                    else
                    {
                        if (WheelDecisionChanged(WheelBothEqual, leftWeight, rightWeight))
                            IceLogging.Info("[Gamba] Both wheels are equal in weight. Randomly selecting one.");
                        if (new Random().Next(2) == 0)
                            SelectWheelLeft(gamba);
                        else
                            SelectWheelRight(gamba);
                    }
                }
                else if (leftWheelEnabled || rightWheelEnabled)
                {
                    // 🔴 只找得到一邊的輪盤鈕。上面那一支呼叫的 SelectWheelLeft/Right 一定會同時寫
                    //    **兩顆**鈕的 Flags（AtkComponentButton.Flags 在 [FieldOffset(0xE8)]），
                    //    少一顆就是往位址 0xE8 寫入 = NullReferenceException；NeoTaskManager 預設
                    //    AbortOnError = true ⇒ 整條佇列被清、轉盤流程無聲中止。這一幀什麼都不做，照舊 return false 下一幀再來。
                    // ⚠️ 「不知道」本身要看得見：要使用者回報的診斷。
                    if (EzThrottler.Throttle("ICE: gamba wheel node missing", 5000))
                        IceLogging.Info($"轉盤視窗只找得到一邊的輪盤按鈕（左 {(leftWheelPresent ? "有" : "缺")}／右 {(rightWheelPresent ? "有" : "缺")}），這一輪不選輪盤。", tag);
                }

                return false;
            }
            else
            {
                return true;
            }

        }
        private static unsafe bool HasEnoughCredits()
        {
            if (!TryGetCosmoCreditItemId(out var itemId))
                return false;

            PlayerHelper.GetItemCount(itemId, out var credits);
            return credits >= 1000;
        }
        private static bool? CloseTalk()
        {
            if (GenericHelpers.TryGetAddonMaster<Talk>("Talk", out var talk) && talk.IsAddonReady)
            {
                // 最後一頁按完 Talk 真的會關，關閉中的幀仍會進來；Talk 類守衛逃生口 15 幀（危險窗口 < 10 幀）。
                if (EzThrottler.Throttle("Closing Talk Window", 250)
                    && AddonPressGuard.TryBeginPress("宇宙好運道：關閉對話", "Talk", talk, "Click", AddonPressGuard.RoutineRePressEscapeFrames))
                    talk.Click();
                return false;
            }
            else
            {
                return true;
            }
        }
        /// <summary>選定其中一邊的輪盤：把兩顆輪盤鈕的 <c>Flags</c> 寫成「這邊選中、那邊沒選」。</summary>
        /// <remarks>
        /// 🔴 <b>兩顆鈕都要寫，所以兩顆都要判空。</b><c>AtkComponentButton.Flags</c> 在
        /// <c>[FieldOffset(0xE8)]</c>，對 null 寫入就是往位址 0xE8 寫 —— NullReferenceException。
        /// 🔑 <b>值已經對了就不寫</b>：呼叫端<b>每一幀</b>都會進來（沒有節流），
        /// 而寫入的對象是可能正在關閉中的 <c>WKSLottery</c> 元件。只在「和目標值不同」時才寫，
        /// 穩態下的每幀寫入直接歸零，最終狀態與原本完全相同；遊戲若自己把旗標改回去，下一幀照樣會補寫。
        /// </remarks>
        private static unsafe bool TrySelectWheel(WKSLottery gamba, uint leftFlags, uint rightFlags,
                                                 string logThrottleKey, string logText)
        {
            var leftWheel = gamba.WheelLeftButton;
            var rightWheel = gamba.WheelRightButton;
            if (leftWheel == null || rightWheel == null)
            {
                // ⚠️ 「不知道」要看得見：要使用者回報的診斷。
                if (EzThrottler.Throttle("ICE: gamba wheel button missing", 5000))
                    IceLogging.Info($"找不到輪盤按鈕（左 {(leftWheel == null ? "缺" : "有")}／右 {(rightWheel == null ? "缺" : "有")}），這一次不寫入輪盤選擇。", "[Gamba]");

                return false;
            }

            if (leftWheel->Flags != leftFlags)
                leftWheel->Flags = leftFlags;
            if (rightWheel->Flags != rightFlags)
                rightWheel->Flags = rightFlags;

            if (EzThrottler.Throttle(logThrottleKey, 3000))
                IceLogging.Debug(logText);

            return true;
        }

        // 🔴 這兩支的任務端呼叫者（GamblingTime 的輪盤選擇分支）以 `return false` 結尾＝
        //    NeoTaskManager 下一幀原地重跑。而這裡寫回去的 Flags **兩個值都帶 Enabled 位元**
        //    （65792 = 0x10100、327936 = 0x50100），所以呼叫端的
        //    `leftWheelEnabled || rightWheelEnabled` 下一幀依然成立 ⇒ 這兩行 log 一定要節流。
        //    左右各自一把節流鑰匙，免得交替選擇時把對側那行吃掉。
        public static unsafe void SelectWheelLeft(WKSLottery gamba)
            => TrySelectWheel(gamba, 327936U, 65792U, // Checked/Enabled/Selected ; Not Checked/Enabled/Not Selected
                              "ICE: gamba wheel select left log", "[Gamba] Selecting Left Wheel");

        public static unsafe void SelectWheelRight(WKSLottery gamba)
            => TrySelectWheel(gamba, 65792U, 327936U, // Not Checked/Enabled/Not Selected ; Checked/Enabled/Selected
                              "ICE: gamba wheel select right log", "[Gamba] Selecting Right Wheel");

        public static bool BigBangGamba()
        {
            // Big Bang Tickets are earned from doing the fates... and this kind fucks with things? 
            // Name of the item is "Bing Bang Fortune (Planet Name)

            return false;
        }
    }
}
