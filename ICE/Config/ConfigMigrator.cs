using ECommons;
using ICE.Ui.MainUi.Settings.Settings_Table;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using ICE.Utilities.MechaOps;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Config
{
    internal class ConfigMigrator
    {
        public static void MigrateConfigv1()
        {
            if (C.ConfigVersion == 0)
            {
                Svc.Log.Information("You seem to be running the old config version, lets migrate you to the new one");

                C.StopOnAbort = OldConfig.StopOnAbort;
                C.RejectUnknownYesno = OldConfig.RejectUnknownYesno;
                C.DelayGrabMission = OldConfig.DelayGrabMission;
                C.DelayIncrease = OldConfig.DelayIncrease;
                C.DelayCraft = OldConfig.DelayCraft;
                C.DelayCraftIncrease = OldConfig.DelayCraftIncrease;
                C.AnimationLockAbandon = OldConfig.AnimationLockAbandon;

                Svc.Log.Information("Migration of the Safety Settings Completed");

                C.SelectedJob = OldConfig.SelectedJob;
                C.XPRelicGrind = OldConfig.XPRelicGrind;
                C.XPRelicIgnoreManual = OldConfig.XPRelicIgnoreManual;
                C.XPRelicOnlyEnabled = OldConfig.XPRelicOnlyEnabled;
                C.ShowCritical = OldConfig.showCritical;
                C.ShowSequential = OldConfig.showSequential;
                C.ShowWeather = OldConfig.showWeather;
                C.ShowTimeRestricted = OldConfig.showTimeRestricted;
                C.ShowClassA = OldConfig.showClassA;
                C.ShowClassB = OldConfig.showClassB;
                C.ShowClassC = OldConfig.showClassC;
                C.ShowClassD = OldConfig.showClassD;

                Svc.Log.Information("Migration of the Main Window Settings Complete");

                C.ShowOverlay = OldConfig.ShowOverlay;
                C.ShowSeconds = OldConfig.ShowSeconds;
                C.ShowExpBars = OldConfig.ShowExpBars;

                Svc.Log.Information("Migration of the overlay settings complete");

                C.OnlyGrabMission = OldConfig.OnlyGrabMission;
                C.TargetLevel = OldConfig.TargetLevel;
                C.StopWhenLevel = OldConfig.StopWhenLevel;
                C.StopOnceHitCosmoCredits = OldConfig.StopOnceHitCosmoCredits;
                C.CosmoCreditsCap = OldConfig.CosmoCreditsCap;
                C.StopOnceHitLunarCredits = OldConfig.StopOnceHitLunarCredits;
                C.LunarCreditsCap = OldConfig.LunarCreditsCap;
                C.StopOnceHitCosmicScore = OldConfig.StopOnceHitCosmicScore;
                C.CosmoCreditsCap = OldConfig.CosmicScoreCap;
                C.SequenceMissionPriority = OldConfig.SequenceMissionPriority;
                C.WeatherMissionPriority = OldConfig.WeatherMissionPriority;
                C.TimedMissionPriority = OldConfig.TimedMissionPriority;

                Svc.Log.Information("Migration of the mission settings complete");

                C.TableSortOption = OldConfig.TableSortOption;
                C.HideUnsupportedMissions = OldConfig.HideUnsupportedMissions;
                C.AutoPickCurrentJob = OldConfig.AutoPickCurrentJob;

                Svc.Log.Information("Migration of the table settings complete");

                C.SelfRepairGather = OldConfig.SelfRepairGather;
                C.RepairPercent = OldConfig.RepairPercent;
                C.SelfSpiritbondGather = OldConfig.SelfSpiritbondGather;
                C.SelectedGatherIndex = OldConfig.SelectedGatherIndex;

                Svc.Log.Information("Migration of the base gather settings complete");

                C.AutoCordial = OldConfig.AutoCordial;
                C.inverseCordialPrio = OldConfig.inverseCordialPrio;
                C.CordialMinGp = OldConfig.CordialMinGp;
                C.UseOnFisher = OldConfig.UseOnFisher;
                C.PreventOvercap = OldConfig.PreventOvercap;
                C.UseOnlyInMission = OldConfig.UseOnlyInMission;

                Svc.Log.Information("Migration of the cordial settings complete");

                C.MoonSprint = OldConfig.EnableAutoSprint;

                Svc.Log.Information("Migration of the Misc settings is done");

                foreach (var entry in OldConfig.GatherSettings)
                {
                    int Id = entry.Id;
                    string Name = entry.Name;
                    int MimimumGp = entry.MinimumGP;
                    int DualClassAmount = entry.InitialGatheringItemMultiplier;

                    var currentEntry = C.GatherSettings.Where(x => x.Id == Id).FirstOrDefault();
                    if (currentEntry != null)
                    {
                        currentEntry.Id = Id;
                        currentEntry.Name = Name;
                        currentEntry.MinimumGp = MimimumGp;
                        currentEntry.DualClassCraftAmount = DualClassAmount;

                        var buffs = currentEntry.GatherBuffs.Buffs;
                        var oldBuffs = entry.Buffs;

                        SetBuff(buffs["BoonIncrease2"], oldBuffs.BoonIncrease2, oldBuffs.BoonIncrease2Gp, oldBuffs.BoonIncrease2MaxUse);
                        SetBuff(buffs["BoonIncrease1"], oldBuffs.BoonIncrease1, oldBuffs.BoonIncrease1Gp, oldBuffs.BoonIncrease1MaxUse);
                        SetBuff(buffs["Tidings"], oldBuffs.TidingsBool, oldBuffs.TidingsGp, oldBuffs.TidingsMaxUse);
                        SetBuff(buffs["YieldII"], oldBuffs.YieldII, oldBuffs.YieldIIGp, oldBuffs.YieldIIMaxUse);
                        SetBuff(buffs["YieldI"], oldBuffs.YieldI, oldBuffs.YieldIGp, oldBuffs.YieldIMaxUse);
                        SetBuff(buffs["BountifulYieldII"], oldBuffs.BountifulYieldII, oldBuffs.BountifulYieldIIGp, oldBuffs.BountifulYieldIIMaxUse);
                        SetBuff(buffs["BonusIntegrity"], oldBuffs.BonusIntegrity, oldBuffs.BonusIntegrityGp, oldBuffs.BonusIntegrityMaxUse);

                        currentEntry.GatherBuffs.BountifulMinItem = entry.Buffs.BountifulMinItem;
                    }
                    else
                    {
                        var newEntry = new GatherProfile
                        {
                            Id = Id,
                            Name = Name,
                            MinimumGp = MimimumGp,
                            DualClassCraftAmount = DualClassAmount,
                            GatherBuffs = new GatherBuffs()
                        };

                        var oldBuffs = entry.Buffs;

                        SetBuff(newEntry.GatherBuffs.Buffs["BoonIncrease2"], oldBuffs.BoonIncrease2, oldBuffs.BoonIncrease2Gp, oldBuffs.BoonIncrease2MaxUse);
                        SetBuff(newEntry.GatherBuffs.Buffs["BoonIncrease1"], oldBuffs.BoonIncrease1, oldBuffs.BoonIncrease1Gp, oldBuffs.BoonIncrease1MaxUse);
                        SetBuff(newEntry.GatherBuffs.Buffs["Tidings"], oldBuffs.TidingsBool, oldBuffs.TidingsGp, oldBuffs.TidingsMaxUse);
                        SetBuff(newEntry.GatherBuffs.Buffs["YieldII"], oldBuffs.YieldII, oldBuffs.YieldIIGp, oldBuffs.YieldIIMaxUse);
                        SetBuff(newEntry.GatherBuffs.Buffs["YieldI"], oldBuffs.YieldI, oldBuffs.YieldIGp, oldBuffs.YieldIMaxUse);
                        SetBuff(newEntry.GatherBuffs.Buffs["BountifulYieldII"], oldBuffs.BountifulYieldII, oldBuffs.BountifulYieldIIGp, oldBuffs.BountifulYieldIIMaxUse);
                        SetBuff(newEntry.GatherBuffs.Buffs["BonusIntegrity"], oldBuffs.BonusIntegrity, oldBuffs.BonusIntegrityGp, oldBuffs.BonusIntegrityMaxUse);

                        newEntry.GatherBuffs.BountifulMinItem = entry.Buffs.BountifulMinItem;

                        C.GatherSettings.Add(newEntry);
                    }
                }

                Svc.Log.Information("Migration of the Gather Profiles Completed");

                foreach (var gambaItem in OldConfig.GambaItemWeights)
                {
                    var existingItem = C.GambaItemWeights.FirstOrDefault(x => x.ItemId == gambaItem.ItemId);
                    if (existingItem != null)
                    {
                        // Update existing item
                        existingItem.Weight = gambaItem.Weight;
                        existingItem.Type = gambaItem.Type;
                    }
                    else
                    {
                        // Create new instance of the target type
                        C.GambaItemWeights.Add(new Gamba
                        {
                            ItemId = gambaItem.ItemId,
                            Weight = gambaItem.Weight,
                            Type = gambaItem.Type
                        });
                    }
                }
                C.GambaEnabled = OldConfig.GambaEnabled;
                C.GambaDelay = OldConfig.GambaDelay;
                C.GambaPreferSmallerWheel = OldConfig.GambaPreferSmallerWheel;
                C.GambaCreditsMinimum = OldConfig.GambaCreditsMinimum;

                Svc.Log.Information("Migration of the Gamba Weights are complete");

                foreach (var missionEntry in OldConfig.Missions)
                {
                    var key = missionEntry.Id;

                    var enabled = missionEntry.Enabled;
                    var manualMode = missionEntry.ManualMode;
                    var gatherProfileId = missionEntry.GatherSettingId;
                    bool useAny = (missionEntry.TurnInGold && missionEntry.TurnInSilver && missionEntry.TurnInASAP);
                    bool turninGold = missionEntry.TurnInGold;
                    bool turninSilver = missionEntry.TurnInSilver;
                    bool turninBronze = missionEntry.TurnInASAP;
                    if (useAny)
                    {
                        turninGold = false;
                        turninSilver = false;
                        turninBronze = false;
                    }

                    if (C.MissionConfig.TryGetValue(key, out var value))
                    {
                        value.Enabled = enabled;
                        value.ManualMode = enabled;
                        value.GatherProfileId = gatherProfileId;
                        value.AutoTurnin = useAny;
                        value.TurninGold = turninGold;
                        value.TurninSilver = turninSilver;
                        value.TurninBronze = turninBronze;
                    }
                    else
                    {
                        C.MissionConfig.Add(key, new MissionSettings
                        {
                            Enabled = enabled,
                            ManualMode = manualMode,
                            GatherProfileId = gatherProfileId,
                            AutoTurnin = useAny,
                            TurninGold = turninGold,
                            TurninSilver = turninSilver,
                            TurninBronze = turninBronze,
                        });
                    }
                }

                Svc.Log.Information("Migration of the mission configs are now complete");
                Svc.Log.Information("Migration complete, Saving the config and updating config version");
                C.ConfigVersion = 1;
                C.Save();
            }
            if (C.ConfigVersion == 1)
            {
                foreach (var mission in C.MissionConfig)
                {
                    if (mission.Key == 508 || mission.Key == 509)
                        { continue; }

                    if (GatheringUtil.FishingPreset.TryGetValue(mission.Key, out var preset) && preset.FishingPreset.Count > 0)
                    {
                        mission.Value.Use_BuildinPreset = true;
                    }
                }
                C.ConfigVersion = 2;
                C.Save();
            }
            if (C.ConfigVersion == 2)
            {
                // 🔴 這一行同時挑中了兩個相反的前提：篩選條件是 `x.Key > 544`，而
                //    SheetMissionDict 在台服 7.20 的鍵集合**正好就是 1..544**
                //    （row 545..1072 是第二顆星 Phaenna 的預留列，Name 全空，建表時被跳過）。
                //    也就是說這個 Where 選出來的每一個 key，SheetMissionDict 都一定沒有。
                //    目前沒炸只是因為 UpdateConfigMissionList() 先跑，MissionConfig 裡不會有 >544 的 key；
                //    但這是外部順序保證，不是這一行自己有守。而且遷移是在外掛載入時跑的 ——
                //    這裡丟例外＝外掛直接載不起來。
                foreach (var mission in C.MissionConfig.Where(x => x.Key > 544
                                                                   && CosmicHelper.SheetMissionDict.TryGetValue(x.Key, out var m)
                                                                   && m.Jobs.Contains(18)))
                {
                    var id = mission.Key;
                    if (GatheringUtil.FishingPreset.TryGetValue(id, out var fishPreset) && fishPreset.FishingPreset.Count > 0)
                    {
                        if (C.MissionConfig.TryGetValue(id, out var config))
                        {
                            config.Use_BuildinPreset = true;
                        }
                    }
                }
                C.ConfigVersion = 3;
                C.Save();
            }
            if (C.ConfigVersion == 3)
            {
                foreach (var mission in C.MissionConfig)
                {
                    if (mission.Value.Times.Count > 0)
                    {
                        foreach (var time in mission.Value.Times)
                        {
                            mission.Value.TurninRecords.Add(new TurninData
                            {
                                Time = time,
                                State = TurninState.None,
                            });
                        }

                        // This is here so we save some file spacing. No reason to keep old data at this point
                        mission.Value.Times.Clear();
                    }
                }

                C.ConfigVersion = 4;
                C.Save();
            }
            if (C.ConfigVersion == 4)
            {
                // Updating the times to properly average each kind of turnin type
                foreach (var mission in C.MissionConfig)
                {
                    var stats = mission.Value;

                    if (stats.TurninRecords.Any())
                    {
                        stats.BestTime = stats.TurninRecords.Min(t => t.Time);
                        stats.AverageTime = stats.TurninRecords.Average(t => t.Time);

                        // Calculate per-state averages
                        var bronzeRecords = stats.TurninRecords.Where(t => t.State == TurninState.Bronze).ToList();
                        var silverRecords = stats.TurninRecords.Where(t => t.State == TurninState.Silver).ToList();
                        var goldRecords = stats.TurninRecords.Where(t => t.State == TurninState.Gold).ToList();
                        var criticalRecords = stats.TurninRecords.Where(t => t.State == TurninState.Critical).ToList();

                        stats.AverageBronzeTime = bronzeRecords.Any() ? bronzeRecords.Average(t => t.Time) : 0;
                        stats.AverageSilverTime = silverRecords.Any() ? silverRecords.Average(t => t.Time) : 0;
                        stats.AverageGoldTime = goldRecords.Any() ? goldRecords.Average(t => t.Time) : 0;
                        stats.AverageCriticalTime = criticalRecords.Any() ? criticalRecords.Average(t => t.Time) : 0;
                    }
                }
                C.ConfigVersion = 5;
                C.Save();
            }
            if (C.ConfigVersion == 5)
            {
                List<uint> CriticalMission = new() { 542, 1037, 1038, 1039 };
                foreach (var mission in CriticalMission)
                {
                    if (C.MissionConfig.TryGetValue(mission, out var config))
                    {
                        config.Use_BuildinPreset = true;
                    }
                }
                C.ConfigVersion = 6;
                C.Save();
            }
            if (C.ConfigVersion == 6)
            {
                IceLogging.Info("Just in case peeps messed with this before it was ready... resetting shopping list");
                ShoppingTab.CheckConfigState();
                C.CosmoShopping.Clear();
                C.CosmoShoppingOrder.Clear();
                C.ConfigVersion = 7;
                C.Save();
            }
            if (C.ConfigVersion == 7)
            {
                List<uint> NewFishPreset = new() { 1004, 1005 };
                foreach (var mission in NewFishPreset)
                {
                    if (C.MissionConfig.TryGetValue(mission, out var config))
                    {
                        config.Use_BuildinPreset = true;
                    }
                }
                C.ConfigVersion = 8;
                C.Save();
            }
            if (C.ConfigVersion == 8)
            {
                Dictionary<int, int> profileTransfer = new();

                // Don't clear - we want to keep the default profile at [0]
                // But we need to handle if the old system also has a profile at ID 0

                foreach (var oldBuff in C.GatherSettings)
                {
                    var oldId = oldBuff.Id;
                    var newId = oldId;

                    // Special handling for ID 0 - skip it if it already exists and is the default
                    if (oldId == 0 && C.GatherProfiles.ContainsKey(0))
                    {
                        // Update the existing default profile instead of creating a duplicate
                        C.GatherProfiles[0].Name = oldBuff.Name;
                        C.GatherProfiles[0].DualClassCraftAmount = oldBuff.DualClassCraftAmount;
                        C.GatherProfiles[0].MinimumGp = oldBuff.MinimumGp;
                        C.GatherProfiles[0].GatherBuffs = oldBuff.GatherBuffs;
                        profileTransfer[oldId] = 0;
                        continue;
                    }

                    // If this ID is already taken, find the next available ID
                    while (C.GatherProfiles.ContainsKey(newId))
                    {
                        newId = C.GatherProfiles.Keys.Max() + 1;
                    }

                    C.GatherProfiles[newId] = new GatherProfile()
                    {
                        Name = oldBuff.Name,
                        DualClassCraftAmount = oldBuff.DualClassCraftAmount,
                        MinimumGp = oldBuff.MinimumGp,
                        GatherBuffs = oldBuff.GatherBuffs
                    };

                    profileTransfer[oldId] = newId;
                }

                // Update mission references
                foreach (var mission in C.MissionConfig)
                {
                    var oldId = mission.Value.GatherProfileId;
                    if (profileTransfer.TryGetValue(oldId, out var newId))
                    {
                        mission.Value.GProfileId = newId;
                    }
                }

                // Clear the old list so it doesn't persist in the config
                C.GatherSettings.Clear();

                C.ConfigVersion = 9;
                C.Save();
            }
            if (C.ConfigVersion == 9)
            {
                // This is here because I added some new gather buffs to the config, and it needs to be updated on the off chance that (in my case, me) I didn't migrate properly
                // ./added them correctly
                var defaultBuffs = new GatherBuffs().Buffs;

                foreach (var profile in C.GatherProfiles.Values)
                {
                    foreach (var (key, defaultBuff) in defaultBuffs)
                    {
                        if (!profile.GatherBuffs.Buffs.ContainsKey(key))
                        {
                            profile.GatherBuffs.Buffs[key] = new GatherBuff
                            {
                                Enabled = defaultBuff.Enabled,
                                MinGp = defaultBuff.MinGp,
                                MaxUse = defaultBuff.MaxUse
                            };
                        }
                    }
                }
                C.ConfigVersion = 10;
                C.Save();
            }
            if (C.ConfigVersion == 10)
            {
                if (C.MissionConfig.TryGetValue(1000, out var profile))
                {
                    profile.Use_BuildinPreset = true;
                }
                C.ConfigVersion = 11;
                C.Save();
            }
            if (C.ConfigVersion == 11)
            {
                MigrateMechaGlobalSlidersToPerSkill();
                C.ConfigVersion = 12;
                C.Save();
            }
        }

        /// <summary>
        /// 機甲行動的兩個<b>全域</b>形狀滑桿（宇宙火焰噴射器扇形角度／宇宙鑽頭矩形長度）
        /// 收斂進 per-skill 覆蓋。
        ///
        /// 🔑 <b>為什麼要搬</b>（2026-08-08 使用者原話：「這兩個滑桿還有用嗎 還是已經並到單項技能了?」）：
        /// 自從有了 per-skill 覆蓋之後，同一個維度有兩個地方可以調，而且優先序是
        /// 「per-skill 覆蓋 &gt; 全域舊鍵 &gt; 內建值」——使用者在全域滑桿上拉半天卻沒反應
        /// （因為 per-skill 已經有覆蓋）是完全可能的，而且沒有任何徵兆。
        ///
        /// 🔴 <b>這是保值不是改行為</b>：搬完之後每一個技能的**效果值逐一相同**。
        /// <list type="bullet">
        ///   <item>舊鍵的值不等於當初的預設 ⇒ 那是使用者自己調的，搬進對應技能的覆蓋格；</item>
        ///   <item>等於預設 ⇒ 不寫覆蓋，改由程式內建常數
        ///         （<c>MechaActionShapes.DefaultConeAngleDeg</c> / <c>DrillCalibratedLength</c>，
        ///         數值與舊預設完全一樣）接手。這樣設定畫面上的「＝預設」標記才不會
        ///         整排變成「已調整」的星號。</item>
        ///   <item>該維度<b>已經</b>有 per-skill 覆蓋 ⇒ 完全不動它。覆蓋本來就贏過舊鍵，
        ///         覆寫它才是真的改了使用者的設定。</item>
        /// </list>
        /// ⚠️ 舊鍵本身<b>留在設定檔裡不刪</b>：萬一日後發現搬錯，原值還在；
        /// 程式從這一版起不再讀它們。
        /// </summary>
        private static void MigrateMechaGlobalSlidersToPerSkill()
        {
            // 這兩個數字就是舊鍵在 2026-08-08 當下的預設值。
            // ⚠️ 不要改成引用 MechaActionShapes 的常數然後「順手」調整——
            //    這裡要比對的是**舊鍵的預設**，不是新的內建值；兩者現在剛好相等，
            //    但日後若調了內建值，這個比對必須維持在舊的那個數字上。
            const float legacyConeDefault = 240f;
            const float legacyDrillDefault = 4f;

            Migrate(MechaActionShapes.CosmicFlamethrowerActionId, C.MechaConeAngleDeg, legacyConeDefault,
                ov => ov.AngleDeg != null, (ov, v) => ov.AngleDeg = v, "扇形角度");

            Migrate(MechaActionShapes.CosmicDrillActionId, C.MechaDrillLength, legacyDrillDefault,
                ov => ov.Primary != null, (ov, v) => ov.Primary = v, "矩形長度");

            static void Migrate(uint actionId, float legacyValue, float legacyDefault,
                Func<MechaShapeOverride, bool> alreadyOverridden, Action<MechaShapeOverride, float> apply, string what)
            {
                // 沒調過就不留痕跡——內建值與舊預設相同，效果一樣。
                if (Math.Abs(legacyValue - legacyDefault) < 0.001f)
                    return;

                if (C.MechaShapeOverrides.TryGetValue(actionId, out var existing) && existing != null)
                {
                    if (alreadyOverridden(existing))
                    {
                        IceLogging.Info(
                            $"機甲形狀設定遷移：技能 {actionId} 的{what}已經有 per-skill 覆蓋，"
                            + $"保留覆蓋、不套用舊的全域值 {legacyValue:F1}（覆蓋本來就優先）。",
                            "[ConfigMigrator]");
                        return;
                    }
                }
                else
                {
                    existing = new MechaShapeOverride();
                    C.MechaShapeOverrides[actionId] = existing;
                }

                apply(existing, legacyValue);
                IceLogging.Info(
                    $"機甲形狀設定遷移：舊的全域{what} {legacyValue:F1} 已搬進技能 {actionId} 的個別覆蓋（效果值不變）。",
                    "[ConfigMigrator]");
            }
        }

        public static void UpdateConfigMissionList()
        {
            // 🔴 key 0 是垃圾資料，而且是**已經存在既有使用者設定檔裡**的垃圾：
            //    MissionTimer.AbandonMission() 以前會在 currentMission 已歸零時
            //    new 出 C.MissionConfig[0] 並存檔（實測使用者的檔案裡 failedCounters 已 118）。
            //    0 不是任務表的 row，任何「迭代 MissionConfig 再索引 SheetMissionDict」
            //    的地方都會被它炸掉。寫入端的哨兵已經補在 MissionTimer，
            //    這裡負責清掉既有檔案裡的殘留（Remove 回 false 就不存檔，冪等）。
            if (C.MissionConfig.Remove(0))
            {
                IceLogging.Warning("已從任務設定中移除無效的任務 ID 0。", "[Config Migrator]");
                C.Save();
            }

            foreach (var entry in CosmicHelper.SheetMissionDict)
            {
                var id = entry.Key;
                if (!C.MissionConfig.ContainsKey(id))
                {
                    C.MissionConfig[id] = new MissionSettings();
                    C.Save();
                }
            }
        }

        private static void SetBuff(GatherBuff buff, bool enabled, int minGp, int maxUse)
        {
            buff.Enabled = enabled;
            buff.MinGp = minGp;
            buff.MaxUse = maxUse;
        }

        public static void CheckMissions()
        {
            foreach (var mission in C.MissionConfig)
            {
                bool any = mission.Value.AutoTurnin
                        || mission.Value.TurninGold
                        || mission.Value.TurninSilver
                        || mission.Value.TurninBronze;

                if (!any)
                {
                    mission.Value.AutoTurnin = true;
                    IceLogging.Info($"{mission.Key} did not have a set turnin. Making it auto-turnin now");
                }
            }
            C.Save();
        }
    }
}
