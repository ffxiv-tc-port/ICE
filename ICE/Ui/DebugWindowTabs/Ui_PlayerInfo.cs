using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;
using System.Reflection;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Ui_PlayerInfo
    {
        public static void Draw()
        {
            ImGui.Text("Need to actually put the player info here. It got lost");
            ImGui.Spacing();
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"Player Position: X:{Player.Position.X:N2}, Y:{Player.Position.Y:N2}, Z:{Player.Position.Z:N2}");
            ImGui.SameLine();
            if (ImGui.Button("Copy Vector2"))
            {
                ImGui.SetClipboardText($"{Player.Position.X:N2}f, {Player.Position.Z:N2}f");
            }
            ImGui.SameLine();
            if (ImGui.Button("Copy Vector3"))
            {
                ImGui.SetClipboardText($"{Player.Position.X:N2}f, {Player.Position.Y:N2}f, {Player.Position.Z:N2}f");
            }
            ImGui.Text($"Job: {Player.Job}");
            ImGui.Text($"JobId: {Player.JobId}");
            ImGui.Text($"Current Territory/ZoneId: {Player.Territory}");
            if (Svc.Targets.Target != null)
            {
                var currentTarget = Svc.Targets.Target;
                if (ImGui.Button($"Name: {currentTarget.Name}"))
                {
                    ImGui.SetClipboardText(currentTarget.Name.ToString());
                }
                if (ImGui.Button($"Id: {currentTarget.BaseId}"))
                {
                    ImGui.SetClipboardText(currentTarget.BaseId.ToString());
                }
                if (ImGui.Button($"Position: X: {currentTarget.Position.X:N2}, Y: {currentTarget.Position.Y:N2}, Z: {currentTarget.Position.Z:N2}"))
                {
                    ImGui.SetClipboardText($"{currentTarget.Position.X:N2}f, {currentTarget.Position.Y:N2}f, {currentTarget.Position.Z:N2}f");
                }
                ImGui.Text($"Distance: {Player.DistanceTo(currentTarget):N2}");
            }

            ImGui.Text($"Items on person: ");
            foreach (var item in ConsumableInfo.Food)
            {
                if (PlayerHelper.GetItemCount(item.Id, out var count) && count > 0)
                    ImGui.Text($"{item.Name} | {item.Id}");
            }
            if (ImGui.Button("Use Gathering Food"))
            {
                P.TaskManager.Enqueue(() => Task_Gather.UseFood());
            }

            ClassInfo();
        }

        private static unsafe void ClassInfo()
        {
            ImGui.Text("Manipulation Check");
            Dictionary<uint, uint> ManipClassInfo = new()
            {
                [8] = 4574,
                [9] = 4575,
                [10] = 4576,
                [11] = 4577,
                [12] = 4578,
                [13] = 4579,
                [14] = 4580,
                [15] = 4581,
            };

            foreach (var job in ManipClassInfo)
            {
                var isUnlocked = ActionManager.Instance()->GetActionStatus(ActionType.Action, job.Value, checkRecastActive: false, checkCastingActive: false) is 574 or 586;
                ImGui.Text($"JobId: {job.Key} | Unlocked: {isUnlocked}");
                // 573 | Not unlocked??? 
                // 574 | Is unlocked
                // 586 | Is unlocked for current class/ready to use
            }

            ImGui.Separator();
            PlayerHelper.UpdateHasManip();

            ImGui.Text($"Custom Is Busy: {PlayerHelper.CustomIsBusy}");
            foreach (var job in PlayerHelper.ManipClassInfo)
            {
                ImGui.Text($"JobID: {job.Key} | HasUnlocked: {job.Value.HasUnlocked}");
            }
        }
    }
}
