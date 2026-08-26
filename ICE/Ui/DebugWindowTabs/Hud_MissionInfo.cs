using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Ui;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Hud_MissionInfo
    {
        public static unsafe void Draw()
        {
            uint currentScore = 0;
            uint silverScore = 0;
            uint goldScore = 0;

            if (GenericHelpers.TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var x) && x.IsAddonReady)
            {
                // ECommons 加固後四個分數 getter 都是 uint?(讀不到回 null);
                // 這裡是除錯 HUD,顯示 0 即可。
                currentScore = x.CurrentScore ?? 0;
                silverScore = x.SilverScore ?? 0;
                goldScore = x.GoldScore ?? 0;

                var isAddonReady = AddonHelper.IsAddonActive("WKSMissionInfomation");
                ImGui.Text($"Addon Ready: {isAddonReady}");
                if (isAddonReady)
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
                    ImGui.Text($"{currentScore}");

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Silver Score:".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{silverScore}");

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Gold Score:".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{goldScore}");

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Critical Value:".Loc());

                    ImGui.TableNextColumn();
                    ImGui.Text($"{x.CriticalScore}");

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
                    if (ImGui.Button("Report".Loc()))
                    {
                        x.Report();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Button("Abandon".Loc()))
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
                if (ImGui.CollapsingHeader("Objective progress raw dump###ICEObjectiveDump"))
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
