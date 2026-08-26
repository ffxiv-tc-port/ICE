using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
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
            if (!C.ShowMechaCooldowns && !C.ShowMechaProcAlert && !C.ShowMechaEventStatus && !C.ShowMechaEventProgress)
                return false;
            if (!PlayerHelper.IsInCosmicZone())
                return false;

            // 沒在機甲階段、也沒有任何事件旗標時就整個收起來，
            // 不要在宇宙探索全程掛一個空視窗。
            if (MechaOpsMonitor.ActiveCandidates.Count > 0)
                return true;

            if (C.ShowMechaEventStatus
                && MechaOpsMonitor.EventFlagsValid
                && MechaOpsMonitor.EventFlags != 0)
                return true;

            // 進度快照只有在取樣端的範圍驗證通過時才不是 null。
            return C.ShowMechaEventProgress && MechaOpsMonitor.EventDetail != null;
        }

        public override void Draw()
        {
            var drewSomething = false;

            if (C.ShowMechaEventStatus)
                drewSomething = DrawEventStatus();

            // 先確認真的有東西可畫，才畫分隔線——避免留下一條下面空無一物的線。
            if (C.ShowMechaEventProgress && MechaOpsMonitor.EventDetail != null)
            {
                if (drewSomething)
                    ImGui.Separator();
                drewSomething |= DrawEventProgress();
            }

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
        /// 事件狀態（報名流程走到哪一步）。資料來源只有 <c>WKSMechaEventModule.Flags</c>
        /// 這一個純量位元欄位（取樣在 <see cref="MechaOpsMonitor.ReadEventFlags"/>）。
        ///
        /// ⚠️ 這裡用的是 <see cref="WKSEventModuleFlag"/>（**模組**的旗標）。
        /// <see cref="DrawEventProgress"/> 用的是 <see cref="WKSMechaEventFlag"/>
        /// （**事件**的旗標）——兩個是不同的列舉、不同的位元定義，不要混用。
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
        /// 時間戳合理性檢查的容許範圍：跟當下的 UTC Unix 秒差距超過 30 天就當作
        /// 「這不是 Unix 秒」，改顯示原始整數。
        /// </summary>
        private const long TimestampSanityWindowSeconds = 60L * 60L * 24L * 30L;

        /// <summary>
        /// 事件進度（P4）。資料來源是 <see cref="MechaOpsMonitor.EventDetail"/>——
        /// 只有在取樣端的**指標範圍驗證通過**時才會有值，驗證不過就是 <c>null</c>、
        /// 這裡完全不畫（不做任何降級顯示）。
        ///
        /// 🔴 這個檔案跟以前一樣，一個原生結構都不碰；所有指標存取都在
        /// <see cref="MechaOpsMonitor"/> 的 Framework 執行緒裡完成。
        ///
        /// ⚠️ 五個時間戳的 epoch 沒有離線證明（CS 只標型別、沒標 epoch，
        /// 台服 7.20 的執行檔也沒反編譯驗證過）。所以這裡**不無條件換算**：
        /// 先跟當下的 UTC Unix 秒做合理性檢查，通過才顯示倒數，
        /// 不通過就原封不動印出那個 int，並在 tooltip 說明。
        /// </summary>
        private static bool DrawEventProgress()
        {
            var detail = MechaOpsMonitor.EventDetail;
            if (detail == null)
                return false;

            // 倒數一律用繪製當下的時間去算，這樣不會被 250ms 的取樣節流卡成一格一格跳。
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            DrawEventFlagLine(detail, nowUnix);

            // 🔴 兩條進度都可能 Max = 0（事件還沒開始，或欄位語意跟預期不同），
            //    除法一律走 DrawProgressRow 裡的防 0 分支。
            DrawProgressRow("Event Progress".Loc(), detail.Progress, detail.ProgressMax);
            DrawProgressRow("Personal".Loc(), detail.PersonalProgress, detail.PersonalProgressMax);

            ImGui.TextUnformatted("Contribution".Loc());
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudWhite, detail.Contribution.ToString());

            DrawDeadline("Event ends in".Loc(), detail.EventEnd, nowUnix, "Ended".Loc());
            DrawDeadline("Sign-up closes in".Loc(), detail.RegistrationEnd, nowUnix, "Closed".Loc());

            return true;
        }

        /// <summary>
        /// 事件旗標那一行。用的是 <see cref="WKSMechaEventFlag"/>（事件的旗標），
        /// 跟 <see cref="DrawEventStatus"/> 的 <see cref="WKSEventModuleFlag"/> 是兩回事。
        /// </summary>
        private static void DrawEventFlagLine(MechaEventDetail detail, long nowUnix)
        {
            var flags = detail.Flags;

            DrawEventFlagChip(flags, WKSMechaEventFlag.IsEventActive, "Active".Loc());
            DrawEventFlagChip(flags, WKSMechaEventFlag.IsParticipating, "Participating".Loc());

            // ⚠️ CS 明文註記：這兩個位元「時間過了也不會被清掉」。
            //    所以位元亮著不代表現在還開放，直接寫「報名開放中」會誤導使用者。
            //    報名有對應的截止時間戳可以交叉比對；傳送只有「開始」時間戳、沒有結束，
            //    所以那一個一律標成「無法判定」。
            DrawStaleableFlagChip(flags, WKSMechaEventFlag.PilotRegistrationOpen,
                "Sign-up".Loc(), detail.RegistrationEnd, nowUnix);
            DrawStaleableFlagChip(flags, WKSMechaEventFlag.GroundSupportTeleportOpen,
                "Teleport".Loc(), null, nowUnix);

            // 已知位元以外的東西照原樣印出來，方便日後鑑識；正常情況不會出現。
            const WKSMechaEventFlag known =
                WKSMechaEventFlag.IsParticipating
                | WKSMechaEventFlag.PilotRegistrationOpen
                | WKSMechaEventFlag.GroundSupportTeleportOpen
                | WKSMechaEventFlag.IsEventActive;
            var unknown = flags & ~known;
            if (unknown != 0)
            {
                ImGui.TextDisabled($"+0x{(uint)unknown:X}");
                ImGui.SameLine();
            }

            // 校準用的原始值。欄位語意與時間基準都還沒在台服證實過，
            // 使用者只要把游標移上去就能把原始數字回報回來，不必去翻 log。
            // （這裡全是欄位名與數字，不進翻譯表。）
            ImGui.TextDisabled($"#{detail.DataRowId}");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"WKSMechaEventDataRowId = {detail.DataRowId}\n" +
                    $"Flags = 0x{(uint)detail.Flags:X}\n" +
                    $"EventStart = {detail.EventStart}\n" +
                    $"EventEnd = {detail.EventEnd}\n" +
                    $"PilotRegistrationStart = {detail.RegistrationStart}\n" +
                    $"PilotRegistrationEnd = {detail.RegistrationEnd}\n" +
                    $"TeleportStart = {detail.TeleportStart}\n" +
                    $"EventProgress = {detail.Progress} / {detail.ProgressMax}\n" +
                    $"PersonalProgress = {detail.PersonalProgress} / {detail.PersonalProgressMax}\n" +
                    $"Contribution = {detail.Contribution}\n" +
                    $"sampled {DateTimeOffset.UtcNow.ToUnixTimeSeconds() - detail.SampledUnixSeconds}s ago");
            }

            ImGui.NewLine();
        }

        private static void DrawEventFlagChip(WKSMechaEventFlag flags, WKSMechaEventFlag bit, string label)
        {
            var active = (flags & bit) != 0;
            ImGui.TextColored(active ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey3, label);
            ImGui.SameLine();
        }

        /// <summary>
        /// 「時間過了也不會被清掉」的旗標。位元亮著只代表「曾經開放過」，所以：
        ///  - 位元沒設 → 灰色，沒有標記。
        ///  - 位元有設 + 有可信的截止時間且還沒到 → 綠色。
        ///  - 位元有設 + 有可信的截止時間但已經過了 → 灰色，加上「*」。
        ///  - 位元有設 + 沒有可比對的時間（<paramref name="deadlineRaw"/> 是 null，
        ///    或時間戳沒通過合理性檢查）→ 黃色，加上「*」＝「旗標還亮著但判定不了」。
        /// 三種帶「*」的情況都掛 tooltip，不要讓使用者把它當成正常狀態。
        /// </summary>
        private static void DrawStaleableFlagChip(WKSMechaEventFlag flags, WKSMechaEventFlag bit, string label,
            int? deadlineRaw, long nowUnix)
        {
            var active = (flags & bit) != 0;
            if (!active)
            {
                ImGui.TextColored(ImGuiColors.DalamudGrey3, label);
                ImGui.SameLine();
                return;
            }

            long remaining = 0;
            var known = deadlineRaw.HasValue
                && TryInterpretUnixSeconds(deadlineRaw.Value, nowUnix, out remaining);
            var stillOpen = known && remaining > 0;

            var color = !known
                ? ImGuiColors.DalamudYellow
                : stillOpen ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey3;

            ImGui.TextColored(color, stillOpen ? label : label + "*");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("The game does not clear this flag when the window closes, " +
                                  "so the bit alone does not mean it is still open.\n" +
                                  "* = the flag is set but the deadline has passed or could not be determined.").Loc());
            }
            ImGui.SameLine();
        }

        /// <summary>
        /// 一行「標題 + 進度條 + 數字」。
        /// 🔴 <paramref name="max"/> 可能是 0，任何情況都不做除法就直接退回只顯示分子。
        /// </summary>
        private static void DrawProgressRow(string label, int value, int max)
        {
            ImGui.TextUnformatted(label);
            ImGui.SameLine();

            if (max <= 0)
            {
                // 沒有分母：不猜、不除，直接把分子印出來。
                ImGui.TextDisabled($"{value} / ?");
                return;
            }

            var fraction = Math.Clamp(value / (float)max, 0f, 1f);
            ImGui.ProgressBar(fraction, new Vector2(160f * ImGuiHelpers.GlobalScale, 0f), $"{value} / {max}");
        }

        /// <summary>
        /// 一行倒數。時間戳的 epoch 沒被證明過，所以只有在合理性檢查通過時才換算，
        /// 否則原封不動印出那個 int。
        /// </summary>
        private static void DrawDeadline(string label, int raw, long nowUnix, string expiredText)
        {
            ImGui.TextUnformatted(label);
            ImGui.SameLine();

            if (TryInterpretUnixSeconds(raw, nowUnix, out var remaining))
            {
                if (remaining > 0)
                    ImGui.TextColored(ImGuiColors.DalamudOrange, FormatDuration(remaining));
                else
                    ImGui.TextColored(ImGuiColors.DalamudGrey3, expiredText);
            }
            else
            {
                ImGui.TextDisabled($"raw={raw}");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("The epoch of this timestamp has not been verified on TC.\n" +
                                  "A countdown is only shown when the raw value plausibly matches the current UTC Unix time; " +
                                  "otherwise the raw integer is shown as-is.\nRaw value: ??").Loc(raw));
            }
        }

        /// <summary>
        /// 合理性檢查：值必須是正數，而且跟當下的 UTC Unix 秒相差在
        /// <see cref="TimestampSanityWindowSeconds"/> 之內，才當作 Unix 秒來換算。
        /// 這不是「猜一個換算式」——不通過的話顯示端會退回顯示原始整數。
        /// </summary>
        private static bool TryInterpretUnixSeconds(int raw, long nowUnix, out long remaining)
        {
            remaining = 0;
            if (raw <= 0)
                return false;

            var delta = raw - nowUnix;
            if (Math.Abs(delta) > TimestampSanityWindowSeconds)
                return false;

            remaining = delta;
            return true;
        }

        private static string FormatDuration(long seconds)
        {
            if (seconds < 0)
                seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1d
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes:00}:{ts.Seconds:00}";
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
