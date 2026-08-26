using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using ICE.Utilities.MechaOps;

namespace ICE.Ui.DebugWindowTabs;

/// <summary>
/// 「機甲事件錄製」的操作面板（<c>/ice d</c>）。
///
/// 🔴 這個檔案完全不碰遊戲的原生結構，只讀 <see cref="MechaEventRecorder"/> 與
/// <see cref="MechaOpsMonitor"/> 發布的純值狀態，並設定錄製器的幾個記憶體內旗標。
/// 純顯示＋開關，零自動化。
///
/// 📌 錄製開關<b>刻意不寫進設定檔</b>：這是高流量的 debug 診斷，
/// 存起來的話使用者忘記關就會一直灌 log。重開遊戲一律回到關閉。
/// </summary>
internal static class Ui_MechaRecorder
{
    public static void Draw()
    {
        ImGuiEx.IconWithText(FontAwesomeIcon.Video, "Mecha Event Recorder".Loc());
        ImGui.Dummy(new Vector2(0, 5));

        ImGui.TextWrapped(
            ("Records everything ICE's mecha target logic reads, from the start of an event to the end, "
             + "so the raw inputs can be replayed offline.\n"
             + "Read only - it never casts, never moves you, and never signs you up for anything.").Loc());
        ImGui.Dummy(new Vector2(0, 5));

        var enabled = MechaEventRecorder.Enabled;
        if (ImGui.Checkbox("Enable Mecha Event Recording".Loc() + "###ICEMechaRecEnable", ref enabled))
            MechaEventRecorder.Enabled = enabled;

        ImGuiEx.TextV(ImGuiColors.DalamudGrey,
            "Leave this on and run one mecha event - the log will then have the full record.".Loc());
        ImGuiEx.TextV(ImGuiColors.DalamudGrey,
            "Not saved to the config file - it is always off again after a restart.".Loc());

        ImGui.Dummy(new Vector2(0, 5));

        using (ImRaii.Disabled(!enabled))
        {
            var force = MechaEventRecorder.ForceSession;
            if (ImGui.Checkbox("Record Even Without An Event".Loc() + "###ICEMechaRecForce", ref force))
                MechaEventRecorder.ForceSession = force;
            Hint(("Recording normally starts by itself once an event signal shows up, and stops about "
                  + "20 seconds after it goes away.\n"
                  + "Turn this on to record right now regardless - useful for checking that the feature "
                  + "works at all without waiting for an event.").Loc());

            var dump = MechaEventRecorder.DumpObjects;
            if (ImGui.Checkbox("Dump The Object Table".Loc() + "###ICEMechaRecDump", ref dump))
                MechaEventRecorder.DumpObjects = dump;
            Hint(("One line per nearby object: id, data id, kind, name (may be empty), position, distance, "
                  + "and whether ICE would filter it out and by which rule.\n"
                  + "This is the bulkiest part of the record - turn it off to keep only the summary "
                  + "and the difference lists.").Loc());

            var radius = MechaEventRecorder.SweepRadius;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderFloat("Recording Sweep Radius".Loc() + "###ICEMechaRecRadius", ref radius, 20f, 200f, "%.0f"))
                MechaEventRecorder.SweepRadius = radius;
            Hint(("How far out objects get recorded, in metres.\n"
                  + "Deliberately wider than ICE's own target range, so the record also shows "
                  + "what ICE left out.").Loc());
        }

        ImGui.Separator();
        DrawSessionState();

        ImGui.Separator();
        DrawLiveState();

        ImGui.Separator();
        ImGui.TextWrapped(
            ("Everything is written at Information level with the [MechaRec] prefix.\n"
             + "The per object detail lines go to dalamud.log only - ICE's own log view keeps just "
             + "3000 entries and would lose everything else.").Loc());
    }

    /// <summary>ICE 既有的提示樣式：同一行一個灰色「?」，滑過去才顯示長文字。</summary>
    private static void Hint(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("?");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    private static void DrawSessionState()
    {
        ImGuiEx.TextV(ImGuiColors.DalamudViolet, "Recording State".Loc());

        if (MechaEventRecorder.SessionActive)
        {
            ImGuiEx.TextV(ImGuiColors.HealerGreen, "Recording".Loc());
            ImGui.Text($"{"Elapsed".Loc()}: {MechaEventRecorder.SessionSeconds:F0}s");
        }
        else
        {
            ImGuiEx.TextV(ImGuiColors.DalamudGrey, MechaEventRecorder.Enabled
                ? "Armed - waiting for an event".Loc()
                : "Off".Loc());

            if (MechaEventRecorder.LastStopReason.Length > 0)
                ImGuiEx.TextV(ImGuiColors.DalamudGrey, $"{"Last stop".Loc()}: {MechaEventRecorder.LastStopReason}");
        }

        ImGui.Text($"{"Snapshots".Loc()}: {MechaEventRecorder.SnapshotCount}"
                 + $" | {"Casts".Loc()}: {MechaEventRecorder.CastCount}"
                 + $" | {"Vanished".Loc()}: {MechaEventRecorder.VanishCount}");
        ImGui.Text($"{"Tracked objects".Loc()}: {MechaEventRecorder.TrackedCount}"
                 + $" | {"Interactions".Loc()}: {MechaEventRecorder.InteractionCount}");
    }

    /// <summary>
    /// 目前讀得到什麼。🔑 <b>「讀不到」本身要在畫面上看得見</b>：
    /// 使用者要能在事件開始前就確認「錄製器現在到底看不看得到東西」，
    /// 而不是跑完一整場才發現 log 是空的。
    /// </summary>
    private static void DrawLiveState()
    {
        ImGuiEx.TextV(ImGuiColors.DalamudViolet, "Live State".Loc());

        var inZone = PlayerHelper.IsInCosmicZone() && Player.Available;
        ImGuiEx.TextV(inZone ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey,
            $"{"In a cosmic zone".Loc()}: {(inZone ? "OK" : "no")}");

        ImGui.Text($"{"Event flags".Loc()}: "
                 + (MechaOpsMonitor.EventFlagsValid
                     ? $"0x{(uint)MechaOpsMonitor.EventFlags:X} ({MechaOpsMonitor.EventFlags})"
                     : "?"));

        var detail = MechaOpsMonitor.EventDetail;
        if (detail == null)
        {
            // ⚠️ 「讀不到」跟「進度是 0」不是同一件事，所以不要畫成 0。
            ImGuiEx.TextV(ImGuiColors.DalamudGrey, $"{"Event".Loc()}: ?");
        }
        else
        {
            var name = MechaObjectNames.EventName(detail.DataRowId);
            ImGui.Text($"{"Event".Loc()}: {detail.DataRowId} {name ?? "?"}");
            ImGui.Text($"{"Progress".Loc()}: {detail.Progress}/{detail.ProgressMax}"
                     + $" | {"Personal".Loc()}: {detail.PersonalProgress}/{detail.PersonalProgressMax}"
                     + $" | {"Contribution".Loc()}: {detail.Contribution}");
        }

        ImGui.Text($"{"Role".Loc()}: " + MechaOpsMonitor.Role switch
        {
            MechaRole.Pilot => "Pilot".Loc(),
            MechaRole.GroundSupport => "Ground Support".Loc(),
            _ => "Cannot tell".Loc(),
        });

        ImGui.Text($"{"Mecha skills".Loc()}: {MechaOpsMonitor.ActiveCandidates.Count}"
                 + $" | {"Markers".Loc()}: {CountText(MechaObjectiveTracker.MarkerCount)}"
                 + $" | {"Confirmed".Loc()}: {CountText(MechaObjectiveTracker.ConfirmedCount)}"
                 + $" | {"ICE targets".Loc()}: {MechaOpsMonitor.ActiveTargets.Count}");

        // 🔑 這是使用者最可能踩到的坑：疊加層總開關預設是關的。
        //    錄製會自己強制取樣，所以這裡是「說明」不是「警告」——但一定要講清楚。
        if (MechaEventRecorder.Enabled && !C.ShowMechaAoeOverlay)
        {
            ImGui.PushTextWrapPos(0);
            ImGuiEx.TextV(ImGuiColors.DalamudYellow,
                ("The mecha overlay is off. Recording turns the sampling on by itself, so the record is "
                 + "still complete - nothing extra is drawn on screen and none of your settings are changed.").Loc());
            ImGui.PopTextWrapPos();
        }
    }

    /// <summary><c>-1</c> ＝「這一輪根本沒讀到」，跟 0 不是同一件事。</summary>
    private static string CountText(int v) => v < 0 ? "?" : v.ToString();
}
