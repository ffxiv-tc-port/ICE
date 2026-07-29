using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    internal class SafetySettings
    {
        private static bool rejectUnknownYesNo = C.RejectUnknownYesno;
        private static bool delayGrabMission = C.DelayGrabMission;
        private static int delayAmount = C.DelayIncrease;
        private static bool delayCraft = C.DelayCraft;
        private static int delayCraftAmount = C.DelayCraftIncrease;

        public static void Draw()
        {
            if (ImGui.Checkbox("Ignore non-Cosmic prompts".Loc() + "###ICEIgnoreNonCosmicPrompts", ref rejectUnknownYesNo))
            {
                C.RejectUnknownYesno = rejectUnknownYesNo;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                ("Warning! This is a safety feature to avoid joining random parties!\n" +
                "If you you uncheck this, YOU WILL JOIN random party invites.\n" +
                "You have been warned. Disable at your own risk.").Loc()
            );
            if (ImGui.Checkbox("Add delay to mission menu".Loc() + "###ICEAddDelayMissionMenu", ref delayGrabMission))
            {
                C.DelayGrabMission = delayGrabMission;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                ("This is here for safety! If you want to decrease the delay between missions be my guest.\n" +
                "Safety is around... 250? If you're having animation locks you can absolutely increase it higher\n" +
                "Or if you're feeling daredevil. Lower it. I'm not your dad (will tell dad jokes though.").Loc());
            if (delayGrabMission)
            {
                ImGui.SetNextItemWidth(150);
                ImGui.SameLine();
                if (ImGui.SliderInt("ms".Loc() + "###Mission", ref delayAmount, 0, 1000))
                {
                    if (C.DelayIncrease != delayAmount)
                    {
                        C.DelayIncrease = delayAmount;
                        C.SaveDebounced();
                    }
                }
            }
            if (ImGui.Checkbox("Add delay to crafting menu".Loc() + "###ICEAddDelayCraftingMenu", ref delayCraft))
            {
                C.DelayCraft = delayCraft;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                ("This is here for safety! If you want to decrease the delay before turnin be my guest.\n" +
                "Safety is around... 2500? If you're having animation locks you can absolutely increase it higher\n" +
                "Or if you're feeling daredevil. Lower it. I'm not your dad (will tell dad jokes though.").Loc());
            if (delayCraft)
            {
                ImGui.SetNextItemWidth(150);
                ImGui.SameLine();
                if (ImGui.SliderInt("ms".Loc() + "###Crafting", ref delayCraftAmount, 500, 5000))
                {
                    if (C.DelayCraftIncrease != delayCraftAmount)
                    {
                        C.DelayCraftIncrease = delayCraftAmount;
                        C.SaveDebounced();
                    }
                }
            }
            bool jumpIfStuck = C.JumpIfStuck;
            if (ImGui.Checkbox("Jump if stuck during nav movement".Loc() + "###ICEJumpIfStuck", ref jumpIfStuck))
            {
                C.JumpIfStuck = jumpIfStuck;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                ("If you get stuck while navmesh moving, this will allow you to jump after a certain time has passed (3s currently)\n" +
                "NOTE: THIS IS EXPERIMENTAL. IT WORKS, BUT IT STILL LOOKS SUS. IF YOU SEE A POINT AND YOUR STUCK, REPORT IT PLEASE\n" +
                "through the logs function, and give info about it so we can fix it.").Loc());
        }
    }
}
