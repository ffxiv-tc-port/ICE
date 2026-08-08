using ICE.Config;
using System.Collections.Generic;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 機甲行動的參與身份。
///
/// 🔑 <b>為什麼要分</b>（2026-08-06 使用者實機回報「協助員身份參加有害菌床驅除指令，目標不一樣」）：
/// 兩種身份的任務目標<b>本來就不同</b>，這一點在遊戲資料裡是白紙黑字的。
/// <c>WKSMechaEventData</c> 第 5 列（有害菌床驅除指令）：
/// <code>
///   欄 Unknown2（駕駛員）：「駕駛輪式鏟裝車，剷除巨型變異菌床。」
///   欄 Unknown3（協助員）：「使用宇宙火焰噴射器焚燒小型變異菌床。
///                            將燃燒後留下的灰燼投入野外探測器分析。」
/// </code>
/// 也就是說駕駛員打的是<b>巨型</b>、協助員打的是<b>小型</b>，另外還要把灰燼投進野外探測器。
/// 資料表白名單（<see cref="MechaObjectNames"/>）收到的
/// 2014720／2014722 是<b>駕駛員</b>的巨型目標，直接拿去給協助員用就是「目標不一樣」。
/// </summary>
internal enum MechaRole
{
    /// <summary>
    /// 判不出來。⚠️ 這不是「兩種都算」——它是<b>保守退化</b>的訊號：
    /// 這種狀態下一律只信遊戲自己給的標記，不套用任何資料表推論出來的白名單。
    /// </summary>
    Unknown = 0,

    /// <summary>駕駛員（開機甲）。</summary>
    Pilot = 1,

    /// <summary>協助員（步行支援，用宇宙工具）。</summary>
    GroundSupport = 2,
}

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
///   42258 宇宙火焰噴射器（協助員）  CastType 13  EffectRange 7   Omen 0            → 扇形 半徑 7；角度表裡沒有（Omen=0），走設定值
///   42037 強力胡蘿蔔加農砲（駕駛）  CastType 12  EffectRange 60  XAxisModifier 10  → 矩形 長 60、全寬 10
///   42071 鏟刃衝鋒（駕駛）         CastType 12  EffectRange 30  XAxisModifier 5   → 矩形 長 30、全寬 5
///   42261 氣動粉碎機（駕駛）       CastType 2   EffectRange 15                    → 自身圓 半徑 15
///   42036 胡蘿蔔槍（駕駛）         CastType 1   Range 30                          → 單體，畫半徑 30 細射程圈
/// </code>
/// 六筆的 ClassJobCategory 都是 35。
/// ⚠️ XAxisModifier 是「全寬」，半寬要 ÷2。
///
/// 🔴 <b>表上的值不等於實戰有效值。</b>2026-08-08 的 [MechaRec] log 量測顯示
/// 42150 的實際觸發距離約 3.5~4m（表上 7），所以解析完之後還會過一層
/// <see cref="ApplyCalibration"/> 把使用者校準過的參數套上去。
/// 需要「Lumina 原值」時要看的是 <see cref="ResolveFromSheet"/>，不是 <see cref="TryResolve"/>。
/// </summary>
internal static class MechaActionShapes
{
    /// <summary>
    /// 已離線驗證過的六個機甲技能（見類別註解的表）。
    /// 只作為設定 UI 的保底清單與候選濾網的白名單；觸發與形狀本身都走 Lumina 表。
    /// </summary>
    public static readonly uint[] BaselineActionIds = [42150, 42258, 42037, 42071, 42261, 42036];

    /// <summary>
    /// 只有<b>駕駛員</b>（開機甲的人）拿得到的技能。
    /// 角色註記本來就寫在類別註解的表裡（2026-08-02 離線驗證），這裡只是把它變成可查詢的資料。
    /// </summary>
    private static readonly uint[] PilotActionIds = [42037, 42071, 42261, 42036];

    /// <summary>
    /// 只有<b>協助員</b>（步行支援）用得到的技能，也就是宇宙工具。
    ///
    /// 🔑 <b>交叉驗證</b>：<c>WKSMechaEventData</c> 的協助員指示文字直接點名這兩個工具——
    /// 第 1 列（巨型偏屬性水晶破壞指令）寫「使用<b>宇宙鑽頭</b>粉碎小型偏屬性水晶」，
    /// 第 5 列（有害菌床驅除指令）寫「使用<b>宇宙火焰噴射器</b>焚燒小型變異菌床」，
    /// 與 42150／42258 的技能名逐字相符。兩條完全獨立的資料（Action 表 vs 事件文字）
    /// 指向同一個結論，所以這個角色歸屬不是猜的。
    /// </summary>
    private static readonly uint[] GroundSupportActionIds = [42150, 42258];

    /// <summary>
    /// 這個 ActionId 屬於哪一種身份。**不在上面兩份清單裡的一律回
    /// <see cref="MechaRole.Unknown"/>**——日後新增的機甲技能在被離線驗證之前
    /// 不該去左右身份判定（寧可判不出來，也不要判錯）。
    /// </summary>
    public static MechaRole RoleOf(uint actionId)
    {
        if (Array.IndexOf(PilotActionIds, actionId) >= 0)
            return MechaRole.Pilot;
        if (Array.IndexOf(GroundSupportActionIds, actionId) >= 0)
            return MechaRole.GroundSupport;
        return MechaRole.Unknown;
    }

    /// <summary>
    /// 機甲技能的 ClassJobCategory（=35，能工巧匠／大地使者）。
    /// ⚠️ 35 不是宇宙專屬——一般 DoH/DoL 動作也可能是 35，所以這只是候選濾網；
    /// 真正的觸發條件是「這個 ActionId 出現在 PetHotbar 裡」（機甲模式才會發生）。
    /// </summary>
    private const uint MechaClassJobCategory = 35;

    private static readonly Dictionary<uint, ActionEntry> Cache = new();

    private readonly record struct ActionEntry(
        MechaAoeShape? Shape,
        string Name,
        bool MechaCategory,
        uint ProcStatusId,
        string ProcStatusName);

    /// <summary>宇宙鑽頭（協助員）。矩形長度走設定值校準，見 <see cref="ApplyCalibration"/>。</summary>
    public const uint CosmicDrillActionId = 42150;

    /// <summary>宇宙火焰噴射器（協助員）。扇形全角走 <see cref="ConeAngleFor"/>（不存在形狀裡）。</summary>
    public const uint CosmicFlamethrowerActionId = 42258;

    /// <summary>解析一個 ActionId 的範圍形狀。查不到表或形狀不支援時回傳 false（name 仍會給）。</summary>
    public static bool TryResolve(uint actionId, out MechaAoeShape shape, out string name)
    {
        var entry = GetEntry(actionId);
        name = entry.Name;
        if (entry.Shape is { } s)
        {
            // 🔴 校準必須在**快取之外**套用：<see cref="Cache"/> 存的是 Lumina 原值，
            //    設定值每次都重新讀 —— 這樣使用者拉滑桿才會當場生效。
            //    若把校準寫進 ResolveFromSheet，第一次解析就會把值凍進快取，
            //    滑桿要重載外掛才有反應（失敗形式是靜默的「滑桿沒作用」）。
            shape = ApplyCalibration(actionId, s);
            return true;
        }
        shape = default;
        return false;
    }

    /// <summary>
    /// 把使用者校準過的參數套到 Lumina 解出來的形狀上。
    ///
    /// 🔑 <b>為什麼需要這一層</b>：Lumina <c>Action</c> 表的 EffectRange 是技能資料上的射程，
    /// 實戰上真正打得到的距離可能更短（42150 宇宙鑽頭實測約 3.5~4m vs 表上 7）。
    /// 表不會錯，但拿它直接畫範圍會誤導使用者，所以留一個可調的校準層。
    ///
    /// ⚠️ 沒有覆蓋、也不是舊鍵管的那一技時，連 Clamp 都不做，原封不動走 Lumina ——
    /// 這樣未來新增的機甲技能仍然不用改碼就能自動適用（維持原本的設計）。
    /// 📌 扇形角度不在這裡：它根本不存在 <see cref="MechaAoeShape"/> 裡（Cone 的 HalfWidth 是 0），
    /// 走 <see cref="ConeAngleFor"/>。
    /// </summary>
    private static MechaAoeShape ApplyCalibration(uint actionId, MechaAoeShape shape)
    {
        var ov = GetOverride(actionId);

        // ---- Primary（Rect 最遠距離／Cone 距離／圓形半徑／射程圈半徑）----
        // 🔑 讀取優先序：per-skill 覆蓋 > 舊鍵 > Lumina 原值。
        // ⚠️ 三條分支刻意寫成「有值才動」：沒有覆蓋、也不是舊鍵管的那一技時，
        //    連 Clamp 都不做，原封不動把 Lumina 的值傳出去
        //    ——這樣升級到本版的人行為是**逐位元相同**的。
        var primary = shape.Primary;
        if (ov?.Primary is { } p)
            primary = Math.Clamp(p, PrimaryMin, PrimaryMax);
        else if (actionId == CosmicDrillActionId && shape.Kind == MechaAoeKind.Rect)
            primary = Math.Clamp(DrillCalibratedLength, DrillLengthMin, DrillLengthMax);

        // ---- HalfWidth（只有矩形有意義）----
        // 🔴 非矩形的 HalfWidth 是 0，這裡**絕對不能**跟著 Clamp 到下限，
        //    否則圓形／扇形會憑空長出 0.5 的半寬。
        var halfWidth = shape.HalfWidth;
        if (shape.Kind == MechaAoeKind.Rect && ov?.HalfWidth is { } hw)
            halfWidth = Math.Clamp(hw, HalfWidthMin, HalfWidthMax);

        return shape with { Primary = primary, HalfWidth = halfWidth };
    }

    /// <summary>
    /// 這個技能的扇形<b>全角</b>（度）。讀取優先序與 <see cref="ApplyCalibration"/> 一致：
    /// per-skill 覆蓋 &gt; 內建預設 <see cref="DefaultConeAngleDeg"/>。
    ///
    /// 📌 角度為什麼不塞進 <see cref="MechaAoeShape"/>：Lumina 的 <c>Action</c> 表<b>沒有</b>這個欄位
    /// （42258 的 Omen=0），它自始至終就是一個純設定值，不是「從表解出來再校準」。
    /// 🔑 所有使用點都要走這個方法，<b>不要</b>自己去讀設定——
    /// 直接讀的話 per-skill 覆蓋會被靜默忽略（失敗形式是「滑桿沒作用」）。
    /// </summary>
    public static float ConeAngleFor(uint actionId)
    {
        var ov = GetOverride(actionId);
        return Math.Clamp(ov?.AngleDeg ?? DefaultConeAngleDeg, ConeAngleMin, ConeAngleMax);
    }

    /// <summary>取這個技能的 per-skill 覆蓋，沒有就回 <c>null</c>。</summary>
    private static MechaShapeOverride? GetOverride(uint actionId)
        => C.MechaShapeOverrides.TryGetValue(actionId, out var ov) ? ov : null;

    /// <summary>
    /// 這個技能<b>沒有任何覆蓋</b>時的形狀（Lumina 原值 ＋ 舊鍵校準）。
    /// UI 用它顯示「預設值是多少」與判斷重設鈕要不要亮。
    /// </summary>
    public static bool TryResolveDefault(uint actionId, out MechaAoeShape shape, out string name)
    {
        var entry = GetEntry(actionId);
        name = entry.Name;
        if (entry.Shape is not { } s)
        {
            shape = default;
            return false;
        }

        // 內建校準值算「預設」的一部分：42150 的預設要顯示成 4.0（實測值）而不是 Lumina 的 7.0，
        // 否則使用者一打開設定就會看到「已覆蓋」的星號，而他根本沒動過。
        var primary = s.Primary;
        if (actionId == CosmicDrillActionId && s.Kind == MechaAoeKind.Rect)
            primary = Math.Clamp(DrillCalibratedLength, DrillLengthMin, DrillLengthMax);

        shape = s with { Primary = primary };
        return true;
    }

    /// <summary>這個技能沒有覆蓋時的扇形全角。</summary>
    public static float DefaultConeAngleFor(uint actionId)
        => Math.Clamp(DefaultConeAngleDeg, ConeAngleMin, ConeAngleMax);

    /// <summary>宇宙鑽頭長度滑桿的下限／上限。UI 與執行期用同一組常數，避免兩邊漂開。</summary>
    public const float DrillLengthMin = 2f;
    public const float DrillLengthMax = 10f;

    /// <summary>
    /// 宇宙鑽頭（42150）矩形長度的<b>內建校準值</b>，取代先前的全域設定鍵 <c>C.MechaDrillLength</c>。
    ///
    /// 📌 值本身沒有變（舊鍵的預設就是 4.0，離線與 log 依據見 <c>MissionConfigs</c> 的舊註解）；
    /// 變的是它從「一個使用者可調的全域鍵」變成「per-skill 覆蓋的預設底值」。
    /// 使用者調過的舊值由 <c>ConfigMigrator</c>（設定版本 11→12）搬進
    /// <c>C.MechaShapeOverrides[42150].Primary</c>，所以效果值逐一相同。
    /// </summary>
    public const float DrillCalibratedLength = 4f;

    /// <summary>
    /// 扇形機甲技能的<b>內建預設全角</b>（度），取代先前的全域設定鍵 <c>C.MechaConeAngleDeg</c>。
    ///
    /// 📌 240 就是那個舊鍵 2026-08-08 起的預設值，所以沒調過的人效果完全一樣；
    /// 調過的人由設定版本 11→12 的遷移搬進 <c>MechaShapeOverrides[42258].AngleDeg</c>。
    /// ⚠️ 舊鍵是**所有**扇形技能共用的，這個常數也是——差別只在於使用者現在改的是
    /// per-skill 的那一格，而不是一個看不出影響範圍的全域值。
    /// </summary>
    public const float DefaultConeAngleDeg = 240f;

    /// <summary>
    /// per-skill 滑桿的範圍。刻意開得比實際值寬很多——真值未知，
    /// 夾太緊會讓使用者連想試的值都拉不到（42258 的 240° 就是這樣才放寬到 360 的）。
    /// </summary>
    /// ⚠️ 上限刻意是 60 而不是 30：<c>42037 強力胡蘿蔔加農砲</c> 的 Lumina 原值就是 60，
    ///    夾在 30 的話使用者一旦動過那一技就再也拉不回預設值（而且是靜默的）。
    ///    「滑桿上限必須 ≥ 該技能的預設值」是這一組常數的硬條件。
    public const float PrimaryMin = 1f;
    public const float PrimaryMax = 60f;
    public const float HalfWidthMin = 0.5f;
    public const float HalfWidthMax = 10f;
    public const float ConeAngleMin = 15f;
    public const float ConeAngleMax = 360f;

    /// <summary>這個 ActionId 是否可能是機甲技能（ClassJobCategory==35 或在白名單裡）。</summary>
    public static bool IsMechaCandidate(uint actionId)
        => GetEntry(actionId).MechaCategory || Array.IndexOf(BaselineActionIds, actionId) >= 0;

    /// <summary>
    /// 這個技能是否被某個 status（proc）把關，以及那個 status 的 id 與名稱。
    /// 資料一律從 <c>Action.ActionProcStatus → ActionProcStatus.Status</c> 讀，不寫死 id——
    /// 未來若有別的機甲技能加上 proc 條件，不用改碼就會自動出現。
    ///
    /// 台服 7.20 實測（exd-tc/7.20，2026-08-02）：整張 Action 表裡只有
    /// <c>42037 強力胡蘿蔔加農砲</c> 的 ActionProcStatus 是 256，而 ActionProcStatus 第 256 列
    /// 指向 <c>Status 4405 胡蘿蔔授權</c>（說明文字「可以發動強力胡蘿蔔加農砲」）。
    /// 也就是說「胡蘿蔔授權」的載體就是一個一般的玩家 status，不是什麼特殊結構。
    /// </summary>
    public static bool TryGetProcStatus(uint actionId, out uint statusId, out string statusName)
    {
        var entry = GetEntry(actionId);
        statusId = entry.ProcStatusId;
        statusName = entry.ProcStatusName;
        return statusId != 0;
    }

    private static ActionEntry GetEntry(uint actionId)
    {
        if (!Cache.TryGetValue(actionId, out var entry))
        {
            entry = ResolveFromSheet(actionId);
            Cache[actionId] = entry;
        }
        return entry;
    }

    private static ActionEntry ResolveFromSheet(uint actionId)
    {
        var sheet = Svc.Data.GetExcelSheet<LuminaAction>();
        if (sheet == null || !sheet.TryGetRow(actionId, out var row))
            return new ActionEntry(null, $"#{actionId}", false, 0, "");

        var name = row.Name.ToString();
        if (string.IsNullOrWhiteSpace(name))
            name = $"#{actionId}";

        MechaAoeShape? shape = row.CastType switch
        {
            // 12/4：矩形，長=EffectRange、全寬=XAxisModifier。
            // （4 理論上要再加施放者 hitbox 半徑；實測六技全是 12，MVP 一律不加。）
            12 or 4 => new MechaAoeShape(MechaAoeKind.Rect, row.EffectRange, row.XAxisModifier * 0.5f),

            // 13/3：扇形，半徑=EffectRange。角度通常得從 Omen 路徑解析（fanXXX），
            // 但 42258 的 Omen=0 → 無從得知，角度由 ConeAngleFor 提供（per-skill 覆蓋／內建預設，實測校準）。
            13 or 3 => new MechaAoeShape(MechaAoeKind.Cone, row.EffectRange, 0f),

            // 2/5：以施放者為圓心的圓（5 理論上加 hitbox，同上不加）。
            2 or 5 => new MechaAoeShape(MechaAoeKind.SelfCircle, row.EffectRange, 0f),

            // 1：單體。射程 > 0 才有東西可畫（細射程圈）。
            1 when row.Range > 0 => new MechaAoeShape(MechaAoeKind.RangeRing, row.Range, 0f),

            _ => null,
        };

        // proc 條件：Action.ActionProcStatus → ActionProcStatus.Status。
        // 兩層都用 ValueNullable，任何一層查不到就當作「沒有 proc」，不會丟例外。
        uint procStatusId = 0;
        var procStatusName = "";
        if (row.ActionProcStatus.ValueNullable is { } procRow)
        {
            procStatusId = procRow.Status.RowId;
            if (procStatusId != 0)
            {
                procStatusName = procRow.Status.ValueNullable?.Name.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(procStatusName))
                    procStatusName = $"#{procStatusId}";
            }
        }

        return new ActionEntry(
            shape,
            name,
            row.ClassJobCategory.RowId == MechaClassJobCategory,
            procStatusId,
            procStatusName);
    }

    /// <summary>把一個 status id 轉成名稱（給不是從 Action 表推出來的情境用）。</summary>
    public static string GetStatusName(uint statusId)
    {
        var sheet = Svc.Data.GetExcelSheet<LuminaStatus>();
        if (sheet != null && sheet.TryGetRow(statusId, out var row))
        {
            var name = row.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return $"#{statusId}";
    }
}
