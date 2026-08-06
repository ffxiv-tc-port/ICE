using Dalamud.Game.ClientState.Objects.Enums;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 一個「畫得出來的目標」在某一幀的**純值**快照。
///
/// 🔴🔴 這個型別刻意只有純值：<b>沒有 IGameObject、沒有 Address、沒有任何原生指標</b>。
/// <c>IGameObject.Address</c> 在建構時就凍結、永遠不會重新解析，<c>IsValid()</c> 只檢查
/// 「有沒有登入」而不是「這個物件還在不在」；跨幀留著它，物件被回收後解參考就是
/// AccessViolationException——那是 corrupted-state exception，try/catch 與
/// <c>HookSafety.ExecuteSafe</c> 都攔不到，會直接把使用者的遊戲帶走。
/// 所以列舉一律每幀重做，只把下面這幾個值抄出來。
/// </summary>
/// <param name="GameObjectId">物件 id，只拿來做穩定排序與「這是不是我當前目標」的比對，不解參考。</param>
/// <param name="HitboxRadius">
/// 命中判定半徑。見 <see cref="MechaCoverage"/> 的說明：AoE 的涵蓋判定是
/// 「目標這個**圓**有沒有碰到形狀」，不是「目標中心點在不在形狀裡」。
/// </param>
/// <param name="Label">
/// 已經解析好的顯示名稱（ObjectTable 的名字 → 資料表 <c>EObjName</c> → 空字串）。
/// ⚠️ 空字串是合法值：台服機甲事件「有害菌床」在遊戲資料裡就是沒有名字，
/// 顯示端要自己決定畫什麼，<b>不能因此什麼都不畫</b>。
/// </param>
/// <param name="Tier">這一筆有多可能是任務目標。見 <see cref="MechaTargetTier"/>。</param>
internal readonly record struct MechaTarget(
    ulong GameObjectId,
    uint DataId,
    string Name,
    string Label,
    Vector3 Position,
    float HitboxRadius,
    ObjectKind Kind,
    bool IsCurrentTarget,
    MechaTargetTier Tier);

/// <summary>
/// 「這個東西有多可能是任務目標」——PalacePal 式的兩態標示再加一個底層。
///
/// 🔑 <b>為什麼不是布林</b>（2026-08-06 使用者回報）：機甲任務的目標物件可能
/// <b>既沒有名字、也不可選取</b>，於是被「只顯示可選取的物件」整批擋掉；
/// 而把那個開關關掉又會讓整片場景與 NPC 灌進清單。二元開關兩邊都不對，
/// 真正缺的是「這一筆是不是任務目標」這個語意。
/// </summary>
internal enum MechaTargetTier
{
    /// <summary>一般物件。沒有任何線索指出它跟任務有關。</summary>
    Other = 0,

    /// <summary>
    /// 疑似任務目標：<c>BaseId</c> 跟某個「被目的指示標記對上過」的物件相同，
    /// 或落在資料表的機甲事件物件白名單裡。遊戲往往只標二十幾個同型目標裡的幾個，
    /// 這一層就是把剩下的補回來。
    /// </summary>
    Likely = 1,

    /// <summary>
    /// 已確認：這一幀真的有一個目的指示標記對上了它。
    /// </summary>
    Objective = 2,
}

/// <summary>
/// 「這個目標有沒有被這個技能的範圍蓋到」的純幾何判定。
///
/// 📌 <b>為什麼是圓不是點</b>（這一題的重點）：
/// FFXIV 的命中判定看的是目標的 <c>HitboxRadius</c>，不是中心點。
/// 參考實作是同艦隊的 BossmodReborn（<c>BossMod/BossModule/AIHints.cs</c>），
/// 它的三個目標判定全部把目標當成半徑 <c>HitboxRadius</c> 的圓：
/// <code>
///   TargetInAOECircle → target.Position.InCircle(origin, radius + target.HitboxRadius)
///   TargetInAOECone   → Intersect.CircleCone(target.Position, target.HitboxRadius, ...)
///   TargetInAOERect   → Intersect.CircleRect(target.Position, target.HitboxRadius, ...)
/// </code>
/// 單體技能的射程同理是「邊緣到邊緣」：<c>BossMod/ActionQueue/ActionQueue.cs</c>
/// 算的是 <c>def.Range + player.HitboxRadius + target.HitboxRadius</c>。
///
/// ⚠️ <b>但這只是很強的旁證，不是二進位證明</b>：BMR 自己在 <c>AIHints.cs</c> 那三行上面
/// 留了 <c>// TODO: verify how source/target hitboxes are accounted for by various aoe shapes</c>。
/// 所以 <c>C.MechaCoverageUseHitbox</c> 做成可以關——關掉就退回
/// 中心點判定（＝比較嚴格的判定），使用者實機發現哪一邊準就用哪一邊。
///
/// ⚠️ 另一個方向的注意：把目標當圓只會讓涵蓋判定**更寬鬆**。
/// 也就是說「以為會中結果沒中」<b>不是</b> hitbox 造成的；hitbox 解釋的是反過來的
/// 「看起來沒對準卻打中了」。真正會讓人對不準的是「根本看不到目標在哪」——
/// 這正是這一版要補的東西。
///
/// 📌 全部在 XZ 平面上算，Y 直接忽略（BMR 的 <c>WPos</c> 也是 2D）。
/// 這裡沒有任何遊戲呼叫、沒有任何指標，可以安全地在繪製執行緒上跑。
/// </summary>
internal static class MechaCoverage
{
    /// <summary>遊戲面向慣例：rotation r → 世界方向 (sin r, 0, cos r)。與 <see cref="MechaAoeOverlay"/> 同一式。</summary>
    public static Vector2 Forward(float rotation) => new(MathF.Sin(rotation), MathF.Cos(rotation));

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    /// <summary>
    /// 目標有沒有被形狀蓋到。
    /// </summary>
    /// <param name="shape">技能形狀（來自 <see cref="MechaActionShapes"/>，Lumina Action 表解出來的）。</param>
    /// <param name="coneAngleRad">扇形的**全**角（弧度）。只有 <see cref="MechaAoeKind.Cone"/> 會用到。</param>
    /// <param name="origin">施放者位置（＝疊加層畫形狀用的同一個錨點）。</param>
    /// <param name="rotation">施放者面向（弧度）。</param>
    /// <param name="casterHitbox">施放者的 hitbox 半徑，只有單體射程圈會加（見類別註解的 ActionQueue 參考）。</param>
    /// <param name="target">目標快照。</param>
    /// <param name="useHitbox">false＝退回中心點判定（把目標半徑當 0）。</param>
    public static bool IsCovered(
        MechaAoeShape shape,
        float coneAngleRad,
        Vector3 origin,
        float rotation,
        float casterHitbox,
        in MechaTarget target,
        bool useHitbox)
    {
        var o = Flat(origin);
        var p = Flat(target.Position);
        var h = useHitbox ? MathF.Max(0f, target.HitboxRadius) : 0f;
        var dir = Forward(rotation);

        return shape.Kind switch
        {
            MechaAoeKind.Rect => CircleRect(p, h, o, dir, shape.Primary, shape.HalfWidth),
            MechaAoeKind.Cone => CircleCone(p, h, o, dir, shape.Primary, coneAngleRad * 0.5f),
            MechaAoeKind.SelfCircle => Vector2.DistanceSquared(p, o) <= Sq(shape.Primary + h),

            // 單體：遊戲的射程檢查是邊緣到邊緣（射程＋雙方 hitbox），見類別註解。
            MechaAoeKind.RangeRing => Vector2.DistanceSquared(p, o)
                <= Sq(shape.Primary + h + (useHitbox ? MathF.Max(0f, casterHitbox) : 0f)),

            _ => false,
        };
    }

    /// <summary>
    /// 目標在不在這個技能的**最遠可及範圍**內（＝形狀的外接圓）。
    ///
    /// 這是「n/m 已涵蓋」的分母。用外接圓而不是「畫面上全部的目標」，
    /// 是因為分母要回答的問題是「這一招現在有機會打到幾個」——
    /// 60 公尺外還有一隻不影響你現在該不該轉身。
    /// 分子（<see cref="IsCovered"/>）永遠是分母的子集：方向對了就相等。
    /// </summary>
    public static bool IsInReach(
        MechaAoeShape shape,
        Vector3 origin,
        float casterHitbox,
        in MechaTarget target,
        bool useHitbox)
    {
        var h = useHitbox ? MathF.Max(0f, target.HitboxRadius) : 0f;
        if (shape.Kind == MechaAoeKind.RangeRing && useHitbox)
            h += MathF.Max(0f, casterHitbox);

        return Vector2.DistanceSquared(Flat(target.Position), Flat(origin)) <= Sq(shape.Primary + h);
    }

    private static float Sq(float v) => v * v;

    /// <summary>
    /// 圓 vs 矩形（矩形從 <paramref name="origin"/> 沿 <paramref name="dir"/> 延伸 <paramref name="length"/>、半寬 <paramref name="halfWidth"/>）。
    /// 作法是把圓心換算到矩形的區域座標，夾到矩形上取最近點，再比距離——
    /// 沒有分支陷阱，退化情形（length 或 halfWidth 為 0）也自然正確。
    /// </summary>
    private static bool CircleRect(Vector2 p, float r, Vector2 origin, Vector2 dir, float length, float halfWidth)
    {
        // right = dir 逆時針轉 90°，與 MechaAoeOverlay.DrawRect 的 (dir.Z, -dir.X) 同一組基底。
        var right = new Vector2(dir.Y, -dir.X);
        var v = p - origin;

        var f = Vector2.Dot(v, dir);     // 沿面向
        var s = Vector2.Dot(v, right);   // 側向

        var df = f - Math.Clamp(f, 0f, MathF.Max(0f, length));
        var ds = s - Math.Clamp(s, -halfWidth, halfWidth);

        return df * df + ds * ds <= r * r;
    }

    /// <summary>
    /// 圓 vs 扇形（頂點 <paramref name="origin"/>、面向 <paramref name="dir"/>、半徑 <paramref name="radius"/>、半角 <paramref name="halfAngle"/>）。
    ///
    /// 判定順序：
    ///  1. 頂點落在圓內 → 一定相交。
    ///  2. 距離超過 radius + r → 一定不相交（含半角多大都一樣）。
    ///  3. 半角 ≥ π → 其實就是圓對圓，前兩關已經給出答案。
    ///  4. 圓心在角度範圍內 → 由第 2 關保證半徑也夠 → 相交。
    ///  5. 否則比對兩條邊（從頂點出發、長度 radius 的線段）的最短距離。
    /// </summary>
    private static bool CircleCone(Vector2 p, float r, Vector2 origin, Vector2 dir, float radius, float halfAngle)
    {
        var v = p - origin;
        var lenSq = v.LengthSquared();

        if (lenSq <= r * r)
            return true;                        // (1)
        if (lenSq > Sq(radius + r))
            return false;                       // (2)
        if (halfAngle >= MathF.PI)
            return true;                        // (3)
        if (halfAngle <= 0f)
            return DistanceToSegmentSq(p, origin, origin + dir * radius) <= r * r;

        var len = MathF.Sqrt(lenSq);
        var cos = Vector2.Dot(v, dir) / len;
        if (cos >= MathF.Cos(halfAngle))
            return true;                        // (4)

        // (5) 兩條邊。
        var (sin, cosH) = (MathF.Sin(halfAngle), MathF.Cos(halfAngle));
        var edgeA = new Vector2(dir.X * cosH - dir.Y * sin, dir.X * sin + dir.Y * cosH);
        var edgeB = new Vector2(dir.X * cosH + dir.Y * sin, -dir.X * sin + dir.Y * cosH);

        var rSq = r * r;
        return DistanceToSegmentSq(p, origin, origin + edgeA * radius) <= rSq
            || DistanceToSegmentSq(p, origin, origin + edgeB * radius) <= rSq;
    }

    private static float DistanceToSegmentSq(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared();
        if (lenSq <= 1e-6f)
            return Vector2.DistanceSquared(p, a);
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        return Vector2.DistanceSquared(p, a + ab * t);
    }
}
