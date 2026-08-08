using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.MechaOps;
using System.Collections.Generic;

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
            if (!C.ShowMechaCooldowns && !C.ShowMechaTargets && !C.ShowMechaProcAlert
                && !C.ShowMechaEventStatus && !C.ShowMechaEventProgress && !C.ShowMechaObjectives
                && !C.ShowMechaSchedule && !C.ShowMechaEmergency)
                return false;
            if (!PlayerHelper.IsInCosmicZone())
                return false;

            // 🔑 排程與緊急事件刻意排在「有沒有機甲技能」之前：
            //    這兩行的**全部價值就是在機甲階段以外**看得到（「下一場幾點」「現在有沒有紅色警報」）。
            //    掛在 ActiveCandidates 底下等於只有已經在打的人才看得到，那就沒有意義了。
            if (C.ShowMechaSchedule && MechaOpsMonitor.Schedule.Count > 0)
                return true;
            if (C.ShowMechaEmergency && MechaOpsMonitor.Emergency is { IsRedAlert: true })
                return true;

            // 沒在機甲階段、也沒有任何事件旗標時就整個收起來，
            // 不要在宇宙探索全程掛一個空視窗。
            if (MechaOpsMonitor.ActiveCandidates.Count > 0)
                return true;

            // 目的指示只要讀到過標記（或使用者釘了東西）就值得掛著——
            // 協助員沒有機甲技能，這一行是他唯一看得到的東西。
            if (C.ShowMechaObjectives
                && (MechaObjectiveTracker.MarkerCount > 0 || MechaObjectiveTracker.PinnedCount > 0))
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

            // 排程與緊急事件放最上面：它們是「隨時掃視」的資訊，
            // 而且在機甲階段以外這個視窗往往只有這兩行。
            if (C.ShowMechaSchedule)
                drewSomething = DrawSchedule();

            if (C.ShowMechaEmergency)
            {
                if (drewSomething)
                    ImGui.Separator();
                drewSomething |= DrawEmergency();
            }

            if (C.ShowMechaEventStatus)
            {
                if (drewSomething)
                    ImGui.Separator();
                drewSomething |= DrawEventStatus();
            }

            // 先確認真的有東西可畫，才畫分隔線——避免留下一條下面空無一物的線。
            if (C.ShowMechaEventProgress && MechaOpsMonitor.EventDetail != null)
            {
                if (drewSomething)
                    ImGui.Separator();
                drewSomething |= DrawEventProgress();
            }

            if (C.ShowMechaObjectives)
            {
                if (drewSomething)
                    ImGui.Separator();
                drewSomething |= DrawObjectiveRow();
            }

            if (C.ShowMechaProcAlert)
            {
                if (drewSomething)
                    ImGui.Separator();
                drewSomething |= DrawProcAlert();
            }

            if (C.ShowMechaCooldowns || C.ShowMechaTargets)
            {
                if (drewSomething)
                    ImGui.Separator();
                DrawSkillRows();
            }
        }

        /// <summary>
        /// 「下次機甲事件：<c>名稱 HH:mm（N 分後）</c>」。
        ///
        /// 🔑 <b>這是使用者原本要的那一行</b>（原話：「機甲任務有下一次任務 但沒提示時間」）。
        /// 遊戲自己的面板只說「有下一場」，不說幾點。
        ///
        /// 🔴 <b>時間換算</b>：時間戳是 <b>unix 秒</b>，倒數一律拿<b>伺服器時間</b>比
        /// （遊戲自己判斷報名／傳送視窗就是拿它比，見 <see cref="MechaEventDetail"/>）；
        /// 而「幾點幾分」那一段用 <c>DateTimeOffset.FromUnixTimeSeconds(x).LocalDateTime</c>
        /// 轉成使用者的當地時間 —— <b>不要</b>用 <c>UtcDateTime</c>，那會整整差一個時區
        /// （台服使用者就是差 8 小時，而且畫面上看起來完全正常）。
        ///
        /// 🔑 <b>「不知道」要看得見</b>：拿不到伺服器時間時不猜「還有幾分鐘」，
        /// 改成灰色的「?」＋原始時鐘，而不是畫一個看起來很正常的錯誤倒數。
        /// </summary>
        private static bool DrawSchedule()
        {
            var entries = MechaOpsMonitor.Schedule;
            if (entries.Count == 0)
                return false;

            var now = entries[0].ServerTimeNow;

            // 下一場＝開始時間還在未來的那些裡面最早的。
            MechaScheduleEntry? next = null;
            MechaScheduleEntry? running = null;
            foreach (var e in entries)
            {
                if (now > 0 && e.EventStart <= now)
                {
                    // 已經開始了：如果還沒結束，它就是「進行中」的那一場。
                    if (e.EventEnd > now && (running == null || e.EventEnd < running.EventEnd))
                        running = e;
                    continue;
                }
                if (next == null || e.EventStart < next.EventStart)
                    next = e;
            }

            // 進行中的那場已經有既有的進度區塊在講，這裡只在「沒有下一場」時才提一句，
            // 免得同一件事在視窗裡出現兩次。
            if (next == null && running == null)
                return false;

            if (next == null)
            {
                ImGui.TextUnformatted("Mecha Event".Loc());
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.HealerGreen, "In progress".Loc());
                ImGui.SameLine();
                ImGui.TextDisabled(NameOf(running!.DataRowId));
                return true;
            }

            ImGui.TextUnformatted("Next Mecha Event".Loc());
            ImGui.SameLine();

            // 事件名稱。查不到就是灰色問號——不要拿空字串充數。
            var name = NameOf(next.DataRowId);
            if (name.Length > 0)
                ImGui.TextColored(ImGuiColors.DalamudWhite, name);
            else
                ImGui.TextColored(ImGuiColors.DalamudGrey3, "?");

            ImGui.SameLine();

            // 幾點幾分（當地時間）——這一段不需要伺服器時間，時間戳本身就是絕對時刻。
            ImGui.TextColored(ImGuiColors.DalamudOrange, FormatClock(next.EventStart));

            // 還有多久——這一段需要伺服器時間，拿不到就標「?」。
            ImGui.SameLine();
            if (TryGetRemaining(next.EventStart, now, out var remaining) && remaining > 0)
            {
                ImGui.TextDisabled($"({FormatDuration(remaining)})");
            }
            else
            {
                ImGui.TextColored(ImGuiColors.DalamudGrey3, "(?)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("The countdown needs the game's own server clock, and it could not be read " +
                                      "this pass (or the timestamp is out of range).\n" +
                                      "The start time itself is still correct - it is an absolute timestamp.").Loc());
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"WKSMechaEventDataRowId = {next.DataRowId}\n" +
                    $"EventStart = {next.EventStart} ({FormatClock(next.EventStart)})\n" +
                    $"EventEnd = {next.EventEnd} ({FormatClock(next.EventEnd)})\n" +
                    $"PilotRegistration = {next.RegistrationStart} .. {next.RegistrationEnd}\n" +
                    $"TeleportStart = {next.TeleportStart}\n" +
                    $"Flags = 0x{(uint)next.Flags:X}  slot={next.Slot}\n" +
                    $"ServerTime = {now}");
            }

            return true;
        }

        private static string NameOf(uint dataRowId)
            => MechaObjectNames.EventName(dataRowId) ?? string.Empty;

        /// <summary>
        /// unix 秒 → 使用者當地時間的「HH:mm」。
        /// 🔴 一定要用 <c>LocalDateTime</c>：<c>UtcDateTime</c> 會整整差一個時區，
        ///    而且畫面上看起來完全正常（台服使用者差 8 小時）。
        /// </summary>
        private static string FormatClock(int unixSeconds)
        {
            if (unixSeconds <= 0)
                return "?";
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("HH:mm");
            }
            catch
            {
                // 垃圾值（超出 DateTimeOffset 範圍）：照樣不要猜，直接說不知道。
                return "?";
            }
        }

        /// <summary>
        /// 緊急事件（紅色警報：磁暴／流星雨／孢子霧）那一行。
        ///
        /// ⚠️ 整段掛在 <c>C.ShowMechaEmergency</c> 底下，而那是<b>預設關的部署閘門</b>
        /// （理由見 <c>MissionConfigs</c> 的註解與 <c>MechaOpsMonitor.ReadEmergency</c>）。
        ///
        /// 🔑 類型名稱走純資料表查詢，查不到就畫灰色「?」——
        /// <b>不要</b>因為查不到名字就整行不畫，那會讓「有紅色警報」這件事本身消失。
        /// </summary>
        private static bool DrawEmergency()
        {
            var em = MechaOpsMonitor.Emergency;
            if (em == null || !em.IsRedAlert)
                return false;

            var (shortName, banner) = MechaEmergencyNames.Lookup(em.InfoRowId, em.InfoSubRowId);

            ImGui.TextColored(ImGuiColors.DalamudRed, "Red Alert".Loc());
            ImGui.SameLine();

            if (shortName != null)
                ImGui.TextColored(ImGuiColors.DalamudWhite, shortName);
            else
                ImGui.TextColored(ImGuiColors.DalamudGrey3, "?");

            ImGui.SameLine();
            ImGui.TextDisabled(em.IsIncoming ? "Incoming".Loc() : "In progress".Loc());

            // 剩餘時間。EndTime 是 unix 秒；同樣拿伺服器時間比，拿不到就「?」。
            var now = em.ServerTimeNow;
            var end = em.EndTime is > 0 and <= int.MaxValue ? (int)em.EndTime : 0;
            ImGui.SameLine();
            if (TryGetRemaining(end, now, out var remaining) && remaining > 0)
                ImGui.TextColored(ImGuiColors.DalamudOrange, FormatDuration(remaining));
            else
                ImGui.TextColored(ImGuiColors.DalamudGrey3, "?");

            if (ImGui.IsItemHovered())
            {
                var raw = $"State = {em.State}\n" +
                          $"EmergencyInfo = {em.InfoRowId}.{em.InfoSubRowId}\n" +
                          $"EndTime = {em.EndTime} ({FormatClock(end)})\n" +
                          $"ServerTime = {now}";
                ImGui.SetTooltip(banner != null ? banner + "\n\n" + raw : raw);
            }

            return true;
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
        /// 時間戳的**垃圾值防護**：跟伺服器時間差距超過 30 天就不換算成倒數，改顯示原始整數。
        ///
        /// ⚠️ 這已經不是「猜 epoch」了——基準已經離線證實（見 <see cref="MechaEventDetail"/>），
        /// 留著只是為了擋掉「欄位還沒填」「事件資料被換成別場」這類情況，
        /// 免得畫出一個幾十年後的倒數。
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
        /// ✅ 五個時間戳的基準**已離線證實**是伺服器時間（<c>Framework.GetServerTime()</c>），
        /// 證據見 <see cref="MechaEventDetail"/>。所以這裡拿的是取樣端帶過來的伺服器秒數，
        /// **不是** <c>DateTimeOffset.UtcNow</c>——使用者的系統時鐘偏掉時，用本機時間算出來的
        /// 倒數會是錯的而且毫無徵兆。拿不到伺服器時間（回 0）時退回顯示原始整數。
        /// </summary>
        private static bool DrawEventProgress()
        {
            var detail = MechaOpsMonitor.EventDetail;
            if (detail == null)
                return false;

            // 取樣時的伺服器秒數＋從那時起經過的牆鐘秒數，這樣不會被 250ms 的取樣節流
            // 卡成一格一格跳，也不必在繪製執行緒上呼叫任何遊戲函式。0 = 拿不到。
            var nowServer = detail.ServerTimeNow;

            DrawEventFlagLine(detail, nowServer);

            // 🔴 兩條進度都可能 Max = 0（事件還沒開始，或欄位語意跟預期不同），
            //    除法一律走 DrawProgressRow 裡的防 0 分支。
            DrawProgressRow("Event Progress".Loc(), detail.Progress, detail.ProgressMax);
            DrawProgressRow("Personal".Loc(), detail.PersonalProgress, detail.PersonalProgressMax);

            ImGui.TextUnformatted("Contribution".Loc());
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudWhite, detail.Contribution.ToString());

            DrawDeadline("Event ends in".Loc(), detail.EventEnd, nowServer, "Ended".Loc());
            DrawDeadline("Sign-up closes in".Loc(), detail.RegistrationEnd, nowServer, "Closed".Loc());

            // 傳送視窗的結束時間就是事件開始時間（遊戲自己的 IsTeleportTimeframeOpen 是這樣判的），
            // 所以協助員也看得到一個真的倒數，不必再猜。
            DrawDeadline("Teleport closes in".Loc(), detail.EventStart, nowServer, "Closed".Loc());

            return true;
        }

        /// <summary>
        /// 事件旗標那一行。用的是 <see cref="WKSMechaEventFlag"/>（事件的旗標），
        /// 跟 <see cref="DrawEventStatus"/> 的 <see cref="WKSEventModuleFlag"/> 是兩回事。
        /// </summary>
        private static void DrawEventFlagLine(MechaEventDetail detail, long nowServer)
        {
            var flags = detail.Flags;

            DrawEventFlagChip(flags, WKSMechaEventFlag.IsEventActive, "Active".Loc());
            DrawEventFlagChip(flags, WKSMechaEventFlag.IsParticipating, "Participating".Loc());

            // ⚠️ CS 明文註記：這兩個位元「時間過了也不會被清掉」，所以位元亮著不代表現在還開放。
            // ✅ 兩個判定現在都直接照抄遊戲自己的函式（反組譯貼在 MechaEventDetail 上），
            //    包含「傳送視窗的結束時間就是事件開始時間」——舊版把傳送寫成「無法判定」是錯的。
            DrawStaleableFlagChip("Sign-up".Loc(), detail.IsRegistrationOpen(nowServer));
            DrawStaleableFlagChip("Teleport".Loc(), detail.IsTeleportOpen(nowServer));

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

            // 校準用的原始值。欄位語意與時間基準雖然已經離線證實，實機的實際內容仍未看過，
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
                    $"ServerTime = {nowServer} (sampled {detail.ServerTimeAtSample}, " +
                    $"{(Environment.TickCount64 - detail.SampledTick) / 1000L}s ago)\n" +
                    $"LocalUtc = {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
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
        /// 「時間過了也不會被清掉」的旗標。判定本身在 <see cref="MechaEventDetail"/>
        /// （照抄遊戲自己的函式），這裡只負責上色：
        ///  - <c>true</c>  → 綠色：位元有設，而且時間視窗現在真的還開著。
        ///  - <c>false</c> → 灰色：位元沒設，或位元設著但時間已經過了／還沒到。
        ///  - <c>null</c>  → 黃色加「*」：位元設著但拿不到伺服器時間或時間戳是空的，
        ///                   判定不了。掛 tooltip，不要讓使用者把它當成正常狀態。
        /// </summary>
        private static void DrawStaleableFlagChip(string label, bool? open)
        {
            var color = open switch
            {
                true => ImGuiColors.HealerGreen,
                false => ImGuiColors.DalamudGrey3,
                null => ImGuiColors.DalamudYellow,
            };

            ImGui.TextColored(color, open == null ? label + "*" : label);
            if (open == null && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("The game does not clear this flag when the window closes, " +
                                  "so the bit alone does not mean it is still open.\n" +
                                  "* = the flag is set but the window could not be determined " +
                                  "(no server time, or the timestamp is empty).").Loc());
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
        /// 一行倒數。基準是伺服器時間（已離線證實），拿不到或值不合理時原封不動印出那個 int。
        /// </summary>
        private static void DrawDeadline(string label, int raw, long nowServer, string expiredText)
        {
            ImGui.TextUnformatted(label);
            ImGui.SameLine();

            if (TryGetRemaining(raw, nowServer, out var remaining))
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
                ImGui.SetTooltip(("Counted against the game's own server clock, not your PC clock.\n" +
                                  "If the server time is unavailable or the value is out of range, " +
                                  "the raw integer is shown as-is.\nRaw value: ??").Loc(raw));
            }
        }

        /// <summary>
        /// 算剩餘秒數。基準是**伺服器時間**——遊戲自己判斷這些視窗開不開時比的就是它
        /// （反組譯證據見 <see cref="MechaEventDetail"/>）。
        /// 兩種情況不換算，退回顯示原始整數：
        ///  - <paramref name="nowServer"/> 是 0：這一輪拿不到伺服器時間。
        ///  - 差距超出 <see cref="TimestampSanityWindowSeconds"/>：欄位還沒填或已經是別場的資料。
        /// </summary>
        private static bool TryGetRemaining(int raw, long nowServer, out long remaining)
        {
            remaining = 0;
            if (raw <= 0 || nowServer <= 0)
                return false;

            var delta = raw - nowServer;
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
        /// 目的指示那一行：「已確認 / 讀到的標記數」。
        ///
        /// 🔑 UI 判準：「不知道」本身必須在**列上**看得見，不能藏進 tooltip，
        /// 更不能畫成 0——把「這一輪根本沒讀到」畫成「0 個標記」會直接誤導使用者。
        /// 所以取樣端用 <c>-1</c> 表示未知，這裡畫成灰色的「?」。
        /// tooltip 藏的是「為什麼」，不是「有沒有問題」。
        /// </summary>
        private static bool DrawObjectiveRow()
        {
            var markerCount = MechaObjectiveTracker.MarkerCount;
            var confirmed = MechaObjectiveTracker.ConfirmedCount;
            var pins = MechaObjectiveTracker.PinnedCount;

            // 🔑 這裡要分清楚三種狀態，不能全部畫成 0：
            //   (a) 根本沒有進行中的事件  → 這一行不該存在（不是「0 個目的指示」）
            //   (b) 有事件但一個標記都讀不到 → 「?」，那是真正的「不知道」
            //   (c) 讀到了 N 個            → 「已確認/N」
            // markerCount < 0 代表「這一輪沒去讀」，要靠事件旗標才分得出 (a) 還是 (b)。
            var hasEvent = MechaOpsMonitor.EventFlagsValid
                && (MechaOpsMonitor.EventFlags & WKSEventModuleFlag.HasCurrentEvent) != 0;

            var rowId = MechaOpsMonitor.EventDetail?.DataRowId ?? 0u;

            if (markerCount < 0 && !hasEvent && pins == 0)
                return false;                       // (a)

            // ⚠️ 「一個標記都沒有」不等於「沒事可做」。協助員的目標（小型變異菌床之類）
            //    不見得會有事件地圖標記，舊碼在這裡直接 return false，結果最需要看到
            //    「我這個身份該做什麼」的人反而什麼都看不到。至少要把指示那一行畫出來。
            if (markerCount == 0 && pins == 0)
                return DrawRoleObjective(rowId);

            ImGui.TextUnformatted("Objectives".Loc());
            ImGui.SameLine();

            if (markerCount < 0)
            {
                // (b) 未知 ≠ 0。灰色問號，tooltip 說明是哪一種未知。
                ImGui.TextColored(ImGuiColors.DalamudGrey3, "?");
            }
            else
            {
                var shown = confirmed < 0 ? 0 : confirmed;
                var color = shown >= markerCount ? ImGuiColors.HealerGreen
                    : shown > 0 ? ImGuiColors.DalamudYellow
                    : ImGuiColors.DalamudRed;
                ImGui.TextColored(color, $"{(confirmed < 0 ? "?" : shown.ToString())}/{markerCount}");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("Objectives confirmed in the object table / map markers read from the event.\n" +
                                  "'?' means there is an event running but no marker could be read at all - " +
                                  "that is not the same as zero.\n" +
                                  "By default only confirmed objectives are drawn, so a low number here " +
                                  "explains why the overlay looks empty.").Loc());
            }

            if (pins > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"+{pins}");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Objects you marked yourself from the right-click menu.".Loc());
            }

            // 標記來源。⚠️ 三種來源的可信度不同，UI 上必須分得開：
            //   Scan（預設）    → 灰色「~」：正常狀態，但清單可能含舊標記。刻意不用警告色。
            //   VectorRejected  → 黃色「*」：使用者開了精準模式而它失敗了，這才是異常。
            //   Vector          → 不畫任何東西。
            switch (MechaObjectiveTracker.Source)
            {
                case MechaMarkerSource.Scan:
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudGrey3, "~");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("Markers come from scanning every marker slot. That never dereferences a " +
                                          "game pointer, so it cannot crash - but the list can still contain " +
                                          "leftovers from an earlier stage.\n" +
                                          "Those are drawn with a faded ring and a '?' next to them.\n" +
                                          "The exact list exists but reading it needs an opt-in that can crash the " +
                                          "game; see 'Use the game's own marker list' in the settings.").Loc());
                    }
                    break;

                case MechaMarkerSource.VectorRejected:
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudYellow, "*");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(("You enabled the game's own marker list, but it failed its shape check " +
                                          "this pass, so ICE fell back to scanning all marker slots.\n" +
                                          "The fallback is safe, but positions may include stale entries.").Loc());
                    }
                    break;
            }

            // 這一場事件叫什麼（例如「有害菌床驅除指令」）。
            // 🔑 為什麼放這裡：機甲事件的目標物件在遊戲資料裡可能**根本沒有名字**，
            //    疊加層上只能畫「目標 1／目標 2」。事件名是使用者唯一看得到的
            //    「我在打什麼」，所以放在列上而不是 tooltip 裡。
            //    取不到就整段不畫（不畫成空白，也不猜）。
            var eventName = MechaObjectNames.EventName(rowId);
            if (eventName != null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(eventName);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(("The mecha event currently running.\n" +
                                      "Its objective objects often have no name in the game data at all, in which case " +
                                      "the overlay falls back to numbering them.").Loc());
                }
            }

            DrawRoleObjective(rowId);
            return true;
        }

        /// <summary>
        /// 「你這一場的指示」——依身份取 <c>WKSMechaEventData</c> 裡對應的那一段文字。
        ///
        /// 🔑 <b>為什麼這一段值得佔版面</b>（2026-08-06 使用者實機回報「協助員身份參加，目標不一樣」）：
        /// 兩種身份的目標本來就不同——駕駛員剷除<b>巨型</b>變異菌床，協助員是用宇宙火焰噴射器
        /// 焚燒<b>小型</b>變異菌床、再把灰燼投進野外探測器。疊加層畫的是遊戲標出來的點位，
        /// 但「我到底該做什麼」只有這段文字講得清楚，所以放列上而不是塞進 tooltip。
        ///
        /// ⚠️ 判不出身份時整段不畫——寧可不講，也不要講錯身份的指示。
        /// </summary>
        private static bool DrawRoleObjective(uint rowId)
        {
            var role = MechaOpsMonitor.Role;
            if (role == MechaRole.Unknown)
                return false;

            var text = MechaObjectNames.EventObjectiveText(rowId, role);
            if (text == null)
                return false;

            var roleLabel = role == MechaRole.Pilot ? "Pilot".Loc() : "Ground Support".Loc();

            ImGui.TextColored(ImGuiColors.DalamudViolet, roleLabel);
            ImGui.SameLine();

            // 指示文字本身可能兩行，而視窗是 AlwaysAutoResize——不設換行寬度會把視窗撐得很寬。
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 320f * ImGuiHelpers.GlobalScale);
            ImGui.TextUnformatted(text.Replace("\n", " "));
            ImGui.PopTextWrapPos();

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("What your own role is supposed to do this event.\n" +
                                  "The pilot and the ground support have different objectives, so the objects you " +
                                  "should be going for are not the same ones.\n" +
                                  "Your role is worked out from the mecha actions currently on your hotbar; if it " +
                                  "cannot be worked out, this line is not shown at all.").Loc());
            }

            return true;
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

        /// <summary>
        /// 每個機甲技能一行：名稱 ＋ 冷卻 ＋「打得到幾個／現在蓋到幾個」。
        ///
        /// 涵蓋數字是 <see cref="MechaAoeOverlay"/> 在同一個 ImGui frame 算出來的
        /// （用的就是它真的拿去畫形狀的那個錨點），所以數字跟畫面上的圖形一致。
        /// </summary>
        private static void DrawSkillRows()
        {
            var candidates = MechaOpsMonitor.ActiveCandidates;
            if (candidates.Count == 0)
                return;

            // 把快照的剩餘時間外推到當下，抵銷 250ms 的掃描節流，讓倒數是平滑的。
            var age = (Environment.TickCount64 - MechaOpsMonitor.SnapshotTick) / 1000f;
            var coverage = MechaAoeOverlay.Coverage;

            foreach (var c in candidates)
            {
                // 沿用技能範圍那組個別開關：關掉範圍的技能這裡也不列。
                if (C.MechaAoeSkillToggles.TryGetValue(c.ActionId, out var enabled) && !enabled)
                    continue;

                var remaining = c.RecastTotal > 0f
                    ? Math.Clamp(c.RecastRemaining - age, 0f, c.RecastTotal)
                    : 0f;
                var onCooldown = remaining > 0.05f;

                ImGui.TextColored(onCooldown ? ImGuiColors.DalamudGrey3 : ImGuiColors.DalamudWhite, c.Name);

                if (C.ShowMechaCooldowns)
                {
                    ImGui.SameLine();
                    if (onCooldown)
                        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{remaining:F1}s");
                    else
                        ImGui.TextColored(ImGuiColors.HealerGreen, "Ready".Loc());
                }

                if (C.ShowMechaTargets)
                    DrawCoverageChip(coverage, c.ActionId);
            }
        }

        /// <summary>
        /// 「已涵蓋 / 打得到」那一顆數字。
        ///  - 分母＝這一招的最遠可及範圍（形狀外接圓）內有幾個目標；
        ///  - 分子＝其中真的被形狀蓋到的有幾個。
        /// 兩個相等就是綠色（全中），部分是黃色，一個都沒蓋到是紅色。
        /// 分母是 0（附近沒東西打）時整顆不畫，免得一直掛一個沒意義的 0/0。
        /// </summary>
        private static void DrawCoverageChip(
            IReadOnlyDictionary<uint, (int Covered, int InReach)> coverage,
            uint actionId)
        {
            if (!coverage.TryGetValue(actionId, out var cov) || cov.InReach <= 0)
                return;

            var color = cov.Covered >= cov.InReach ? ImGuiColors.HealerGreen
                : cov.Covered > 0 ? ImGuiColors.DalamudYellow
                : ImGuiColors.DalamudRed;

            ImGui.SameLine();
            ImGui.TextColored(color, $"{cov.Covered}/{cov.InReach}");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("Targets covered / targets within this skill's reach.\n" +
                                  "Green rings on the ground are covered, red ones are not.\n" +
                                  "The ring is the target's hitbox - that is what the game checks, not the centre dot.").Loc());
            }
        }
    }
}
