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

    // 目的指示（ABGR）。⚠️ 顯示風格以 NecroLens 為基準：**有方向、有外框、不疊顏色**——
    // 所以底下全部是描邊與線段，一個 Filled 都沒有（唯一的例外是中心那顆小圓點）。
    // 顏色也刻意跟目標點位的綠／紅錯開，免得兩套疊在一起分不出誰是誰。
    private const uint ObjectiveConfirmedColor = 0xFF32C8FF;   // 金（實）：來源可信 ＋ ObjectTable 已確認
    private const uint ObjectiveStaleRiskColor = 0x9032C8FF;   // 金（淡）：同上，但來源是逐格掃描，可能是舊標記
    private const uint ObjectiveUnconfirmedColor = 0xB0A0A0A0; // 灰：只有座標，沒對上物件
    private const uint ObjectivePinColor = 0xFFFFC040;         // 淺藍：使用者自己釘的
    private const uint ObjectiveTextColor = 0xE0FFFFFF;
    private const uint ObjectiveStaleTextColor = 0xB0C0E0FF;   // 過期風險的標籤，跟一般標籤分得開

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

    /// <summary>同上，目的指示用的共用空清單。</summary>
    private static readonly List<MechaObjective> NoObjectives = [];

    public static void Draw()
    {
        try
        {
            // 右鍵選單按下的「複製診斷」在這裡才真的寫剪貼簿——
            // 那個 callback 跑在 ImGui frame 之外，這裡才是 frame 內。
            // 放在 DrawInner 之前，這樣疊加層關著也照樣複製得到。
            MechaContextMenu.FlushPendingCopy();

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

        // 目的指示跟技能範圍是**兩件獨立的事**：協助員（沒有機甲技能）也要看得到目的地，
        // 所以這裡不能沿用「沒有候選技能就整個 return」的舊條件。
        var objectives = C.ShowMechaObjectives
            ? MechaObjectiveTracker.Active
            : NoObjectives;

        if (candidates.Count == 0 && objectives.Count == 0)
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

        if (candidates.Count > 0)
        {
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
                        // 角度不在遊戲資料裡（Omen=0），走設定值。
                        // 🔑 一律問 ConeAngleFor(actionId)，不要直接讀 C.MechaConeAngleDeg——
                        //    直接讀會靜默忽略 per-skill 覆蓋（表現成「滑桿沒作用」）。
                        var angleRad = MechaActionShapes.ConeAngleFor(c.ActionId) * MathF.PI / 180f;
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
        else
        {
            // 只有目的指示要畫（例如協助員）：這一幀沒有涵蓋統計可言。
            coverage = Empty;
        }

        // 目的指示畫在最後，這樣它的描邊不會被技能範圍的半透明填色蓋掉。
        DrawObjectives(drawList, objectives, origin);
    }

    /// <summary>
    /// 目的指示（機甲事件自己的 map marker，資料來源見 <see cref="MechaObjectiveTracker"/>）。
    ///
    /// 🔑 顯示風格以 <b>NecroLens</b> 為基準：<b>有方向、有外框、不疊顏色</b>——
    /// 所以這裡畫的全是描邊圓與線段，沒有任何半透明填色蓋在地面上。
    /// 兩態標示參考 <b>PalacePal</b>：
    ///  - 金色粗框＝ObjectTable 已確認（位置就是那個物件當下的位置，會跟著它走）；
    ///  - 灰色細框＋「?」＝只有標記座標、沒對上實體物件（預設根本不會走到這裡，
    ///    要 <c>C.MechaObjectiveRequireObjectTable</c> 關掉才畫）。
    ///
    /// 全程只有浮點運算與 <see cref="PctDrawList"/> 呼叫，沒有遊戲函式、沒有原生指標。
    /// </summary>
    private static void DrawObjectives(PctDrawList drawList, IReadOnlyList<MechaObjective> objectives, Vector3 origin)
    {
        if (objectives.Count == 0)
            return;

        foreach (var o in objectives)
        {
            // 三態 ＋ 一個「來源可不可信」的維度：
            //   淺藍          ＝ 使用者自己釘的（來源就是他本人，不可能過期）
            //   金（實）粗框  ＝ ObjectTable 已確認，而且標記來自遊戲的有效清單
            //   金（淡）中框  ＝ ObjectTable 已確認，但標記是逐格掃描來的 → 可能是舊的
            //   灰   細框     ＝ 連 ObjectTable 都沒對上（預設不會走到這裡）
            var color = o.IsPin ? ObjectivePinColor
                : !o.Confirmed ? ObjectiveUnconfirmedColor
                : o.StaleRisk ? ObjectiveStaleRiskColor
                : ObjectiveConfirmedColor;

            var thickness = !o.Confirmed ? 1.5f : o.StaleRisk ? 2f : 3f;
            var radius = MathF.Max(o.Radius, 1.5f);

            // 外框：描邊，不填色。
            drawList.AddCircle(o.Position, radius, color, 0, thickness);

            // 中心點：唯一的實心元素，而且只有 4 像素——用來標「精確座標在這」。
            drawList.AddDot(o.Position, 4f, color);

            // 方向：從玩家往目的指示的短箭頭。刻意不畫整條長線，
            // 60 公尺外拉一條線過去只會擋住畫面，而使用者要的是「往哪邊走」。
            if (C.ShowMechaObjectiveDirection)
                DrawDirectionArrow(drawList, origin, o.Position, radius, color);

            DrawObjectiveLabel(drawList, o, origin);
        }
    }

    /// <summary>
    /// 目的指示的標籤。
    ///
    /// 🔑 <b>「不確定」本身一定要在畫面上看得見</b>（使用者的 UI 判準：tooltip 藏的是
    /// 「為什麼」，不是「有沒有問題」）。所以：
    ///  - 沒對上 ObjectTable、或標記可能是上一階段留下的 → 一律附上「?」，
    ///    而且 <b>就算使用者把名稱關掉也照畫</b>（只是縮到只剩「?」）。
    ///  - 兩者都沒問題時才尊重 <c>ShowMechaObjectiveNames</c>，該關就整個不畫。
    /// 只靠外框深淺區分是不夠的——淡一點的金色在明亮地形上很容易看不出來。
    /// </summary>
    private static void DrawObjectiveLabel(PctDrawList drawList, in MechaObjective o, Vector3 origin)
    {
        var uncertain = !o.Confirmed || o.StaleRisk;
        if (!uncertain && !C.ShowMechaObjectiveNames)
            return;

        var color = uncertain ? ObjectiveStaleTextColor : ObjectiveTextColor;

        if (!C.ShowMechaObjectiveNames)
        {
            // 名稱關著，但不確定性還是得說出來。
            drawList.AddText(o.Position, color, MechaPrivacy.Unknown, 1f);
            return;
        }

        var dist = Vector3.Distance(origin, o.Position);

        // 📌 Label 現在永遠有值：ObjectTable 名 → 資料表 EObjName → 「目標 N」
        //    （見 MechaObjectiveTracker.ResolveLabel）。舊碼在沒對上時硬畫「?」，
        //    等於把「這裡有一個目的指示」跟「我不知道它叫什麼」混成同一件事。
        var name = o.Label.Length > 0 ? o.Label : MechaPrivacy.Unknown;
        var suffix = uncertain ? "  " + MechaPrivacy.Unknown : "";
        drawList.AddText(o.Position, color, $"{name}  {dist:F0}m{suffix}", 1f);
    }

    /// <summary>
    /// 玩家腳邊往目的指示的方向箭頭：一段線 ＋ 兩根倒鉤，全部是線段（不填色）。
    /// 目的指示就在腳邊時（距離小於外框半徑＋3）整個不畫，免得箭頭跟外框糊在一起。
    /// </summary>
    private static void DrawDirectionArrow(PctDrawList drawList, Vector3 origin, Vector3 target, float targetRadius, uint color)
    {
        var flat = new Vector3(target.X - origin.X, 0f, target.Z - origin.Z);
        var dist = flat.Length();
        if (dist <= targetRadius + 3f || dist <= 0.01f)
            return;

        var dir = flat / dist;

        // 箭桿固定畫在腳邊 3~7 公尺處：位置固定，眼睛才不用重新找它在哪。
        // 目的指示比 7 公尺近的話就縮短到它前面一點點。
        var far = MathF.Min(7f, dist - targetRadius - 0.5f);
        var near = MathF.Min(3f, far - 1f);
        if (far <= near)
            return;

        var y = origin.Y;
        var start = new Vector3(origin.X + dir.X * near, y, origin.Z + dir.Z * near);
        var tip = new Vector3(origin.X + dir.X * far, y, origin.Z + dir.Z * far);

        drawList.AddLine(start, tip, 0f, color, 3f);

        // 倒鉤：從箭尖往回 1.2 公尺、左右各偏 0.7 公尺。
        var right = new Vector3(dir.Z, 0f, -dir.X);
        var back = new Vector3(tip.X - dir.X * 1.2f, y, tip.Z - dir.Z * 1.2f);
        var barbA = new Vector3(back.X + right.X * 0.7f, y, back.Z + right.Z * 0.7f);
        var barbB = new Vector3(back.X - right.X * 0.7f, y, back.Z - right.Z * 0.7f);

        drawList.AddLine(tip, barbA, 0f, color, 3f);
        drawList.AddLine(tip, barbB, 0f, color, 3f);
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

        var useHitbox = C.MechaCoverageUseHitbox;

        // 「目標 N」的 N。只有真的沒有名字的任務目標才會用到（見 DrawTargetLabel）。
        var unnamedOrdinal = 0;

        foreach (var t in targets)
        {
            var covered = false;

            foreach (var c in candidates)
            {
                if (!IsSkillEnabled(c.ActionId))
                    continue;

                var entry = counts[c.ActionId];
                // ⚠️ 扇形角度改成 per-skill 之後就**不能**在迴圈外算一次了：
                //    每個技能可以有自己的角度，提到外面等於全部套用第一個技能的值。
                var coneRad = MechaActionShapes.ConeAngleFor(c.ActionId) * MathF.PI / 180f;
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

            DrawTargetLabel(drawList, t, ref unnamedOrdinal);
        }

        // 全部算完才發布，讀取端不會看到半成品。
        coverage = counts;
    }

    /// <summary>
    /// 目標的標籤。
    ///
    /// 🔑 三件事在這裡收斂：
    ///  1. <b>「沒有名字」不等於不畫</b>——台服機甲事件「有害菌床」在遊戲資料裡就是空字串
    ///     （離線證據見 <see cref="MechaObjectNames"/>），舊碼的
    ///     <c>!string.IsNullOrEmpty(t.Name)</c> 讓它整個無聲消失，使用者看到的就是
    ///     「目標沒名字」。無名的<b>任務目標</b>改畫「目標 N」。
    ///  2. <b>兩態標示</b>（PalacePal 式）用符號而不是顏色：
    ///     <c>◆</c>＝已確認（目的指示標記真的對上它）、<c>◇</c>＝疑似（同型物件）。
    ///     顏色那一維已經被「有沒有被技能蓋到」（綠／紅）用掉了，再疊一層顏色會分不出誰是誰。
    ///  3. 任務目標的標籤<b>不受「顯示目標名稱」開關影響</b>——那個開關要解決的是
    ///     「一堆雜魚的名字很吵」，而任務目標只有幾個，把它藏起來就回到原本的 bug。
    ///     一般物件仍然照舊尊重開關。
    ///
    /// 🔴 名字一律過 <see cref="MechaPrivacy"/>：機甲行動是多人內容，
    /// 世界疊加層上的角色名一截圖就帶出去了。在**顯示端**做是為了設定一改就立刻生效。
    /// </summary>
    private static void DrawTargetLabel(PctDrawList drawList, in MechaTarget t, ref int unnamedOrdinal)
    {
        var isObjective = t.Tier != MechaTargetTier.Other;

        // 已確認的那一個由 DrawObjectives 畫（同一個座標畫兩行字會疊在一起）。
        if (t.Tier == MechaTargetTier.Objective && C.ShowMechaObjectives)
            return;

        if (!isObjective && (!C.ShowMechaTargetNames || t.Label.Length == 0))
            return;

        var name = t.Label.Length > 0
            ? MechaPrivacy.Sanitize(t.Label, t.Kind)
            : isObjective ? "Objective ??".Loc(++unnamedOrdinal) : MechaPrivacy.Unknown;

        var mark = t.Tier switch
        {
            MechaTargetTier.Objective => "◆ ",
            MechaTargetTier.Likely => "◇ ",
            _ => "",
        };

        drawList.AddText(t.Position, TargetNameColor, mark + name, 1f);
    }

    /// <summary>
    /// 個別技能開關：設定裡沒有紀錄＝開。
    /// ⚠️ <c>internal</c> 而不是 <c>private</c>：<see cref="MechaEventRecorder"/> 要用同一份判準
    /// 決定「這一輪要記哪幾個技能的預測範圍」，抄一份過去會漂移。
    /// </summary>
    internal static bool IsSkillEnabled(uint actionId)
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
