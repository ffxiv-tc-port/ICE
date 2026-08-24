using Dalamud.Interface.Utility.Raii;
using ICE.Config;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.Json;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    internal class GatherSettings
    {
        private static string newProfileName = "";
        private static string[] MissionTypes => ["Limited Nodes".Loc(), "Gather x Amount".Loc(), "Time Attack".Loc(), "Chained Scoring".Loc(), "Boon Scoring".Loc(), "Chain + Boon Scoring".Loc(), "Dual Class".Loc()];
        private static int MissionIndex = 0;

        private static readonly string PROFILE_PREFIX = "IceGatherProfile_";

        public static string ExportGatherProfile(int profileId)
        {
            if (!C.GatherProfiles.TryGetValue(profileId, out var profile))
                return string.Empty;

            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            var bytes = Encoding.UTF8.GetBytes(json);
            var base64 = Convert.ToBase64String(bytes);

            return PROFILE_PREFIX + base64;
        }

        public static bool ImportGatherProfile(string importString, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                // Check for and remove the prefix
                if (!importString.StartsWith(PROFILE_PREFIX))
                {
                    errorMessage = "Invalid import string: Missing prefix";
                    return false;
                }

                var base64String = importString.Substring(PROFILE_PREFIX.Length);

                var bytes = Convert.FromBase64String(base64String);
                var json = Encoding.UTF8.GetString(bytes);

                var profile = JsonSerializer.Deserialize<GatherProfile>(json);
                if (profile == null)
                {
                    errorMessage = "Failed to deserialize profile";
                    return false;
                }

                // Get the next available ID
                int nextId = C.GatherProfiles.Keys.Count > 0
                    ? C.GatherProfiles.Keys.Max() + 1
                    : 0;

                profile.Id = nextId;
                C.GatherProfiles[nextId] = profile;

                // Save the configuration
                C.Save();

                return true;
            }
            catch (FormatException)
            {
                errorMessage = "Invalid import string: Not valid base64";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Import failed: {ex.Message}";
                return false;
            }
        }

        public static bool InitialSetupProfile(string importString, string type, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                // Check for and remove the prefix
                if (!importString.StartsWith(PROFILE_PREFIX))
                {
                    errorMessage = "Invalid import string: Missing prefix";
                    return false;
                }

                var base64String = importString.Substring(PROFILE_PREFIX.Length);

                var bytes = Convert.FromBase64String(base64String);
                var json = Encoding.UTF8.GetString(bytes);

                var profile = JsonSerializer.Deserialize<GatherProfile>(json);
                if (profile == null)
                {
                    errorMessage = "Failed to deserialize profile";
                    return false;
                }

                // Get the next available ID
                int nextId = C.GatherProfiles.Keys.Count > 0
                    ? C.GatherProfiles.Keys.Max() + 1
                    : 0;

                profile.Id = nextId;
                C.GatherProfiles[nextId] = profile;

                foreach (var mission in C.MissionConfig)
                {
                    var id = mission.Key;

                    // 🔴🔴 這是目前**實機就會炸**的那一顆：迭代 C.MissionConfig 卻直接索引 SheetMissionDict。
                    //     兩個字典的鍵集合不一樣 —— 實測使用者的 Mission Config.yaml，
                    //     missionConfig 的鍵是 **0..544**，而 SheetMissionDict 是 **1..544**。
                    //     key 0 是 MissionTimer.AbandonMission() 在「currentMission 已經歸零」時寫進去
                    //     並存檔的（該使用者的 key 0 已經累積 failedCounters: 118），所以按下這個按鈕
                    //     就是 KeyNotFoundException —— 在 ImGui 繪製迴圈裡丟例外會讓整個視窗畫不出來。
                    if (!CosmicHelper.SheetMissionDict.TryGetValue(id, out var missionDict))
                        continue;

                    bool craftMission = missionDict.Attributes.HasFlag(MissionAttributes.Craft);
                    bool gatherMission = missionDict.Attributes.HasFlag(MissionAttributes.Gather);

                    bool LimitedQuant = missionDict.Attributes.HasFlag(MissionAttributes.Limited);
                    // Gather X Amount is just "Gather" 
                    bool TimedMission = missionDict.Attributes.HasFlag(MissionAttributes.ScoreTimeRemaining);
                    bool ChainedMission = missionDict.Attributes.HasFlag(MissionAttributes.ScoreChains);
                    bool BoonMission = missionDict.Attributes.HasFlag(MissionAttributes.ScoreGatherersBoon);
                    bool collectableMission = missionDict.Attributes.HasFlag(MissionAttributes.Collectables);
                    bool stellerReductionMission = missionDict.Attributes.HasFlag(MissionAttributes.ReducedItems);

                    bool GatherX = !stellerReductionMission && !collectableMission && !BoonMission && !ChainedMission && !TimedMission && !LimitedQuant;

                    void UpdateMissions()
                    {
                        mission.Value.GProfileId = nextId;
                    }

                    if (gatherMission && (!collectableMission && !stellerReductionMission))
                    {
                        if (type == "limited" && LimitedQuant)
                            UpdateMissions();
                        else if (type == "timed" && TimedMission)
                            UpdateMissions();
                        else if (type == "chained" && ChainedMission && !BoonMission)
                            UpdateMissions();
                        else if (type == "boon" && BoonMission && !ChainedMission)
                            UpdateMissions();
                        else if (type == "boonChain" && ChainedMission && BoonMission)
                            UpdateMissions();
                        // ⚠️ 這條鏈裡的 dualCraft／gatherX 順序**在單次呼叫內沒有語意差別**
                        //    （type 是固定值，兩個分支不可能同時成立）；排成與 SetupAllProfiles()
                        //    的呼叫順序一致，只是為了讓兩處讀起來對得上。真正決定勝負的是那邊。
                        else if (type == "gatherX" && GatherX)
                            UpdateMissions();
                        else if (type == "dualCraft" && craftMission)
                            UpdateMissions();
                    }
                }



                return true;
            }
            catch (FormatException)
            {
                errorMessage = "Invalid import string: Not valid base64";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Import failed: {ex.Message}";
                return false;
            }
        }

        private static Dictionary<uint, string> Foods = new();

        public static void Draw()
        {
            int maxGp = 1200;

            bool SelfSpiritbondGather = C.SelfSpiritbondGather;
            if (ImGui.Checkbox("Extract Spiritbond on Gather".Loc() + "###ICEExtractSpiritbondOnGather", ref SelfSpiritbondGather))
            {
                if (C.SelfSpiritbondGather != SelfSpiritbondGather)
                {
                    C.SelfSpiritbondGather = SelfSpiritbondGather;
                    C.Save();
                }
            }

            bool AutoCordial = C.AutoCordial;
            if (ImGui.Checkbox("Auto Cordial".Loc() + "###ICEAutoCordial", ref AutoCordial))
            {
                C.AutoCordial = AutoCordial;
                C.Save();
            }
            ImGuiEx.HelpMarker(("Will only work while using ICE and not manual mode\n" +
                               "Will also pause pandora cordial usage while on the moon").Loc());
            if (ImGui.CollapsingHeader("Cordial Settings".Loc() + "###ICECordialSettings"))
            {
                bool InverseCordialPrio = C.inverseCordialPrio;
                bool PreventOvercap = C.PreventOvercap;
                int CordialMinGp = C.CordialMinGp;

                if (ImGui.Checkbox("Inverse Priority (Watered -> Regular -> Hi)".Loc() + "###ICEInverseCordialPrio", ref InverseCordialPrio))
                {
                    C.inverseCordialPrio = InverseCordialPrio;
                    C.Save();
                }
                if (ImGui.Checkbox("Prevent Overcap".Loc() + "###ICEPreventOvercap", ref PreventOvercap))
                {
                    C.PreventOvercap = PreventOvercap;
                    C.Save();
                }
                ImGui.SetNextItemWidth(200);
                if (ImGui.SliderInt("Use cordial when below the following GP".Loc() + "###ICECordialMinGp", ref CordialMinGp, 0, maxGp))
                {
                    C.CordialMinGp = CordialMinGp;
                    C.SaveDebounced();
                }
                ImGui.SameLine();
                ImGuiEx.HelpMarker(("What's the minimum gp you can have before it uses a cordial.\n" +
                                   "If set to 0, it'll never use a cordial even with it enabled (because... you'll never have 0 gp)").Loc());
            }

            if (ImGui.CollapsingHeader("Food Settings".Loc() + "###ICEFoodSettings"))
            {
                bool useFood = C.UseGatheringFood;
                if (ImGui.Checkbox("Use food on gathering missions".Loc() + "###ICEUseGatheringFood", ref useFood))
                {
                    C.UseGatheringFood = useFood;
                    C.Save();
                }

                if (ImGui.Button("Select Gathering Food".Loc() + "###ICESelectGatheringFood"))
                {
                    foreach (var item in ConsumableInfo.Food)
                    {
                        if (PlayerHelper.GetItemCount(item.Id, out var count) && count > 0)
                            Foods[item.Id] = item.Name;
                    }

                    ImGui.OpenPopup("Food Selection");
                }
                ImGui.SameLine();
                if (C.GatheringFood == 0)
                {
                    ImGui.Text("No Food Selected".Loc());
                }
                else
                {
                    // 原為 Where(x => x.RowId == C.GatheringFood).FirstOrDefault():對整張 Item 表
                    // (約 4.5 萬列)做 O(n) 線性走訪來找主鍵,而這裡在 Draw 路徑上、每幀執行。
                    // GetRowOrDefault 是索引查詢 O(1)。找不到時原本會拿到 default(Item),
                    // Name.ToString() 得到空字串;這裡用 ?? "" 維持完全相同的行為。
                    var itemName = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(C.GatheringFood)?.Name.ToString() ?? "";
                    ImGui.Text($"{itemName}");
                }

                if (ImGui.BeginPopup("Food Selection"))
                {
                    if (ImGui.BeginTable("Food Item Selection", 2, ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("Food Item".Loc());
                        ImGui.TableSetupColumn("Amount".Loc());

                        // First Column, pretty much giving an option for "None" if they want none
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        if (ImGui.Selectable("Use no gathering food".Loc() + "###ICEUseNoGatheringFood"))
                        {
                            C.GatheringFood = 0;
                            C.Save();
                            ImGui.CloseCurrentPopup();
                        }

                        foreach (var item in Foods)
                        {
                            ImGui.TableNextRow();
                            ImGui.PushID(item.Key);

                            ImGui.TableSetColumnIndex(0);
                            if (ImGui.Selectable($"{item.Value}"))
                            {
                                C.GatheringFood = item.Key;
                                C.Save();

                                ImGui.CloseCurrentPopup();
                            }

                            ImGui.TableNextColumn();
                            PlayerHelper.GetItemCount(item.Key, out var count);
                            if (ImGui.Selectable($"x {count}"))
                            {
                                C.GatheringFood = item.Key;
                                C.Save();

                                ImGui.CloseCurrentPopup();
                            }
                        }

                        ImGui.EndTable();
                    }

                    ImGui.EndPopup();
                }
            }

            ImGui.Separator();

            if (ImGui.BeginTable("Gathering Profile Settings", 2, ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Profile Selection".Loc());
                ImGui.TableSetupColumn("Gathering Settings".Loc());

                // 1st Row, technically only really used for the gather profile name creator
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.SetNextItemWidth(200);
                ImGui.InputText("New Profile Name".Loc() + "###ICENewProfileName", ref newProfileName, 64);
                using (ImRaii.Disabled(newProfileName == ""))
                {
                    if (ImGui.Button("Add Profile".Loc() + "###ICEAddProfile") && !string.IsNullOrWhiteSpace(newProfileName))
                    {
                        var newId = C.GatherProfiles.Keys.Max() + 1;
                        C.GatherProfiles[newId] = new()
                        {
                            Name = newProfileName,
                        };
                        C.Save();
                        newProfileName = "";
                    }
                }

                // 2nd Row, Actually profile selector
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                #region Profile Selection

                ImGui.Text("Gather Profiles".Loc());

                bool canDelete = C.GatherProfiles.Count > 1 && C.SelectedGatherIndex != 0;
                using (ImRaii.Disabled(!canDelete))
                {
                    if (ImGui.Button("Delete Selected Profile".Loc() + "###ICEDeleteSelectedProfile"))
                    {
                        int deletedId = C.SelectedGatherIndex;

                        // Don't allow deleting the default profile
                        if (deletedId == 0)
                        {
                            return;
                        }

                        // Remove the profile
                        C.GatherProfiles.Remove(deletedId);

                        // Update all missions using this GatherSettingId
                        foreach (var mission in C.MissionConfig)
                        {
                            if (mission.Value.GProfileId == deletedId)
                            {
                                mission.Value.GProfileId = 0; // fallback to default
                            }
                        }

                        // Clamp the selected index and save
                        C.SelectedGatherIndex = 0;
                        C.Save();
                    }
                }

                ImGui.BeginChild("GatherProfileChild", new Vector2(300, ImGui.GetTextLineHeightWithSpacing() * 5 + 10), true);
                foreach (var profile in C.GatherProfiles)
                {
                    var id = profile.Key;
                    bool isSelected = C.SelectedGatherIndex == id;
                    if (ImGui.Selectable($"{profile.Value.Name}##{profile.Value.Name}_{id}", isSelected))
                    {
                        C.SelectedGatherIndex = id;
                        C.Save();
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndChild();

                GatherProfile entry = C.GatherProfiles[C.SelectedGatherIndex];

                ImGui.Combo("Mission Type".Loc() + "###ICEMissionType", ref MissionIndex, MissionTypes, MissionTypes.Length);
                if (ImGui.Button("Apply to Mission Types".Loc() + "###ICEApplyToMissionTypes"))
                {
                    foreach (var mission in C.MissionConfig)
                    {
                        var id = mission.Key;

                        // 同上：迭代 MissionConfig（含 key 0）卻直接索引 SheetMissionDict（沒有 key 0）。
                        if (!CosmicHelper.SheetMissionDict.TryGetValue(id, out var missionDict))
                            continue;

                        bool craftMission = missionDict.Attributes.HasFlag(MissionAttributes.Craft);
                        bool gatherMission = missionDict.Attributes.HasFlag(MissionAttributes.Gather);

                        bool LimitedQuant = missionDict.Attributes.HasFlag(MissionAttributes.Limited);
                        // Gather X Amount is just "Gather" 
                        bool TimedMission = missionDict.Attributes.HasFlag(MissionAttributes.ScoreTimeRemaining);
                        bool ChainedMission = missionDict.Attributes.HasFlag(MissionAttributes.ScoreChains);
                        bool BoonMission = missionDict.Attributes.HasFlag(MissionAttributes.ScoreGatherersBoon);
                        bool collectableMission = missionDict.Attributes.HasFlag(MissionAttributes.Collectables);
                        bool stellerReductionMission = missionDict.Attributes.HasFlag(MissionAttributes.ReducedItems);

                        bool GatherX = !stellerReductionMission && !collectableMission && !BoonMission && !ChainedMission && !TimedMission && !LimitedQuant;

                        void UpdateMissions()
                        {
                            mission.Value.GProfileId = entry.Id;
                        }

                        if (gatherMission && (!collectableMission && !stellerReductionMission))
                        {
                            if (MissionIndex == 0 && LimitedQuant)
                                UpdateMissions();
                            else if (MissionIndex == 2 && TimedMission)
                                UpdateMissions();
                            else if (MissionIndex == 3 && ChainedMission && !BoonMission)
                                UpdateMissions();
                            else if (MissionIndex == 4 && BoonMission && !ChainedMission)
                                UpdateMissions();
                            else if (MissionIndex == 5 && ChainedMission && BoonMission)
                                UpdateMissions();
                            // 同上：MissionIndex 是固定值，這裡的先後同樣不影響結果，
                            // 排成與 SetupAllProfiles() 一致純粹是為了對照方便。
                            else if (MissionIndex == 1 && GatherX)
                                UpdateMissions();
                            else if (MissionIndex == 6 && craftMission)
                                UpdateMissions();
                        }
                    }

                    C.Save();
                }

                #endregion

                #region Profile Editor

                ImGui.TableNextColumn();
                #region Minimum GP + Dual Class Info

                int minGP = entry.MinimumGp;
                ImGui.SetNextItemWidth(100);
                if (ImGui.SliderInt("Minimum GP to start mission".Loc() + "###ICEMinimumGpToStart", ref minGP, -1, maxGp))
                {
                    entry.MinimumGp = minGP;
                    C.SaveDebounced();
                }

                ImGui.Text("Where'd the dual craft amount go?".Loc());
                ImGui.SameLine();
                ImGui.Dummy(new(5, 0));
                ImGui.SameLine();
                ImGui.TextDisabled("?");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetNextWindowSize(new(400.0f, 0.0f)); // Fixed width, auto height
                    ImGui.BeginTooltip();

                    ImGui.TextWrapped(("Short answer: It's built in now\n" +
                     "Long answer: Honestly, this was a cumbersome system in itself. And with square deciding to not continue on with dual crafting missions going into the 2nd moon, I figured it would be better to just tie it into the scoring system. You realistically only need:\n" +
                     "Gold: 3 Items\n" +
                     "Silver: 2 Items\n" +
                     "Bronze: 1 Item\n" +
                     "to be able to hit the threshold. And even then, if you manage to not hit it on the first attempt, it'll just keep gathering. Plus. This makes it to where I can not have to worry about profile managing on fishing for... 4 missions? Seemed minorly reduntant in my eyes.\n" +
                     "So now how it'll work. Select the turnin option (Gold/Any both work the same) and it will now gather up to the necessary amount -> turnin when it's ready.\n" +
                     "NOW NONE OF YOU CAN TELL IT TO CRAFT 27 ITEMS. STOP IT. IT SAID CRAFT (╯°Д°)╯︵/(.□ . \\)").Loc());
                    ImGui.EndTooltip();
                }

                #endregion

                #region Boon Increase 2

                if (ImGui.CollapsingHeader("Pioneer's | Mountaineer's Gift II".Loc() + "###ICEBuffBoonIncrease2"))
                {
                    string buffName = "BoonIncrease2";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = "Apply a 30% buff to your boon chance.".Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }

                    ImGui.PopID();
                }

                #endregion

                #region Boon Increase 1

                if (ImGui.CollapsingHeader("Pioneer's | Mountaineer's Gift I".Loc() + "###ICEBuffBoonIncrease1"))
                {
                    string buffName = "BoonIncrease1";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = "Apply a 10% buff to your boon chance.".Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.PopID();
                }

                #endregion

                #region Nophica's / Nald'thal's Tidings

                if (ImGui.CollapsingHeader("Nophica's / Nald'thal's Tidings Buff".Loc() + "###ICEBuffTidings"))
                {
                    string buffName = "Tidings";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = "Increases item yield from Gatherer's Boon by 1".Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.PopID();
                }

                #endregion

                #region Blessed / Kings Yield II

                if (ImGui.CollapsingHeader("Blessed / Kings Yield II".Loc() + "###ICEBuffYieldII"))
                {
                    string buffName = "YieldII";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = ("Increases the number of items obtained when gathering by 2\n" +
                                        "Will only apply when the gathering node has full durability").Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.PopID();
                }

                #endregion

                #region Blessed / Kings Yield I

                if (ImGui.CollapsingHeader("Blessed / Kings Yield I".Loc() + "###ICEBuffYieldI"))
                {
                    string buffName = "YieldI";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = ("Increases the number of items obtained when gathering by 1\n" +
                                        "Will only apply when the gathering node has full durability").Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.PopID();
                }

                #endregion

                #region Bonus Integrity

                if (ImGui.CollapsingHeader("Ageless Words / Solid Reason".Loc() + "###ICEBuffBonusIntegrity"))
                {
                    string buffName = "BonusIntegrity";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = ("Increase the Integrity by 1\n" +
                                        "50% chance to grant Eureka Moment").Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.PopID();
                }

                #endregion

                #region Bountiful Yield II

                if (ImGui.CollapsingHeader("Bountiful Yield II / Bountiful Harvest II".Loc() + "###ICEBuffBountifulYieldII"))
                {
                    string buffName = "BountifulYieldII";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = ("Increases the number of items obtained when gathering by 2\n" +
                                        "Will only apply when the gathering node has full durability").Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.Text("Minumum Items To Gather".Loc());
                    ImGui.SameLine();
                    int minItems = entry.GatherBuffs.BountifulMinItem;
                    if (ImGui.DragInt("##MinItemsGather", ref minItems, 1, 2, 4))
                    {
                        entry.GatherBuffs.BountifulMinItem = minItems;
                        C.SaveDebounced();
                    }

                    ImGui.PopID();
                }

                #endregion

                #region Field Mastery (Gather Chance)

                #region Field Mastery III

                if (ImGui.CollapsingHeader("Field Mastery | Sharp Vision III".Loc() + "###ICEBuffFieldMasteryIII"))
                {
                    string buffName = "FieldMasteryIII";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = ("Increases the gather chance by 50%\n" +
                                        "Please note: You can have multiple enabled, but only the one that will get you the closest to " +
                                        "100% the cheapest will be applied").Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.PopID();
                }

                #endregion

                #region Field Mastery II

                if (ImGui.CollapsingHeader("Field Mastery | Sharp Vision II".Loc() + "###ICEBuffFieldMasteryII"))
                {
                    string buffName = "FieldMasteryII";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = ("Increases the gather chance by 15%\n" +
                                        "Please note: You can have multiple enabled, but only the one that will get you the closest to " +
                                        "100% the cheapest will be applied").Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.PopID();
                }

                #endregion

                #region Field Mastery I

                if (ImGui.CollapsingHeader("Field Mastery | Sharp Vision I".Loc() + "###ICEBuffFieldMasteryI"))
                {
                    string buffName = "FieldMasteryI";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = ("Increases the gather chance by 5%\n" +
                                        "Please note: You can have multiple enabled, but only the one that will get you the closest to " +
                                        "100% the cheapest will be applied").Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.PopID();
                }

                #endregion

                #region Field Mastery [Temp]

                if (ImGui.CollapsingHeader("Flora Mastery | Clear Vision [Temp]".Loc() + "###ICEBuffFieldMasteryTemp"))
                {
                    string buffName = "FieldMasteryTemp";

                    ImGui.PushID(buffName);

                    bool currentlyEnabled = entry.GatherBuffs.Buffs[buffName].Enabled;
                    int minUseGp = entry.GatherBuffs.Buffs[buffName].MinGp;
                    int minActionGp = GatheringUtil.GathActionDict[buffName].RequiredGp;
                    int maxActionUsage = entry.GatherBuffs.Buffs[buffName].MaxUse;
                    string ActionInfo = ("Increases the gather chance by 15%\n" +
                                        "This can be applied with normal field mastery, but will only apply per hit").Loc();

                    ImGui.Text("Action Info: ".Loc());
                    ImGuiEx.HelpMarker(ActionInfo);

                    if (ImGui.Checkbox("Enable".Loc(), ref currentlyEnabled))
                    {
                        entry.GatherBuffs.Buffs[buffName].Enabled = currentlyEnabled;
                        C.Save();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.SliderInt("Minimum Gp for Usage".Loc(), ref minUseGp, minActionGp, maxGp))
                    {
                        entry.GatherBuffs.Buffs[buffName].MinGp = minUseGp;
                        C.SaveDebounced();
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputInt("Max Use".Loc(), ref maxActionUsage))
                    {
                        entry.GatherBuffs.Buffs[buffName].MaxUse = maxActionUsage;
                        C.SaveDebounced();
                    }
                    ImGuiEx.HelpMarker(("Set to -1 to allow for infinite uses \n" +
                                       "Set to 1-> X to set maximum amount of uses per mission").Loc());

                    ImGui.PopID();
                }

                #endregion

                #endregion

                #endregion

                ImGui.EndTable();
            }

            ImGui.Separator();
            if (ImGui.Button("Copy Selected Profile".Loc() + "###ICECopySelectedProfile"))
            {
                string export = ExportGatherProfile(C.SelectedGatherIndex);
                ImGui.SetClipboardText(export);
            }

            if (ImGui.Button("Import Selected Profile".Loc() + "###ICEImportSelectedProfile"))
            {
                string importProfile = ImGui.GetClipboardText();
                string errorMessage = "";
                ImportGatherProfile(importProfile, out errorMessage);
                if (errorMessage != "")
                {
                    IceLogging.Error(errorMessage);
                }
                C.Save();
            }

            ImGui.Dummy(new Vector2(0, 10));

            ImGui.Separator();

            ImGui.Dummy(new Vector2(0, 10));

            using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.LeftShift)))
            {
                if (ImGui.Button("Setup Gathering Profiles".Loc() + "###ICESetupAllGatheringProfiles"))
                {
                    SetupAllProfiles();

                    C.Save();
                }
            }
            ImGuiEx.HelpMarker(("PLEASE NOTE:\n" +
                               "This will wipe out all your current profiles, and apply what I would suggest for each one.\n" +
                               "For most of you this would be fine, this is really only here if you don't know what to apply for each one." +
                               "If you're okay with this, hold left shift and apply").Loc());
        }

        public static void SetupAllProfiles()
        {
            foreach (var profile in C.GatherProfiles)
            {
                if (profile.Key == 0)
                    continue;
                else
                {
                    C.GatherProfiles.Remove(profile.Key);
                    foreach (var mission in C.MissionConfig)
                    {
                        if (mission.Value.GProfileId == profile.Key)
                        {
                            mission.Value.GProfileId = 0; // fallback to default
                        }
                    }
                }
            }

            string timedMissions = "IceGatherProfile_eyJJZCI6MCwiTmFtZSI6IlRpbWVkIE1pc3Npb25zIiwiTWluaW11bUdwIjoxMDAsIkR1YWxDbGFzc0NyYWZ0QW1vdW50IjoxLCJHYXRoZXJCdWZmcyI6eyJCdWZmcyI6eyJCb29uSW5jcmVhc2UyIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MTAwLCJNYXhVc2UiOi0xfSwiQm9vbkluY3JlYXNlMSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjUwLCJNYXhVc2UiOi0xfSwiVGlkaW5ncyI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjIwMCwiTWF4VXNlIjotMX0sIllpZWxkSUkiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjUwMCwiTWF4VXNlIjotMX0sIllpZWxkSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjQwMCwiTWF4VXNlIjotMX0sIkJvdW50aWZ1bFlpZWxkSUkiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkJvbnVzSW50ZWdyaXR5Ijp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MzAwLCJNYXhVc2UiOi0xfSwiQm9udXNJbnRlZ3JpdHlDaGFuY2UiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlJSUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjoyNTAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlJSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeVRlbXAiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX19LCJCb3VudGlmdWxNaW5JdGVtIjo0fX0=";
            string limitedMissions = "IceGatherProfile_eyJJZCI6MCwiTmFtZSI6IkxpbWl0ZWQgTm9kZXMiLCJNaW5pbXVtR3AiOjEwMCwiRHVhbENsYXNzQ3JhZnRBbW91bnQiOjEsIkdhdGhlckJ1ZmZzIjp7IkJ1ZmZzIjp7IkJvb25JbmNyZWFzZTIiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkJvb25JbmNyZWFzZTEiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX0sIlRpZGluZ3MiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjoyMDAsIk1heFVzZSI6LTF9LCJZaWVsZElJIjp7IkVuYWJsZWQiOnRydWUsIk1pbkdwIjo1MDAsIk1heFVzZSI6LTF9LCJZaWVsZEkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo0MDAsIk1heFVzZSI6LTF9LCJCb3VudGlmdWxZaWVsZElJIjp7IkVuYWJsZWQiOnRydWUsIk1pbkdwIjoxMDAsIk1heFVzZSI6LTF9LCJCb251c0ludGVncml0eSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjMwMCwiTWF4VXNlIjotMX0sIkJvbnVzSW50ZWdyaXR5Q2hhbmNlIjp7IkVuYWJsZWQiOnRydWUsIk1pbkdwIjowLCJNYXhVc2UiOi0xfSwiRmllbGRNYXN0ZXJ5SUlJIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MjUwLCJNYXhVc2UiOi0xfSwiRmllbGRNYXN0ZXJ5SUkiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeUkiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjUwLCJNYXhVc2UiOi0xfSwiRmllbGRNYXN0ZXJ5VGVtcCI6eyJFbmFibGVkIjp0cnVlLCJNaW5HcCI6NTAsIk1heFVzZSI6LTF9fSwiQm91bnRpZnVsTWluSXRlbSI6NH19";
            string chainedMissions = "IceGatherProfile_eyJJZCI6MCwiTmFtZSI6IkNoYWluZWQiLCJNaW5pbXVtR3AiOjEwMCwiRHVhbENsYXNzQ3JhZnRBbW91bnQiOjEsIkdhdGhlckJ1ZmZzIjp7IkJ1ZmZzIjp7IkJvb25JbmNyZWFzZTIiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjoxMDAsIk1heFVzZSI6LTF9LCJCb29uSW5jcmVhc2UxIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6NTAsIk1heFVzZSI6LTF9LCJUaWRpbmdzIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MjAwLCJNYXhVc2UiOi0xfSwiWWllbGRJSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjUwMCwiTWF4VXNlIjotMX0sIllpZWxkSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjQwMCwiTWF4VXNlIjotMX0sIkJvdW50aWZ1bFlpZWxkSUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjoxMDAsIk1heFVzZSI6LTF9LCJCb251c0ludGVncml0eSI6eyJFbmFibGVkIjp0cnVlLCJNaW5HcCI6MzAwLCJNYXhVc2UiOi0xfSwiQm9udXNJbnRlZ3JpdHlDaGFuY2UiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlJSUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjoyNTAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlJSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeVRlbXAiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX19LCJCb3VudGlmdWxNaW5JdGVtIjo0fX0=";
            string DualClass = "IceGatherProfile_eyJJZCI6MCwiTmFtZSI6IkR1YWwgQ2xhc3MiLCJNaW5pbXVtR3AiOjEwMCwiRHVhbENsYXNzQ3JhZnRBbW91bnQiOjIsIkdhdGhlckJ1ZmZzIjp7IkJ1ZmZzIjp7IkJvb25JbmNyZWFzZTIiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkJvb25JbmNyZWFzZTEiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjUwLCJNYXhVc2UiOi0xfSwiVGlkaW5ncyI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjIwMCwiTWF4VXNlIjotMX0sIllpZWxkSUkiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjUwMCwiTWF4VXNlIjotMX0sIllpZWxkSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjQwMCwiTWF4VXNlIjotMX0sIkJvdW50aWZ1bFlpZWxkSUkiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkJvbnVzSW50ZWdyaXR5Ijp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MzAwLCJNYXhVc2UiOi0xfSwiQm9udXNJbnRlZ3JpdHlDaGFuY2UiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlJSUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjoyNTAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlJSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeVRlbXAiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX19LCJCb3VudGlmdWxNaW5JdGVtIjo0fX0=";
            string boonMissions = "IceGatherProfile_eyJJZCI6MCwiTmFtZSI6IkJvb24iLCJNaW5pbXVtR3AiOjEwMCwiRHVhbENsYXNzQ3JhZnRBbW91bnQiOjEsIkdhdGhlckJ1ZmZzIjp7IkJ1ZmZzIjp7IkJvb25JbmNyZWFzZTIiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkJvb25JbmNyZWFzZTEiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX0sIlRpZGluZ3MiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjoyMDAsIk1heFVzZSI6LTF9LCJZaWVsZElJIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6NTAwLCJNYXhVc2UiOi0xfSwiWWllbGRJIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6NDAwLCJNYXhVc2UiOi0xfSwiQm91bnRpZnVsWWllbGRJSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkJvbnVzSW50ZWdyaXR5Ijp7IkVuYWJsZWQiOnRydWUsIk1pbkdwIjozMDAsIk1heFVzZSI6LTF9LCJCb251c0ludGVncml0eUNoYW5jZSI6eyJFbmFibGVkIjp0cnVlLCJNaW5HcCI6MCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeUlJSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjI1MCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeUlJIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MTAwLCJNYXhVc2UiOi0xfSwiRmllbGRNYXN0ZXJ5SSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjUwLCJNYXhVc2UiOi0xfSwiRmllbGRNYXN0ZXJ5VGVtcCI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjUwLCJNYXhVc2UiOi0xfX0sIkJvdW50aWZ1bE1pbkl0ZW0iOjR9fQ==";
            string ChainBoonMission = "IceGatherProfile_eyJJZCI6MCwiTmFtZSI6IkNoYWluZWQgXHUwMDJCIEJvb24iLCJNaW5pbXVtR3AiOjEwMCwiRHVhbENsYXNzQ3JhZnRBbW91bnQiOjEsIkdhdGhlckJ1ZmZzIjp7IkJ1ZmZzIjp7IkJvb25JbmNyZWFzZTIiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkJvb25JbmNyZWFzZTEiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjUwLCJNYXhVc2UiOi0xfSwiVGlkaW5ncyI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjIwMCwiTWF4VXNlIjotMX0sIllpZWxkSUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MDAsIk1heFVzZSI6LTF9LCJZaWVsZEkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo0MDAsIk1heFVzZSI6LTF9LCJCb3VudGlmdWxZaWVsZElJIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MTAwLCJNYXhVc2UiOi0xfSwiQm9udXNJbnRlZ3JpdHkiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjMwMCwiTWF4VXNlIjotMX0sIkJvbnVzSW50ZWdyaXR5Q2hhbmNlIjp7IkVuYWJsZWQiOnRydWUsIk1pbkdwIjowLCJNYXhVc2UiOi0xfSwiRmllbGRNYXN0ZXJ5SUlJIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MjUwLCJNYXhVc2UiOi0xfSwiRmllbGRNYXN0ZXJ5SUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjoxMDAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlJIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6NTAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlUZW1wIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6NTAsIk1heFVzZSI6LTF9fSwiQm91bnRpZnVsTWluSXRlbSI6NH19";
            string GatherXAmount = "IceGatherProfile_eyJJZCI6MCwiTmFtZSI6IkdhdGhlciBYIEFtb3VudCIsIk1pbmltdW1HcCI6LTEsIkR1YWxDbGFzc0NyYWZ0QW1vdW50IjoxLCJHYXRoZXJCdWZmcyI6eyJCdWZmcyI6eyJCb29uSW5jcmVhc2UyIjp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MTAwLCJNYXhVc2UiOi0xfSwiQm9vbkluY3JlYXNlMSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjUwLCJNYXhVc2UiOi0xfSwiVGlkaW5ncyI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjIwMCwiTWF4VXNlIjotMX0sIllpZWxkSUkiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjUwMCwiTWF4VXNlIjotMX0sIllpZWxkSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjQwMCwiTWF4VXNlIjotMX0sIkJvdW50aWZ1bFlpZWxkSUkiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkJvbnVzSW50ZWdyaXR5Ijp7IkVuYWJsZWQiOmZhbHNlLCJNaW5HcCI6MzAwLCJNYXhVc2UiOi0xfSwiQm9udXNJbnRlZ3JpdHlDaGFuY2UiOnsiRW5hYmxlZCI6dHJ1ZSwiTWluR3AiOjAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlJSUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjoyNTAsIk1heFVzZSI6LTF9LCJGaWVsZE1hc3RlcnlJSSI6eyJFbmFibGVkIjpmYWxzZSwiTWluR3AiOjEwMCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeUkiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX0sIkZpZWxkTWFzdGVyeVRlbXAiOnsiRW5hYmxlZCI6ZmFsc2UsIk1pbkdwIjo1MCwiTWF4VXNlIjotMX19LCJCb3VudGlmdWxNaW5JdGVtIjo0fX0=";

            GatherSettings.InitialSetupProfile(timedMissions, "timed", out var _);
            GatherSettings.InitialSetupProfile(limitedMissions, "limited", out var _);
            GatherSettings.InitialSetupProfile(chainedMissions, "chained", out var _);
            GatherSettings.InitialSetupProfile(boonMissions, "boon", out var _);
            GatherSettings.InitialSetupProfile(ChainBoonMission, "boonChain", out var _);
            // 🔴 dualCraft 必須排在 gatherX **後面**。
            //    InitialSetupProfile 是「符合條件就把 GProfileId 覆寫成這一份的 id」，
            //    而 GatherX 的條件（!採集品 !還元 !恩惠 !連鎖 !限時 !限量）**沒有排除 craft**，
            //    所以雙職（採集＋製作）任務會同時落在 dualCraft 與 gatherX 兩桶裡，
            //    **後呼叫的那一個贏**。原本的順序讓 gatherX 蓋掉 dualCraft ⇒ 雙職任務永遠拿不到
            //    「Dual Class」設定檔（那份的 DualClassCraftAmount 是 2，其餘都是 1），
            //    而使用者只會看到雙職任務的採集量不對，沒有任何錯誤訊息。
            GatherSettings.InitialSetupProfile(GatherXAmount, "gatherX", out var _);
            GatherSettings.InitialSetupProfile(DualClass, "dualCraft", out var _);
        }
    }
}