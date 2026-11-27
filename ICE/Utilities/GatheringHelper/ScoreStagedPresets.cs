using System;
using System.Collections.Generic;
using System.Text.Json;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.Utilities.GatheringHelper;

/// <summary>
/// 評價型釣魚任務依目前分數切換內建 AutoHook preset：分數到銀星門檻就改選「銀星以上」那一筆，
/// 未達門檻用清單最後一筆（逐筆匯入後本來就被選取的那筆）。
/// </summary>
/// <remarks>
/// 契約：只在框架執行緒上用；只管 ICE 自己匯入、而且全部匯入完成的內建 preset（自訂 preset、資料夾型都不動）。
/// 🔴 名稱從 preset 字串自己解出來再加 AutoHook 匿名前綴；解不出來就整個任務不切換 ——
///    AutoHook 的 SetPreset 找不到名稱會把選取清空、退回預設 preset，比不切換糟。
/// </remarks>
internal static class ScoreStagedPresets
{
    /// <summary>任務 → 分數到銀星後要選的那筆在 FishingPreset 清單裡的索引（不能是最後一筆）。</summary>
    internal static readonly Dictionary<uint, int> AtSilverIndex = new()
    {
        [492] = 0,
    };

    private const string AnonPrefix = "anon_";

    private static uint importedMission;
    private static string? belowName;
    private static string? atSilverName;
    private static string? applied;

    internal static bool Has(uint missionId) => AtSilverIndex.ContainsKey(missionId);

    internal static void ClearImported()
    {
        importedMission = 0;
        belowName = atSilverName = applied = null;
    }

    /// <summary>該任務的內建 preset 全部交給 AutoHook 之後呼叫；此時被選取的是清單最後一筆。</summary>
    internal static void NoteImported(uint missionId, string handle)
    {
        ClearImported();
        if (!AtSilverIndex.TryGetValue(missionId, out var index)
            || !GatheringUtil.FishingPreset.TryGetValue(missionId, out var tools))
            return;

        var list = tools.FishingPreset;
        var below = list.Count >= 2 ? AnonName(list[^1]) : null;
        var atSilver = index >= 0 && index < list.Count - 1 ? AnonName(list[index]) : null;
        if (below == null || atSilver == null || below == atSilver)
        {
            IceLogging.Info($"任務 {missionId}：依分數切換 preset 的設定對不上（共 {list.Count} 筆、銀星那筆索引 {index}），" +
                            "這一場不切換，全程用匯入後選取的那筆。", handle);
            return;
        }

        importedMission = missionId;
        belowName = below;
        atSilverName = atSilver;
        applied = below;
    }

    internal static bool AtOrAboveSilver(uint score, uint silverScore) => silverScore > 0 && score >= silverScore;

    /// <summary>每次查分時呼叫；要換才呼叫 <paramref name="setPreset"/>。回傳換成的名稱，沒換回 null。</summary>
    internal static string? Apply(uint missionId, uint? currentScore, uint silverScore, Action<string> setPreset, string handle)
    {
        if (missionId == 0 || missionId != importedMission || currentScore is not { } score)
            return null;

        var atSilver = AtOrAboveSilver(score, silverScore);
        var want = atSilver ? atSilverName! : belowName!;
        if (want == applied)
            return null;

        applied = want;
        setPreset(want);
        IceLogging.Info($"任務 {missionId}：目前分數 {score}{(atSilver ? "已達" : "未達")}銀星 {silverScore}，" +
                        $"AutoHook 改用 preset「{want}」。", handle);
        return want;
    }

    private static string? AnonName(string preset)
    {
        try
        {
            var json = PresetBaitPlans.Decode(preset);
            if (json == null)
                return null;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("PresetName", out var name)
                   && name.ValueKind == JsonValueKind.String
                   && name.GetString() is { Length: > 0 } s
                ? AnonPrefix + s
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
