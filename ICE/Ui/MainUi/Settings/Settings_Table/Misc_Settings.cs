using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ICE.Config;
using ICE.Utilities.MechaOps;
using Lumina.Excel.Sheets;
using Pictomancy;
using System.Collections.Generic;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    internal class Misc_Settings
    {
        public static void Draw()
        {
            OverlaySettings();
            Separator();

            MechaAoeSettings();
            Separator();

            AutoUse();
            Separator();

            RepairSettings();
            Separator();

            TimeRecords();
            Separator();

            MountSelection();
            Separator();

            ShowSystemButtons();
            Separator();

            PostMissionCommands();
            Separator();

            ImGuiEx.IconWithText(FontAwesomeIcon.ExclamationTriangle, "Safety Settings".Loc());
            ImGui.Dummy(new Vector2(0, 5));
            SafetySettings.Draw();
        }

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
        /// 機甲行動技能範圍標示（Utilities/MechaOps）。純顯示、零自動化。
        /// </summary>
        private static void MechaAoeSettings()
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

                // 🔑 <b>這裡原本有兩個全域滑桿</b>（宇宙火焰噴射器扇形角度／宇宙鑽頭矩形長度）。
                //    2026-08-08 收斂進底下「個別技能開關」裡的 per-skill 滑桿——
                //    同一個維度有兩個地方可調、而且 per-skill 靜默優先，是使用者
                //    「這兩個滑桿還有用嗎」這個疑問的來源。舊值由設定版本 11→12 的遷移
                //    自動搬進對應技能的覆蓋格，效果值不變（見 ConfigMigrator）。
                ImGui.TextDisabled("Skill shapes are now per skill - see 'Per-skill Toggles' below.".Loc());

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

                // ---- 目標點位 ----
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

                // ---- 目的指示標示 ----
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

            // ⚠️ 下面兩項刻意放在總開關的 Disabled 範圍**之外**：
            //    右鍵選單與角色名遮蔽都跟「有沒有畫技能範圍」無關，
            //    總開關關著時它們照樣有作用，所以也必須照樣改得到。
            ImGui.Dummy(new Vector2(0, 5));

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

        private static void ShowSystemButtons()
        {
            ImGuiEx.IconWithText(FontAwesomeIcon.WindowRestore, "Show / Hide Tabs".Loc());
            ImGui.Dummy(new(0, 5));

            bool showStopWhen = C.Show_StopWhen;
            if (ImGui.Checkbox("Show Stop When... Tab".Loc() + "###ICEShowStopWhenTab", ref showStopWhen))
            {
                C.Show_StopWhen = showStopWhen;
                C.Save();
            }

            bool showGProfile = C.Show_GatheringProfile;
            if (ImGui.Checkbox("Show Gathering Profile Tab".Loc() + "###ICEShowGatheringProfileTab", ref showGProfile))
            {
                C.Show_GatheringProfile = showGProfile;
                C.Save();
            }

            bool showMissionPrio = C.Show_MissionPriority;
            if (ImGui.Checkbox("Show Mission Priority Tab".Loc() + "###ICEShowMissionPriorityTab", ref showMissionPrio))
            {
                C.Show_MissionPriority = showMissionPrio;
                C.Save();
            }

            bool showMisc = C.Show_MiscSettings;
            if (ImGui.Checkbox("Show Misc Settings Tab".Loc() + "###ICEShowMiscSettingsTab", ref showMisc))
            {
                C.Show_MiscSettings = showMisc;
                C.Save();
            }

            bool showHubActivities = C.Show_HubActivities;
            if (ImGui.Checkbox("Show Hub Activities Section".Loc() + "###ICEShowHubActivitiesSection", ref showHubActivities))
            {
                C.Show_HubActivities = showHubActivities;
                C.Save();
            }
        }
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
