using ICE.Utilities.Cosmic_Helper;
using Pictomancy;

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
        if (!C.ShowMechaAoeOverlay)
            return;
        if (!PlayerHelper.IsInCosmicZone())
            return;

        var candidates = MechaOpsMonitor.ActiveCandidates;
        if (candidates.Count == 0)
            return;

        var lp = Svc.ClientState.LocalPlayer;
        if (lp == null)
            return;

        // 錨點假設：騎乘機甲時 LocalPlayer 的 Position/Rotation 跟著機甲走。
        // 不成立時形狀會畫在錯的位置/方向（顯示錯誤，不會崩），由實機校準。
        var origin = lp.Position;
        var rotation = lp.Rotation;

        // 🔴 clipNativeUI 必須關：AddonClipper 在機甲行動期間有節點瞬變風險，第一版整面關掉。
        using var drawList = PictoService.Draw(null, new PctDrawHints(clipNativeUI: false));
        if (drawList == null)
            return;

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
