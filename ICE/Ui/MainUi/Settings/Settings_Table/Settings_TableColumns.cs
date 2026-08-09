using Dalamud.Interface.Utility.Raii;
using ICE.Config;
using ICE.Utilities.Cosmic_Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.MainUi.Settings.Settings_Table;

public static class Settings_TableColumns
{
    private static string[] missionSortOptions => ["Id", "Name".Loc(), "Cosmo Credits".Loc(), "Lunar Credits".Loc(), "Exp I".Loc(), "Exp II".Loc(), "Exp III".Loc(), "Exp IV".Loc(), "Exp V".Loc(), "Map Location".Loc(), "Class Score".Loc()];

    public static void ColumnSettings()
    {
        int missionSelectedOption = C.TableSortOption;
        if (ImGui.BeginCombo("Sort By".Loc() + "###ICETableSortOption", missionSortOptions[missionSelectedOption]))
        {
            for (int i = 0; i < missionSortOptions.Length; i++)
            {
                bool isSelected = (i == missionSelectedOption);
                if (ImGui.Selectable(missionSortOptions[i], isSelected))
                {
                    missionSelectedOption = i;
                }
                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
                if (missionSelectedOption != C.TableSortOption)
                {
                    C.TableSortOption = missionSelectedOption;
                    C.Save();
                }
            }
            ImGui.EndCombo();
        }

        // 📌「隱藏尚未支援的任務」原本在這裡，2026-08-09 搬到任務頁的篩選列上
        //    （modeSelect_Standard.DrawFilterRow）—— 它是每天都會切的顯示篩選，
        //    不該躺在一個預設收合的設定欄裡。設定鍵與控制項 id 都沒有改，
        //    只是換了畫的位置，所以既有設定照舊生效。

        bool showExtraInfo = C.ShowExtraMissionInfo;
        if (ImGui.Checkbox("Show Extra Mission Info Side-Window".Loc() + "###ICEShowExtraMissionInfo", ref showExtraInfo))
        {
            C.ShowExtraMissionInfo = showExtraInfo;
            C.Save();
        }

        bool autoShowToken = C.Auto_ShowTokens;
        if (ImGui.Checkbox("Auto Hide/Show Planet Tokens".Loc() + "###ICEAutoShowTokens", ref autoShowToken))
        {
            C.Auto_ShowTokens = autoShowToken;
            C.Save();
        }



        bool showManualMode = C.ShowManualMode;
        if (ImGui.Checkbox("Show Manual Mode Column".Loc() + "###ICEShowManualMode", ref showManualMode))
        {
            C.ShowManualMode = showManualMode;
            if (!showManualMode)
            {
                foreach (var mission in C.MissionConfig)
                {
                    mission.Value.ManualMode = false;
                }
            }
            C.Save();
        }
        ImGuiEx.HelpMarker(("Only enable this if you want plan on doing missions YOURSELF. AND NOT AUTOMATING IT. " +
                           "Or if you're letting a different plugin do all the automating of turning in, craftings, gathering... and not letting I.C.E. handle interacting with those plugins").Loc());
    }

    private static bool ApplyToAllClasses = true;
    private static bool ApplyToSpecicClass = false;
    private static int SpecificClass = 8;
    private static int selectedClassIndex = 0;

    private static string[] classOptions => new[]
    {
        "Carpenter (CRP)".Loc(),      // 0
        "Blacksmith (BSM)".Loc(),     // 1
        "Armorer (ARM)".Loc(),        // 2
        "Goldsmith (GSM)".Loc(),      // 3
        "Leatherworker (LTW)".Loc(),  // 4
        "Weaver (WVR)".Loc(),         // 5
        "Alchemist (ALC)".Loc(),      // 6
        "Culinarian (CUL)".Loc(),     // 7
        "Miner (MIN)".Loc(),          // 8
        "Botanist (BTN)".Loc(),       // 9
        "Fisher (FSH)".Loc()          // 10
    };

    private static readonly int[] classIds = new[]
    {
        8,  // Carpenter
        9,  // Blacksmith
        10, // Armorer
        11, // Goldsmith
        12, // Leatherworker
        13, // Weaver
        14, // Alchemist
        15, // Culinarian
        16, // Miner
        17, // Botanist
        18  // Fisher
    };

    private static bool AnyTurnin = true;
    private static bool TurninGold = false;
    private static bool TurninSilver = false;
    private static bool TurninBronze = false;

    public static void GeneralMissionSettings()
    {
        bool onlyGrabMission = C.OnlyGrabMission;
        if (ImGui.Checkbox("Only grab mission".Loc() + "###ICEOnlyGrabMission", ref onlyGrabMission))
        {
            C.OnlyGrabMission = onlyGrabMission;
            C.Save();
        }

        bool removeGold = C.RemoveAfterGold;
        if (ImGui.Checkbox("Remove Mission Upon Gold Completion".Loc() + "###ICERemoveAfterGold", ref removeGold))
        {
            C.RemoveAfterGold = removeGold;
            C.Save();
        }

        // 緊急任務例外。縮排一格掛在上面那項底下，上面關著時整項變灰（它只在那時有意義）。
        using (ImRaii.Disabled(!removeGold))
        {
            ImGui.Indent();
            bool keepCritical = C.RemoveAfterGoldKeepCritical;
            if (ImGui.Checkbox("Keep Critical missions enabled".Loc() + "###ICERemoveAfterGoldKeepCritical", ref keepCritical))
            {
                C.RemoveAfterGoldKeepCritical = keepCritical;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("?");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(("Critical (emergency) missions stay in the pool even after you gold them.\n" +
                                  "Those missions only show up during a Red Alert - magnetic storm, meteor shower " +
                                  "or spore mist - so once the golded ones are removed there can be nothing left " +
                                  "to take when an alert actually starts.\n" +
                                  "Off by default: behaviour is unchanged unless you turn this on.").Loc());
            }
            ImGui.Unindent();
        }

        ImGui.Checkbox("Stop after current mission".Loc() + "###ICEGeneralStopAfterCurrent", ref Mission_Settings.StopAfterCurrent);

        DrawRelicTurninCheckbox("GeneralSetting");

        // ── 快速套用回報設定（2026-08-09：彈窗改成就地展開）────────────────────────
        // 使用者點名這個彈窗難用：它蓋在任務表上、點到旁邊就整個關掉、
        // 而且按下「套用」之前完全不知道會動到幾個任務 —— 一鍵改掉幾百筆設定卻沒有預覽。
        // ⇒ ①改成就地展開的收合區塊（不會蓋住別的東西、點旁邊也不會消失）
        //   ②「套用」上面多一行：目前這組條件會套用到幾個任務。
        //
        // ⚠️ 篩選條件只有一份（QuickApplyTargetCount / 實際套用共用 IsQuickApplyTarget），
        //    預覽數字與真正會被改到的筆數不可能對不上。
        // 📌 套用之後區塊**不會**自己收起來（原本的彈窗會關掉）：留著才看得到套用後的筆數，
        //    也方便接著換一個職業再套一次。
        if (ImGui.TreeNode("Quick Apply Turnins".Loc() + "###ICEQuickApplyTurnins"))
        {
            if (ImGui.RadioButton("Apply to all classes".Loc() + "###ICEApplyToAllClasses", ApplyToAllClasses))
            {
                ApplyToAllClasses = true;
                ApplyToSpecicClass = false;
            }

            if (ImGui.RadioButton("Apply to specific class".Loc() + "###ICEApplyToSpecificClass", ApplyToSpecicClass))
            {
                ApplyToAllClasses = false;
                ApplyToSpecicClass = true;
            }
            // 🔴 一定要指定寬度：這個下拉原本住在彈出視窗裡（寬度由彈窗決定），
            //    改成畫在表格欄位裡之後，沒指定寬度的控制項會回頭去問「欄位有多寬」，
            //    而這個表格是依內容自動決定欄寬的 —— 兩邊互相參照會讓欄寬在每一幀之間跳動。
            //    用字型大小當基準，使用者調 UI 縮放時會跟著縮放。
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12f);
            if (ImGui.Combo("##ClassSelector", ref selectedClassIndex, classOptions, classOptions.Length))
            {
                // Update SpecificClass when selection changes
                SpecificClass = classIds[selectedClassIndex];
                IceLogging.Debug($"Selected class: {classOptions[selectedClassIndex]}, ID: {SpecificClass}");
            }
            ImGui.Separator();
            ImGui.Text("Select Turnin Options".Loc());
            ImGui.Dummy(new Vector2(0, 2));

            if (ImGui.Checkbox("Auto".Loc() + "###ICEQuickTurninAuto", ref AnyTurnin))
            {
                if (AnyTurnin)
                {
                    TurninGold = false;
                    TurninSilver = false;
                    TurninBronze = false;

                    AnyTurnin = true;
                }
                else
                {
                    if (!(TurninBronze && TurninSilver && TurninGold))
                    {
                        AnyTurnin = true;
                    }
                }

                // 📌 這裡原本有一行 C.Save()，已移除：這一格與下面的金／銀／銅三格都是
                //    **純畫面暫存**（static 欄位，不在設定檔裡），要等按下「套用」才會
                //    寫進任何一個任務的設定。只有這一格多了 Save，另外三格都沒有 ——
                //    典型的複製貼上殘留。它存的是「當下的設定」，與這個勾選項無關，
                //    所以拿掉不會少存任何東西。
            }
            ImGuiEx.HelpMarker("This option will strive to get the best result, but will turn in any result if necessary without stopping.".Loc());

            ImGui.Separator();

            if (ImGui.Checkbox("Gold".Loc() + "###ICEQuickTurninGold", ref TurninGold))
            {
                if (AnyTurnin && TurninGold)
                    AnyTurnin = false;

            }
            if (ImGui.Checkbox("Silver".Loc() + "###ICEQuickTurninSilver", ref TurninSilver))
            {
                if (AnyTurnin && TurninSilver)
                    AnyTurnin = false;

            }
            if (ImGui.Checkbox("Bronze".Loc() + "###ICEQuickTurninBronze", ref TurninBronze))
            {
                if (AnyTurnin && TurninBronze)
                    AnyTurnin = false;

            }

            if (!AnyTurnin && !TurninGold && !TurninSilver && !TurninBronze)
                AnyTurnin = true;

            ImGui.Separator();

            // 按下去之前先講清楚會動到幾筆。這一行刻意畫在按鈕正上方而不是塞進 tooltip：
            // 「會改掉幾百個任務的設定」屬於按下去之前一定要看到的資訊，不是起疑才查的。
            ImGui.TextUnformatted("Will apply to ?? missions".Loc(QuickApplyTargetCount()));

            if (ImGui.Button("Apply".Loc() + "###ICEQuickApplyConfirm"))
            {
                var amountApplied = 0;
                foreach (var mission in C.MissionConfig)
                {
                    if (!IsQuickApplyTarget(mission.Key))
                        continue;

                    var config = mission.Value;
                    config.AutoTurnin = AnyTurnin;
                    config.TurninGold = TurninGold;
                    config.TurninSilver = TurninSilver;
                    config.TurninBronze = TurninBronze;
                    amountApplied += 1;
                }
                C.SaveDebounced();

                Notify.Success("Applied settings to: ?? missions, just for you buddy.".Loc(amountApplied));
            }

            ImGui.TreePop();
        }
    }

    /// <summary>
    /// 「宇宙工具完成就自動繳交」的勾選項。
    /// 🔴 這個開關在畫面上有**兩個**入口（任務設定欄、宇宙工具設定欄），
    /// 原本是兩份逐字重複的複製碼 —— 連「勾起來時順手關掉限定任務模式」這個副作用
    /// 與整段 tooltip 都各寫了一次。兩份會漂移，而漂移的失敗形式是
    /// 「同一個開關在 A 處會關掉限定任務、在 B 處不會」這種沒人查得出來的怪 bug。
    /// ⚠️ <paramref name="idSuffix"/> 逐字沿用原本兩處的控制項 id 尾巴
    /// （<c>GeneralSetting</c> / <c>RelicGrind</c>）：ImGui 用 id 認控制項，
    /// 兩個入口必須是不同 id，否則同一幀畫兩次會互相搶狀態。
    /// </summary>
    public static void DrawRelicTurninCheckbox(string idSuffix)
    {
        bool relicTurnin = C.TurninRelic;
        if (ImGui.Checkbox("Turnin if relic is complete".Loc() + "##RelicTurnin_" + idSuffix, ref relicTurnin))
        {
            if (relicTurnin)
                C.GrindProvisionals = false;

            C.TurninRelic = relicTurnin;
            C.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("?");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(("THIS IS YOUR HEADS UP ON HOW THIS WORKS. If I change this in the future, this tooltip will also change.\n" +
                             "1: This will check for your current CLASS [not menu class, actual current class] for relic turnin.\n" +
                             "2: You must not have the tool eqipped for this to run full auto. \n" +
                             "\t- This is due to the fact that I cba coding this in at this time. (might change my mind in the future *shrugs*)\n" +
                             "3: This will take prio over \"Stop @ Relic Turnin\", in the sense that if you have both enabled, it will turnin vs stop. And continue about it's day\n" +
                             "4: If you're on a crafting class, it will return you back to the stop you were crafting post turnin. \n" +
                             "\t- This is optional, you can disable it at your own free will, I just like this so I can just go back to an isolated area of my choosing").Loc());
        }
    }

    /// <summary>
    /// 「快速套用回報設定」會不會動到這個任務。
    /// 🔴 預覽筆數與實際套用**只能有這一份判定** —— 兩邊各寫一次的話，
    /// 條件哪天改了只改一邊，失敗形式是「預覽說 120 筆、實際改了 300 筆」這種
    /// 使用者按下去才發現、而且無法復原的靜默錯誤。
    /// 判定內容與改成收合區塊之前逐字相同：查不到任務資料的跳過、
    /// 選了特定職業就只算該職業的、計時計分（ScoreTimeRemaining）的任務永遠跳過。
    /// </summary>
    private static bool IsQuickApplyTarget(uint missionId)
    {
        if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var sheetInfo))
            return false;

        if (ApplyToSpecicClass && !sheetInfo.Jobs.Contains((uint)SpecificClass))
            return false;

        if (sheetInfo.Attributes.HasFlag(MissionAttributes.ScoreTimeRemaining))
            return false;

        return true;
    }

    private static int QuickApplyTargetCount()
    {
        var count = 0;
        foreach (var mission in C.MissionConfig)
        {
            if (IsQuickApplyTarget(mission.Key))
                count += 1;
        }
        return count;
    }
}
