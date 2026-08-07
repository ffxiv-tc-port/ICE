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
        private static int maxRerolls = C.MaxConsecutiveRerolls;

        public static void Draw()
        {
            ImGui.SetNextItemWidth(120f);
            if (ImGui.InputInt("Stop after this many failed rerolls".Loc() + "###ICEMaxRerolls", ref maxRerolls))
            {
                if (maxRerolls < 0) maxRerolls = 0;
                C.MaxConsecutiveRerolls = maxRerolls;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                ("Stops the plugin after this many consecutive rerolls that found no mission. 0 disables the limit.\n" +
                "Without it an empty candidate pool makes the plugin reroll forever with no indication at all - " +
                "the usual cause is 'disable mission after gold' combined with every currently available mission " +
                "already being golded.\n" +
                "Consider 'Prioritize missions without a gold star' instead: it only reorders, it never removes " +
                "missions from the pool.").Loc()
            );

            bool autoSwitchJob = C.AutoSwitchJobWhenPoolEmpty;
            if (ImGui.Checkbox("Switch class instead of stopping".Loc() + "###ICEAutoSwitchJob", ref autoSwitchJob))
            {
                C.AutoSwitchJobWhenPoolEmpty = autoSwitchJob;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                ("When the reroll limit above is reached, look for the next class in your class priority order \n" +
                "that still has missions without a gold star and switch to it, instead of stopping right away. \n" +
                "Stopping is still what happens once every class has been tried.\n" +
                "It only switches to a class you own a gear set for whose main hand weapon is actually on you - \n" +
                "anything else is skipped with a line in the log.\n" +
                "This equips gear, so it is off by default.").Loc());

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

            // 「剩餘材料算不到金星就收手」。破壞性動作，所以預設是「關閉」，而且中間留了
            // 「只提示」這一檔 —— 判定依賴的計分模型只能靠實機才驗得完，先讓使用者用
            // 「只提示」對一個晚上的記錄檔，確認判斷準了再交給它真的收手。
            var goldUnreachable = (int)C.CraftGoldUnreachable;
            string[] goldUnreachableOptions =
            [
                "Off (keep crafting until materials run out)".Loc(),
                "Notify only".Loc(),
                "Stop the mission".Loc(),
            ];
            ImGui.SetNextItemWidth(320f);
            if (ImGui.Combo("When gold is no longer possible".Loc() + "###ICECraftGoldUnreachable",
                            ref goldUnreachable, goldUnreachableOptions, goldUnreachableOptions.Length))
            {
                C.CraftGoldUnreachable = (GoldUnreachableAction)goldUnreachable;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                ("Crafting missions hand you a fixed, limited amount of materials. Once a few crafts have come out " +
                "poorly the gold star can already be out of reach, and the plugin will still keep crafting until the " +
                "materials are gone.\n" +
                "This estimates the best score still reachable: current rating + (crafts you can still afford) x " +
                "(the highest rating a single item can be worth, which is the gold threshold divided by the number of " +
                "items the mission asks for).\n" +
                "It only applies while the only thing you are still waiting for is gold, i.e. 'Auto turnin' or " +
                "'Turnin at gold' is on for that mission.\n" +
                "Anything it cannot work out (rating not readable, teleporting, critical or time-graded missions) " +
                "counts as 'still possible' and nothing happens.\n" +
                "'Stop the mission' hands over to the routine the plugin already uses when materials run out. " +
                "Be aware of what that routine actually does: it clicks 'Report results' once and then abandons on " +
                "the next tick if the mission is still running, so a silver or bronze you had already earned can " +
                "still be thrown away.").Loc()
            );

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
