using ICE.Utilities.Cosmic_Helper;
using Pictomancy;
using System.Collections.Generic;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 機甲行動技能範圍繪製（P1）。
/// 🔴 必須掛在 <c>Svc.PluginInterface.UiBuilder.Draw</c>——<see cref="PctDrawList"/>
/// 用的是 ImGui drawlist，只能在 ImGui frame 內使用（不能掛 Framework.Update）。
/// 遊戲結構的讀取都在 <see cref="MechaOpsMonitor"/>（Framework 執行緒）完成，
/// 這裡只讀它發布的快照與 IClientState.LocalPlayer 的 Position/Rotation。
/// 純顯示，零自動化：不施放、不走位、不報名。
/// </summary>
internal static class MechaAoeOverlay
{
    // 半透明填色（ABGR）。刻意寫死不開放設定——MVP 不過度工程。
    private const uint RectFill = 0x4000A5FF;      // 橘
    private const uint ConeFill = 0x400045FF;      // 橘紅
    private const uint CircleFill = 0x40FF9000;    // 藍青
    private const uint RangeRingColor = 0xA0FFFFFF; // 射程細圈：白

    // 目標點位（ABGR）。綠＝這一刻真的會被蓋到，紅＝沒蓋到。
    private const uint TargetCoveredColor = 0xFF40FF40;
    private const uint TargetMissedColor = 0xFF4040FF;
    private const uint TargetCurrentColor = 0xFFFFFFFF;   // 目前選取的目標再加一圈白邊
    private const uint TargetNameColor = 0xE0FFFFFF;

    /// <summary>
    /// 每個技能「打得到幾個 / 現在蓋到幾個」。<see cref="Ui.MechaOpsWindow"/> 讀這一份。
    ///
    /// ⚠️ 這是 <b>Draw 產生、Draw 消費</b>的東西：判定用的錨點就是同一幀真的拿去畫形狀的
    /// 那個 origin/rotation，所以顏色與數字跟畫面上的圖形永遠一致。
    /// 狀態視窗若剛好排在疊加層之前繪製，看到的會是上一幀的數字（差一幀，肉眼看不出來）。
    /// 每次 <see cref="DrawInner"/> 開頭都會清空，所以不會殘留過期資料。
    /// </summary>
    public static IReadOnlyDictionary<uint, (int Covered, int InReach)> Coverage => coverage;
    private static Dictionary<uint, (int Covered, int InReach)> coverage = [];

    /// <summary>共用的空表。只拿來當「這一幀沒有東西可算」的發布值，永遠不會被寫入。</summary>
    private static readonly Dictionary<uint, (int Covered, int InReach)> Empty = [];

    public static void Draw()
    {
        try
        {
            DrawInner();
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaAoeOverlayError", 60_000))
                IceLogging.Error($"MechaAoeOverlay draw failed: {ex}", "[MechaOps]");
        }
    }

    private static void DrawInner()
    {
        // 任何一條提前 return 都代表「這一幀沒有東西可算」→ 換上空的統計，
        // 狀態視窗就不會拿到過期的數字。
        // 🔑 一律「整份換參考」，不就地清空——這樣讀取端永遠拿到一份完整的表，
        //    不會在這個方法還沒填完的時候讀到半成品。
        if (!C.ShowMechaAoeOverlay)
        {
            coverage = Empty;
            return;
        }
        if (!PlayerHelper.IsInCosmicZone())
        {
            coverage = Empty;
            return;
        }

        var candidates = MechaOpsMonitor.ActiveCandidates;
        if (candidates.Count == 0)
        {
            coverage = Empty;
            return;
        }

        var lp = Svc.ClientState.LocalPlayer;
        if (lp == null)
        {
            coverage = Empty;
            return;
        }

        // 錨點假設：騎乘機甲時 LocalPlayer 的 Position/Rotation 跟著機甲走。
        // 不成立時形狀會畫在錯的位置/方向（顯示錯誤，不會崩），由實機校準。
        var origin = lp.Position;
        var rotation = lp.Rotation;

        // 單體技能的射程檢查是邊緣到邊緣（射程＋雙方 hitbox），所以需要施放者半徑。
        // ⚠️ 只在這一幀內讀，不存進任何欄位。
        var casterHitbox = lp.HitboxRadius;

        // 🔴 clipNativeUI 必須關：AddonClipper 在機甲行動期間有節點瞬變風險，第一版整面關掉。
        using var drawList = PictoService.Draw(null, new PctDrawHints(clipNativeUI: false));
        if (drawList == null)
        {
            coverage = Empty;
            return;
        }

        foreach (var c in candidates)
        {
            if (!IsSkillEnabled(c.ActionId))
                continue;

            switch (c.Shape.Kind)
            {
                case MechaAoeKind.Rect:
                    DrawRect(drawList, origin, rotation, c.Shape.Primary, c.Shape.HalfWidth);
                    break;
                case MechaAoeKind.Cone:
                    // 角度不在遊戲資料裡（Omen=0），用設定值（預設 90°，待實機校準）。
                    var angleRad = Math.Clamp(C.MechaConeAngleDeg, 15f, 360f) * MathF.PI / 180f;
                    drawList.AddConeFilled(origin, c.Shape.Primary, ToPictoRotation(rotation), angleRad, ConeFill);
                    break;
                case MechaAoeKind.SelfCircle:
                    drawList.AddCircleFilled(origin, c.Shape.Primary, CircleFill);
                    break;
                case MechaAoeKind.RangeRing:
                    drawList.AddCircle(origin, c.Shape.Primary, RangeRingColor);
                    break;
            }
        }

        // 形狀畫完之後才畫目標點，這樣點與圈不會被半透明填色蓋掉。
        DrawTargets(drawList, candidates, origin, rotation, casterHitbox);
    }

    /// <summary>
    /// 把目標的實際點位畫出來，並直接標示「這一刻有沒有被蓋到」。
    ///
    /// 🔑 這一段才是直接回答「範圍畫得對、但對不準」的東西：
    /// 圓圈畫的是 <b>hitbox</b>（命中判定看的是它，不是中心點），
    /// 中心的小點畫的是物件座標本身，顏色直接說有沒有蓋到——
    /// 不用自己用眼睛比對半透明形狀的邊界在哪。
    ///
    /// 全程只做浮點運算與 <see cref="PctDrawList"/> 呼叫，沒有任何遊戲函式呼叫、
    /// 沒有任何原生指標（目標資料是 <see cref="MechaOpsMonitor"/> 抄出來的純值）。
    /// </summary>
    private static void DrawTargets(
        PctDrawList drawList,
        IReadOnlyList<MechaCandidate> candidates,
        Vector3 origin,
        float rotation,
        float casterHitbox)
    {
        if (!C.ShowMechaTargets)
        {
            coverage = Empty;
            return;
        }

        var targets = MechaOpsMonitor.ActiveTargets;

        // 分母先建起來：即使一個目標都沒有，狀態視窗也要看得到「0/0」而不是整行消失。
        var counts = new Dictionary<uint, (int Covered, int InReach)>();
        foreach (var c in candidates)
        {
            if (IsSkillEnabled(c.ActionId))
                counts[c.ActionId] = (0, 0);
        }

        if (targets.Count == 0)
        {
            coverage = counts;
            return;
        }

        var coneRad = Math.Clamp(C.MechaConeAngleDeg, 15f, 360f) * MathF.PI / 180f;
        var useHitbox = C.MechaCoverageUseHitbox;

        foreach (var t in targets)
        {
            var covered = false;

            foreach (var c in candidates)
            {
                if (!IsSkillEnabled(c.ActionId))
                    continue;

                var entry = counts[c.ActionId];
                if (MechaCoverage.IsInReach(c.Shape, origin, casterHitbox, t, useHitbox))
                    entry.InReach++;
                if (MechaCoverage.IsCovered(c.Shape, coneRad, origin, rotation, casterHitbox, t, useHitbox))
                {
                    entry.Covered++;
                    covered = true;
                }
                counts[c.ActionId] = entry;
            }

            var color = covered ? TargetCoveredColor : TargetMissedColor;

            // hitbox 圈。半徑 0 的物件（大部分場景物件）給個 0.5 的最小值，否則畫不出來。
            var radius = MathF.Max(t.HitboxRadius, 0.5f);
            if (C.ShowMechaTargetHitbox)
                drawList.AddCircle(t.Position, radius, color, 0, covered ? 3f : 2f);

            // 物件座標本身。使用者要的「實際點位」就是這一顆。
            drawList.AddDot(t.Position, 4f, color);

            // 目前選取的目標再加一圈白邊，方便對照遊戲自己的目標框。
            if (t.IsCurrentTarget)
                drawList.AddCircle(t.Position, radius + 0.4f, TargetCurrentColor, 0, 2f);

            if (C.ShowMechaTargetNames && !string.IsNullOrEmpty(t.Name))
                drawList.AddText(t.Position, TargetNameColor, t.Name, 1f);
        }

        // 全部算完才發布，讀取端不會看到半成品。
        coverage = counts;
    }

    /// <summary>個別技能開關：設定裡沒有紀錄＝開。</summary>
    private static bool IsSkillEnabled(uint actionId)
        => !C.MechaAoeSkillToggles.TryGetValue(actionId, out var enabled) || enabled;

    /// <summary>
    /// 矩形：從施放者位置沿面向延伸（長 length、半寬 halfWidth）。
    /// 遊戲面向慣例：rotation r → 世界方向 (sin r, 0, cos r)
    /// （與 BossModReborn Angle.ToDirection 相同，實戰驗證過的慣例）。
    /// </summary>
    private static void DrawRect(PctDrawList drawList, Vector3 origin, float rotation, float length, float halfWidth)
    {
        var dir = new Vector3(MathF.Sin(rotation), 0f, MathF.Cos(rotation));
        var right = new Vector3(dir.Z, 0f, -dir.X);

        var a = origin - right * halfWidth; // 近端左
        var b = origin + right * halfWidth; // 近端右
        var c = b + dir * length;           // 遠端右
        var d = a + dir * length;           // 遠端左
        drawList.AddQuadFilled(a, b, c, d, RectFill);
    }

    /// <summary>
    /// 遊戲 rotation → Pictomancy 扇形 rotation。
    /// Pictomancy 的扇形角度慣例（FanFill.cs shader）：p → 世界方向 (cos(π/2+p), 0, sin(π/2+p)) = (−sin p, 0, cos p)；
    /// 遊戲面向是 (sin r, 0, cos r)，兩式相等 ⇔ p = −r。
    /// （同倉 Ui_FishingEditor 的 atan2(dz,dx)−π/2 寫法代數化簡後同值。）
    /// </summary>
    private static float ToPictoRotation(float gameRotation) => -gameRotation;
}
