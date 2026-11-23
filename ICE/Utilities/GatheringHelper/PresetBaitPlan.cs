using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.Utilities.GatheringHelper;

/// <summary>內建 AutoHook preset 自己決定餌的設定：起始餌，以及釣魚途中會被換上的每一種餌。</summary>
/// <remarks>
/// 契約：只描述 AutoHook 會照 preset 主動換上的餌。<c>ListOfBaits</c> 是各種餌的提鉤設定，不換餌，刻意不算。
/// <c>ExtraCfg.Enabled</c> 為 false 時 AutoHook 改用使用者預設 preset 的 ExtraCfg，所以那一段整個不算。
/// </remarks>
internal sealed class PresetBaitPlan
{
    /// <summary>被選取的那一筆 preset 的 <c>ExtraCfg.ForcedBaitId</c>（<c>/ahstart</c> 時 AutoHook 先換上）；0＝沒指定。</summary>
    public uint StartBait;

    /// <summary>餌 ID → 第一個指定它的欄位（給 log 用）。</summary>
    public readonly Dictionary<uint, string> Named = new();

    internal void Add(long id, string field)
    {
        if (id > 0 && id <= uint.MaxValue)
            Named.TryAdd((uint)id, field);
    }
}

/// <summary>
/// ICE 匯入內建 preset 之後，判斷要不要把換餌交給 AutoHook。
/// 🔴 fail-safe：沒匯入、AutoHook 沒裝、自訂 preset、字串讀不懂 ⇒ 一律回「不交給」，維持 ICE 原本的換餌行為。
/// 只在框架執行緒上用（狀態沒有同步）。
/// </summary>
internal static class PresetBaitPlans
{
    private static readonly Dictionary<uint, PresetBaitPlan?> cache = new();

    /// <summary>ICE 這一次實際把內建 preset 交給 AutoHook 的任務；0＝沒有（自訂 preset、匯入失敗、還沒匯入）。</summary>
    private static uint importedMission;

    private static uint loggedMission;

    internal static void NoteImported(uint missionId)
    {
        importedMission = missionId;
        loggedMission = 0;
    }

    internal static void ClearImported() => importedMission = 0;

    /// <summary>目前任務可採信的 preset 換餌設定；null＝照 ICE 原本的行為。</summary>
    internal static PresetBaitPlan? ForMission(uint missionId)
    {
        if (missionId == 0 || importedMission != missionId || !P.AutoHook.Installed)
            return null;

        if (!cache.TryGetValue(missionId, out var plan))
        {
            plan = Build(missionId);
            cache[missionId] = plan;
        }
        return plan is { Named.Count: > 0 } ? plan : null;
    }

    /// <summary>沒掛餌時要先掛哪一個：preset 指定的起始餌且身上有；0＝照原本的挑法。</summary>
    internal static uint PreferredInitialBait(uint missionId, string handle)
    {
        var plan = ForMission(missionId);
        if (plan == null || plan.StartBait == 0
            || !PlayerHelper.GetItemCount(plan.StartBait, out var count) || count <= 0)
            return 0;

        LogOnce(missionId, $"任務 {missionId}：preset 的 ExtraCfg.ForcedBaitId 指定起始餌 {plan.StartBait}，沒掛餌時改掛它（不掛身上第一個月面餌）。", handle);
        return plan.StartBait;
    }

    /// <summary>還沒甩竿時 ICE 要不要讓掛著的餌維持原樣（交給 AutoHook）。</summary>
    internal static bool ShouldLeaveBaitToAutoHook(uint missionId, uint currentBait, string handle)
    {
        var plan = ForMission(missionId);
        if (plan == null || currentBait == 0)
            return false;

        // 掛著的餌已經用完時照舊由 ICE 換：AutoHook 會不會換掉一個 0 個的餌不可知，交給它可能一路甩不了竿。
        if (!PlayerHelper.GetItemCount(currentBait, out var held) || held <= 0)
            return false;

        if (plan.Named.TryGetValue(currentBait, out var field))
        {
            LogOnce(missionId, $"任務 {missionId}：掛著的餌 {currentBait} 是 preset 的 {field} 指定的，ICE 不改掛，交給 AutoHook。", handle);
            return true;
        }

        // 掛著的不是月面餌（甩不了竿）時照舊由 ICE 換，避免 AutoHook 沒換成就卡住。
        var currentIsMoonBait = GatheringUtil.MoonBaits.Values.Any(ids => ids.Contains(currentBait));
        if (currentIsMoonBait && plan.StartBait != 0
            && PlayerHelper.GetItemCount(plan.StartBait, out var count) && count > 0)
        {
            LogOnce(missionId, $"任務 {missionId}：preset 的 ExtraCfg.ForcedBaitId 指定起始餌 {plan.StartBait}，" +
                               "送 /ahstart 時由 AutoHook 換上，ICE 不改掛。", handle);
            return true;
        }

        return false;
    }

    private static void LogOnce(uint missionId, string message, string handle)
    {
        if (loggedMission == missionId)
            return;
        loggedMission = missionId;
        IceLogging.Info(message, handle);
    }

    private static PresetBaitPlan? Build(uint missionId)
    {
        const string handle = "[Preset 換餌設定]";
        if (!GatheringUtil.FishingPreset.TryGetValue(missionId, out var tools) || tools.FishingPreset.Count == 0)
            return null;

        try
        {
            var plan = new PresetBaitPlan();
            var strings = tools.FishingPreset;
            var folder = strings[0].TrimStart().StartsWith("AHFOLDER", StringComparison.Ordinal);
            // 與匯入路徑一致：資料夾只匯第一筆、選進入點；單筆逐筆匯入，最後一筆是被選取的。
            var sources = folder ? strings.Take(1).ToList() : strings;

            var presets = new List<JsonElement>();
            var docs = new List<JsonDocument>();
            try
            {
                foreach (var s in sources)
                {
                    var json = Decode(s);
                    if (json == null)
                    {
                        IceLogging.Info($"任務 {missionId}：內建 preset 的格式讀不懂（前綴 {Prefix(s)}），換餌照 ICE 原本的做法。", handle);
                        return null;
                    }
                    var doc = JsonDocument.Parse(json);
                    docs.Add(doc);
                    if (folder)
                        CollectFolder(doc.RootElement, presets, 0);
                    else
                        presets.Add(doc.RootElement);
                }

                if (presets.Count == 0)
                    return null;

                for (var i = 0; i < presets.Count; i++)
                    Collect(presets[i], $"第 {i + 1} 筆", plan);

                var selected = folder ? presets[0] : presets[^1];
                plan.StartBait = ForcedBait(selected);
            }
            finally
            {
                foreach (var d in docs)
                    d.Dispose();
            }
            return plan;
        }
        catch (Exception e)
        {
            IceLogging.Info($"任務 {missionId}：解析內建 preset 的換餌設定失敗（{e.GetType().Name}: {e.Message}），換餌照 ICE 原本的做法。", handle);
            return null;
        }
    }

    private static string Prefix(string s)
    {
        var t = s.TrimStart();
        var i = t.IndexOf('_');
        return i is > 0 and < 12 ? t[..(i + 1)] : "?";
    }

    /// <summary>只認得 AutoHook 現行 schema 的前綴；舊版 AH_／AH3_ 結構不同，回 null。</summary>
    private static string? Decode(string s)
    {
        s = s.Trim();
        (string Prefix, bool Brotli)[] known = [("AHFOLDER2_", true), ("AHFOLDER_", false), ("AH7_", true), ("AH6_", false), ("AH4_", false)];
        foreach (var (prefix, brotli) in known)
        {
            if (!s.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var data = Convert.FromBase64String(s[prefix.Length..]);
            using var input = new MemoryStream(data);
            using Stream inflate = brotli
                ? new BrotliStream(input, CompressionMode.Decompress)
                : new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflate.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }
        return null;
    }

    private static void CollectFolder(JsonElement folder, List<JsonElement> presets, int depth)
    {
        if (depth > 8 || folder.ValueKind != JsonValueKind.Object)
            return;
        if (folder.TryGetProperty("Presets", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var p in list.EnumerateArray())
                if (p.ValueKind == JsonValueKind.Object)
                    presets.Add(p);
        if (folder.TryGetProperty("ChildFolders", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var c in children.EnumerateArray())
                CollectFolder(c, presets, depth + 1);
    }

    private static uint ForcedBait(JsonElement preset)
    {
        if (!TryObject(preset, "ExtraCfg", out var extra) || !Bool(extra, "Enabled") || !Bool(extra, "ForceBaitSwap"))
            return 0;
        var id = Long(extra, "ForcedBaitId");
        return id is > 0 and <= uint.MaxValue ? (uint)id : 0;
    }

    private static void Collect(JsonElement preset, string where, PresetBaitPlan plan)
    {
        var forced = ForcedBait(preset);
        if (forced != 0)
            plan.Add(forced, $"{where} ExtraCfg.ForcedBaitId（起始餌）");

        if (TryObject(preset, "ExtraCfg", out var extra) && Bool(extra, "Enabled"))
        {
            (string Flag, string Bait, string Label)[] extras =
            [
                ("SwapBaitIntuitionGain", "BaitToSwapIntuitionGain", "獲得漁師之識時換餌"),
                ("SwapBaitIntuitionLost", "BaitToSwapIntuitionLost", "失去漁師之識時換餌"),
                ("SwapBaitSpectralCurrentGain", "BaitToSwapSpectralCurrentGain", "幻海流出現時換餌"),
                ("SwapBaitSpectralCurrentLost", "BaitToSwapSpectralCurrentLost", "幻海流結束時換餌"),
                ("SwapBaitAnglersArt", "BaitToSwapAnglersArt", "漁師之計層數換餌"),
            ];
            foreach (var (flag, bait, label) in extras)
                if (Bool(extra, flag))
                    plan.Add(BaitId(extra, bait), $"{where} ExtraCfg.{flag}（{label}）");

            if (extra.TryGetProperty("Triggers", out var triggers) && triggers.ValueKind == JsonValueKind.Array)
            {
                var n = 0;
                foreach (var t in triggers.EnumerateArray())
                {
                    n++;
                    if (t.ValueKind != JsonValueKind.Object)
                        continue;
                    var enabled = !t.TryGetProperty("Enabled", out var en) || en.ValueKind != JsonValueKind.False;
                    if (enabled && Bool(t, "SwapBait"))
                        plan.Add(BaitId(t, "BaitToSwap"), $"{where} ExtraCfg.Triggers 第 {n} 條規則（換餌）");
                }
            }
        }

        if (preset.TryGetProperty("ListOfFish", out var fish) && fish.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fish.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object)
                    continue;
                // AutoHook 端：有條件組就以條件為準，沒有就看 SwapBait（見其 FishCaught 換餌段）。
                var hasConditions = f.TryGetProperty("SwapBaitConditionSet", out var cs)
                                    && cs.ValueKind == JsonValueKind.Object && cs.EnumerateObject().Any();
                if (hasConditions || Bool(f, "SwapBait"))
                    plan.Add(BaitId(f, "BaitToSwap"), $"{where} ListOfFish（釣到魚 {BaitId(f, "Fish")} 後換餌）");
            }
        }
    }

    private static bool TryObject(JsonElement e, string name, out JsonElement value)
        => e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static long Long(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    private static long BaitId(JsonElement e, string name)
        => TryObject(e, name, out var bait) ? Long(bait, "Id") : 0;
}
