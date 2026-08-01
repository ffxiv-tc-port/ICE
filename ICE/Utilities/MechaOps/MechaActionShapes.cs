using System.Collections.Generic;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace ICE.Utilities.MechaOps;

/// <summary>形狀種類。座標語意見 <see cref="MechaAoeOverlay"/>。</summary>
internal enum MechaAoeKind
{
    /// <summary>從施放者位置沿面向延伸的矩形。</summary>
    Rect,
    /// <summary>以施放者為頂點、沿面向展開的扇形。</summary>
    Cone,
    /// <summary>以施放者為圓心的圓形。</summary>
    SelfCircle,
    /// <summary>單體技能：只畫一圈細的射程圈。</summary>
    RangeRing,
}

/// <summary>
/// 一個技能的範圍形狀。
/// Rect：Primary=長度、HalfWidth=半寬；Cone：Primary=半徑（角度執行期讀設定值）；
/// SelfCircle / RangeRing：Primary=半徑。
/// </summary>
internal readonly record struct MechaAoeShape(MechaAoeKind Kind, float Primary, float HalfWidth);

/// <summary>
/// 機甲行動技能的範圍形狀資料來源。
/// 形狀一律從 Lumina <c>Action</c> 表解讀（CastType / EffectRange / XAxisModifier / Range），
/// CastType 語意比照 BossModReborn <c>AIHintsBuilder.GuessShape</c>——
/// 這樣未來新增的機甲技能不用改碼就能自動適用。
///
/// 驗證基準（台服 7.20 EXD 實測，來源 exd-tc/7.20/Action.csv，2026-08-02）：
/// <code>
///   42150 宇宙鑽頭（協助員）       CastType 12  EffectRange 7   XAxisModifier 5   → 矩形 長 7、全寬 5
///   42258 宇宙火焰噴射器（協助員）  CastType 13  EffectRange 7   Omen 0            → 扇形 半徑 7；角度表裡沒有（Omen=0），走設定值，預設 90°
///   42037 強力胡蘿蔔加農砲（駕駛）  CastType 12  EffectRange 60  XAxisModifier 10  → 矩形 長 60、全寬 10
///   42071 鏟刃衝鋒（駕駛）         CastType 12  EffectRange 30  XAxisModifier 5   → 矩形 長 30、全寬 5
///   42261 氣動粉碎機（駕駛）       CastType 2   EffectRange 15                    → 自身圓 半徑 15
///   42036 胡蘿蔔槍（駕駛）         CastType 1   Range 30                          → 單體，畫半徑 30 細射程圈
/// </code>
/// 六筆的 ClassJobCategory 都是 35。
/// ⚠️ XAxisModifier 是「全寬」，半寬要 ÷2。
/// </summary>
internal static class MechaActionShapes
{
    /// <summary>
    /// 已離線驗證過的六個機甲技能（見類別註解的表）。
    /// 只作為設定 UI 的保底清單與候選濾網的白名單；觸發與形狀本身都走 Lumina 表。
    /// </summary>
    public static readonly uint[] BaselineActionIds = [42150, 42258, 42037, 42071, 42261, 42036];

    /// <summary>
    /// 機甲技能的 ClassJobCategory（=35，能工巧匠／大地使者）。
    /// ⚠️ 35 不是宇宙專屬——一般 DoH/DoL 動作也可能是 35，所以這只是候選濾網；
    /// 真正的觸發條件是「這個 ActionId 出現在 PetHotbar 裡」（機甲模式才會發生）。
    /// </summary>
    private const uint MechaClassJobCategory = 35;

    private static readonly Dictionary<uint, (MechaAoeShape? Shape, string Name, bool MechaCategory)> Cache = new();

    /// <summary>解析一個 ActionId 的範圍形狀。查不到表或形狀不支援時回傳 false（name 仍會給）。</summary>
    public static bool TryResolve(uint actionId, out MechaAoeShape shape, out string name)
    {
        var entry = GetEntry(actionId);
        name = entry.Name;
        if (entry.Shape is { } s)
        {
            shape = s;
            return true;
        }
        shape = default;
        return false;
    }

    /// <summary>這個 ActionId 是否可能是機甲技能（ClassJobCategory==35 或在白名單裡）。</summary>
    public static bool IsMechaCandidate(uint actionId)
        => GetEntry(actionId).MechaCategory || Array.IndexOf(BaselineActionIds, actionId) >= 0;

    private static (MechaAoeShape? Shape, string Name, bool MechaCategory) GetEntry(uint actionId)
    {
        if (!Cache.TryGetValue(actionId, out var entry))
        {
            entry = ResolveFromSheet(actionId);
            Cache[actionId] = entry;
        }
        return entry;
    }

    private static (MechaAoeShape?, string, bool) ResolveFromSheet(uint actionId)
    {
        var sheet = Svc.Data.GetExcelSheet<LuminaAction>();
        if (sheet == null || !sheet.TryGetRow(actionId, out var row))
            return (null, $"#{actionId}", false);

        var name = row.Name.ToString();
        if (string.IsNullOrWhiteSpace(name))
            name = $"#{actionId}";

        MechaAoeShape? shape = row.CastType switch
        {
            // 12/4：矩形，長=EffectRange、全寬=XAxisModifier。
            // （4 理論上要再加施放者 hitbox 半徑；實測六技全是 12，MVP 一律不加。）
            12 or 4 => new MechaAoeShape(MechaAoeKind.Rect, row.EffectRange, row.XAxisModifier * 0.5f),

            // 13/3：扇形，半徑=EffectRange。角度通常得從 Omen 路徑解析（fanXXX），
            // 但 42258 的 Omen=0 → 無從得知，角度由設定值提供（預設 90°，待實機校準）。
            13 or 3 => new MechaAoeShape(MechaAoeKind.Cone, row.EffectRange, 0f),

            // 2/5：以施放者為圓心的圓（5 理論上加 hitbox，同上不加）。
            2 or 5 => new MechaAoeShape(MechaAoeKind.SelfCircle, row.EffectRange, 0f),

            // 1：單體。射程 > 0 才有東西可畫（細射程圈）。
            1 when row.Range > 0 => new MechaAoeShape(MechaAoeKind.RangeRing, row.Range, 0f),

            _ => null,
        };

        return (shape, name, row.ClassJobCategory.RowId == MechaClassJobCategory);
    }
}
