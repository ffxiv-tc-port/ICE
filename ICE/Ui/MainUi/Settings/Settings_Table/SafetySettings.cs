using Dalamud.Interface.Colors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    internal class SafetySettings
    {
        // 📌 這裡原本有六個 `private static x = C.某鍵;` 欄位當 widget 的暫存值。
        //    static 初始化器**一輩子只跑一次**（型別第一次被碰到的那一刻），之後就再也
        //    不看設定了 —— 而這個類別第一次被碰到的時機不保證在設定載入／遷移之後。
        //    症狀不是「壞掉」而是「顯示舊值」：設定頁畫的是快照當下的值，使用者一動控件
        //    就把那個舊值原封不動寫回設定，已經遷移好的值被靜默還原。
        // ⇒ 全部改成在使用點當場讀 C（同檔 autoSwitchJob / jumpIfStuck 一直都是這個寫法）。
        //    寫回的動作逐字不變：讀 local → 控件改 local → 寫回 C.某鍵 → Save。
        //    設定鍵、控制項 id、Save/SaveDebounced 的選擇全部沒動。

        public static void Draw()
        {
            var maxRerolls = C.MaxConsecutiveRerolls;
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

            bool rejectUnknownYesNo = C.RejectUnknownYesno;
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

            DrawUnexpectedYesnoSetting();
            bool delayGrabMission = C.DelayGrabMission;
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
                var delayAmount = C.DelayIncrease;
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
            bool delayCraft = C.DelayCraft;
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
                var delayCraftAmount = C.DelayCraftIncrease;
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

        /// <summary>
        /// 「其他確認框」的三檔開關。
        /// </summary>
        /// <remarks>
        /// 上面那個「Ignore non-Cosmic prompts」只蓋接任務與放棄任務兩處，其餘幾個地方
        /// （修理、換套裝、收藏品、好運道、繳交研究材料）本來是不看文字一律按下確定。<br/>
        /// 🔴 分成獨立的三檔而不是併進上面那個核取方塊：那個核取方塊<b>預設就是開的</b>，
        /// 把新檢查掛上去等於未經同意改掉所有既有使用者的行為。<br/>
        /// 📌 中間留「只記錄」這一檔的理由與「無法取得金星時」那個選項相同 ——
        /// 比對基準是離線從 Addon 表推出來的，實機到底長什麼樣沒人證明過。
        /// 先用「只記錄」跑一晚、對著記錄檔確認判斷準了，再交給它真的按取消。
        /// </remarks>
        private static void DrawUnexpectedYesnoSetting()
        {
            var unexpectedYesno = (int)C.UnexpectedYesno;
            string[] unexpectedYesnoOptions =
            [
                "Always confirm (current behaviour)".Loc(),
                "Log the text only".Loc(),
                "Cancel anything unrecognised".Loc(),
            ];
            ImGui.SetNextItemWidth(320f);
            if (ImGui.Combo("Other confirmation dialogs".Loc() + "###ICEUnexpectedYesno",
                            ref unexpectedYesno, unexpectedYesnoOptions, unexpectedYesnoOptions.Length))
            {
                C.UnexpectedYesno = (UnexpectedYesnoAction)unexpectedYesno;
                C.Save();
            }
            ImGuiEx.HelpMarker(
                ("The option above only covers taking and abandoning missions. Everywhere else the plugin " +
                "presses Yes on whatever confirmation dialog happens to be open without reading it: repairing, " +
                "switching gear sets, collectables while fishing, the Cosmic Fortune wheel and handing in " +
                "research materials.\n" +
                "'Always confirm' is the behaviour the plugin has always had - the dialog text is not even read, " +
                "so it costs nothing.\n" +
                "'Log the text only' changes nothing at all. It still presses Yes, it just writes the text of " +
                "every dialog it did not recognise into the log, so you can check the matching is right before " +
                "you let it act on it.\n" +
                "'Cancel anything unrecognised' presses No instead, and writes a line into the log. Pressing No " +
                "rather than doing nothing is deliberate: a stray dialog left open would block the plugin " +
                "forever.\n" +
                "What counts as 'expected' is read from the game's own text data, so it follows your client " +
                "language automatically.").Loc());

            if (C.UnexpectedYesno == UnexpectedYesnoAction.AlwaysConfirm)
                return;

            // 「不知道」本身要在列上看得見：某個情境沒有比對基準時，這一檔對它就是沒有作用，
            // 而那件事只藏在 tooltip 裡的話跟「有在保護」長得一模一樣。
            var unguarded = new List<string>();
            foreach (var situation in Enum.GetValues<YesnoSituation>())
            {
                if (!YesnoGuard.HasMarkers(situation))
                    unguarded.Add(SituationLabel(situation));
            }

            if (unguarded.Count > 0)
            {
                ImGuiEx.TextWrapped(ImGuiColors.DalamudOrange,
                    "No text to match against for: ??. Those are only logged, never cancelled.".Loc(string.Join("、", unguarded)));
            }
        }

        private static string SituationLabel(YesnoSituation situation) => situation switch
        {
            YesnoSituation.Repair => "Repairing".Loc(),
            YesnoSituation.GearsetMainHand => "Gear set switching".Loc(),
            YesnoSituation.FishingCollect => "Collectables".Loc(),
            YesnoSituation.Lottery => "Cosmic Fortune".Loc(),
            YesnoSituation.RelicTurnin => "Research material handover".Loc(),
            _ => situation.ToString(),
        };
    }
}
