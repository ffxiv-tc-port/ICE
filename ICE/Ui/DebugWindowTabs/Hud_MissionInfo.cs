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
                // ECommons 加固後 CurrentScore 是 uint?(讀不到回 null);
                // 這裡是除錯 HUD,顯示 0 即可。
                currentScore = x.CurrentScore ?? 0;
                silverScore = x.SilverScore;
                goldScore = x.GoldScore;

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
                    ImGui.Text("Current Score:");

                    ImGui.TableNextColumn();
                    ImGui.Text($"{currentScore}");

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Silver Score:");

                    ImGui.TableNextColumn();
                    ImGui.Text($"{silverScore}");

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Gold Score:");

                    ImGui.TableNextColumn();
                    ImGui.Text($"{goldScore}");

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Critical Value:");

                    ImGui.TableNextColumn();
                    ImGui.Text($"{x.CriticalScore}");

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Button("Cosmo Pouch"))
                    {
                        x.CosmoPouch();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Button("Cosmo Crafting Log"))
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
                    if (ImGui.Button("Report"))
                    {
                        x.Report();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Button("Abandon"))
                    {
                        x.Abandon();
                    }

                    var wks = WKSManager.Instance();

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Score 1");
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
                    ImGui.Text("Bait:");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{currentlyEquippped}");


                    ImGui.EndTable();
                }

                // 目標進度的原始資料傾印。之後若要把來源從「走訪節點樹」改成寫死的
                // AtkValue 索引（比較便宜），就靠這裡的輸出來校準 —— 不要用猜的。
                if (ImGui.CollapsingHeader("Objective progress raw dump###ICEObjectiveDump"))
                {
                    if (ImGui.Button("Copy to clipboard###ICEObjectiveDumpCopy"))
                        ImGui.SetClipboardText(string.Join("\n", MissionObjectiveReader.DumpDiagnostics()));

                    foreach (var line in MissionObjectiveReader.DumpDiagnostics())
                        ImGui.TextUnformatted(line);

                    ImGui.Separator();
                    ImGui.TextUnformatted("Parsed objectives:");
                    foreach (var objective in MissionObjectiveReader.Get(CosmicHelper.CurrentLunarMission))
                        ImGui.TextUnformatted($"  {objective.Text} = {objective.Current}/{objective.Required} (done={objective.Done})");
                }
            }
            else
            {
                ImGui.Text("Waiting for \"WKSMissionInfomation\" to be visible");
            }
        }
    }
}
