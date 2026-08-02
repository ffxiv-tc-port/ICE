using Dalamud.Interface.Colors;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.MechaOps;

namespace ICE.Ui
{
    /// <summary>
    /// 機甲行動狀態視窗（P3）。純顯示、零自動化：不施放技能、不走位、不報名。
    ///
    /// 區塊各有子開關，全部掛在總開關 <c>C.ShowMechaAoeOverlay</c> 底下。
    ///
    /// 🔴 這個檔案完全不碰遊戲的原生結構，只讀 <see cref="MechaOpsMonitor"/> 發布的
    ///    不可變快照。所有指標存取都集中在 Framework 執行緒的 monitor 裡。
    /// </summary>
    internal class MechaOpsWindow : Window
    {
        public MechaOpsWindow() : base("Mecha Ops".Loc() + "###ICEMechaOpsWindow", ImGuiWindowFlags.AlwaysAutoResize)
        {
            P.windowSystem.AddWindow(this);

            AllowPinning = true;
            AllowClickthrough = true;

            // 不要吃掉遊戲的 ESC（Dalamud 會攔掉原生關窗與 ESC 主選單）。
            RespectCloseHotkey = false;
        }

        public void Dispose()
        {
            P.windowSystem.RemoveWindow(this);
        }

        public override bool DrawConditions()
        {
            if (!C.ShowMechaAoeOverlay)
                return false;
            if (!C.ShowMechaCooldowns && !C.ShowMechaProcAlert)
                return false;
            if (!PlayerHelper.IsInCosmicZone())
                return false;

            // 沒在機甲階段就整個收起來，不要在宇宙探索全程掛一個空視窗。
            return MechaOpsMonitor.ActiveCandidates.Count > 0;
        }

        public override void Draw()
        {
            var drewSomething = false;

            if (C.ShowMechaProcAlert)
                drewSomething = DrawProcAlert();

            if (C.ShowMechaCooldowns)
            {
                if (drewSomething)
                    ImGui.Separator();
                DrawCooldowns();
            }
        }

        /// <summary>
        /// proc 提示（台服目前就是「胡蘿蔔授權」→「強力胡蘿蔔加農砲」）。
        /// 只在 proc 亮著時才佔一行，平時不佔空間。
        /// </summary>
        private static bool DrawProcAlert()
        {
            var drew = false;
            foreach (var proc in MechaOpsMonitor.ActiveProcs)
            {
                if (!proc.Ready)
                    continue;
                if (C.MechaAoeSkillToggles.TryGetValue(proc.ActionId, out var enabled) && !enabled)
                    continue;

                drew = true;
                ImGui.TextColored(ImGuiColors.DalamudYellow, $"● {proc.StatusName}");
                ImGui.SameLine();

                // 剩餘秒數只有在該 status 真的掛在本機玩家身上時才拿得到；
                // 若判定是走 IsActionHighlighted，就只顯示「就緒」。
                if (proc.RemainingSeconds > 0f)
                    ImGui.TextColored(ImGuiColors.DalamudYellow, $"{proc.RemainingSeconds:F0}s");
                else
                    ImGui.TextColored(ImGuiColors.DalamudYellow, "Ready".Loc());

                ImGui.SameLine();
                ImGui.TextDisabled($"→ {proc.ActionName}");
            }
            return drew;
        }

        /// <summary>機甲技能冷卻。</summary>
        private static void DrawCooldowns()
        {
            var candidates = MechaOpsMonitor.ActiveCandidates;
            if (candidates.Count == 0)
                return;

            // 把快照的剩餘時間外推到當下，抵銷 250ms 的掃描節流，讓倒數是平滑的。
            var age = (Environment.TickCount64 - MechaOpsMonitor.SnapshotTick) / 1000f;

            foreach (var c in candidates)
            {
                // 沿用技能範圍那組個別開關：關掉範圍的技能也不列冷卻。
                if (C.MechaAoeSkillToggles.TryGetValue(c.ActionId, out var enabled) && !enabled)
                    continue;

                var remaining = c.RecastTotal > 0f
                    ? Math.Clamp(c.RecastRemaining - age, 0f, c.RecastTotal)
                    : 0f;

                if (remaining > 0.05f)
                {
                    ImGui.TextColored(ImGuiColors.DalamudGrey3, c.Name);
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudOrange, $"{remaining:F1}s");
                }
                else
                {
                    ImGui.TextUnformatted(c.Name);
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.HealerGreen, "Ready".Loc());
                }
            }
        }
    }
}
