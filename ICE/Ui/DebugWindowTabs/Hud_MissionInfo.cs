using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Ui;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Hud_MissionInfo
    {
        /// <summary>讀不到時畫這個 —— 刻意不畫 0，分數 0 本身是有效值。</summary>
        private const string UnknownMark = "?";

        /// <summary>有值就照畫，讀不到就畫灰色的「?」。</summary>
        /// <remarks>
        /// 🔴 為什麼不能沿用 <c>ImGui.Text($"{值}")</c>：那幾個 getter 是 <c>uint?</c>，
        /// 而 <c>$"{(uint?)null}"</c> 渲染出來是<b>空字串</b> —— 表格上是一個空白格，
        /// 看起來像「這個欄位不適用」而不是「這一幀讀不到」。「不知道」本身要在列上看得見。
        /// </remarks>
        private static void DrawValueOrUnknown(string? value)
        {
            if (value == null)
                ImGui.TextColored(ImGuiColors.DalamudGrey, UnknownMark);
            else
                ImGui.Text(value);
        }

        public static unsafe void Draw()
        {
            if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var x) && x.IsAddonReady)
            {
                // 🔴🔴 x.IsAddonReady 只驗 ULD 節點樹（IsVisible ＋ UldManager.LoadedState==Loaded
                //    ＋ IsFullyLoaded），它<b>完全沒有驗 AtkValues</b> —— 而這幾個分數 getter 讀的正是
                //    AtkValues 裡的字串指標。按下「回報結果」／「放棄任務」之後那扇窗有「正在關閉中」
                //    的幾幀，IsAddonReady 三關照樣全過，此時去讀 AtkValues 字串就是攔不到的
                //    AccessViolationException（AVE 在 .NET Core 是 corrupted-state exception，
                //    try/catch 與任何例外隔離都無效）。
                //    🔑 ICE 既有的讀窗守衛就是 AddonPressGuard.IsHeld —— Task_TurninMission 與
                //    Task_AbandonMission 讀窗前都先問它（它的說明逐字寫著「被擋的那幾幀連文字都不去讀」），
                //    只有這個除錯分頁漏了沒問，而它是每幀無條件讀。
                var valuesReadable = !AddonPressGuard.IsHeld(
                    "WKSMissionInfomation", x, AddonPressGuard.WksMissionExitPressKey);

                // 🔑 讀不到一律留 null 讓下游畫「?」，不要 `?? 0` —— 分數 0 本身是有效值，
                //    畫 0 會讓使用者以為「真的是 0 分」。同 repo 的 OverlayWindow 早就是這個做法
                //    （CurrentScore?.ToString() ?? UnknownMark），原本只有這個分頁寫 `?? 0`。
                var currentScore = valuesReadable ? x.CurrentScore?.ToString() : null;
                var silverScore = valuesReadable ? x.SilverScore?.ToString() : null;
                var goldScore = valuesReadable ? x.GoldScore?.ToString() : null;

                var isAddonReady = AddonHelper.IsAddonActive("WKSMissionInfomation");
                ImGui.Text($"Addon Ready: {isAddonReady}");
                if (isAddonReady && valuesReadable)
                {
                    ImGui.Text($"Node Text: {AddonHelper.GetNodeText("WKSMissionInfomation", 27)}");
                }

                ImGuiTableFlags tableFlags = ImGuiTableFlags.RowBg |
                                             ImGuiTableFlags.Borders |
                                             ImGuiTableFlags.SizingFixedFit |
                                             ImGuiTableFlags.Resizable |           // Allow column resizing
                                             ImGuiTableFlags.Reorderable |         // Allow column reordering
                                             ImGuiTableFlags.Hideable;             // Allow hiding columns via right-click

                if (ImGui.BeginTable("WKSMissionInfomationAddon_Table", 2, tableFlags))
                {
                    ImGui.TableSetupColumn("###Info", ImGuiTableColumnFlags.WidthFixed, 150);
                    ImGui.TableSetupColumn("###UiInfo", ImGuiTableColumnFlags.WidthFixed, 100);

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Current Score:".Loc());

                    ImGui.TableNextColumn();
                    DrawValueOrUnknown(currentScore);

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Silver Score:".Loc());

                    ImGui.TableNextColumn();
                    DrawValueOrUnknown(silverScore);

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Gold Score:".Loc());

                    ImGui.TableNextColumn();
                    DrawValueOrUnknown(goldScore);

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Critical Value:".Loc());

                    ImGui.TableNextColumn();
                    // 🔴 CriticalScore 是 uint?，$"{null}" 會渲染成空字串（看起來像「不適用」）。
                    //    讀得到但解析不出可信數字時，一併印出面板原字串 —— CriticalScoreRaw 存在的
                    //    理由就是診斷：光看 null 分不出「面板還沒載入」與「載入了但格式跟預期不一樣」。
                    if (!valuesReadable)
                        ImGui.TextColored(ImGuiColors.DalamudGrey, UnknownMark);
                    else if (x.CriticalScore is uint criticalScore)
                        ImGui.Text($"{criticalScore}");
                    else
                        ImGui.TextColored(ImGuiColors.DalamudGrey,
                            $"{UnknownMark}（面板原字串：「{x.CriticalScoreRaw ?? "尚未載入"}」）");

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Button("Cosmo Pouch".Loc()))
                    {
                        x.CosmoPouch();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Button("Cosmo Crafting Log".Loc()))
                    {
                        x.CosmoCraftingLog();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Button("Steller Reduction"))
                    {
                        x.StellerReduction();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    // 🔴 偵錯視窗的手動按鈕，按法 key 與 Task_TurninMission／Task_AbandonMission **同一把**：
                    //    手動按下回報之後那扇窗開始關閉，排程下一個 tick 接手按放棄就是攔不到的存取違規。
                    //    守衛擋下時這次點擊不動作（會寫一行 Information 說明原因）。
                    if (ImGui.Button("Report".Loc())
                        && AddonPressGuard.TryBeginPress("任務資訊：手動回報結果", "WKSMissionInfomation", x, AddonPressGuard.WksMissionExitPressKey))
                    {
                        x.Report();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Button("Abandon".Loc())
                        && AddonPressGuard.TryBeginPress("任務資訊：手動放棄任務", "WKSMissionInfomation", x, AddonPressGuard.WksMissionExitPressKey))
                    {
                        x.Abandon();
                    }

                    // 🔴 原本直接 wks->Scores / wks->FishingBait，沒有判空。
                    //    WKSManager 在宇宙探索內容以外是 null，直接解參考＝攔不到的
                    //    AccessViolationException（corrupted-state exception，try/catch 無效）。
                    //    🔑 讀不到就整段畫「?」，不要畫 0 —— 分數 0 與餌 0 都是有效值。
                    var wks = WKSManager.Instance();

                    if (wks == null)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text("Scores".Loc());
                        ImGui.TableNextColumn();
                        ImGui.Text("?（讀不到 WKSManager，不在宇宙探索內容裡）");

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text("Bait:".Loc());
                        ImGui.TableNextColumn();
                        ImGui.Text("?");
                    }
                    else
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text("Score 1".Loc());
                        ImGui.TableNextColumn();
                        ImGui.Text($"{wks->Scores.Length}");

                        int score = 0;

                        foreach (var item in wks->Scores)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.Text($"Score: [{score}]");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{wks->Scores[score]}");
                            score += 1;
                        }

                        var currentlyEquippped = wks->FishingBait | 0;

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text("Bait:".Loc());
                        ImGui.TableNextColumn();
                        ImGui.Text($"{currentlyEquippped}");
                    }


                    ImGui.EndTable();
                }

                // 目標進度的原始資料傾印。之後若要把來源從「走訪節點樹」改成寫死的
                // AtkValue 索引（比較便宜），就靠這裡的輸出來校準 —— 不要用猜的。
                // 🔴 這段會走訪 addon 的節點樹與 AtkValues（MissionObjectiveReader 自己有判空與
                //    深度上限，但關閉中的那幾幀連走訪都不該做，理由同上面的 valuesReadable）。
                if (!valuesReadable)
                {
                    ImGui.TextColored(ImGuiColors.DalamudGrey,
                        $"Objective progress raw dump: {UnknownMark}（視窗正在關閉中，這一幀不讀）");
                }
                else if (ImGui.CollapsingHeader("Objective progress raw dump###ICEObjectiveDump"))
                {
                    if (ImGui.Button("Copy to clipboard###ICEObjectiveDumpCopy".Loc()))
                        ImGui.SetClipboardText(string.Join("\n", MissionObjectiveReader.DumpDiagnostics()));

                    foreach (var line in MissionObjectiveReader.DumpDiagnostics())
                        ImGui.TextUnformatted(line);

                    ImGui.Separator();
                    ImGui.TextUnformatted("Parsed objectives:".Loc());
                    foreach (var objective in MissionObjectiveReader.Get(CosmicHelper.CurrentLunarMission))
                        ImGui.TextUnformatted($"  {objective.Text} = {objective.Current}/{objective.Required} (done={objective.Done})");
                }
            }
            else
            {
                ImGui.Text("Waiting for \"WKSMissionInfomation\" to be visible".Loc());
            }
        }
    }
}
