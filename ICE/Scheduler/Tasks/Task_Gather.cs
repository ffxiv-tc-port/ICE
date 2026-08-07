using Dalamud.Bindings.ImPlot;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ICE.Resources.GatheringRoutes;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using Microsoft.VisualBasic.ApplicationServices;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using YamlDotNet.Core.Tokens;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_Gather
    {

        public static void Enqueue()
        {
            if (GenericHelpers.TryGetAddonMaster<Gathering>("Gathering", out var gather) && gather.IsAddonReady || GenericHelpers.TryGetAddonMaster<GatheringMasterpiece>("GatheringMasterpiece", out var collectable) && collectable.IsAddonReady)
            {
                IceLogging.Debug("Current in a gathering session");
                Task_CheckScore.Enqueue();
                P.TaskManager.Enqueue(() => GatheringInteraction(), Utils.TaskConfig);
            }
            else
            {
                IceLogging.Debug("Not currently gathering, starting fresh instead");
                P.TaskManager.EnqueueDelay(100);

                // ✅ 曾經是零守衛的字典索引，已修：守衛＝下一行的 SchedulerMain.CurrentMissionUnavailable。
                //    原因留存：這裡是 Enqueue（跑在 SchedulerMain.Tick 上、不在任務內），
                //    例外會直接冒到 Framework.Update 而且每個 tick 重來一次 —— 正是上次事故
                //    「同一行連噴 37 次」的形狀。
                if (SchedulerMain.CurrentMissionUnavailable("[Task_Gather: Enqueue]", out var enqueueMission))
                    return;

                if (enqueueMission.Attributes.HasFlag(MissionAttributes.ReducedItems))
                {
                    Task_CheckScore.Enqueue();
                    P.TaskManager.Enqueue(() => CheckReduceMission(), "Checking to see if we need to reduce items");
                    P.TaskManager.EnqueueDelay(500);
                    Task_CheckScore.Enqueue();
                }
                else
                {
                    Task_CheckScore.Enqueue();
                }
                P.TaskManager.Enqueue(() => Mission_Settings.ResetCollectableState());
                P.TaskManager.Enqueue(() => UseFood());
                // PathandCheckNode 是 vnav 走去採集點（分鐘級），CheckCurrentLocation 也可能
                // 等 navmesh 建圖。用預設 30 秒 + AbortOnTimeout 等於每 30 秒把整個佇列砍一次。
                P.TaskManager.Enqueue(() => CheckCurrentLocation(), "Checking to see if gathering flags needs updated", Utils.TaskConfig);
                P.TaskManager.Enqueue(() => PathandCheckNode(), "Pathing to the gathering node", Utils.TaskConfig);
            }
        }

        public static bool? CheckCurrentLocation()
        {
            ThrottleMessage("- - - Check Gather Locations Task - - -", "[Check Gather Locations]");

            var zoneId = Player.Territory;
            if (SchedulerMain.CurrentMissionUnavailable("[Check Gather Locations]", out var missionEntry))
                return true;

            var missionFlag = missionEntry.MapPosition;
            var gatherInfo = GatheringRouteLoader.GetRoute(zoneId, missionFlag);

            // ⚠️ 原本這裡只有 `if (gatherInfo != null)`，查不到路線時會一路掉到最後的
            //    `return false` —— 也就是這個任務永遠不會完成。TaskConfig 是
            //    timeLimitMS 30 分鐘 + abortOnTimeout: false，所以失敗形式是「靜靜地卡住半小時」。
            //    真正會講話的守衛在下一個任務 PathandCheckNode 裡，所以這裡放行讓它去講。
            if (gatherInfo == null || gatherInfo.Count == 0)
            {
                if (EzThrottler.Throttle("ICE: gather route missing (CheckCurrentLocation)", 5000))
                    IceLogging.Info($"任務 {CosmicHelper.CurrentLunarMission} 在區域 {zoneId} 座標 {missionFlag} " +
                                    "找不到採集路線，這一步先放行，由下一步回報。", "[Check Gather Locations]");
                return true;
            }

            if (Mission_Settings.previousMap != missionFlag)
            {
                // We're currently at a whole new area. So going to check the gathering nodes to see which one we're closest to
                Mission_Settings.previousMap = missionFlag;
                Mission_Settings.nodeCounter = PickStartNodeIndex(gatherInfo, "[Check Gather Locations]");
            }
            else
            {
                // we're currently in a map location that has been previously recorded, so we're going to check to see if we're within range of any first
                var closestDistance = gatherInfo.Where(x => Player.DistanceTo(x.Position) < 5).FirstOrDefault();
                if (closestDistance == null)
                {
                    // 離所有採集點都還很遠。原本這裡只做索引邊界檢查，等於沿用上一輪留下來的索引 ——
                    // 那個索引跟「玩家現在站在哪」完全無關，人被傳送或走遠之後就會往回跑。
                    if (C.GatherPickClosestNode &&
                        TrySelectClosestNode(gatherInfo, excludeNodeId: 0, "[Check Gather Locations]", out var reselected))
                    {
                        Mission_Settings.nodeCounter = reselected;
                        return true;
                    }

                    // going to rely on the index to tell us where we should be
                    if (Mission_Settings.nodeCounter >= gatherInfo.Count)
                    {
                        // resetting it back to 0 because we're outside the normal index array
                        Mission_Settings.nodeCounter = 0;
                    }
                    return true;

                }
                else
                {
                    // We're currently close to a node, time to check and see if it's a viable node, or if we need to pathfind to the next
                    var nodeId = closestDistance.NodeId;
                    var closestNode = Svc.Objects.Where(x => x.DataId == nodeId && x.IsTargetable).FirstOrDefault();

                    if (closestNode != null)
                    {
                        // Node is targetable, set the counter to this node's index
                        var currentNodeIndex = gatherInfo.FindIndex(x => x.NodeId == nodeId);
                        if (currentNodeIndex >= 0)
                        {
                            Mission_Settings.nodeCounter = currentNodeIndex;
                        }
                        return true;
                    }
                    else
                    {
                        // 🔴 這裡就是使用者回報的「明明有更近的採集點卻跑去遠的」的真正來源。
                        //    腳下這個採集點剛採完（或還沒重生），原本無條件 nodeCounter++ 跳到
                        //    「路線檔裡的下一個」—— 那是<b>檔案順序</b>，跟距離毫無關係。
                        //    路線是環狀的（PathandCheckNode 會遞增並回繞），從哪一個點接下去都合法，
                        //    所以改成挑最近而且還採得到的那一個。
                        //    ⚠️ 一定要把腳下這個點排除掉，否則「全部都不可採」時會選回自己＝原地打轉。
                        if (C.GatherPickClosestNode &&
                            TrySelectClosestNode(gatherInfo, excludeNodeId: nodeId, "[Check Gather Locations]", out var nextNode))
                        {
                            Mission_Settings.nodeCounter = nextNode;
                            return true;
                        }

                        // Node is not targetable, increment to next node
                        Mission_Settings.nodeCounter++;

                        // Check if we're out of bounds and wrap back to 0
                        if (Mission_Settings.nodeCounter >= gatherInfo.Count)
                        {
                            Mission_Settings.nodeCounter = 0;
                        }
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 從路線裡挑出「離玩家最近、而且現在還採得到」的採集點索引。
        /// </summary>
        /// <param name="route">呼叫端必須先保證非空。</param>
        /// <param name="excludeNodeId">要排除的採集點 id（0＝不排除）。腳下那個剛採完的點要從這裡排掉。</param>
        /// <param name="index">挑到的索引；回傳 <c>false</c> 時為 -1。</param>
        /// <returns>挑得到就 <c>true</c>。<b>挑不到一律回 false 讓呼叫端沿用原本的行為</b>，不自己亂猜。</returns>
        /// <remarks>
        /// 兩個順位，刻意分開：
        /// <list type="number">
        /// <item>已經在 ObjectTable 裡而且<b>現在就可以採</b>的點 —— 用實際物件座標算距離（最準）。</item>
        /// <item>還沒載入 ObjectTable 的點 —— 用路線檔的<b>靜態座標</b>算距離。
        /// 🔑 這一順位刻意<b>排除「已載入但不可採」</b>的點：那些是剛採完或還沒重生的，
        /// 選它們等於原地打轉。而「已載入且可採」已經被第一順位收走了，
        /// 所以「沒載入」與「已載入且可採」在這裡是同一件事的兩面。</item>
        /// </list>
        /// ⚠️ ObjectTable 的物件只在這一次呼叫（同一幀）內使用，<b>不存起來跨幀用</b>。
        /// <para>
        /// 📌 出處：兩段式挑點的作法與三個呼叫點的位置，取自另一個台服移植版
        /// <c>4liang0121/Ices-Cosmic-Exploration</c> 的 <c>api13-tw</c> 分支
        /// （commit <c>1f2028c</c>）裡的 <c>Task_Gather.SelectClosestTargetableNode</c>。
        /// 該專案與本專案同為 GPL-3.0，授權相容。
        /// 本實作依我們這一版的既有結構重寫（回傳 bool ＋ 排除腳下的點 ＋ Information 級 log），
        /// 並非逐字複製。
        /// </para>
        /// </remarks>
        private static bool TrySelectClosestNode(List<GathNodeInfo> route, uint excludeNodeId, string handle, out int index)
        {
            // 第一順位：載入了而且現在可以採 —— 用實際物件座標。
            var live = route.Select((node, i) => new
                            {
                                Index = i,
                                Node = node,
                                Obj = Svc.Objects.FirstOrDefault(o => o.ObjectKind == ObjectKind.GatheringPoint
                                                                  && o.IsTargetable
                                                                  && o.DataId == node.NodeId)
                            })
                            .Where(x => x.Node.NodeId != excludeNodeId && x.Obj != null)
                            .OrderBy(x => Player.DistanceTo(x.Obj!.Position))
                            .FirstOrDefault();

            if (live != null)
            {
                index = live.Index;
                IceLogging.Info($"挑最近的採集點：選索引 {index}（採集點 {live.Node.NodeId}，" +
                                $"距離 {Player.DistanceTo(live.Obj!.Position):N1}，已載入且可採）。", handle);
                return true;
            }

            // 第二順位：還沒載入的點（離太遠所以不在 ObjectTable 裡）—— 用路線檔的靜態座標。
            var unloaded = route.Select((node, i) => new
                                {
                                    Index = i,
                                    Node = node,
                                    Loaded = Svc.Objects.Any(o => o.ObjectKind == ObjectKind.GatheringPoint
                                                               && o.DataId == node.NodeId)
                                })
                                .Where(x => x.Node.NodeId != excludeNodeId && !x.Loaded)
                                .OrderBy(x => Player.DistanceTo(x.Node.Position))
                                .FirstOrDefault();

            if (unloaded != null)
            {
                index = unloaded.Index;
                IceLogging.Info($"挑最近的採集點：可採的點都不在 ObjectTable 裡，改用路線檔座標挑最近 —— " +
                                $"選索引 {index}（採集點 {unloaded.Node.NodeId}，" +
                                $"距離 {Player.DistanceTo(unloaded.Node.Position):N1}，尚未載入）。", handle);
                return true;
            }

            // 路線上每一個點都已載入而且都不可採（整條路線剛被採光）。
            // 這裡回 false 而不是硬挑一個，讓呼叫端沿用原本的行為 —— 硬挑最近的
            // 只會選回腳下那個剛採完的點，那是原地打轉而不是前進。
            index = -1;
            IceLogging.Info($"挑最近的採集點：這條路線上 {route.Count} 個點目前都採不到，" +
                            "沿用原本的挑點方式。", handle);
            return false;
        }

        /// <summary>
        /// 換到新的任務旗標時，決定「從路線上的哪一個採集點開始跑」。
        /// </summary>
        /// <remarks>
        /// 🔴 這裡原本是
        /// <c>route.Where(在 ObjectTable 裡且可選取).OrderBy(距離).Select(索引).FirstOrDefault(0)</c>。
        /// 篩選條件要求採集點<b>此刻就在 ObjectTable 裡而且可選取</b>，但剛傳送進區域、
        /// 或人站在旗標圈的另一邊時，較遠的採集點根本還沒載入 —— 篩選結果是空的，
        /// 然後 <c>FirstOrDefault(0)</c> <b>靜默回傳索引 0</b>，也就是「路線檔裡的第一個點」，
        /// 跟「離玩家最近」完全無關。使用者看到的就是「明明旁邊有採集點，它卻跑去遠的那個」。<br/>
        /// 第二層陷阱：「篩選全空」與「最近的剛好就是索引 0」<b>回傳值一模一樣</b>，
        /// 事後看 log 也分不出來 —— 典型的「把不知道當成一個具體值」。<br/>
        /// 現在：先交給 <see cref="TrySelectClosestNode"/>（它會優先挑「可採」的點）；
        /// 它挑不到時才退回下面原本的兩段：ObjectTable 有命中就用實際座標，
        /// 都沒命中就用<b>路線檔自己的靜態座標</b>挑最近（那是 YAML 裡的資料，不需要物件載入）。
        /// 每一條路徑都各寫一行 Information，使用者的 log 可以直接證明走了哪一條、
        /// 選了第幾個點、距離多遠。
        /// </remarks>
        /// <param name="route">呼叫端必須先保證非空。</param>
        private static int PickStartNodeIndex(List<GathNodeInfo> route, string handle)
        {
            // 新的挑點邏輯優先；它挑不到時原封不動走下面原本的兩段退路。
            if (C.GatherPickClosestNode && TrySelectClosestNode(route, excludeNodeId: 0, handle, out var picked))
                return picked;

            // ⚠️ ObjectTable 的物件只在這一次呼叫（同一幀）內使用，不存起來跨幀用。
            var live = route.Select((node, index) => new
                            {
                                Index = index,
                                Node = node,
                                Obj = Svc.Objects.FirstOrDefault(o => o.ObjectKind == ObjectKind.GatheringPoint
                                                                  && o.IsTargetable
                                                                  && o.DataId == node.NodeId)
                            })
                            .Where(x => x.Obj != null)
                            .OrderBy(x => Player.DistanceTo(x.Obj!.Position))
                            .ToList();

            if (live.Count > 0)
            {
                var best = live[0];
                IceLogging.Info($"挑起始採集點：ObjectTable 命中 {live.Count}/{route.Count} 個，" +
                                $"選索引 {best.Index}（採集點 {best.Node.NodeId}，" +
                                $"距離 {Player.DistanceTo(best.Obj!.Position):N1}）。", handle);
                return best.Index;
            }

            var fallback = route.Select((node, index) => new { Index = index, Node = node })
                                .OrderBy(x => Player.DistanceTo(x.Node.Position))
                                .First();

            IceLogging.Info($"挑起始採集點：ObjectTable 一個都沒命中（共 {route.Count} 個點，" +
                            "採集點還沒載入或目前不可選取），改用路線檔的座標挑最近 —— " +
                            $"選索引 {fallback.Index}（採集點 {fallback.Node.NodeId}，" +
                            $"距離 {Player.DistanceTo(fallback.Node.Position):N1}）。", handle);
            return fallback.Index;
        }

        public static bool? PathandCheckNode()
        {
            const string pathHandle = "[Task_Gather: PathandCheckNode]";

            var zoneId = Player.Territory;
            if (SchedulerMain.CurrentMissionUnavailable(pathHandle, out var missionEntry))
                return true;

            var missionFlag = missionEntry.MapPosition;
            var gatherInfo = GatheringRouteLoader.GetRoute(zoneId, missionFlag);

            // ⚠️ 這兩個解參考跟字典守衛是同一個 bug class 的變形：GetRoute 查不到路線時回 null，
            //    而 nodeCounter 是跨任務保留的索引，換了任務／路線變短就會越界。
            //    兩者都只會在任務裡丟例外 → 表現成「卡住不動」而且沒有訊息。
            if (gatherInfo == null || gatherInfo.Count == 0)
            {
                if (EzThrottler.Throttle("ICE: gather route missing log", 5000))
                    IceLogging.ChatError($"任務 {CosmicHelper.CurrentLunarMission} 在區域 {zoneId} 座標 {missionFlag} " +
                                         "找不到採集路線，無法自動前往採集點。", "[ICE]");
                SchedulerMain.AbortToStateCheck();
                return true;
            }

            if (Mission_Settings.nodeCounter < 0 || Mission_Settings.nodeCounter >= gatherInfo.Count)
            {
                IceLogging.Info($"採集點索引 {Mission_Settings.nodeCounter} 超出這條路線的範圍" +
                                $"（共 {gatherInfo.Count} 個點），歸零重來。", pathHandle);
                Mission_Settings.nodeCounter = 0;
            }

            var location = gatherInfo[Mission_Settings.nodeCounter];
            if (!Task_NavmeshMove.NavToDestination(location.LandZone, distance: 1))
            {
                UseCordial();
                ThrottleMessage("Currently in the process of moving, so going to wait", "Task_Gather: NavmeshMovement");
                return false;
            }
            else
            {
                if (CosmicHandler.IsMissionTimedOut())
                {
                    IceLogging.Info($"We've managed to time out the mission. Going to attempt to turnin, and abandon if not", "[Gathering: Open Gathering Menu]");
                    SchedulerMain.State = IceState.AbandonMission;
                    P.TaskManager.Tasks.Clear();
                    return true;
                }
                else if (Svc.Condition[ConditionFlag.Gathering] && GenericHelpers.TryGetAddonMaster<Gathering>("Gathering", out var gather) && gather.IsAddonReady || GenericHelpers.TryGetAddonMaster<GatheringMasterpiece>("GatheringMasterpiece", out var collectable) && collectable.IsAddonReady)
                {
                    Mission_Settings.CollectableStep = 0;

                    IceLogging.Info($"Gathering window is now visible, continuing onto GatheringInteraction Task", "[Gathering: OpenGatheringMenu]");
                    P.TaskManager.Insert(() => GatheringInteraction(), "Gathering at the node", Utils.TaskConfig);
                    return true;
                }
                else
                {
                    Utils.TryGetObjectByDataId(location.NodeId, out var node);
                    if (node != null && !Player.IsJumping)
                    {
                        if (node.IsTargetable)
                        {
                            if (EzThrottler.Throttle("Target + Interacting w/ node"))
                            {
                                Utils.TargetgameObject(node);
                                Utils.InteractWithObject(node);
                            }
                        }
                        else
                        {
                            // Node doesn't exist/isn't targetable. 
                            IceLogging.Info($"The current node doesn't exist, continuing onto the next", "[Gathering: OpenGatheringMenu]");
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        public static unsafe bool? GatheringInteraction()
        {
            // ✅ 曾經是零守衛的字典索引 ×2（CurrentMissionInfo 與 C.MissionConfig），已修：
            //    前者的守衛＝下一行的 CurrentMissionUnavailable，後者＝下方的 C.MissionConfig.TryGetValue。
            if (SchedulerMain.CurrentMissionUnavailable("[Task_Gather: Gathering Interaction]", out var missionInfo))
                return true;

            bool collectableItem = missionInfo.Attributes.HasFlag(MissionAttributes.Collectables);
            bool reduceItems = missionInfo.Attributes.HasFlag(MissionAttributes.ReducedItems);
            // MissionConfig 的鍵集合跟 SheetMissionDict 不同（前者被 MissionTimer 按需補上、含 0），
            // 所以要分開守；查不到就退回預設採集設定檔。
            var configId = C.MissionConfig.TryGetValue(CosmicHelper.CurrentLunarMission, out var gatherMissionConfig)
                ? gatherMissionConfig.GProfileId
                : 0;
            if (!C.GatherProfiles.TryGetValue(configId, out var gatherConfig) &&
                !C.GatherProfiles.TryGetValue(0, out gatherConfig))
            {
                // 原本是 C.GatherProfiles[0] 直接索引 —— 設定檔裡沒有 0 號設定檔就是 KeyNotFoundException。
                IceLogging.ChatError("找不到任何可用的採集設定檔（連預設的 0 號都沒有），無法自動採集。", "[ICE]");
                SchedulerMain.AbortToStateCheck();
                return true;
            }
            var gathActions = GatheringUtil.GathActionDict;

            if (EzThrottler.Throttle("Saying what profile you're using", 2000))
            {
                IceLogging.Info($"Gathering Profile Info\n" +
                                $"Mission: {CosmicHelper.CurrentLunarMission}\n" +
                                $"Selected profile ID: {configId}\n" +
                                $"Gather profile Name: {gatherConfig.Name}");
            }

            var collectorBuffs = GatheringUtil.GathCollectableBuffs;
            var collectorAction = GatheringUtil.GathCollectableActions;
            var jobId = Player.JobId;

            if (P.Navmesh.IsRunning())
            {
                if (EzThrottler.Throttle("Stopping navmesh, cause we shouldn't be running here"))
                    P.Navmesh.Stop();
            }

            if (Svc.Condition[ConditionFlag.Gathering])
            {
                // This should always be true while either
                // -> Enter gathering window
                // -> Using Actions
                // -> Gathering item
                // -> Exiting the gathering state.

                if (!Svc.Condition[ConditionFlag.ExecutingGatheringAction])
                {
                    // We don't want to try and execute another action while we're currently in the middle of one

                    if (GenericHelpers.TryGetAddonMaster<Gathering>("Gathering", out var gather) && gather.IsAddonReady)
                    {
                        // this is the first window that you see. 
                        if (gather.CurrentIntegrity != 0)
                        {
                            if (collectableItem || reduceItems)
                            {
                                // no buffs are needed to apply before we go into the collectable window
                                foreach (var item in gather.GatheredItems)
                                {
                                    if (item.IsCollectable)
                                    {
                                        if (EzThrottler.Throttle("Swapping to collectable menu"))
                                        {
                                            item.Gather();
                                            Mission_Settings.item_collectableId = item.ItemID;

                                            if (PlayerHelper.GetGp() >= 400)
                                            {
                                                Mission_Settings.SelectedRotation = 1;
                                            }
                                            else
                                            {
                                                Mission_Settings.SelectedRotation = 0;
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                bool missingDur = gather.CurrentIntegrity != gather.TotalIntegrity;
                                var testItem = gather.GatheredItems.Where(x => x.ItemID != 0).FirstOrDefault();
                                int gatherChance = testItem.GatherChance;
                                int boonChance = testItem.BoonChance;
                                int playerGp = PlayerHelper.GetGp();

                                if (UseGatherAction(configId, gatherChance, boonChance, missingDur, playerGp))
                                {
                                    return false;
                                }

                                // 用方法開頭守衛拿到的 missionInfo，不要再讀一次（原本這裡是
                                // CosmicHelper.CurrentMissionInfo 的零守衛索引）。
                                foreach (var item in missionInfo.Gathering_Min.OrderByDescending(x => x.Value))
                                {
                                    if (PlayerHelper.GetItemCount(item.Key, out var count) && count < item.Value)
                                    {
                                        if (EzThrottler.Throttle("Gathering Item"))
                                        {
                                            gather.GatheredItems.Where(x => x.ItemID == item.Key).FirstOrDefault().Gather();
                                        }
                                        return false;
                                    }
                                }

                                if (EzThrottler.Throttle("Gathering item for score", 100))
                                {
                                    // if we're here, then we just need to gather for score. So... gathering for score lol
                                    gather.GatheredItems.Where(x => x.ItemID != 0).FirstOrDefault().Gather();
                                }
                                return false;
                            }
                        }
                        else
                        {
                            // No more integrity is left, time to just wait for you to stop gathering
                        }
                    }
                    else if (GenericHelpers.TryGetAddonMaster<GatheringMasterpiece>("GatheringMasterpiece", out var collectable) && collectable.IsAddonReady)
                    {
                        // Specifically for gathering collectables at the nodes (this also includes the collectables -> reducables... ugh)
                        var currentQuality = collectable.CurrentCollectability;
                        var minQuality = collectable.MinCollectability;
                        var midQuality = collectable.MidCollectability;
                        var highQuality = collectable.HighCollectability;
                        var currentDur = collectable.CurrentIntegrity;
                        var maxDur = collectable.TotalIntegrity;
                        bool missingDur = currentDur < maxDur;

                        if (Mission_Settings.item_collectableId != collectable.ItemID)
                        {
                            IceLogging.Debug($"Setting Mission CollectableId to: {collectable.ItemID}", "[Gather: Collectable Interacting]");
                            Mission_Settings.item_collectableId = collectable.ItemID;
                        }

                        // Something to note. It sometimes doesn't have all 3. One of these could be a 0... something to think about/need to check
                        // Think the process is going to be 
                        // Check to see if you meet tier 2/3 thresh
                        // If you have > 2 durability && If you don't meet these requirements
                        //   If you don't have the increase stat buff, and have it for this mission, use it
                        //   Purple Button on the bottom left -> Increase Quality + Chance to not use dur
                        // If you meet requirements
                        //   -> If missing durability, check to see if increaseInteg Skill is usable
                        //   -> If not missing durability, collect

                        if (Mission_Settings.SelectedRotation == 1)
                        {
                            NormalGpRotation(currentQuality, missingDur);
                        }
                        else
                        {
                            NoGpRotation(currentDur, currentQuality, highQuality);
                        }

                    }
                }
                else
                {
                    P.TaskManager.Insert(() => WaitToGather());
                    return true;
                }
            }
            else
            {
                // No longer gathering an item. Time to check current state
                return true;
            }

            return false;
        }
        private static bool? WaitToGather()
        {
            if (!Svc.Condition[ConditionFlag.ExecutingGatheringAction])
            {
                IceLogging.Info("No longer executing a gathering action", "[Task Gather: Wait To Gather]");
                return true;
            }
            else
            {
                if (Mission_Settings.NextCollectableStep != Mission_Settings.CollectableStep)
                {
                    IceLogging.Debug($"Current Collectable Step: {Mission_Settings.CollectableStep} | Setting it to: {Mission_Settings.NextCollectableStep}");
                    Mission_Settings.CollectableStep = Mission_Settings.NextCollectableStep;
                }
                return false;
            }
        }
        public static unsafe bool UseGatherAction(int profileId, int gatherChance, int? boonChance, bool missingDur, int availableGp)
        {
            C.GatherProfiles.TryGetValue(profileId, out var gatherProfile);
            if (gatherProfile == null)
            {
                gatherProfile = C.GatherProfiles[0];
                if (EzThrottler.Throttle("Null Profile Selected"))
                {
                    IceLogging.Error("Hey! We've somehow stumbled into a null profile being selected. Please make sure:\n" +
                                     "1: The mission you have selected has a gathering profile selected\n" +
                                     "2: If it does have one, try to click on it again\n" +
                                     "3: If that still doesn't work, let me know you're getting this error message.\n" +
                                     $"Expected profileId: {profileId} | Defaulted to the default profile");
                }
            }

            if (gatherChance != 100)
            {
                if (EzThrottler.Throttle("Helper Log"))
                {
                    IceLogging.Debug($"Gathering Chance: {gatherChance}", debugOnly: true);
                }
                uint MasteryBuff = GatheringUtil.GathActionDict["FieldMasteryI"].StatusId;

                string? SelectBestFieldMastery(int currentChance, int availableGp)
                {
                    int playerLevel = Player.Level;

                    bool MasteryIII = gatherProfile.GatherBuffs.Buffs["FieldMasteryIII"].Enabled
                                   && (gatherProfile.GatherBuffs.Buffs["FieldMasteryIII"].MinGp <= PlayerHelper.GetGp())
                                   && (PlayerHelper.GetGp() >= GatheringUtil.GathActionDict["FieldMasteryIII"].RequiredGp)
                                   && (playerLevel >= GatheringUtil.GathActionDict["FieldMasteryIII"].RequiredLv)
                                   && (gatherProfile.GatherBuffs.Buffs["FieldMasteryIII"].MaxUse == -1 
                                       || Mission_Settings.SkillUseAmount["FieldMasteryIII"] < gatherProfile.GatherBuffs.Buffs["FieldMasteryIII"].MaxUse);
                    bool MasteryII = gatherProfile.GatherBuffs.Buffs["FieldMasteryII"].Enabled
                                  && (gatherProfile.GatherBuffs.Buffs["FieldMasteryII"].MinGp <= PlayerHelper.GetGp())
                                  && (PlayerHelper.GetGp() >= GatheringUtil.GathActionDict["FieldMasteryII"].RequiredGp)
                                  && (playerLevel >= GatheringUtil.GathActionDict["FieldMasteryII"].RequiredLv)
                                  && (gatherProfile.GatherBuffs.Buffs["FieldMasteryII"].MaxUse == -1
                                      || Mission_Settings.SkillUseAmount["FieldMasteryII"] < gatherProfile.GatherBuffs.Buffs["FieldMasteryII"].MaxUse);
                    bool MasteryI = gatherProfile.GatherBuffs.Buffs["FieldMasteryI"].Enabled
                                 && (gatherProfile.GatherBuffs.Buffs["FieldMasteryI"].MinGp <= PlayerHelper.GetGp())
                                 && (PlayerHelper.GetGp() >= GatheringUtil.GathActionDict["FieldMasteryI"].RequiredGp)
                                 && (playerLevel >= GatheringUtil.GathActionDict["FieldMasteryI"].RequiredLv)
                                 && (gatherProfile.GatherBuffs.Buffs["FieldMasteryI"].MaxUse == -1
                                     || Mission_Settings.SkillUseAmount["FieldMasteryI"] < gatherProfile.GatherBuffs.Buffs["FieldMasteryI"].MaxUse);

                    // Already at 100%? No skill needed, so continuing on
                    if (currentChance >= 100)
                        return null;

                    int neededBonus = 100 - currentChance;

                    // Find the cheapest skill that gets us to 100%
                    if (neededBonus <= 5 && availableGp >= 50 && MasteryI)
                        return "FieldMasteryI";

                    if (neededBonus <= 15 && availableGp >= 100 && MasteryII)
                        return "FieldMasteryII";

                    if (neededBonus <= 50 && availableGp >= 250 && MasteryIII)
                        return "FieldMasteryIII";

                    // If we can't reach 100%, use the best skill we can afford 
                    if (availableGp >= 250  && MasteryIII)
                        return "FieldMasteryIII";

                    if (availableGp >= 100 && MasteryII)
                        return "FieldMasteryII";

                    if (availableGp >= 50  && MasteryI)
                        return "FieldMasteryI";

                    return null; // Can't afford any skill
                }

                if (!PlayerHelper.HasStatusId(MasteryBuff) && (SelectBestFieldMastery(gatherChance, availableGp) != null))
                {
                    string? ActionName = SelectBestFieldMastery(gatherChance, availableGp);
                    if (ActionName != null)
                    {
                        if (EzThrottler.Throttle($"Using Gathering Action: {ActionName}", 100))
                        {
                            uint jobId = Player.JobId;

                            IceLogging.Debug($"Using the following action: {ActionName} to gain some collectability from the node", debugOnly: true);
                            var actionId = GatheringUtil.GathActionDict[ActionName].ClassAction[jobId].ActionId;
                            ActionManager.Instance()->UseAction(ActionType.Action, actionId);
                            Mission_Settings.SkillUseAmount[ActionName] += 1;
                        }
                        return true;
                    }
                }

                int playerLevel = Player.Level;
                uint TempMasteryBuffId = GatheringUtil.GathActionDict["FieldMasteryTemp"].StatusId;
                bool TempMasteryBuff = gatherProfile.GatherBuffs.Buffs["FieldMasteryTemp"].Enabled
                                    && (gatherProfile.GatherBuffs.Buffs["FieldMasteryTemp"].MinGp <= PlayerHelper.GetGp())
                                    && (PlayerHelper.GetGp() >= GatheringUtil.GathActionDict["FieldMasteryTemp"].RequiredGp)
                                    && (playerLevel >= GatheringUtil.GathActionDict["FieldMasteryTemp"].RequiredLv)
                                    && (gatherProfile.GatherBuffs.Buffs["FieldMasteryTemp"].MaxUse == -1);

                if (!PlayerHelper.HasStatusId(TempMasteryBuffId) && TempMasteryBuff)
                {
                    if (EzThrottler.Throttle($"Using Gathering Action: {"FieldMasteryTemp"}", 100))
                    {
                        uint jobId = Player.JobId;

                        IceLogging.Debug($"Using the following action: {"FieldMasteryTemp"} to gain some collectability from the node", debugOnly: true);
                        var actionId = GatheringUtil.GathActionDict["FieldMasteryTemp"].ClassAction[jobId].ActionId;
                        ActionManager.Instance()->UseAction(ActionType.Action, actionId);
                        Mission_Settings.SkillUseAmount["FieldMasteryTemp"] += 1;
                    }
                    return true;
                }
            }

            // general logic for checking for the rest of the buffs now
            foreach (var buff in Mission_Settings.SkillUseAmount)
            {
                string action = buff.Key;
                if (CanUseGatheringAction(action, profileId, missingDur, boonChance))
                {
                    var actionInfo = GatheringUtil.GathActionDict[action];
                    if (EzThrottler.Throttle($"Using Gathering Action: {action}"))
                    {
                        uint jobId = Player.JobId;

                        IceLogging.Debug($"Using the following action: {action} on the node", debugOnly: true);
                        var actionId = GatheringUtil.GathActionDict[action].ClassAction[jobId].ActionId;
                        ActionManager.Instance()->UseAction(ActionType.Action, actionId);
                        Mission_Settings.SkillUseAmount[action] += 1;
                    }

                    return true;
                }
            }

            return false;
        }
        public static bool CanUseGatheringAction(string actionName, int profileId, bool missingDur, int? boonChance = null)
        {
            var actionInfo = GatheringUtil.GathActionDict[actionName];
            bool hasStatus = PlayerHelper.HasStatusId(actionInfo.StatusId);
            bool hasGp = PlayerHelper.GetGp() >= actionInfo.RequiredGp;
            var used = Mission_Settings.SkillUseAmount[actionName];
            bool properLvl = Player.Level >= actionInfo.RequiredLv;

            if (actionName == "BonusIntegrityChance")
            {
                return hasStatus && missingDur;
            }

            var gatherBuff = C.GatherProfiles[profileId].GatherBuffs.Buffs[actionName];

            return actionName switch
            {
                "BoonIncrease1" => gatherBuff.Enabled 
                                && boonChance < 100 
                                && !hasStatus
                                && !missingDur 
                                && hasGp 
                                && PlayerHelper.GetGp() >= gatherBuff.MinGp
                                && (gatherBuff.MaxUse == -1 || gatherBuff.MaxUse > used)
                                && properLvl,
                "BoonIncrease2" => gatherBuff.Enabled 
                                && boonChance < 100 
                                && !hasStatus 
                                && !missingDur 
                                && hasGp 
                                && PlayerHelper.GetGp() >= gatherBuff.MinGp
                                && (gatherBuff.MaxUse == -1 || gatherBuff.MaxUse > used)
                                && properLvl,
                "Tidings" => gatherBuff.Enabled 
                          && !hasStatus 
                          && !missingDur 
                          && hasGp 
                          && PlayerHelper.GetGp() >= gatherBuff.MinGp
                          && (gatherBuff.MaxUse == -1 || gatherBuff.MaxUse > used)
                          && properLvl,
                "YieldI" => gatherBuff.Enabled 
                          && !hasStatus 
                          && !missingDur 
                          && hasGp 
                          && PlayerHelper.GetGp() >= gatherBuff.MinGp
                          && (gatherBuff.MaxUse == -1 || gatherBuff.MaxUse > used)
                          && properLvl,
                "YieldII" => gatherBuff.Enabled 
                         && !hasStatus 
                         && !missingDur 
                         && hasGp 
                         && PlayerHelper.GetGp() >= gatherBuff.MinGp
                         && (gatherBuff.MaxUse == -1 || gatherBuff.MaxUse > used)
                         && properLvl,
                "BonusIntegrity" => gatherBuff.Enabled 
                                    && missingDur 
                                    && hasGp 
                                    && PlayerHelper.GetGp() >= gatherBuff.MinGp 
                                    && (gatherBuff.MaxUse == -1 || gatherBuff.MaxUse > used)
                                    && properLvl,
                "BountifulYieldII" => gatherBuff.Enabled 
                                   && !hasStatus 
                                   && hasGp 
                                   && PlayerHelper.GetGp() >= gatherBuff.MinGp
                                   && (gatherBuff.MaxUse == -1 || gatherBuff.MaxUse > used) 
                                   && properLvl,
                _ => false,
            };
        }
        private static bool CanUseCollectableAction(string action, bool missingDur = false)
        {
            var actionInfo = GatheringUtil.GathCollectableBuffs[action];
            bool hasStatus = PlayerHelper.HasStatusId(actionInfo.StatusId);
            bool hasGp = PlayerHelper.GetGp() >= actionInfo.RequiredGp;

            return action switch
            {
                "Scrutiny" => !hasStatus
                           && hasGp,
                "Focus" => !hasStatus
                        && hasGp,
                "Priming" => !hasStatus 
                          && hasGp,
                "CollectorsHigh" => !hasStatus
                                 && hasGp,
                "BonusIntegrityChance" => hasStatus
                                       && missingDur,
                "BonusIntegrity" => hasGp
                                 && missingDur
                                 && PlayerHelper.GetGp() >= 300,
                _ => false,
            };
        }
        public static unsafe bool NormalGpRotation(int collectability, bool missingDur = false)
        {
            if (EzThrottler.Throttle("Executing HighGPRotation", 100))
            {
                // 400+ gp
                int step = Mission_Settings.CollectableStep;

                if (step == 0)
                {
                    if (!PlayerHelper.HasStatusId(3911) && GatheringUtil.CollectStandardCharges() > 0)
                    {
                        if (EzThrottler.Throttle("Using special buff", 100))
                        {
                            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 27);
                        }
                    }
                    else if (CanUseCollectableAction("Scrutiny"))
                    {
                        UseCollectableBuff("Scrutiny");
                    }
                    else
                    {
                        UseCollectableAction("Meticulous");
                        Mission_Settings.NextCollectableStep = 1;
                    }
                }
                else if (step == 1)
                {
                    // Option 1
                    if (!PlayerHelper.HasStatusId(3911))
                    {
                        if (!PlayerHelper.HasStatusId(3911) && GatheringUtil.CollectStandardCharges() > 0)
                        {
                            if (EzThrottler.Throttle("Using special buff", 100))
                            {
                                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 27);
                            }
                        }
                        else if (CanUseCollectableAction("Scrutiny"))
                        {
                            UseCollectableBuff("Scrutiny");
                        }
                        else
                        {
                            UseCollectableAction("Meticulous");
                            Mission_Settings.NextCollectableStep = 2;
                        }
                    }
                    else // Option 2, Has "Collector's High Standard"
                    {
                        if (CanUseCollectableAction("Scrutiny"))
                        {
                            UseCollectableBuff("Scrutiny");
                        }
                        else
                        {
                            UseCollectableAction("Brazen");
                            Mission_Settings.NextCollectableStep = 3;
                        }
                    }
                }
                else if (step == 2)
                {
                    if ((PlayerHelper.HasStatusId(3911) && collectability > 800) || (collectability >= 850 && collectability <= 999))
                    {
                        // Top Row option, 
                        UseCollectableAction("Meticulous");
                    }
                    else if (collectability == 1000)
                    {
                        Mission_Settings.CollectableStep = 4;
                    }
                    else
                    {
                        UseCollectableAction("Scour");
                        Mission_Settings.NextCollectableStep = 4;
                    }
                }
                else if (step == 3)
                {
                    if (collectability < 1000)
                    {
                        UseCollectableAction("Meticulous");
                        Mission_Settings.NextCollectableStep = 4;
                    }
                    else
                    {
                        Mission_Settings.CollectableStep = 4;
                    }
                }
                else if (step == 4)
                {
                    IceLogging.Debug($"Missing durability: {missingDur}");
                    if (CanUseCollectableAction("BonusIntegrityChance", missingDur))
                    {
                        UseCollectableAction("BonusIntegrityChance");
                    }
                    else if (CanUseCollectableAction("BonusIntegrity", missingDur))
                    {
                        UseCollectableAction("BonusIntegrity");
                    }
                    else
                    {
                        UseCollectableAction("Collect");
                    }
                }
            }

            return false;
        }
        public static bool NoGpRotation(uint currentDur, int collectability, uint hqCollectability)
        {
            if (currentDur > 1 && collectability < hqCollectability)
            {
                UseCollectableAction("Meticulous");
            }
            else
            {
                UseCollectableAction("Collect");
            }

            return false;
        }
        public static unsafe void UseCollectableBuff(string action)
        {
            var collectorBuffs = GatheringUtil.GathCollectableBuffs;
            var jobId = Player.JobId;

            var actionId = collectorBuffs[action].ClassAction[jobId].ActionId;
            if (EzThrottler.Throttle("Using Action Buff", 100))
            {
                ActionManager.Instance()->UseAction(ActionType.Action, actionId);
            }
        }
        public static unsafe void UseCollectableAction(string action)
        {
            var collectorAction = GatheringUtil.GathCollectableActions;
            var jobId = Player.JobId;

            var actionId = collectorAction[action].ClassAction[jobId].ActionId;
            if (EzThrottler.Throttle("using Action Action for collectables", 100))
            {
                ActionManager.Instance()->UseAction(ActionType.Action, actionId);
            }
        }
        public static bool? CheckReduceMission()
        {
            IceLogging.Info($"Current itemId: {Mission_Settings.item_collectableId}", "[Gather: Check Reduce Mission]");

            // ✅ 曾經是零守衛的字典索引，已修：守衛＝下方的 SchedulerMain.CurrentMissionUnavailable。
            // ⚠️ 這裡的 hasCollectable 讀到 0 只是「不做精選」，不是破壞性判斷，所以不必擋換區。
            if (SchedulerMain.CurrentMissionUnavailable("[Gather: Check Reduce Mission]", out var reduceMission))
                return true;

            bool hasCollectable = PlayerHelper.GetItemCount(Mission_Settings.item_collectableId, out var count) && count > 0;
            bool isReducableMission = reduceMission.Attributes.HasFlag(MissionAttributes.ReducedItems);
            if (hasCollectable && isReducableMission)
            {
                P.TaskManager.InsertMulti(
                                            new(() => CheckReduceItems(), "Starting the desynth process"),
                                            new(() => WaitForDesynthCompletion(), "Waiting for desyntht to complete")
                                         );
            }

            return true;
        }
        public static unsafe bool? CheckReduceItems()
        {
            if (Svc.Condition[ConditionFlag.Occupied39])
            {
                IceLogging.Info("We're currently desynthing an item, continuing on to wait to stop", "[Task Gather: Reducing Item Check]");
                return true;
            }
            else
            {
                if (GenericHelpers.TryGetAddonMaster<Gathering>("Gathering", out var gather) && gather.IsAddonReady)
                {
                    // we shouldn't have this open while we're desynthing. . . closing it out.
                    if (EzThrottler.Throttle("Closing gather window"))
                    {
                        // 
                    }
                }

                // We have items to desynth! Time to check and see which window we need to interact with... or just wait. 
                if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("PurifyItemSelector", out var desynthWindow) && desynthWindow->IsReady)
                {
                    if (EzThrottler.Throttle("Desynthing the item"))
                    {
                        if (!Player.IsBusy)
                            ECommons.Automation.Callback.Fire(desynthWindow, true, 12, 0);
                    }
                }
                else if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var missionInfo) && missionInfo.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Opening the desynth window"))
                    {
                        missionInfo.StellerReduction();
                    }
                }
                else if (GenericHelpers.TryGetAddonMaster<WKSHud>("WKSHud", out var moonHud) && moonHud.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Opening the moon hud"))
                    {
                        moonHud.Mission();
                    }
                }
            }

            return false;
        }
        public static bool? WaitForDesynthCompletion()
        {
            if (!Svc.Condition[ConditionFlag.Occupied39])
            {
                PlayerHelper.GetItemCount(Mission_Settings.item_collectableId, out var count);
                if (count != 0)
                {
                    // Still have some more items to desynth, going to reset the current task count and re-check
                    P.TaskManager.Tasks.Clear();
                }

                return true;
            }

            return false;
        }
        public static unsafe void UseCordial()
        {
            if (EzThrottler.Throttle("Cordial usage check while moving"))
            {
                if (!Player.IsBusy)
                {
                    IceLogging.Debug("Cordial Checkers");
                    if (C.AutoCordial)
                    {
                        IceLogging.Debug($"Min GP: {C.CordialMinGp} <= {PlayerHelper.GetGp()}");

                        if (PlayerHelper.GetGp() <= C.CordialMinGp)
                        {
                            Dictionary<uint, int> cordials = new()
                            {
                                { 12669, 400}, // Hi
                                { 1006141, 350}, // HQ Regular
                                { 6141, 300}, // NQ Regular
                                { 1016911, 200}, // HQ Watered
                                { 16911, 150} // HQ Watered
                            };

                            foreach (var cordial in C.inverseCordialPrio ? cordials.Reverse() : cordials)
                            {
                                IceLogging.Debug($"Checking Cordial: {cordial.Key}");
                                bool hq = cordial.Key >= 1_000_000;
                                if (PlayerHelper.GetItemCount(cordial.Key, out var amount, hq, !hq) && amount > 0)
                                {
                                    if (ActionManager.Instance()->GetActionStatus(ActionType.Item, cordial.Key) == 0)
                                    {
                                        if (!C.PreventOvercap || (C.PreventOvercap && !WillOvercap(cordial.Value)))
                                        {
                                            if (EzThrottler.Throttle("Using the cordial"))
                                            {
                                                ActionManager.Instance()->UseAction(ActionType.Item, cordial.Key, extraParam: 65535);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    IceLogging.Info("Cordial Check Complete");
                }
            }
        }
        private static bool WillOvercap(int recoveryGP)
        {
            return ((PlayerHelper.GetGp() + recoveryGP) > PlayerHelper.MaxGp());
        }
        public static unsafe bool? UseFood()
        {
            var ItemId = C.GatheringFood;
            if (C.UseGatheringFood && ItemId != 0)
            {
                PlayerHelper.GetItemCount(ItemId, out var HqCount, includeNq: false);
                PlayerHelper.GetItemCount(ItemId, out var NqCount, includeHq: false);

                if (HqCount > 0 || NqCount > 0)
                {
                    // We've gotten this far, which means we have a gathering item to use...
                    if (!PlayerHelper.HasFoodRunning())
                    {
                        // We need to apply the food, since we have some, we're going to use some here
                        if (EzThrottler.Throttle("Using Food Item", 3000))
                        {
                            if (HqCount > 0)
                                ItemId += 1_000_000;

                            ActionManager.Instance()->UseAction(ActionType.Item, ItemId, extraParam: 65535);
                            IceLogging.Debug($"Attempting to use food: {ItemId}");
                        }
                        return false;
                    }
                    else
                    {
                        IceLogging.Info("We have food running, and it's the proper one! Continuing");
                        return true;
                    }
                }
                else
                {
                    IceLogging.Info("We are out of the current food, continuing on w/o buff");
                    return true;
                }
            }
            else
            {
                IceLogging.Info("We either don't have use food enabled, or have no food selected. Continuing on\n" +
                               $"Use Food Enabled: {C.UseGatheringFood}\n" +
                               $"ItemId of food: {ItemId}");
                return true;
            }
        }
        private static void ThrottleMessage(string s, string handle)
        {
            if (EzThrottler.Throttle($"Throttling the following message: {s}", 1000))
            {
                IceLogging.Debug(s, handle);
            }
        }
    }
}
