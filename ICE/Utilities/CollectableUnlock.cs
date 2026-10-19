using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;

namespace ICE.Utilities;

/// <summary>
/// 這個角色解鎖收藏品／精選了沒，以及哪些任務需要它們。只用來提醒，不影響任何挑選或執行判斷。
/// </summary>
/// <remarks>
/// 解鎖狀態回 <c>null</c> ＝不知道（沒登入、遊戲函式沒解析到），呼叫端一律當成「不提醒」。<br/>
/// 只能在框架執行緒呼叫：會呼叫遊戲函式，快取也沒有鎖。
/// </remarks>
internal static class CollectableUnlock
{
    [Flags]
    internal enum Unlock
    {
        None = 0,
        Collectables = 1,
        Reduction = 2,
    }

    // 「一流工匠的新工作」：獎勵「收藏品技能」，也是收藏品採集各技能（提煉、集中檢查、收藏品採集…）的解鎖連結。
    internal const ushort CollectablesQuestId = 2095;

    // 「生命、精選，另一個答案」：獎勵通用技能「精選」。上游有一版把這兩個 ID 對調，不要照抄。
    internal const ushort ReductionQuestId = 2097;

    private const long StateTtlMs = 1000;
    private static bool stateRead;
    private static long stateReadAt;
    private static bool? collectablesState;
    private static bool? reductionState;
    private static bool loggedUnresolved;

    private static readonly Dictionary<uint, Unlock> requirementCache = new();

    private static ulong warnedCid;
    private static readonly HashSet<uint> warnedMissions = new();

    internal static bool? IsCollectablesUnlocked()
    {
        RefreshState();
        return collectablesState;
    }

    internal static bool? IsReductionUnlocked()
    {
        RefreshState();
        return reductionState;
    }

    // 表格每列每幀都會問，所以讀一次留 1 秒；換角色後最多舊 1 秒。
    private static void RefreshState()
    {
        var now = Environment.TickCount64;
        if (stateRead && now - stateReadAt < StateTtlMs)
            return;

        stateRead = true;
        stateReadAt = now;
        collectablesState = IsQuestComplete(CollectablesQuestId);
        reductionState = IsQuestComplete(ReductionQuestId);
    }

    private static unsafe bool? IsQuestComplete(ushort questId)
    {
        if (!Player.Available)
            return null;

        if (QuestManager.MemberFunctionPointers.IsQuestComplete is null)
        {
            if (!loggedUnresolved)
            {
                loggedUnresolved = true;
                IceLogging.Info("找不到遊戲的「任務是否已完成」函式，無法判斷收藏品／精選解鎖了沒，相關提醒不會出現。", "[收藏品解鎖]");
            }
            return null;
        }

        return QuestManager.IsQuestComplete(questId);
    }

    /// <summary>這個任務需要哪些解鎖（只看任務表，不看角色）。</summary>
    internal static Unlock Requirements(uint missionId)
    {
        if (requirementCache.TryGetValue(missionId, out var cached))
            return cached;

        // 任務表還沒建好時不快取，免得把「查不到」永久記成「不需要」。
        if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var info))
            return Unlock.None;

        var needs = Unlock.None;
        if (info.Attributes.HasFlag(MissionAttributes.ReducedItems))
            needs |= Unlock.Reduction | Unlock.Collectables;
        if (info.Attributes.HasFlag(MissionAttributes.Collectables) || CraftsCollectable(info))
            needs |= Unlock.Collectables;

        requirementCache[missionId] = needs;
        return needs;
    }

    // 製作任務的屬性沒有標收藏品，改看交貨品本身是不是收藏品。
    private static bool CraftsCollectable(CosmicHelper.CosmicInfo info)
    {
        if (info.Crafts_Main.Count == 0 && info.Crafts_Pre.Count == 0)
            return false;

        var items = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        foreach (var craft in info.Crafts_Main.Values.Concat(info.Crafts_Pre.Values))
        {
            if (items.TryGetRow(craft.ItemId, out var item) && item.IsCollectable)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 這個任務需要、而這個角色<b>確定</b>還沒解鎖的項目。不知道解鎖狀態的項目不算缺。
    /// </summary>
    internal static Unlock Missing(uint missionId)
    {
        var needs = Requirements(missionId);
        if (needs == Unlock.None)
            return Unlock.None;

        var missing = Unlock.None;
        if (needs.HasFlag(Unlock.Collectables) && IsCollectablesUnlocked() == false)
            missing |= Unlock.Collectables;
        if (needs.HasFlag(Unlock.Reduction) && IsReductionUnlocked() == false)
            missing |= Unlock.Reduction;
        return missing;
    }

    /// <summary>任務的名稱與所在地（台服遊戲資料）。查不到就回 "?"。</summary>
    internal static (string Name, string Place) QuestInfo(ushort questId)
    {
        if (Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Quest>().TryGetRow(0x10000u + questId, out var quest))
        {
            var name = quest.Name.ExtractText();
            var place = quest.PlaceName.ValueNullable?.Name.ExtractText();
            return (string.IsNullOrEmpty(name) ? "?" : name, string.IsNullOrEmpty(place) ? "?" : place);
        }
        return ("?", "?");
    }

    // 表格每列每幀都要拿說明文字；組合只有三種，組一次就留著。
    private static readonly string?[] tooltipCache = new string?[4];

    /// <summary>表格滑鼠移過去時的說明文字。</summary>
    internal static string TooltipText(Unlock missing)
    {
        var slot = (int)missing & 3;
        return tooltipCache[slot] ??= BuildTooltip(missing);
    }

    private static string BuildTooltip(Unlock missing)
    {
        var lines = new List<string>();
        if (missing.HasFlag(Unlock.Collectables))
        {
            var (name, place) = QuestInfo(CollectablesQuestId);
            lines.Add("This mission needs collectables, but this character has not completed the side quest \"??\" (??) that unlocks them.\nWithout it the collectable gathering skills cannot be used, and crafting may not produce collectables either."
                .Loc(name, place));
        }
        if (missing.HasFlag(Unlock.Reduction))
        {
            var (name, place) = QuestInfo(ReductionQuestId);
            lines.Add("This mission needs aetherial reduction, but this character has not completed the side quest \"??\" (??) that unlocks it. Reduction may not be possible."
                .Loc(name, place));
        }
        lines.Add("ICE still picks and runs this mission as before. This is only a reminder.".Loc());
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 排程挑定任務時呼叫：缺解鎖就在聊天視窗提醒一次。
    /// </summary>
    /// <remarks>
    /// 同一個角色的同一個任務只提醒一次（換角色重新算），不論重挑幾輪；不改變任何排程行為。
    /// </remarks>
    internal static void WarnIfMissing(uint missionId)
    {
        // 提醒出錯不能連帶中斷挑任務：吞掉、記一行。
        try
        {
            WarnIfMissingCore(missionId);
        }
        catch (Exception ex)
        {
            IceLogging.Info($"收藏品解鎖提醒失敗（任務 {missionId}）：{ex.GetType().Name}: {ex.Message}", "[收藏品解鎖]");
        }
    }

    private static void WarnIfMissingCore(uint missionId)
    {
        var missing = Missing(missionId);
        if (missing == Unlock.None)
            return;

        var cid = Player.CID;
        if (cid != warnedCid)
        {
            warnedCid = cid;
            warnedMissions.Clear();
        }
        if (!warnedMissions.Add(missionId))
            return;

        var missionName = CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var info) ? info.Name : "?";
        if (missing.HasFlag(Unlock.Collectables))
        {
            var (name, place) = QuestInfo(CollectablesQuestId);
            IceLogging.ChatInfo(
                "Mission [??] ?? needs collectables, but this character has not completed the side quest \"??\" (??) that unlocks them. ICE will carry on as usual, but this mission will probably fail."
                    .Loc(missionId, missionName, name, place),
                "[ICE]");
        }
        if (missing.HasFlag(Unlock.Reduction))
        {
            var (name, place) = QuestInfo(ReductionQuestId);
            IceLogging.ChatInfo(
                "Mission [??] ?? needs aetherial reduction, but this character has not completed the side quest \"??\" (??) that unlocks it. ICE will carry on as usual, but reduction may not work."
                    .Loc(missionId, missionName, name, place),
                "[ICE]");
        }
    }
}
