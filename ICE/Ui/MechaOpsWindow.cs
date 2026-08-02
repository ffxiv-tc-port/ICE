using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
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
            if (!C.ShowMechaCooldowns && !C.ShowMechaProcAlert && !C.ShowMechaEventStatus)
                return false;
            if (!PlayerHelper.IsInCosmicZone())
                return false;

            // 沒在機甲階段、也沒有任何事件旗標時就整個收起來，
            // 不要在宇宙探索全程掛一個空視窗。
            if (MechaOpsMonitor.ActiveCandidates.Count > 0)
                return true;

            return C.ShowMechaEventStatus
                && MechaOpsMonitor.EventFlagsValid
                && MechaOpsMonitor.EventFlags != 0;
        }

        public override void Draw()
        {
            var drewSomething = false;

            if (C.ShowMechaEventStatus)
                drewSomething = DrawEventStatus();

            if (C.ShowMechaProcAlert)
            {
                if (drewSomething)
                    ImGui.Separator();
                drewSomething |= DrawProcAlert();
            }

            if (C.ShowMechaCooldowns)
            {
                if (drewSomething)
                    ImGui.Separator();
                DrawCooldowns();
            }
        }

        /// <summary>
        /// 事件狀態。資料來源只有 <c>WKSMechaEventModule.Flags</c> 這一個純量位元欄位
        /// （取樣在 <see cref="MechaOpsMonitor.ReadEventFlags"/>，那裡有完整的紅線說明）。
        ///
        /// ⚠️ 本輪刻意不做「事件進度」（第幾階段、剩餘時間、目標還剩幾個）。
        /// 那些欄位全都在 <c>WKSMechaEvent</c> 裡——0x5130 的大結構，台服完全沒有
        /// 驗證過它的內部佈局，讀錯偏移會拿到垃圾指標，而 AccessViolationException
        /// 在 .NET Core 是 corrupted-state exception，try/catch 攔不到，會直接把
        /// 使用者的遊戲帶走。
        ///
        /// 未來要做進度顯示，先決條件是這三件事都成立（缺一不可）：
        ///  (1) 對台服 7.20 的 ffxiv_dx11.exe 反編譯出 WKSMechaEvent 的實際佈局，
        ///      逐欄位確認與上游 CS 的 struct 定義一致——不能只因為 CS 有定義就上，
        ///      台服的結構位移在 7.20 已經咬過我們好幾次；
        ///  (2) 實機以唯讀方式驗證那些進度欄位在機甲行動期間真的會變動且數值合理
        ///      （全 0 或亂跳都代表偏移錯）；
        ///  (3) 確認 CurrentEvent 指標的生命週期（什麼時候被釋放、換位），
        ///      並且設計上絕不跨幀保存該指標。
        /// 在這三點齊備之前，這裡永遠只讀 Flags。
        /// </summary>
        private static bool DrawEventStatus()
        {
            if (!MechaOpsMonitor.EventFlagsValid)
                return false;

            var flags = MechaOpsMonitor.EventFlags;
            if (flags == 0)
                return false;

            ImGui.TextUnformatted("Mecha Event".Loc());
            ImGui.SameLine();

            // 報名 → 中籤 → 過場 → 加入。已達成的亮色，未達成的灰色，
            // 這樣一眼看得出目前走到哪一步。
            DrawFlagChip(flags, WKSEventModuleFlag.PilotApplicationSubmitted, "Applied".Loc());
            DrawFlagChip(flags, WKSEventModuleFlag.PilotApplicationAccepted, "Selected".Loc());
            DrawFlagChip(flags, WKSEventModuleFlag.PilotCutscenePlaying, "Cutscene".Loc());
            DrawFlagChip(flags, WKSEventModuleFlag.IsJoined, "Joined".Loc());

            // 已知位元以外的東西照原樣印出來，方便日後鑑識；正常情況不會出現。
            const WKSEventModuleFlag known =
                WKSEventModuleFlag.HasCurrentEvent
                | WKSEventModuleFlag.PilotApplicationSubmitted
                | WKSEventModuleFlag.PilotApplicationAccepted
                | WKSEventModuleFlag.PilotCutscenePlaying
                | WKSEventModuleFlag.IsJoined;
            // （每個 chip 結尾都已經 SameLine 過了，這裡不用再呼叫一次。）
            var unknown = flags & ~known;
            if (unknown != 0)
                ImGui.TextDisabled($"+0x{(uint)unknown:X}");

            ImGui.NewLine();
            return true;
        }

        private static void DrawFlagChip(WKSEventModuleFlag flags, WKSEventModuleFlag bit, string label)
        {
            var active = (flags & bit) != 0;
            ImGui.TextColored(active ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey3, label);
            ImGui.SameLine();
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
