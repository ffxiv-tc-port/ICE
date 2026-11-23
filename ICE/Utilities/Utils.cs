using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices.Legacy;
using ECommons.GameHelpers;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.MechaOps;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ICE.Utilities;

/// <summary>
/// Misc unused (yet) or weird functions i didn't know where to put - Chika
/// </summary>
public static unsafe class Utils
{
    public static float enableColumnLength = 0.0f;
    public static float IDLength = 0.0f;
    public static float missionLength = 0.0f;
    public static float flagLength = 0.0f;
    public static float cosmicLength = 0.0f;
    public static float lunarLength = 0.0f;
    public static float XPLength = 0.0f;
    public static float TurninComboLength = 100.0f;
    public static float GatherConfigLength = 0.0f;
    public static float MissionNotesLength = 0.0f;

    // Same answer as DalamudReflector.TryGetDalamudPlugin(name, ignoreCache: true) (a same-named entry that is loaded),
    // without reflection and without writing ECommons' static plugin cache, so it is safe from any thread.
    // Dalamud hands out a read-only snapshot list; indexing it skips the enumerator allocation.
    public static bool HasPlugin(string name)
    {
        var plugins = Svc.PluginInterface.InstalledPlugins;
        if (plugins is IReadOnlyList<IExposedPlugin> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p.InternalName == name && p.IsLoaded)
                    return true;
            }
            return false;
        }

        foreach (var p in plugins)
            if (p.InternalName == name && p.IsLoaded)
                return true;
        return false;
    }

    /// <summary>已載入的同名插件版本；沒裝或沒載入回 null。</summary>
    public static string? LoadedPluginVersion(string name)
    {
        foreach (var p in Svc.PluginInterface.InstalledPlugins)
            if (p.InternalName == name && p.IsLoaded)
                return p.Version?.ToString();
        return null;
    }
    public static TaskManagerConfiguration TaskConfig => new(timeLimitMS: 10 * 60 * 3000, abortOnTimeout: false);

    public static unsafe void SetFlagForNPC(uint territoryId, float x, float y)
    {
        var map = ExcelHelper.TerritorySheet.GetRow(territoryId).Map.Value;

        // 🔴 AgentMap.Instance() 是 AgentGetterGenerator 產出的兩層可空取得器
        //    （agentModule == null ? null : GetAgentByInternalId(...)），合法回 null。
        //    下面第一件事就是 `agent->FlagMarkerCount = 0` —— 那是**寫入**，
        //    裸寫等於往位址 0 寫，是攔不到的 AVE。拿不到就整個不插旗。
        var agent = AgentMap.Instance();
        if (agent == null)
        {
            IceLogging.Info($"AgentMap 尚未就緒，不設定 NPC 旗標（territory {territoryId}）。", "[Utils]");
            return;
        }

        Vector2 pos = MapToWorld(new Vector2(x, y), map.SizeFactor, map.OffsetX, map.OffsetY);

        agent->FlagMarkerCount = 0;
        agent->SetFlagMapMarker(territoryId, map.RowId, pos.X, pos.Y);
        agent->OpenMapByMapId(map.RowId, territoryId);
    }

    public static void SetRawFlagforNpc(uint territoryId, float x, float y)
    {
        var map = ExcelHelper.TerritorySheet.GetRow(territoryId).Map.Value;

        // 同 SetFlagForNPC：取得器合法回 null，而下一行是寫入。
        var agent = AgentMap.Instance();
        if (agent == null)
        {
            IceLogging.Info($"AgentMap 尚未就緒，不設定 NPC 旗標（territory {territoryId}）。", "[Utils]");
            return;
        }

        agent->FlagMarkerCount = 0;
        agent->SetFlagMapMarker(territoryId, map.RowId, x, y);
        agent->OpenMapByMapId(map.RowId, territoryId);
    }

    public static float MapToWorld(float value, uint scale, int offset) => -offset * (scale / 100.0f) + 50.0f * (value - 1) * (scale / 100.0f);

    public static Vector2 MapToWorld(Vector2 coordinates, ushort sizeFactor, short offsetX, short offsetY)
    {
        var scalar = sizeFactor / 100.0f;

        var xWorldCoord = MapToWorld(coordinates.X, sizeFactor, offsetX);
        var yWorldCoord = MapToWorld(coordinates.Y, sizeFactor, offsetY);

        var objectPosition = new Vector2(xWorldCoord, yWorldCoord);
        var center = new Vector2(1024.0f, 1024.0f);

        return objectPosition / scalar - center / scalar;
    }

    public static bool? TargetgameObjectTask(IGameObject? gameObject)
    {
        var x = gameObject;
        if (x == null)
            return false;

        if (Svc.Targets.Target != null && Svc.Targets.Target.BaseId == x.BaseId)
            return true;

        if (!GenericHelpers.IsOccupied())
        {
            if (x != null)
            {
                if (EzThrottler.Throttle($"Throttle Targeting {x.BaseId}"))
                {
                    Svc.Targets.SetTarget(x);
                    IceLogging.Info($"Setting the target to {x.BaseId}");
                }
            }
        }
        return false;
    }
    internal static bool TryGetObjectByDataId(ulong dataId, out IGameObject? gameObject) => (gameObject = Svc.Objects.OrderBy(PlayerHelper.GetDistanceToPlayer).FirstOrDefault(x => x.BaseId == dataId)) != null;
    internal static bool TryGetNpcObject(NpcData.NPCInfo npc, out IGameObject? gameObject)
    {
        foreach (var npcId in npc.AlternateNpcIds.Prepend(npc.NpcId))
        {
            if (TryGetObjectByDataId(npcId, out gameObject))
                return true;
        }

        const float fallbackRadius = 5f;
        var configuredIds = string.Join(", ", npc.AlternateNpcIds.Prepend(npc.NpcId));
        var currentTarget = Svc.Targets.Target;
        if (IsNpcNearConfiguredLocation(currentTarget, npc.NpcLocation, fallbackRadius))
        {
            gameObject = currentTarget;
            IceLogging.Warning(
                $"Configured NPC IDs [{configuredIds}] were not found; using targeted NPC " +
                $"{gameObject.BaseId} ({gameObject.Name}) near the configured location.",
                "[NPC Resolver]");
            return true;
        }

        gameObject = Svc.Objects
            .Where(x => IsNpcNearConfiguredLocation(x, npc.NpcLocation, fallbackRadius))
            .OrderBy(x => Vector3.Distance(x.Position, npc.NpcLocation))
            .FirstOrDefault();

        if (gameObject != null)
        {
            IceLogging.Warning(
                $"Configured NPC IDs [{configuredIds}] were not found; using nearby NPC " +
                $"{gameObject.BaseId} ({gameObject.Name}) at the configured location.",
                "[NPC Resolver]");
            return true;
        }

        return false;
    }

    private static bool IsNpcNearConfiguredLocation(IGameObject? gameObject, Vector3 location, float radius)
    {
        if (gameObject == null || !gameObject.IsTargetable)
            return false;

        if (gameObject.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc
            && gameObject.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
            return false;

        return Vector3.Distance(gameObject.Position, location) <= radius;
    }

    public static IGameObject? TryGetObjectNearestEventObject()
    {
        return Svc.Objects.OrderBy(PlayerHelper.GetDistanceToPlayer).FirstOrDefault(x => x.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj);
    }
    /// <summary>
    /// 找緊急任務（紅色警報）的繳交點。
    /// </summary>
    /// <param name="expectedLocation">
    /// 這個任務的繳交點**應該**在哪（<c>GatheringUtil.CriticalLocations</c> 的原始座標）。
    /// 不給就是舊行為：只按「離玩家最近」挑。
    /// </param>
    /// <param name="maxDistanceFromExpected">離 <paramref name="expectedLocation"/> 多遠以內才算數。</param>
    public static IGameObject? TryGetObjectCollectionPoint(Vector3? expectedLocation = null, float maxDistanceFromExpected = 100f)
    {
        var candidates = Svc.Objects.Where(x => x.IsTargetable && (x.BaseId == 2014616 || x.BaseId == 2014618));

        if (expectedLocation is { } location)
        {
            return candidates
                .Where(x => Vector3.Distance(x.Position, location) <= maxDistanceFromExpected)
                .OrderBy(PlayerHelper.GetDistanceToPlayer)
                .ThenBy(x => Vector3.Distance(x.Position, location))
                .FirstOrDefault();
        }

        return candidates.OrderBy(PlayerHelper.GetDistanceToPlayer).FirstOrDefault();
    }
    public static void TargetgameObject(IGameObject? gameObject)
    {
        var x = gameObject;
        if (x == null)
            return;

        var currentTarget = Svc.Targets.Target;
        if (currentTarget != null && currentTarget.BaseId == x.BaseId)
            return;

        if (!GenericHelpers.IsOccupied())
        {
            if (x != null)
            {
                if (EzThrottler.Throttle($"Throttle targeting: {x.BaseId}"))
                {
                    IceLogging.Info($"Attempting to set the target to: {x.BaseId} | {x.Name}", "[Target Game Object]");
                    // 🔑 機甲事件錄製要分得出「人選的」與「外掛選的」——這裡是 ICE 主動指定目標
                    //    的唯一路徑，先登記再真的設定。錄製沒開時整個方法第一行就 return。
                    MechaEventRecorder.NoteIceAction("target", x);
                    Svc.Targets.SetTarget(x);
                }
            }
        }
    }
    public static unsafe void InteractWithObject(IGameObject? gameObject)
    {
        try
        {
            if (gameObject == null || !gameObject.IsTargetable)
                return;
            // ICE 自己發起的互動（機甲事件錄製要分得出人按的與外掛按的）。呼叫點全部經過這裡，所以只要記這一處。
            MechaEventRecorder.NoteIceAction("interact", gameObject);
            var gameObjectPointer = (GameObject*)gameObject.Address;
            TargetSystem.Instance()->InteractWithObject(gameObjectPointer, false);
        }
        catch (Exception ex)
        {
            IceLogging.Error($"InteractWithObject: Exception: {ex}");
        }
    }
    public static unsafe void SetGatheringRing(uint territoryId, int x, int y, int radius, string? tooltip = "Node Location")
    {
        var map = ExcelHelper.TerritorySheet.GetRow(territoryId).Map.Value;
        // 同 SetFlagForNPC：取得器合法回 null，而下面第一件事是寫 FlagMarkerCount。
        var agent = AgentMap.Instance();
        if (agent == null)
        {
            IceLogging.Info($"AgentMap 尚未就緒，不畫採集圈（territory {territoryId}）。", "[Utils]");
            return;
        }

        Vector2 pos = MapToWorld(new Vector2(x, y), map.SizeFactor, map.OffsetX, map.OffsetY);
        IceLogging.Debug($"Current map: {map.RowId} {territoryId} | {map.PlaceName.Value.Name} | {pos.X} {pos.Y} | {x} {y} | {radius} | {tooltip}");

        agent->FlagMarkerCount = 0;
        // agent->IsFlagMarkerSet = false;
        agent->SetFlagMapMarker(territoryId, map.RowId, x, y);
        agent->TempMapMarkerCount = 0;
        agent->AddGatheringTempMarker(x, y, radius, tooltip: tooltip);
        agent->OpenMap(map.RowId, territoryId, tooltip, MapType.GatheringLog);
    }
    internal static unsafe bool MountUnlockQueryMissing()
        => GameFunctionGuard.Missing(PlayerState.MemberFunctionPointers.IsMountUnlocked is null,
            "PlayerState.IsMountUnlocked", "判斷不出坐騎解鎖了沒，改用隨機坐騎，坐騎選單只列隨機坐騎。");

    public static unsafe void MountAction()
    {
        bool useMount = C.MountId != 0 && !MountUnlockQueryMissing() && PlayerState.Instance()->IsMountUnlocked(C.MountId);

        if (!Player.IsCasting && !Player.Mounting)
        {
            if (useMount)
            {
                ActionManager.Instance()->UseAction(ActionType.Mount, C.MountId);
                IceLogging.Info($"Attempting to mount: {C.MountName}");
            }
            else
            {
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
                IceLogging.Info($"Resorting to using the mount roulette");
            }
        }
    }
    public static unsafe void Dismount()
    {
        if (Player.Mounted)
        {
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
        }
    }

    // 兩次輪詢隔超過 2 秒＝新的一次開工（失敗後的退避至少 3 秒）。
    private static readonly DismountWait craftDismountWait = new(2_000);

    /// <summary>
    /// 交給 Artisan 製作前先下坐騎：坐騎上開工遊戲會拒絕，Artisan 十秒內連錯五次就自己停用。
    /// 用一般動作 23（跳下），不用 <see cref="Dismount"/> 的 9（隨機坐騎）：狀態旗標慢一拍時重送 9 會再騎上去，23 落地後重送沒有作用。
    /// </summary>
    /// <returns>true＝沒在坐騎上，可以送；false＝還在等；null＝等滿上限仍在坐騎上，這次不要送製作指令。</returns>
    public static bool? DismountBeforeCraft(string handle)
    {
        var mounted = Svc.Condition[ConditionFlag.Mounted];
        var am = ActionManager.Instance();
        var canPress = mounted && am != null && am->GetActionStatus(ActionType.GeneralAction, 23) == 0;
        var step = craftDismountWait.Poll(Environment.TickCount64, mounted, canPress);

        if (craftDismountWait.StartedRound)
            IceLogging.Info($"人在坐騎上，先下坐騎再交給 Artisan 製作（最多等 {DismountWait.TimeoutMs / 1000} 秒）。", handle);

        switch (step)
        {
            case DismountWait.Step.Proceed:
                if (craftDismountWait.FinishedWaiting)
                    IceLogging.Info($"已下坐騎（送了 {craftDismountWait.Presses} 次「跳下」、等了 {craftDismountWait.ElapsedMs / 1000.0:0.0} 秒），交給 Artisan。", handle);
                return true;
            case DismountWait.Step.Press:
                var accepted = am->UseAction(ActionType.GeneralAction, 23);
                IceLogging.Debug($"送出「跳下」第 {craftDismountWait.Presses} 次（遊戲回傳 {accepted}）。", handle);
                return false;
            case DismountWait.Step.Wait:
                return false;
            case DismountWait.Step.AfterGiveUp:
                return null;
            default:
                IceLogging.Info($"等了 {craftDismountWait.ElapsedMs / 1000.0:0.0} 秒仍在坐騎上（送了 {craftDismountWait.Presses} 次「跳下」），"
                                + "這次不送製作指令，當成製作沒有進展處理（退避後重試，連續五次就停止 ICE）。", handle);
                return null;
        }
    }

    public static uint ToUintABGR(Vector4 col)
    {
        byte a = (byte)(col.W * 255);
        byte b = (byte)(col.Z * 255);
        byte g = (byte)(col.Y * 255);
        byte r = (byte)(col.X * 255);
        return (uint)((a << 24) | (b << 16) | (g << 8) | r);
    }

    public static Vector4 FromUintABGR(uint color)
    {
        float a = ((color >> 24) & 0xFF) / 255f;
        float b = ((color >> 16) & 0xFF) / 255f;
        float g = ((color >> 8) & 0xFF) / 255f;
        float r = (color & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }

    public static void VnavBuildInfo()
    {
        // 🔴 BuildProgress 是 Func<float> 欄位，一定要加括號呼叫（少了括號，字串插值印出的是型別名）。
        // vnavmesh 的約定是「載入任務沒在跑時回負數」，也就是根本沒在建置。
        var progress = P.Navmesh.BuildProgress();

        if (progress < 0)
        {
            // 沒有任何載入任務在跑 → 這張圖的網格既沒載入也沒在建，光等不會有結果。
            // 觸發一次 Reload（允許讀快取，便宜）。不自動 Rebuild —— 那是強制全重建、很貴。
            if (EzThrottler.Throttle("Vnavmesh auto reload", 10000))
            {
                IceLogging.Info("導航網格未載入、且沒有任何建置任務在跑，觸發一次 Reload", "[Vnavmesh]");
                P.Navmesh.Reload();
            }
            return;
        }

        if (EzThrottler.Throttle("Vnavmesh throttle message", 1000))
            IceLogging.Info($"導航網格建置中：{progress:P0}", "[Vnavmesh]");
    }
}
