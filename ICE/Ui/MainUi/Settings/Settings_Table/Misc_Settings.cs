using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ICE.Config;
using ICE.Ui.DebugWindowTabs;
using ICE.Utilities.ImGuiTools;
using ICE.Utilities.MechaOps;
using Lumina.Excel.Sheets;
using Pictomancy;
using System.Collections.Generic;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    internal class Misc_Settings
    {
        // ── 「設定」一級項（2026-08-08 UI 重構第四批）────────────────────────────
        //
        // 使用者反饋：第二／三批把設定拆成「顯示與疊加層」「自動化與安全」「介面」三個
        // 一級項，但每一個底下只有一兩項，「沒整理效果」。⇒ 全部收回同一頁，改用分節。
        //
        // 📌 每一節的內容都是**原封不動**的既有區塊，一個字都沒改，只是換了位置；
        //    節與節之間不再需要 Separator()，收合標題本身就是分隔。
        // 🔴 這一頁**永遠不可以**被藏起來（側欄那邊刻意沒有 C.Show_Page_* 條件）：
        //    分頁顯示／隱藏的開關本身就住在「介面與導覽」這一節裡。它自己也能被藏的話，
        //    使用者就沒有任何辦法把藏掉的東西叫回來，只能去手改設定檔 —— 等於把人鎖在門外。
        // ⚠️ 每個 PageSection 的第二個參數是不翻譯的唯一 id：ImGui 記收合狀態是用控制項 id，
        //    而 id 預設就是標籤本身 —— 兩節翻成同一個字串會靜默共用開合狀態。
        internal static void DrawSettingsPage()
        {
            if (ImGui_Tools.PageSection("Stop When...".Loc(), "ICESecStopWhen", defaultOpen: true))
                StopWhen.Draw();

            if (ImGui_Tools.PageSection("Safety Settings".Loc(), "ICESecSafety"))
                SafetySettings.Draw();

            // 自動使用道具與自動修理原本是 Misc 頁上下相鄰的兩節，各自帶自己的小標題；
            // 併成一節之後那兩個小標題留著當子標題，中間的 Separator() 也保留。
            if (ImGui_Tools.PageSection("Automation".Loc(), "ICESecAutomation"))
            {
                AutoUse();
                Separator();
                RepairSettings();
            }

            if (ImGui_Tools.PageSection("Mount Settings".Loc(), "ICESecMount"))
                MountSelection();

            if (ImGui_Tools.PageSection("Post Mission Commands".Loc(), "ICESecPostMission"))
                PostMissionCommands();

            if (ImGui_Tools.PageSection("Overlay Window".Loc(), "ICESecOverlay"))
                OverlaySettings();

            if (ImGui_Tools.PageSection("Interface & Navigation".Loc(), "ICESecInterface"))
                InterfaceSettings.Draw();

            if (ImGui_Tools.PageSection("Record Settings".Loc(), "ICESecRecords"))
                TimeRecords();
        }

        // 📌 這裡原本有 Draw()（舊的「其他設定」頁）與 DrawDisplayPage()／DrawSafetyPage()
        //    兩個第二批加的獨立頁入口。第四批把它們的內容全部收進 DrawSettingsPage() 的
        //    對應節之後，三個組頁函式都沒有呼叫端了，一併移除 ——
        //    移除的只是「把哪幾節排在一起」的外殼，每一節的內容都還在，而且只出現一次。

        private static void OverlaySettings()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.WindowMaximize, "Overlay Window".Loc());
            ImGui.Dummy(new (0, 5));

            bool showOverlay = C.ShowOverlay;
            if (ImGui.Checkbox("Show Overlay".Loc() + "###ICEShowOverlay", ref showOverlay))
            {
                C.ShowOverlay = showOverlay;
                C.Save();
            }

            bool ShowSeconds = C.ShowSeconds;
            if (ImGui.Checkbox("Show Seconds".Loc() + "###ICEShowSeconds", ref ShowSeconds))
            {
                C.ShowSeconds = ShowSeconds;
                C.Save();
            }

            bool showExpOverlay = C.ShowExpBars;
            if (ImGui.Checkbox("Show Experience Bars on Overlay".Loc() + "###ICEShowExpBars", ref showExpOverlay))
            {
                C.ShowExpBars = showExpOverlay;
                C.Save();
            }

            bool showTotalScore = C.ShowTotalScore;
            if (ImGui.Checkbox("Show Total Score".Loc() + "###ICEShowTotalScore", ref showTotalScore))
            {
                C.ShowTotalScore = showTotalScore;
                C.Save();
            }

            // ---- 疊加層各區塊（2026-08-08 使用者要求：「預報和成果 也能加開關嗎?」）----
            // ⚠️ 三個都預設開＝現行版面零改變；這一組解決的是「我不想看這一塊」。
            //    刻意縮排在「顯示疊加層」底下：它們全都只在疊加層開著時才有意義。
            using (ImRaii.Disabled(!showOverlay))
            {
                ImGui.Indent();

                bool showWeather = C.ShowOverlayWeather;
                if (ImGui.Checkbox("Show Weather Forecast".Loc() + "###ICEShowOverlayWeather", ref showWeather))
                {
                    C.ShowOverlayWeather = showWeather;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("The weather line on the overlay: what it is now, what comes next, and how long " +
                                      "until it changes.\nOn by default - turning it off only hides the line.").Loc());
                }

                bool showTimed = C.ShowOverlayTimedMissions;
                if (ImGui.Checkbox("Show Timed Missions".Loc() + "###ICEShowOverlayTimedMissions", ref showTimed))
                {
                    C.ShowOverlayTimedMissions = showTimed;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("The job icons for the missions available this hour and the next one.\n" +
                                      "On by default - turning it off only hides the line.").Loc());
                }

                bool showJobScore = C.ShowOverlayJobScore;
                if (ImGui.Checkbox("Show Job Score Bars".Loc() + "###ICEShowOverlayJobScore", ref showJobScore))
                {
                    C.ShowOverlayJobScore = showJobScore;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("The relic tool score bar for the job of your current mission.\n" +
                                      "This is a different line from 'Show Total Score' above, which is the combined one.\n" +
                                      "On by default - turning it off only hides the bars.").Loc());
                }

                ImGui.Unindent();
            }
        }

        /// <summary>
        /// 一個機甲技能的形狀維度滑桿組（矩形：最遠距離＋最寬範圍；扇形：距離＋角度；圓形：半徑）。
        ///
        /// 🔑 <b>「預設」與「已覆蓋」必須在畫面上分得出來</b>：滑桿本身只看得到一個數字，
        /// 看不出那是遊戲資料的原值還是自己拉出來的。所以已覆蓋的維度後面掛一個
        /// 橘色的 <c>*</c>，tooltip 講預設值是多少，並且只有真的有覆蓋時才出現「重設」。
        ///
        /// ⚠️ 滑桿上限用 <c>max(常數上限, 該技能的預設值)</c>：42037 的預設距離是 60，
        /// 若上限比預設值小，使用者一旦動過就再也拉不回預設（而且是靜默的）。
        /// </summary>
        private static void DrawSkillShapeSliders(uint actionId, MechaAoeShape shape, bool enabled)
        {
            using var disabled = ImRaii.Disabled(!enabled);
            ImGui.Indent();
            ImGui.PushID((int)actionId);

            var hasDefault = MechaActionShapes.TryResolveDefault(actionId, out var def, out _);
            C.MechaShapeOverrides.TryGetValue(actionId, out var ov);

            var kindLabel = shape.Kind switch
            {
                MechaAoeKind.Rect => "Rectangle".Loc(),
                MechaAoeKind.Cone => "Cone".Loc(),
                MechaAoeKind.SelfCircle => "Circle".Loc(),
                _ => "Range ring".Loc(),
            };
            ImGui.TextDisabled(kindLabel);

            var primaryLabel = shape.Kind switch
            {
                MechaAoeKind.Rect => "Max distance".Loc(),
                MechaAoeKind.Cone => "Cone distance".Loc(),
                _ => "Radius".Loc(),
            };

            var defPrimary = hasDefault ? def.Primary : shape.Primary;
            if (Dim(primaryLabel, "P", shape.Primary, defPrimary,
                    MechaActionShapes.PrimaryMin, MechaActionShapes.PrimaryMax,
                    ov?.Primary != null, out var newPrimary))
            {
                Ensure().Primary = newPrimary;
                C.SaveDebounced();
            }

            if (shape.Kind == MechaAoeKind.Rect)
            {
                var defHalf = hasDefault ? def.HalfWidth : shape.HalfWidth;
                if (Dim("Max width (half)".Loc(), "W", shape.HalfWidth, defHalf,
                        MechaActionShapes.HalfWidthMin, MechaActionShapes.HalfWidthMax,
                        ov?.HalfWidth != null, out var newHalf))
                {
                    Ensure().HalfWidth = newHalf;
                    C.SaveDebounced();
                }
            }

            if (shape.Kind == MechaAoeKind.Cone)
            {
                var current = MechaActionShapes.ConeAngleFor(actionId);
                var defAngle = MechaActionShapes.DefaultConeAngleFor(actionId);
                if (Dim("Cone angle".Loc(), "A", current, defAngle,
                        MechaActionShapes.ConeAngleMin, MechaActionShapes.ConeAngleMax,
                        ov?.AngleDeg != null, out var newAngle))
                {
                    Ensure().AngleDeg = newAngle;
                    C.SaveDebounced();
                }
            }

            if (ov is { IsEmpty: false })
            {
                if (ImGui.SmallButton("Reset to default".Loc() + "###ICEMechaShapeReset"))
                {
                    // 整筆移除而不是把三個維度設回 null：留一個空物件在 yaml 裡只是噪音。
                    C.MechaShapeOverrides.Remove(actionId);
                    C.Save();
                }
            }

            ImGui.PopID();
            ImGui.Unindent();

            // 需要寫入時才建立字典項目——沒動過的技能不該在 yaml 裡留下痕跡。
            MechaShapeOverride Ensure()
            {
                if (!C.MechaShapeOverrides.TryGetValue(actionId, out var existing) || existing == null)
                {
                    existing = new MechaShapeOverride();
                    C.MechaShapeOverrides[actionId] = existing;
                }
                return existing;
            }

            static bool Dim(string label, string tag, float current, float defaultValue,
                            float min, float max, bool overridden, out float value)
            {
                // 上限至少要容得下預設值，否則「拉回預設」這件事做不到。
                var hi = MathF.Max(max, defaultValue);
                value = Math.Clamp(current, min, hi);
                ImGui.SetNextItemWidth(140);
                var changed = ImGui.SliderFloat(label + "###ICEMechaShape" + tag, ref value, min, hi, "%.1f");

                ImGui.SameLine();
                if (overridden)
                    ImGui.TextColored(ImGuiColors.DalamudOrange, "*");
                else
                    ImGui.TextDisabled("=");

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(overridden
                        ? ("You changed this. Default: ??").Loc(defaultValue.ToString("F1"))
                        : ("Currently the default: ??").Loc(defaultValue.ToString("F1")));
                }

                return changed;
            }
        }

        /// <summary>
        /// 「這一列要不要畫」的核取方塊。機甲行動區塊底下的逐列開關都走這一個，
        /// 免得同一段十行樣板複製七次（複製到第三次就會有一個忘記 <c>C.Save()</c>）。
        /// </summary>
        /// <param name="tooltip">需要額外說明時才給；多數列的標題本身就說完了。</param>
        private static void RowToggle(string label, string id, bool current, Action<bool> setter, string? tooltip = null)
        {
            var value = current;
            if (ImGui.Checkbox(label + "###" + id, ref value))
            {
                setter(value);
                C.Save();
            }

            if (tooltip == null)
                return;

            ImGui.SameLine();
            ImGui.TextDisabled("?");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);
        }

        /// <summary>
        /// 「機甲行動」一級項（Utilities/MechaOps）。純顯示、零自動化。
        /// </summary>
        /// <remarks>
        /// 2026-08-08 UI 重構第四批：原本是一整條四十幾個核取方塊的長捲軸，改成五節 ——
        /// 技能範圍／目標點位／目的指示／狀態與進度／診斷與錄製。
        /// <b>每一項的設定鍵、標籤、###id 與副作用都逐字未改</b>，只是分了節。
        ///
        /// ⚠️ 總開關（<c>ShowMechaAoeOverlay</c>）與「同時管兩邊」的身份分流刻意留在頁首、
        /// <b>不進任何一節</b>：五節全部掛在總開關底下，把它收進某一節之後其他四節會變成
        /// 「勾了沒反應」；身份分流則是同時作用於目標點位與目的指示，收進其中一節會誤導。
        ///
        /// ⚠️ 每一節的內容各自重新開一次 <c>ImRaii.Disabled(!showMechaAoe)</c>：
        /// ImGui 的 disabled 是一個堆疊，push/pop 必須在同一幀內配對，跨不了收合標題的邊界。
        /// 停用範圍與改版前逐字相同（右鍵選單與玩家名遮蔽本來就在停用範圍外）。
        /// </remarks>
        internal static void DrawMechaOpsPage()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.Crosshairs, "Mecha Skill Range Hints".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            bool showMechaAoe = C.ShowMechaAoeOverlay;
            if (ImGui.Checkbox("Show Mecha Skill Ranges".Loc() + "###ICEShowMechaAoe", ref showMechaAoe))
            {
                C.ShowMechaAoeOverlay = showMechaAoe;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("?");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("Draws range hints on the ground for mecha event skills while piloting or assisting.\n" +
                                  "Display only - never casts skills or moves for you.").Loc());
            }

            using (ImRaii.Disabled(!showMechaAoe))
            {
                // ---- 版面：併進主視窗 vs 獨立視窗 ----
                bool mechaInOverlay = C.ShowMechaInOverlay;
                if (ImGui.Checkbox("Show Mecha Ops In The ICE Overlay".Loc() + "###ICEShowMechaInOverlay", ref mechaInOverlay))
                {
                    C.ShowMechaInOverlay = mechaInOverlay;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("On (default): the mecha ops status becomes a collapsible section inside the ICE " +
                                      "overlay window, next to the relic tool XP one - one window less to arrange.\n" +
                                      "Off: it gets its own small window again, which can be pinned and clicked through.\n" +
                                      "The two are never shown at the same time. If the ICE overlay itself is turned off, " +
                                      "the separate window takes over automatically, so this never hides mecha ops.").Loc());
                }

                // ---- 身份分流 ----
                // ⚠️ 刻意放在目標點位與目的指示**兩組之前**、而且不縮排：它同時管兩邊。
                //    縮進任何一組底下都會讓人以為只對那一組有效。
                bool showOtherRole = C.MechaShowOtherRoleTargets;
                if (ImGui.Checkbox("Show The Other Role's Targets".Loc() + "###ICEMechaShowOtherRoleTargets", ref showOtherRole))
                {
                    C.MechaShowOtherRoleTargets = showOtherRole;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Off (default): only your own role's targets are drawn - as ground support you no " +
                                      "longer see the pilot's giant target, and the other way round.\n" +
                                      "Shared objects such as the field probe are always drawn for both roles.\n" +
                                      "When your role cannot be worked out (before you board, or outside an event), or " +
                                      "when a target cannot be attributed to either role, everything is drawn as before.\n" +
                                      "On: draws every mecha event target again, whichever role it belongs to.").Loc());
                }
            }

            // ── ① 技能範圍 ────────────────────────────────────────────────────
            //    技能形狀是 per-skill 的，所以這一節就是「哪些技能要畫、畫多大」。
            if (ImGui_Tools.PageSection("Skill Ranges".Loc(), "ICEMechaSecRanges", defaultOpen: true))
            {
                using var sectionDisabled = ImRaii.Disabled(!showMechaAoe);

                // 🔑 <b>這裡原本有兩個全域滑桿</b>（宇宙火焰噴射器扇形角度／宇宙鑽頭矩形長度）。
                //    2026-08-08 收斂進底下「個別技能開關」裡的 per-skill 滑桿——
                //    同一個維度有兩個地方可調、而且 per-skill 靜默優先，是使用者
                //    「這兩個滑桿還有用嗎」這個疑問的來源。舊值由設定版本 11→12 的遷移
                //    自動搬進對應技能的覆蓋格，效果值不變（見 ConfigMigrator）。
                ImGui.TextDisabled("Skill shapes are now per skill - see 'Per-skill Toggles' below.".Loc());

                // 📌 這一段原本在整頁的最後面（紅色警報之後）。第四批把它搬到它真正屬於的
                //    「技能範圍」節裡 —— 內容、###id、副作用逐字未改，只是換了位置。
                //    刻意保留外面那層 TreeNode：技能數 × 三個滑桿展開後很長，
                //    收合標題預設展開時直接攤開會把這一節撐爆。
                if (ImGui.TreeNode("Per-skill Toggles".Loc() + "###ICEMechaSkillToggles"))
                {
                    // 保底清單（離線驗證過的六技）＋執行期在 PetHotbar 上發現的新技能。
                    var ids = new List<uint>(MechaActionShapes.BaselineActionIds);
                    foreach (var candidate in MechaOpsMonitor.ActiveCandidates)
                    {
                        if (!ids.Contains(candidate.ActionId))
                            ids.Add(candidate.ActionId);
                    }

                    foreach (var id in ids)
                    {
                        var hasShape = MechaActionShapes.TryResolve(id, out var shape, out var name);
                        bool enabled = !C.MechaAoeSkillToggles.TryGetValue(id, out var v) || v;
                        if (ImGui.Checkbox($"{name}###ICEMechaSkill{id}", ref enabled))
                        {
                            C.MechaAoeSkillToggles[id] = enabled;
                            C.Save();
                        }

                        if (hasShape)
                            DrawSkillShapeSliders(id, shape, enabled);
                    }
                    ImGui.TreePop();
                }
            }

            // ── ② 目標點位 ────────────────────────────────────────────────────
            if (ImGui_Tools.PageSection("Target Markers".Loc(), "ICEMechaSecTargets"))
            {
                using var sectionDisabled = ImRaii.Disabled(!showMechaAoe);

                bool showTargets = C.ShowMechaTargets;
                if (ImGui.Checkbox("Show Target Markers".Loc() + "###ICEShowMechaTargets", ref showTargets))
                {
                    C.ShowMechaTargets = showTargets;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Marks where the targets actually are, and colours them by whether your skill range " +
                                      "currently covers them: green = covered, red = not covered.\n" +
                                      "The circle is the target's hitbox, the dot is its exact position.\n" +
                                      "Display only - it never targets, casts or moves for you.").Loc());
                }

                using (ImRaii.Disabled(!showTargets))
                {
                    ImGui.Indent();

                    bool showHitbox = C.ShowMechaTargetHitbox;
                    if (ImGui.Checkbox("Draw Hitbox Circles".Loc() + "###ICEShowMechaTargetHitbox", ref showHitbox))
                    {
                        C.ShowMechaTargetHitbox = showHitbox;
                        C.Save();
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("?");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("The game decides a hit against the target's hitbox, not against its centre point, " +
                                          "so a big target can be hit while its centre is outside your range.\n" +
                                          "Turn this off if you only want the centre dots.").Loc());
                    }

                    bool useHitbox = C.MechaCoverageUseHitbox;
                    if (ImGui.Checkbox("Count Hitbox As Covered".Loc() + "###ICEMechaCoverageUseHitbox", ref useHitbox))
                    {
                        C.MechaCoverageUseHitbox = useHitbox;
                        C.Save();
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("?");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("On: a target counts as covered as soon as its hitbox touches the shape - " +
                                          "this matches how the game itself is understood to work.\n" +
                                          "Off: only the centre point counts, which is the stricter reading.\n" +
                                          "If what you see in game disagrees with the colours, try flipping this.").Loc());
                    }

                    bool showNames = C.ShowMechaTargetNames;
                    if (ImGui.Checkbox("Show Target Names".Loc() + "###ICEShowMechaTargetNames", ref showNames))
                    {
                        C.ShowMechaTargetNames = showNames;
                        C.Save();
                    }

                    float targetRadius = C.MechaTargetRadius;
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.SliderFloat("Marker Range".Loc() + "###ICEMechaTargetRadius", ref targetRadius, 10f, 120f, "%.0f"))
                    {
                        C.MechaTargetRadius = targetRadius;
                        C.SaveDebounced();
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("?");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("How far out to look for targets, in yalms.\n" +
                                          "60 is the longest mecha skill range, so the default already covers " +
                                          "everything you could possibly hit.").Loc());
                    }

                    bool targetableOnly = C.MechaTargetsTargetableOnly;
                    if (ImGui.Checkbox("Targetable Objects Only".Loc() + "###ICEMechaTargetableOnly", ref targetableOnly))
                    {
                        C.MechaTargetsTargetableOnly = targetableOnly;
                        C.Save();
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("?");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("This filter no longer applies to mission objectives: anything an objective " +
                                          "marker has matched, and anything of the same kind, is always listed even if " +
                                          "it cannot be targeted and has no name at all.\n" +
                                          "Turn this off only if some ordinary object you can clearly attack is still " +
                                          "missing - it then shows every nearby object instead.").Loc());
                    }

                    // 只有在上面那項關掉時才有意義：那個開關一關，過濾就只剩這一道。
                    using (ImRaii.Disabled(targetableOnly))
                    {
                        ImGui.Indent();

                        bool hideNoise = C.MechaTargetsHideSceneryAndNpcs;
                        if (ImGui.Checkbox("Hide Scenery And NPCs".Loc() + "###ICEMechaHideSceneryAndNpcs", ref hideNoise))
                        {
                            C.MechaTargetsHideSceneryAndNpcs = hideNoise;
                            C.Save();
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("?");
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(("Only matters while 'Targetable Objects Only' is off.\n" +
                                              "On (default): still hides things that are never a target - NPCs, aetherytes, " +
                                              "gathering points, housing, area and cutscene objects, and unnamed scenery " +
                                              "you cannot target.\n" +
                                              "Mission objectives are never hidden by this, even when they have no name.\n" +
                                              "Off: shows literally every object nearby.").Loc());
                        }

                        ImGui.Unindent();
                    }

                    bool includePlayers = C.MechaTargetsIncludePlayers;
                    if (ImGui.Checkbox("Include Other Players".Loc() + "###ICEMechaIncludePlayers", ref includePlayers))
                    {
                        C.MechaTargetsIncludePlayers = includePlayers;
                        C.Save();
                    }

                    ImGui.Unindent();
                }
            }

            // ── ③ 目的指示 ────────────────────────────────────────────────────
            if (ImGui_Tools.PageSection("Objective Markers".Loc(), "ICEMechaSecObjectives"))
            {
                using var sectionDisabled = ImRaii.Disabled(!showMechaAoe);

                bool showObjectives = C.ShowMechaObjectives;
                if (ImGui.Checkbox("Show Objective Markers".Loc() + "###ICEShowMechaObjectives", ref showObjectives))
                {
                    C.ShowMechaObjectives = showObjectives;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Draws the mecha event's own objective markers in the world: an outlined ring where " +
                                      "the objective is, plus a short arrow at your feet pointing at it.\n" +
                                      "Useful as ground support too, where you have no mecha skills at all.\n" +
                                      "Display only - it never moves you and never picks a target.").Loc());
                }

                using (ImRaii.Disabled(!showObjectives))
                {
                    ImGui.Indent();

                    bool requireObjectTable = C.MechaObjectiveRequireObjectTable;
                    if (ImGui.Checkbox("Only Confirmed Objectives".Loc() + "###ICEMechaObjectiveRequireObjectTable", ref requireObjectTable))
                    {
                        C.MechaObjectiveRequireObjectTable = requireObjectTable;
                        C.Save();
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("?");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("On (recommended): only draws a marker once a real object has been found at that " +
                                          "spot in the game's object table, and then follows that object as it moves.\n" +
                                          "Off: also draws markers that matched nothing, in grey, at the raw marker position. " +
                                          "Those can be stale or simply wrong.\n" +
                                          "Leaving this on also means that if a future patch shifts the fields ICE reads, " +
                                          "the overlay quietly shows nothing instead of scattering wrong rings on the ground.").Loc());
                    }

                    float matchRadius = C.MechaObjectiveMatchRadius;
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.SliderFloat("Objective Match Radius".Loc() + "###ICEMechaObjectiveMatchRadius", ref matchRadius, 1f, 30f, "%.0f"))
                    {
                        C.MechaObjectiveMatchRadius = matchRadius;
                        C.SaveDebounced();
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("?");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("How close an object has to be to a marker to count as that objective, in yalms.\n" +
                                          "There is no official value for this - 5 is a guess. Raise it if objectives are " +
                                          "never confirmed, lower it if the wrong thing gets marked.").Loc());
                    }

                    bool showDirection = C.ShowMechaObjectiveDirection;
                    if (ImGui.Checkbox("Show Direction Arrow".Loc() + "###ICEShowMechaObjectiveDirection", ref showDirection))
                    {
                        C.ShowMechaObjectiveDirection = showDirection;
                        C.Save();
                    }

                    bool showObjectiveNames = C.ShowMechaObjectiveNames;
                    if (ImGui.Checkbox("Show Objective Names".Loc() + "###ICEShowMechaObjectiveNames", ref showObjectiveNames))
                    {
                        C.ShowMechaObjectiveNames = showObjectiveNames;
                        C.Save();
                    }

                    // 視窗裡那一列（「目的指示 2/5」＋事件名＋你的指示），跟地上的圈分開控制。
                    RowToggle("Objectives Row In The Status Block".Loc(), "ICEShowMechaRowObjectives",
                        C.ShowMechaRowObjectives, v => C.ShowMechaRowObjectives = v,
                        ("Only hides that line in the mecha ops block. The rings drawn in the world are the " +
                         "'Show Objective Markers' option above and stay as they are.").Loc());

                    // 🔴🔴 部署閘門下的選用路徑。這是這一組設定裡唯一一個「開了可能讓遊戲
                    //      直接關閉」的開關，所以警告不藏 tooltip —— 打開之後在列下面用紅字講。
                    bool useVector = C.MechaObjectiveUseMarkerVector;
                    if (ImGui.Checkbox("Use the game's own marker list".Loc() + "###ICEMechaObjectiveUseMarkerVector", ref useVector))
                    {
                        C.MechaObjectiveUseMarkerVector = useVector;
                        C.Save();
                    }
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudRed, "!");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("Off (default, recommended): ICE scans every marker slot. It never follows a " +
                                          "game pointer it cannot verify first, so this can never crash - but the list " +
                                          "may include leftovers from an earlier stage. Those are drawn faded, with a " +
                                          "'?' next to them.\n\n" +
                                          "On: ICE reads the list the game itself keeps of which markers are live. " +
                                          "That list is exact and never stale.\n" +
                                          "The catch is that the list lives in the game's heap, and there is no way for " +
                                          "ICE to check that memory is still valid before reading it. If that assumption " +
                                          "ever stops holding, the failure is an access violation, which cannot be " +
                                          "caught - the game closes on the spot.\n\n" +
                                          "Only turn this on if stale markers are actually getting in your way.").Loc());
                    }

                    if (useVector)
                    {
                        ImGui.Indent();
                        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 360f * ImGuiHelpers.GlobalScale);
                        ImGui.TextColored(ImGuiColors.DalamudRed,
                            ("This reads game memory that ICE cannot verify beforehand. If the assumption behind it " +
                             "ever breaks, the game closes immediately and nothing can catch it. " +
                             "Turn it back off unless you are specifically fighting stale markers.").Loc());
                        ImGui.PopTextWrapPos();
                        ImGui.Unindent();
                    }

                    ImGui.Unindent();
                }
            }

            // ── ④ 狀態與進度 ──────────────────────────────────────────────────
            //    技能冷卻／觸發提示／報名狀態／事件進度／下一場時間／駕駛申請書／紅色警報，
            //    也就是「機甲行動狀態」那個區塊要顯示哪幾列。
            if (ImGui_Tools.PageSection("Status & Progress".Loc(), "ICEMechaSecStatus"))
            {
                using var sectionDisabled = ImRaii.Disabled(!showMechaAoe);

                bool showCooldowns = C.ShowMechaCooldowns;
                if (ImGui.Checkbox("Show Mecha Skill Cooldowns".Loc() + "###ICEShowMechaCooldowns", ref showCooldowns))
                {
                    C.ShowMechaCooldowns = showCooldowns;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Shows a small window with the remaining cooldown of each mecha skill.\n" +
                                      "Only appears while mecha skills are actually available.").Loc());
                }

                bool showProcAlert = C.ShowMechaProcAlert;
                if (ImGui.Checkbox("Show Mecha Proc Alerts".Loc() + "###ICEShowMechaProcAlert", ref showProcAlert))
                {
                    C.ShowMechaProcAlert = showProcAlert;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Highlights when a mecha skill becomes usable through a proc\n" +
                                      "(on TC that is Carrot Clearance, which enables Carrot Cannon Overload).").Loc());
                }

                bool showEventStatus = C.ShowMechaEventStatus;
                if (ImGui.Checkbox("Show Mecha Event Status".Loc() + "###ICEShowMechaEventStatus", ref showEventStatus))
                {
                    C.ShowMechaEventStatus = showEventStatus;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Shows how far along the pilot sign-up flow you are: applied, selected, cutscene, joined.").Loc());
                }

                bool showEventProgress = C.ShowMechaEventProgress;
                if (ImGui.Checkbox("Show Mecha Event Progress".Loc() + "###ICEShowMechaEventProgress", ref showEventProgress))
                {
                    C.ShowMechaEventProgress = showEventProgress;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Shows the event and personal progress bars, your contribution, and the remaining time.\n" +
                                      "Display only - it never signs you up and never acts for you.\n" +
                                      "The values are read only after the game's event pointer passes a range check; " +
                                      "if it does not, nothing is shown at all.").Loc());
                }

                // 進度區塊底下的逐列開關（2026-08-08 使用者要求：「機甲ui的各項 能加開關嗎」）。
                // ⚠️ 縮排掛在上面那個總開關底下，而且總開關關著時一併 disable——
                //    否則使用者會在一組「勾了也沒反應」的核取方塊上打轉。
                using (ImRaii.Disabled(!showEventProgress))
                {
                    ImGui.Indent();

                    RowToggle("Event Progress Bar".Loc(), "ICEShowMechaRowEventProgress",
                        C.ShowMechaRowEventProgress, v => C.ShowMechaRowEventProgress = v);
                    RowToggle("Personal Progress Bar".Loc(), "ICEShowMechaRowPersonalProgress",
                        C.ShowMechaRowPersonalProgress, v => C.ShowMechaRowPersonalProgress = v);
                    RowToggle("Contribution Row".Loc(), "ICEShowMechaRowContribution",
                        C.ShowMechaRowContribution, v => C.ShowMechaRowContribution = v);
                    RowToggle("Event Time Remaining".Loc(), "ICEShowMechaRowEventEnd",
                        C.ShowMechaRowEventEnd, v => C.ShowMechaRowEventEnd = v);
                    RowToggle("Sign-up Deadline".Loc(), "ICEShowMechaRowSignupEnd",
                        C.ShowMechaRowSignupEnd, v => C.ShowMechaRowSignupEnd = v);
                    RowToggle("Teleport Deadline".Loc(), "ICEShowMechaRowTeleportEnd",
                        C.ShowMechaRowTeleportEnd, v => C.ShowMechaRowTeleportEnd = v);

                    ImGui.Unindent();
                }

                bool showSchedule = C.ShowMechaSchedule;
                if (ImGui.Checkbox("Show Next Mecha Event Time".Loc() + "###ICEShowMechaSchedule", ref showSchedule))
                {
                    C.ShowMechaSchedule = showSchedule;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Shows when the next mecha event starts, as a clock time and a countdown.\n" +
                                      "The game's own panel tells you there is a next event but never says when.\n" +
                                      "The start time is readable well before the event begins (measured: 19 minutes " +
                                      "ahead), and the countdown runs off the game's server clock, not your PC clock.\n" +
                                      "Display only - it never signs you up.").Loc());
                }

                bool showPilotTicket = C.ShowMechaPilotTicket;
                if (ImGui.Checkbox("Show Pilot Application".Loc() + "###ICEShowMechaPilotTicket", ref showPilotTicket))
                {
                    C.ShowMechaPilotTicket = showPilotTicket;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Shows whether you are carrying a pilot application - you can only hold one, " +
                                      "and it is what lets you sign up as the mecha pilot.\n" +
                                      "It is not an inventory item, so the game only shows it inside its own mecha " +
                                      "ops panel; this puts it where you can see it before the sign-up window opens.\n" +
                                      "Display only - it never signs you up and never buys anything.").Loc());
                }

                bool showEmergency = C.ShowMechaEmergency;
                if (ImGui.Checkbox("Show Red Alert (Emergency) Events".Loc() + "###ICEShowMechaEmergency", ref showEmergency))
                {
                    C.ShowMechaEmergency = showEmergency;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    // 🔴 這個 tooltip 必須誠實說出「這一項還沒有實機驗證過」——
                    //    它是部署閘門，不是一般的顯示開關。
                    ImGui.SetTooltip(("Shows the current Red Alert (magnetic storm / meteor shower / spore mist): " +
                                      "which one it is and how long is left.\n\n" +
                                      "OFF BY DEFAULT ON PURPOSE. This one reads a game structure whose size we could " +
                                      "not verify offline on the TC client. If the layout differs there, the read goes " +
                                      "out of bounds and that crashes the game outright - it cannot be caught.\n" +
                                      "Everything else in this window has been verified offline; this has not. " +
                                      "Turn it on only if you are willing to hit that.").Loc());
                }

            }

            // ── ⑤ 診斷與錄製 ──────────────────────────────────────────────────
            // ⚠️ 這一節整節**不套** ImRaii.Disabled(!showMechaAoe)：右鍵選單、角色名遮蔽
            //    與錄製器都跟「有沒有畫技能範圍」無關，總開關關著時它們照樣有作用
            //    （錄製器自己會強制取樣），所以也必須照樣改得到。這與改版前逐字相同。
            if (ImGui_Tools.PageSection("Diagnostics & Recording".Loc(), "ICEMechaSecDiagnostics"))
            {
                bool showContextMenu = C.ShowMechaContextMenu;
                if (ImGui.Checkbox("Mecha Right-click Menu".Loc() + "###ICEShowMechaContextMenu", ref showContextMenu))
                {
                    C.ShowMechaContextMenu = showContextMenu;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Adds ICE entries to the right-click menu while you are in a cosmic zone: mark an object " +
                                      "in the mecha overlay, and copy the mecha event diagnostics.\n" +
                                      "Display only - nothing there acts for you.").Loc());
                }

                bool showFullNames = C.MechaShowFullPlayerNames;
                if (ImGui.Checkbox("Show Full Player Names".Loc() + "###ICEMechaShowFullPlayerNames", ref showFullNames))
                {
                    C.MechaShowFullPlayerNames = showFullNames;
                    C.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("Off (default): other players' character names are shortened to initials both on the " +
                                      "overlay and in ICE's own log history.\n" +
                                      "Mecha ops is group content, so the log - which has a 'copy to clipboard' button - " +
                                      "would otherwise carry other people's character names out of the game with it.\n" +
                                      "Your own name is never shortened.").Loc());
                }

                ImGui.Dummy(new Vector2(0, 5));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0, 5));

                // 📌 錄製器面板本來只在 /ice d 的偵錯視窗裡（分頁 26）。這裡是**同一個函式的
                //    第二個呼叫點**，不是複製 —— 兩邊讀寫的是 MechaEventRecorder 上同一組
                //    記憶體內靜態旗標，不會不同步，偵錯視窗那個入口也照舊留著。
                //    放進主視窗是因為要使用者交機甲行動的診斷時，「請開 /ice d 找第 26 個分頁」
                //    這句話本身就是一道門檻。
                // 📌 錄製旗標刻意不寫進設定檔（見 Ui_MechaRecorder 的註解），重開遊戲一律回到關閉。
                Ui_MechaRecorder.Draw();
            }
        }

        private static void AutoUse()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.PersonRays, "Auto-Use".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            bool AutoMoonSprint = C.MoonSprint;
            if (ImGui.Checkbox("Auto-Use Moon Sprint".Loc() + "###ICEAutoMoonSprint", ref AutoMoonSprint))
            {
                C.MoonSprint = AutoMoonSprint;
                C.Save();
            }

            bool DisableLunarAura = C.RemoveStellarStatus;
            if (ImGui.Checkbox("Auto-Remove Stellar Status".Loc() + "###ICEAutoRemoveStellarStatus", ref DisableLunarAura))
            {
                C.RemoveStellarStatus = DisableLunarAura;
                C.Save();
            }

            bool DisableRedAlertPathing = C.DisablePathfindingToRedAlert;
            if (ImGui.Checkbox("Disable Pathfinding to Red Alerts".Loc() + "###ICEDisableRedAlertPathing", ref DisableRedAlertPathing))
            {
                C.DisablePathfindingToRedAlert = DisableRedAlertPathing;
                C.Save();
            }
        }

        private static void RepairSettings()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.Hammer, "Repair Settings".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            bool repairAtVendor = C.RepairAtVendor;
            if (ImGui.Checkbox("Repair at Vendor".Loc() + "###ICERepairAtVendor", ref repairAtVendor))
            {
                C.RepairAtVendor = repairAtVendor;
                C.Save();
            }

            using (ImRaii.Disabled(repairAtVendor))
            {
                bool selfRepairGather = C.SelfRepairGather;
                if (ImGui.Checkbox("Self Repair Gather".Loc() + "###ICESelfRepairGather", ref selfRepairGather))
                {
                    C.SelfRepairGather = selfRepairGather;
                    C.Save();
                }

                bool selfRepairCrafter = C.SelfRepairCrafter;
                if (ImGui.Checkbox("Self Repair Crafter".Loc() + "###ICESelfRepairCrafter", ref selfRepairCrafter))
                {
                    C.SelfRepairCrafter= selfRepairCrafter;
                    C.Save();
                }
            }

            float repairAmount = C.RepairPercent;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderFloat("###Repair %", ref repairAmount, 0f, 99f, "%.0f%%"))
            {
                if (C.RepairPercent != repairAmount)
                {
                    C.RepairPercent = (int)repairAmount;
                    C.SaveDebounced();
                }
            }
        }

        private static void TimeRecords()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.Clock, "Record Settings".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            int TimeHistory = C.TimeHistoryLimit;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Average Time History to keep".Loc() + "###ICETimeHistoryLimit", ref TimeHistory))
            {
                C.TimeHistoryLimit = TimeHistory;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("?");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("Anything below 0 to keep all logs\n" +
                                 "Above 0 to keep a set limit").Loc());
            }
        }

        private static bool visualizeRadius = false;
        private static bool visualizeDismountRadius = false;
        private static Dictionary<uint, string> availableMounts = new();

        private static string mountSearchText = "";
        private static int mountDisplayOffset = 0;
        private static int mountItemsPerPage = 10;

        private static unsafe void MountSelection()
        {
            bool mountOutsideMission = C.UseMountOutsideMission;
            bool mountInMission = C.UseMountInMission;
            float minMountRange = C.MountRadius;
            float dismountRange = C.DismountRadius;

            ImGuiEx.IconWithText(FontAwesomeIcon.Feather, "Mount Settings".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            if (ImGui.Button("Select Mounting Option".Loc() + "###ICESelectMountingOption"))
            {
                availableMounts.Clear();
                availableMounts[0] = "Mount Roulette";

                var mountSheet = Svc.Data.GetExcelSheet<Mount>();

                foreach (var mountItem in mountSheet)
                {
                    //Checking to see if the current mount is unlocked
                    if (!PlayerState.Instance()->IsMountUnlocked(mountItem.RowId)) continue;

                    string mountName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(mountItem.Singular.ToString().ToLower());
                    uint id = mountItem.RowId;

                    availableMounts[id] = mountName;
                }

                mountSearchText = "";
                mountDisplayOffset = 0;

                ImGui.OpenPopup("Mount Options");
            }
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Mount: ??".Loc(C.MountName.Loc()));

            if (ImGui.BeginPopup("Mount Options"))
            {
                // Search box
                ImGui.InputText("Search".Loc() + "###ICEMountSearch", ref mountSearchText, 100);

                // Filter mounts based on search
                var filteredMounts = availableMounts
                    .Where(kvp => string.IsNullOrEmpty(mountSearchText) ||
                                  kvp.Value.Contains(mountSearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Calculate page count here, just to peeps know how many pages there are
                int totalItems = filteredMounts.Count;
                int maxOffset = Math.Max(0, totalItems - mountItemsPerPage);
                mountDisplayOffset = Math.Min(mountDisplayOffset, maxOffset);

                // Display current page of mounts
                var displayMounts = filteredMounts
                    .Skip(mountDisplayOffset)
                    .Take(mountItemsPerPage);

                foreach (var mount in displayMounts)
                {
                    if (ImGui.Selectable($"{mount.Value.Loc()}##{mount.Key}"))
                    {
                        C.MountId = mount.Key;
                        C.MountName = mount.Value;
                        C.Save();
                        ImGui.CloseCurrentPopup();
                    }
                }

                // Navigation buttons
                ImGui.Separator();

                if (ImGui.Button("Previous".Loc() + "###ICEMountPrev") && mountDisplayOffset > 0)
                {
                    mountDisplayOffset = Math.Max(0, mountDisplayOffset - mountItemsPerPage);
                }

                ImGui.SameLine();
                ImGui.Text("??-?? of ??".Loc(mountDisplayOffset + 1, Math.Min(mountDisplayOffset + mountItemsPerPage, totalItems), totalItems));

                ImGui.SameLine();
                if (ImGui.Button("Next".Loc() + "###ICEMountNext") && mountDisplayOffset < maxOffset)
                {
                    mountDisplayOffset = Math.Min(maxOffset, mountDisplayOffset + mountItemsPerPage);
                }

                ImGui.EndPopup();
            }

            if (ImGui.Checkbox("Use mount outside mission".Loc() + "###ICEUseMountOutsideMission", ref mountOutsideMission))
            {
                C.UseMountOutsideMission = mountOutsideMission;
                C.Save();
            }

            if (ImGui.Checkbox("Use mount in mission".Loc() + "###ICEUseMountInMission", ref mountInMission))
            {
                C.UseMountInMission = mountInMission;
                C.Save();
            }

            ImGui.SetNextItemWidth(100);
            if (ImGui.DragFloat("Minimum Mounting Range".Loc() + "###ICEMinMountRange", ref minMountRange, 1))
            {
                C.MountRadius = minMountRange;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.Checkbox("Visualize radius".Loc() + "###ICEVisualizeMountRadius", ref visualizeRadius);
            ImGui.SetNextItemWidth(100);
            if (ImGui.DragFloat("Dismount Target Range".Loc() + "###ICEDismountRange", ref dismountRange, 1))
            {
                C.DismountRadius = dismountRange;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.Checkbox("Visualize Dismount Radius".Loc() + "###ICEVisualizeDismountRadius", ref visualizeDismountRadius);

            using (var drawList = PictoService.Draw())
            {
                if (drawList == null)
                    return;

                var playerPos = Player.Position;

                if (visualizeRadius)
                    PictoService.VfxRenderer.AddCircle("Mount_Radius Circle", playerPos, C.MountRadius, Utils.FromUintABGR(2616716297));
                if (visualizeDismountRadius)
                    PictoService.VfxRenderer.AddCircle("Dismount_Radius Circle", playerPos, C.DismountRadius, Utils.FromUintABGR(2601121571));
            }
        }

        // 📌 原本這裡有 ShowSystemButtons()（顯示／隱藏分頁的五個開關）。
        //    UI 重構第三批整段搬到 InterfaceSettings.Draw()，因為那些開關要跟
        //    「哪些分頁被藏起來了」的提示放在同一頁，而且「介面」是永遠藏不掉的分頁。
        //    是搬家不是複製 —— 這裡不再畫它們。
        private static void PostMissionCommands()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.Play, "Post Mission Commands".Loc());
            ImGui.Dummy(new Vector2(0, 5));

            ImGui.TextWrapped(("Input below a list of commands that you would like to run after a run has been completed. \n" +
                              "This is kind of my way of letting you somewhat script/set up a sequence of other things that you would like to do that might not be included in the plugin itself. \n" +
                              "If you want something more complex, just make an SND script at that point. And have this run that script post lol.").Loc());

            if (ImGui.Button("Add New Command".Loc() + "###ICEAddNewCommand"))
            {
                C.PostMissionCommands.Add(new Config.MissionCommand 
                { 
                    command = "", 
                    Delay = 0,
                });
                C.Save();
            }

            MissionCommand? toRemove = null;
            int entryCounter = 0;

            if (ImGui.BeginTable("Mission Commands", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders))
            {
                ImGui.TableSetupColumn("Command".Loc());
                ImGui.TableSetupColumn("Delay".Loc());
                ImGui.TableSetupColumn("Remove".Loc());

                ImGui.TableHeadersRow();

                foreach (var entry in C.PostMissionCommands)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.SetNextItemWidth(200);

                    ImGui.PushID($"{entryCounter}_MissionCommand");
                    string command = entry.command;
                    if (ImGui.InputText("##Command", ref command))
                    {
                        entry.command = command;
                        C.SaveDebounced();
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(100);
                    int delay = entry.Delay;
                    if (ImGui.InputInt("###Delay", ref delay))
                    {
                        entry.Delay = delay;
                        C.SaveDebounced();
                    }

                    ImGui.TableNextColumn();
                    if (ImGuiEx.IconButton(FontAwesomeIcon.Trash, $"remove{C.PostMissionCommands.IndexOf(entry)}"))
                    {
                        toRemove = entry;
                    }
                    ImGui.PopID();
                    entryCounter += 1;
                }

                if (toRemove != null)
                {
                    C.PostMissionCommands.Remove(toRemove);
                    C.Save();
                }

                ImGui.EndTable();
            }
        }

        private static void Separator()
        {
            ImGui.Dummy(new Vector2(0, 5));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 5));
        }
    }
}
