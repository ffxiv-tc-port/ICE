using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Config;
using ICE.Ui.MainUi.ModeSelect;
using ICE.Ui.MainUi.Settings.Settings_Table;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using static ICE.Enums.MissionAttributes;
using static ICE.Utilities.CosmicHelper;
using static ICE.Utilities.ExcelHelper;

namespace ICE;

public sealed partial class ICE
{
    public static unsafe void DictionaryCreation()
    {
        var wk = WKSManager.Instance();

        foreach (var entry in MoonMissionSheet)
        {
            Dictionary<ushort, CosmicHelper.CraftingInfo> crafts_Main = new();
            Dictionary<ushort, CosmicHelper.CraftingInfo> crafts_Pre = new();
            Dictionary<uint, int> gathering_Min = new();
            HashSet<uint> jobs = new();
            Dictionary<int, int> relicXp = new();
            bool isExpert = false;

            uint keyId = entry.RowId;
            string missionName = entry.Name.ToString();
            missionName = missionName.Replace("<nbsp>", " ");
            missionName = missionName.Replace("<->", "");

            if (missionName == "")
                continue;

            jobs.Add(entry.ClassJobCategory[0].RowId - 1);
            var Job2 = entry.ClassJobCategory[1].RowId;
            if (Job2 != 0)
            {
                jobs.Add(Job2 - 1);
            }
            uint timeLimit = entry.MissionTime;
            uint silver = entry.SilverStarRequirement;
            uint gold = entry.GoldStarRequirement;
            HashSet<uint> previousMissionId = new() { entry.LockedBehind.RowId };

            uint timeAndWeather = entry.WKSMissionLotterySpecialCond.RowId;
            uint startTime = 0;
            uint endTime = 0;
            CosmicWeather weather = CosmicWeather.FairSkies;
            if (!CosmicHelper.WeatherSelection.Contains(timeAndWeather))
            {
                var timeSheet = Svc.Data.GetExcelSheet<WKSMissionLotterySpecialCond>().GetRow(timeAndWeather);
                startTime = timeSheet.Unknown1; // Start Time
                endTime = timeSheet.Unknown2; // End Time
            }
            else
            {
                weather = (CosmicWeather)(timeAndWeather - 12);
                // TODO: Go back and assign enums based on the value instead... or just directly give it a flag. Unsure. Feels dirty
            }

            uint rank = entry.LevelGroup;
            bool isCritical = entry.IsSpecialQuest;

            uint RecipeId = entry.WKSMissionRecipe.RowId;

            uint toDoValue = entry.MissionToDo[0].RowId;

            var wksToDo = ToDoSheet.GetRow(toDoValue);
            uint missionText = wksToDo.WKSMissionText.Value.RowId;
            var marker = MarkerSheet.GetRow(wksToDo.Unknown13);
            // ⚠️ 這個 545 是**國際服的邊界，直接寫死在原始碼裡**，而且它比其他寫死的表嚴重：
            //    算出來的 territoryId 會原封不動存進 SheetMissionDict.TerritoryId，
            //    是排程器（傳送、導航、釣點/採集點查表）真的會拿去用的值，不是純顯示。
            //    台服沒有第二顆星的資料可以驗證這個分界，所以**這裡刻意不動它**；
            //    改成在載入時把分配結果印一行 Information 出來 —— 見 ReportMissionTerritorySplit()。
            uint territoryId = 1237;
            if (keyId < 545)
            {
                territoryId = 1237;
            }
            else
            {
                territoryId = 1291;
            }
            // TODO: Make this set the correct territoryId once new planets are added and we figure out where it is.

            int _x = marker.Unknown1 - 1024;
            int _y = marker.Unknown2 - 1024;
            int radius = marker.Unknown3;

            MissionAttributes attributes = None;

            if (CosmicHelper.CrafterJobList.Overlaps(jobs) && CosmicHelper.GatheringJobList.Overlaps(jobs))
            {
                if (jobs.Contains(18))
                    attributes = Craft | Fish;
                else
                    attributes = Craft | Gather;
            }
            else if (CosmicHelper.CrafterJobList.Overlaps(jobs))
            {
                // Just making this nice and simple. Making all crafter missions just check for crafting.
                // Then going to add critical after
                attributes = Craft;
            }
            else if (CosmicHelper.GatheringJobList.Overlaps(jobs))
            {
                if (jobs.Contains(18))
                    attributes |= Fish;
                else
                    attributes |= Gather;

                attributes = missionText switch
                {
                    103 => Gather | Limited,
                    104 => Gather | ScoreTimeRemaining,
                    105 => Gather,
                    106 => Gather | ScoreChains,
                    107 => Gather | ScoreGatherersBoon,
                    108 => Gather | ScoreChains | ScoreGatherersBoon,
                    109 or 111 => Gather | Collectables,
                    110 => Gather | ReducedItems | ScoreTimeRemaining,
                    112 => Gather | ReducedItems,
                    113 => Fish | ScoreVariety | ScoreTimeRemaining,
                    114 or 115 => Fish | ScoreTimeRemaining,
                    116 => Fish | Limited | ScoreVariety,
                    117 => Fish | Limited | ScoreLargestSize,
                    118 => Fish | Limited | Collectables,
                    119 or 121 => Fish,
                    120 => Fish | ScoreLargestSize,
                    122 => Fish | Collectables,
                    >= 123 and <= 134 => Craft | Gather, // Dual class
                    >= 135 and <= 138 => Craft | Fish,  // Dual class
                    139 => jobs.Contains(18) ? Fish : Gather, // Critical
                    141 => Fish,
                    _ => None
                };
            }

            attributes |= isCritical ? Critical : None;
            attributes |= weather != CosmicWeather.FairSkies ? ProvisionalWeather : None;
            attributes |= (startTime != 0 || endTime != 0) ? ProvisionalTimed : None;
            attributes |= !previousMissionId.Contains(0) ? ProvisionalSequential : None;

            // - - - HEY. BRONZE SCORE IS KEPT HERE - - - //
            uint bronze = wksToDo.Unknown2; // Bronze score for Score missions

            if (CrafterJobList.Overlaps(jobs))
            {
                var wksRecipeRow = wksMissionRecipe.GetRow(RecipeId);

                if (isCritical) // Criticals are sus
                {
                    var itemAmount = 3; // It's a pass/fail progress, you need to go till you are full on score
                    if (keyId < 535)
                        itemAmount = 3;
                    else if (keyId < 1039)
                        itemAmount = 2;

                    var missionRecipeRow = RecipeSheet?.Where(e => e.RowId == wksRecipeRow.Recipe[0].RowId).FirstOrDefault();
                    var itemId = missionRecipeRow.Value.ItemResult. RowId;
                    var itemName = ItemSheet.GetRow(itemId).Name.ToString();
                    var craftingType = missionRecipeRow.Value.CraftType.Value.RowId;
                    IceLogging.Verbose($"Recipe Row ID: {missionRecipeRow.Value.RowId} | for item: {itemId} | {itemName}", debugOnly: true);
                    var item1RecipeId = missionRecipeRow.Value.RowId;

                    crafts_Main[(ushort)item1RecipeId] = new CraftingInfo()
                    {
                        ItemId = itemId,
                        RequiredAmount = itemAmount,
                    };
                }
                else
                {
                    // Reason for the following code is this:
                    // If it's a pre-craft, it should be further down the list, which means adding it first to the pre-crafts
                    // If it's required, then all of them SHOULD... be required. *-shrugs-*
                    List<ushort> recipeIds = new();
                    for (int x = 2; x >= 0; x--)
                    {
                        var recipeId = (ushort)wksRecipeRow.Recipe[x].Value.RowId;
                        if (recipeId != 0 && !recipeIds.Contains(recipeId))
                            recipeIds.Add(recipeId);
                    }

                    if (recipeIds.Count == 1)
                    {
                        // Only a single item exist in this table. So into the maincrafts it goes
                        IceLogging.Verbose($"Mission: {keyId} had 1 recipie", debugOnly:true);
                        var recipeId = recipeIds[0];
                        var recipeRow = RecipeSheet.GetRow(recipeId);
                        var itemId = recipeRow.ItemResult.RowId;
                        var amountNeeded = wksToDo.RequiredItemQuantity[0];
                        if (amountNeeded == 0)
                        {
                            // this should never happen. But on the off chance that square decides to be a dick and change it's place
                            amountNeeded = 1;
                        }
                        var requiredItem = recipeRow.Ingredient[0].RowId;
                        var requiredAmount = recipeRow.AmountIngredient[0];
                        var requiredItem2 = recipeRow.Ingredient[1].RowId;
                        var requiredAmount2 = recipeRow.AmountIngredient[1];

                        if (requiredItem2 != 0)
                        {
                            crafts_Main[recipeId] = new()
                            {
                                ItemId = itemId,
                                RequiredAmount = amountNeeded,
                                RequiredItems = new()
                                {
                                    [requiredItem] = requiredAmount,
                                    [requiredItem2] = requiredAmount2
                                }
                            };
                        }
                        else
                        {
                            crafts_Main[recipeId] = new()
                            {
                                ItemId = itemId,
                                RequiredAmount = amountNeeded,
                                RequiredItems = new()
                                {
                                    [requiredItem] = requiredAmount
                                }
                            };
                        }

                        isExpert |= recipeRow.IsExpert;
                        if (isExpert)
                            IceLogging.Verbose($"{recipeRow.RowId} is an expert craft", debugOnly: true);
                    }
                    else if (recipeIds.Count == 2)
                    {
                        IceLogging.Verbose($"Mission: {keyId} had 2 recipies", debugOnly: true);
                        // First one is going to be the main item that you need.

                        var recipeId = recipeIds[0];
                        var recipeRow = RecipeSheet.GetRow(recipeId);
                        var itemId = recipeRow.ItemResult.RowId;
                        var amountNeeded = wksToDo.RequiredItemQuantity[0];
                        if (amountNeeded == 0)
                        {
                            // this should never happen. But on the off chance that square decides to be a dick and change it's place
                            amountNeeded = 1;
                        }
                        var requiredItem = recipeRow.Ingredient[0].RowId;
                        var requiredAmount = recipeRow.AmountIngredient[0];
                        crafts_Main[recipeId] = new()
                        {
                            ItemId = itemId,
                            RequiredAmount = amountNeeded,
                            RequiredItems = new()
                            {
                                [requiredItem] = requiredAmount
                            }
                        };

                        isExpert |= recipeRow.IsExpert;
                        if (isExpert)
                            IceLogging.Verbose($"{recipeRow.RowId} is an expert craft", debugOnly: true);

                        // Second one is going to be the pre-crafting mat that you need
                        var preRecipeId = recipeIds[1];
                        var preRecipeRow = RecipeSheet.GetRow(preRecipeId);
                        var preItemId = preRecipeRow.ItemResult.RowId;
                        var preAmountNeeded = requiredAmount;

                        var crateId = preRecipeRow.Ingredient[0].RowId;

                        crafts_Pre[preRecipeId] = new()
                        {
                            ItemId = preItemId,
                            RequiredAmount = preAmountNeeded,
                            RequiredItems = new()
                            {
                                [crateId] = preAmountNeeded
                            }
                        };

                    }
                    else if (recipeIds.Count == 3)
                    {
                        IceLogging.Verbose($"Mission: {keyId} had 3 recipies", debugOnly: true);
                        // all of these should be valid. 
                        for (int i = 0; i < recipeIds.Count; i++)
                        {
                            // Only a single item exist in this table. So into the maincrafts it goes
                            var recipeId = recipeIds[i];
                            var recipeRow = RecipeSheet.GetRow(recipeId);
                            var itemId = recipeRow.ItemResult.RowId;
                            var amountNeeded = wksToDo.RequiredItemQuantity[i];
                            if (amountNeeded == 0)
                            {
                                // this should never happen. But on the off chance that square decides to be a dick and change it's place
                                amountNeeded = 1;
                            }
                            var requiredItem = recipeRow.Ingredient[0].RowId;
                            var requiredAmount = recipeRow.AmountIngredient[0];
                            crafts_Main[recipeId] = new()
                            {
                                ItemId = itemId,
                                RequiredAmount = amountNeeded,
                                RequiredItems = new()
                                {
                                    [requiredItem] = requiredAmount
                                }
                            };
                            isExpert |= recipeRow.IsExpert;
                            if (isExpert)
                                IceLogging.Verbose($"{recipeRow.RowId} is an expert craft", debugOnly: true);
                        }
                    }

                    // This is just a general sanity check in itself for mission where there isn't a required item count, but moreso just needs score. 
                    if (crafts_Main.Count == 0)
                    {
                        // These are missions that don't require an item, but for the sanity check of it all, going to just have it be 1. 
                        // Still need to hardcode the bronze scores in though

                        foreach (var item in crafts_Pre)
                        {
                            item.Value.RequiredAmount = 1;
                            crafts_Main.Add(item);
                            crafts_Pre.Remove(item);
                        }
                    }
                }
            }

            // - - - Attribute check for experts here cause needs to be done post crafting - - - - // 
            if (isExpert)
                attributes |= ExpertCraft;

            if (GatheringJobList.Overlaps(jobs))
            {
                var todoRow = ToDoSheet.GetRow(toDoValue);

                if (todoRow.RequiredItem[0].RowId != 0) // First item in the gathering list. Shouldn't be 0...
                {
                    var minAmount = todoRow.RequiredItemQuantity[0].ToInt();
                    var itemInfoId = MoonItemInfoSheet.GetRow(todoRow.RequiredItem[0].RowId).Item.RowId;
                    if (!gathering_Min.ContainsKey(itemInfoId))
                    {
                        gathering_Min.Add(itemInfoId, minAmount);
                    }
                }
                if (todoRow.RequiredItem[1].RowId != 0) // First item in the gathering list. Shouldn't be 0...
                {
                    var minAmount = todoRow.RequiredItemQuantity[1].ToInt();
                    var itemInfoId = MoonItemInfoSheet.GetRow(todoRow.RequiredItem[1].RowId).Item.RowId;
                    if (!gathering_Min.ContainsKey(itemInfoId))
                    {
                        gathering_Min.Add(itemInfoId, minAmount);
                    }
                }
                if (todoRow.RequiredItem[2].RowId != 0) // First item in the gathering list. Shouldn't be 0...
                {
                    var minAmount = todoRow.RequiredItemQuantity[2].ToInt();
                    var itemInfoId = MoonItemInfoSheet.GetRow(todoRow.RequiredItem[2].RowId).Item.RowId;
                    if (!gathering_Min.ContainsKey(itemInfoId))
                    {
                        gathering_Min.Add(itemInfoId, minAmount);
                    }
                }
            }

            // Col 3 -> Cosmocredits - Unknown 0
            // Col 4 -> Lunar Credits - Unknown 1
            // Col 7 ->  Lv. 1 Type - Unknown 12
            // Col 8 ->  Lv. 1 Exp - Unknown 2
            // Col 10 -> Lv. 2 Type - Unknown 13
            // Col 11 -> Lv. 2 Exp - Unknown 3
            // Col 13 -> Lv. 3 Type - Unknown 14
            // Col 14 -> Lv. 3 Exp - Unknown 4

            // Something to note here, a mission can only have a max of 3 types of XP at a time.
            // Which is why there's only 3 entries.

            uint Cosmo = ExpSheet.GetRow(keyId).Unknown0;
            uint Lunar = ExpSheet.GetRow(keyId).Unknown1;
            uint rewardItemId = 0;
            uint rewardItemAmount = 0;

            if (ExpSheet.GetRow(keyId).Unknown2 != 0)
            {
                var xp1Kind = ExpSheet.GetRow(keyId).Unknown12;
                var xp1Amount = ExpSheet.GetRow(keyId).Unknown2;
                relicXp[xp1Kind] = xp1Amount;
            }
            if (ExpSheet.GetRow(keyId).Unknown3 != 0)
            {
                var xp2Kind = ExpSheet.GetRow(keyId).Unknown13;
                var xp2Amount = ExpSheet.GetRow(keyId).Unknown3;
                relicXp[xp2Kind] = xp2Amount;
            }
            if (ExpSheet.GetRow(keyId).Unknown4 != 0)
            {
                var xp3Kind = ExpSheet.GetRow(keyId).Unknown14;
                var xp3Amount = ExpSheet.GetRow(keyId).Unknown4;
                relicXp[xp3Kind] = xp3Amount;
            }
            if (ExpSheet.GetRow(keyId).Unknown15 != 0)
            {
                rewardItemId = ExpSheet.GetRow(keyId).Unknown15; // Column 15 | Item
                rewardItemAmount = ExpSheet.GetRow(keyId).Unknown8;
            }

            if (!SheetMissionDict.ContainsKey(keyId))
            {
                SheetMissionDict[keyId] = new CosmicInfo()
                {
                    Name = missionName,
                    Jobs = jobs,
                    ToDoId = toDoValue,
                    Rank = rank,
                    Attributes = attributes,
                    Weather = weather,
                    StartTime = startTime,
                    EndTime = endTime,
                    CosmoCredit = Cosmo,
                    LunarCredit = Lunar,
                    PreviousMissions = previousMissionId,
                    RelicXpInfo = relicXp,
                    BronzeScore = bronze,
                    SilverScore = silver,
                    GoldScore = gold,

                    RewardItem = rewardItemId,
                    RewardItemAmount = rewardItemAmount,

                    MapPosition = new Vector2(_x, _y),
                    Radius = radius,
                    TerritoryId = territoryId,
                    MarkerId = marker.RowId,

                    Gathering_Min = gathering_Min,

                    Crafts_Main = crafts_Main,
                    Crafts_Pre = crafts_Pre,
                    IsExpert = isExpert
                };
            }
        }

        foreach (var Icon in LeveAssignmentSheet)
        {
            var iconId = Icon.RowId;

            if (iconId is 2 or 3 or 4)
            {
                iconId += 14;
            }
            else if (iconId > 4 && iconId < 13)
            {
                iconId += 3;
            }
            else
                continue;

            if (Icon.Name != "" && Icon.Icon is { } jobicon)
            {
                if (Svc.Texture.TryGetFromGameIcon(jobicon, out var texture))
                {
                    JobIconDict.TryAdd(iconId, texture);
                }
            }
        }

        for (int i = 0; i < GreyIconList.Count; i++)
        {
            var slot = i + 8;
            var iconId = GreyIconList[i];

            if (Svc.Texture.TryGetFromGameIcon(iconId, out var texture))
            {
                GreyTexture.TryAdd((uint)slot, texture);
            }
        }

        CosmicHelper.LoadMissionScores();

        foreach (var entry in C.ScoreKeeper)
        {
            if (SheetMissionDict.TryGetValue(entry.Key, out var missionEntry) && missionEntry.ClassScore == 0)
                missionEntry.ClassScore = entry.Value;
        }

        foreach (var entry in SheetMissionDict)
        {
            var missionId = entry.Key;

            if (MissionScoreDict.TryGetValue(missionId, out var score) && score != 0)
            {
                entry.Value.ClassScore = score;
            }
            else if (C.ScoreKeeper.TryGetValue(missionId, out var storedScore) && storedScore != 0)
            {
                entry.Value.ClassScore = storedScore;
            }
            else
            {
                entry.Value.ClassScore = 0;
            }
        }

        foreach (var item in MoonItemInfoSheet)
        {
            var itemId = item.Item.RowId;
            if (itemId == 0) continue;
            string itemName = ItemSheet.GetRow(itemId).Name.ToString();
            var type = item.WKSItemSubCategory.RowId;
            IceLogging.Debug($"RowID: {item.RowId} | ID: {itemId} | Name: {itemName}", debugOnly: true);

            if (CosmicHelper.GatheringItems.TryGetValue(itemName, out var itemEntry))
            {
                itemEntry.itemIds.Add(itemId);
            }
            else
            {
                IceLogging.Debug($"Adding a new entry: {itemName}", debugOnly: true);

                CosmicHelper.GatheringItems[itemName] = new()
                {
                    Type = item.WKSItemSubCategory.RowId,
                    itemIds = new HashSet<uint> { itemId },
                };
            }
        }

        foreach (var weather in WeatherIds)
        {
            if (Svc.Texture.TryGetFromGameIcon(weather.Value, out var texture))
            {
                WeatherIconDict[weather.Key] = texture;
            }
        }

        foreach (var supply in Svc.Data.GetExcelSheet<WKSItemInfo>())
        {
            if (supply.Item.RowId == 0) continue;
            if (supply.WKSItemSubCategory.RowId != 2 && supply.WKSItemSubCategory.RowId != 5) continue;

            var itemId = supply.Item.RowId;
            var kind = supply.WKSItemSubCategory.RowId;
            var name = Svc.Data.GetExcelSheet<Item>().GetRow(itemId).Name.ToString();
            IceLogging.Info($"Name: {name} | ItemID: {itemId} | kind: {kind}");

            // Seafood/fish
            if (kind == 2)
            {
                if (GatheringUtil.MoonFish.TryGetValue(name, out var fishList))
                {
                    if (!fishList.Contains(itemId))
                    {
                        fishList.Add(itemId);
                    }
                }
                else
                {
                    GatheringUtil.MoonFish[name] = new() { itemId };
                }
            }
            // Baits
            else if (kind == 5)
            {
                if (GatheringUtil.MoonBaits.TryGetValue(name, out var fishBait))
                {
                    if (!fishBait.Contains(itemId))
                    {
                        fishBait.Add(itemId);
                    }
                }
                else
                {
                    GatheringUtil.MoonBaits[name] = new() { itemId };
                }
            }
        }

        foreach (var mission in C.MissionConfig)
        {
            if (!C.GatherProfiles.ContainsKey(mission.Value.GProfileId))
            {
                mission.Value.GProfileId = 0;
            }
        }

        // This is here, merely for the reason of I want a random joke to show up every time they boot up the plugin. I even added some more!
        var random = new Random();
        modeSelect_TableInfo.jokeId = random.Next(0, modeSelect_TableInfo.JokeList.Count-1);

        if (!C.ShowManualMode)
        {
            foreach (var mission in C.MissionConfig)
            {
                mission.Value.ManualMode = false;
            }
        }

        // Just a safety check for fishing missions. 
        // If a mission has a preset, and it's not on by default/on the off chance someone turned it off and didn't input a preset name, will auto enable
        foreach (var mission in C.MissionConfig)
        {
            var id = mission.Key;
            if (GatheringUtil.FishingPreset.TryGetValue(id, out var fishProfile))
            {
                if (fishProfile.FishingPreset.Count > 0)
                {
                    // we have a fishing preset here. Time to check to see if we need to enable it (if it doesn't have a custom profile)
                    if (!mission.Value.Use_BuildinPreset && mission.Value.AutoHookPresetName == string.Empty)
                        mission.Value.Use_BuildinPreset = true;
                }
            }
        }

        // quick check on gathering profiles. We should always have a "default" profile set
        // and for specifically first time creation, if the default profile is the only existing, then we should go ahead and import -> set all the profiles
        if (!C.GatherProfiles.TryGetValue(0, out var profileDefault))
        {
            // We somehow are missing a default profile. . . which is honestly quite impressive how the fuck people manage to do this. 
            C.GatherProfiles.Add(0, new GatherProfile
            {
                Id = 0,
                Name = "Default"
            });
        }
        if (C.GatherProfiles.Count == 1)
        {
            // This is a first time setup more than likely (nobody at this point has just the "default" profile for things, that's insanity)
            // So going to inialize the first time setup and auto-select all the profiles at once

            GatherSettings.SetupAllProfiles();
        }


        C.SaveDebounced();
    }

    /// <summary>
    /// 盤點「原始碼裡寫死的 ID 表」有多少筆在**這個客戶端**查不到對應資料，並記一次 Information。
    /// </summary>
    /// <remarks>
    /// ICE 分岔自上游的國際服版本，裡面有好幾張表是把國際服的任務 ID 直接寫死在原始碼裡。
    /// 台服的 <c>WKSMissionUnit</c> <b>已經帶著完整的列數</b>（0..1072），只是第二顆星 Phaenna
    /// 的那些列（545 以後）<b>Name 是空字串、其餘欄位全 0</b> —— 也就是
    /// <b>「表裡沒這列」在台服是 0 筆，全部都是「有列但整列是空的」</b>。
    /// 這一點決定了處理方式：不能用「這個 row 存不存在」判斷，只能用「這個 row 有沒有內容」，
    /// 而 <c>SheetMissionDict</c>（上面那個迴圈遇到空名字就 <c>continue</c>）正好就是那份名單。
    /// <br/><br/>
    /// 🔑 <b>不刪掉上游資料</b>：台服遲早會開放 Phaenna，屆時同一批 row 會原地變成有效，
    /// 那些寫死的時段表／緊急任務座標／釣魚預設會直接派上用場。
    /// 這裡只做「說清楚現在有多少對不上」，實際的安全性靠各消費端的守衛
    /// （<c>PlayerHandlers.KnownMissionsOnly</c>、<c>Task_FindMission</c> 的釣點查表、
    /// <c>NpcData.TryGetMoonNpc</c>、<c>SchedulerMain.CurrentMissionUnavailable</c> 等）。
    /// <br/><br/>
    /// 🔑 <b>這行 log 就是第二顆星開放當天的驗證點</b>：開放後如果數字沒有掉到 0，
    /// 代表上游那些寫死的 ID 跟台服對不上，要逐表重建而不是沿用。
    /// <br/><br/>
    /// ⚠️ <b>呼叫時機很重要</b>：<c>CriticalLocations</c> 是由
    /// <c>GatheringUtil.UpdateCriticalWeather()</c> 填的，而它在 <c>ICE.OnPluginLoad</c> 裡排在
    /// <c>DictionaryCreation()</c> <b>之後</b>。所以這個函式必須從 ICE.cs 的初始化尾端呼叫，
    /// 不能塞在 DictionaryCreation 裡 —— 否則那張表永遠被量成「0 筆對不上」，
    /// 是一個看起來完全正常的假陰性。
    /// </remarks>
    internal static void ReportHardcodedTableCoverage()
    {
        // 這些是「鍵是任務 ID、而且內容是上游照國際服寫死」的表。
        // ⚠️ 不含 UnsupportedMissions（那是釣魚任務黑名單，本來就允許指向不存在的任務）。
        (string Name, IEnumerable<uint> Ids)[] tables =
        [
            ("時段任務表 SinusMapV2", PlayerHandlers.SinusMapV2.SelectMany(x => x.Value).Select(x => x.MissionId)),
            ("時段任務表 PhaennaMapV2", PlayerHandlers.PhaennaMapV2.SelectMany(x => x.Value).Select(x => x.MissionId)),
            ("任務解鎖表 MissionUnlock", MissionUnlock.Keys.Concat(MissionUnlock.Values.SelectMany(x => x))),
            ("任務註記 CustomMissionNotes", CustomMissionNotes.Keys),
            ("緊急任務座標 CriticalLocations", GatheringUtil.CriticalLocations.Keys),
            ("釣魚預設 FishingPreset", GatheringUtil.FishingPreset.Keys),
            ("內建分數表 MissionScores.csv", MissionScoreDict.Keys),
        ];

        var lines = new List<string>();
        foreach (var (name, ids) in tables)
        {
            var distinct = ids.Distinct().ToList();
            var unknown = distinct.Count(id => !SheetMissionDict.ContainsKey(id));
            if (unknown > 0)
                lines.Add($"{name} {unknown}/{distinct.Count}");
        }

        if (lines.Count > 0)
        {
            IceLogging.Info(
                "以下寫死的任務表有部分 ID 在目前的客戶端查不到任務資料（通常是尚未開放的星球，屬預期行為）："
                + string.Join("、", lines)
                + "。相關功能會自動略過那些任務，不會中止流程。",
                "[資料盤點]");
        }

        ReportMissionTerritorySplit();
        ReportRouteCoverage();
    }

    /// <summary>
    /// 把 <see cref="DictionaryCreation"/> 裡那個寫死的 <c>keyId &lt; 545</c> 分界
    /// 在實機 log 上變成看得見的數字：任務各被分到哪個 territory、各幾筆。
    /// </summary>
    /// <remarks>
    /// ⚠️ 那個 545 是<b>國際服的邊界</b>，而它產出的 <c>TerritoryId</c> 會原封不動存進
    /// <c>SheetMissionDict</c>，是排程器真的會用的值（傳送、導航、釣點與採集點查表都吃它）——
    /// 所以它比那些只影響顯示的寫死表嚴重得多。<br/><br/>
    /// 📌 <b>台服 7.20 離線量測</b>（<c>exd-tc/7.20</c>）：<c>TerritoryType</c> 的 row <b>1291 整列是空的</b>，
    /// 也就是台服目前<b>根本沒有</b>這個區域（row 1237 = <c>PlaceName</c> 5219「渴望灣」）。
    /// 同時 <c>WKSMissionUnit</c> 的 545 以後全部是空名字，在上面的建表迴圈就被 <c>continue</c> 掉了，
    /// 所以台服這一行的 1291 <b>應該是 0 筆</b>。<br/><br/>
    /// 🔑 <b>為什麼兩個數字都印、而且 0 也要印</b>：只印 1291 的話，「0」既可能是真的 0，
    /// 也可能是這個統計自己壞了；把 1237 的筆數一起印出來，就有了「已知會命中」的校準基準。
    /// 同理，1291 就算是 0 也要出現在字面上 —— 「沒印出來」跟「是 0」在 log 上分不出來。<br/><br/>
    /// 🔑 <b>第二顆星開放當天就看這一行</b>：1291 從 0 變成非 0 才算正常。
    /// 若台服實際的 territory 不是 1291，這些任務會被導去一個不存在的區域，
    /// 屆時要改的是 <see cref="DictionaryCreation"/> 裡那個分界，不是這個函式。
    /// </remarks>
    private static void ReportMissionTerritorySplit()
    {
        var byTerritory = new Dictionary<uint, int>();
        foreach (var info in SheetMissionDict.Values)
            byTerritory[info.TerritoryId] = byTerritory.GetValueOrDefault(info.TerritoryId) + 1;

        // ⚠️ 這兩個一定要列出來，即使是 0。理由見上面的 remarks。
        uint[] known = [1237, 1291];
        var parts = known.Select(id => $"{id} {byTerritory.GetValueOrDefault(id)} 筆").ToList();
        parts.AddRange(byTerritory.Where(x => !known.Contains(x.Key))
                                  .OrderBy(x => x.Key)
                                  .Select(x => $"{x.Key} {x.Value} 筆（預期外）"));

        IceLogging.Info(
            $"任務表建立完成，共 {SheetMissionDict.Count} 筆；依區域分配："
            + string.Join("、", parts)
            + "。分界是原始碼寫死的 keyId>=545（沿用自國際服），"
            + "台服目前第二顆星尚未開放，1291 應為 0 筆。",
            "[資料盤點]");
    }

    /// <summary>
    /// 反向盤點：<b>這個客戶端真的有的任務</b>，有多少找不到對應的採集路線／釣點座標。
    /// </summary>
    /// <remarks>
    /// 🔑 這是最直接預測「會不會卡住」的指標，因為採集路線與釣點都是**以座標為鍵**
    /// （<c>Vector2</c> 浮點數相等比對）的寫死資料，跟任務 ID 對得上完全是兩回事。<br/>
    /// 台服 7.20 離線量測基準：Sinus Ardorum <b>28/28 採集旗標、9/9 釣點旗標全中</b>
    /// （逐筆重跑建表邏輯比對 exd-tc/7.20 的 WKSMissionUnit／WKSMissionToDo／WKSMissionMapMarker），
    /// 所以現在這個函式在台服應該一行都不印。<br/>
    /// ⚠️ Phaenna 的 28 個採集路線與 11 個釣點旗標<b>無法離線驗證</b>（那些任務列目前整列是空的），
    /// 第二顆星開放當天請先看這一行 —— 有輸出就代表座標資料要重建。
    /// </remarks>
    private static void ReportRouteCoverage()
    {
        var missingGather = new Dictionary<uint, int>();
        var missingFish = new Dictionary<uint, int>();

        foreach (var info in SheetMissionDict.Values)
        {
            var territory = info.TerritoryId;

            // ⚠️ 分支順序刻意跟消費端 Task_FindMission.Navmesh_MoveToMission 一致
            //    （那邊也是先判 Gather 再 else if Fish）。順序寫反的話，
            //    兩邊對「同一個任務算採集還是釣魚」的認定就會分岔，盤點結果會誤導人。
            if (info.Attributes.HasFlag(Gather))
            {
                var route = GatheringRouteLoader.GetRoute(territory, info.MapPosition);
                if (route == null || route.Count == 0)
                    missingGather[territory] = missingGather.GetValueOrDefault(territory) + 1;
            }
            else if (info.Attributes.HasFlag(Fish))
            {
                var hasSpot = GatheringUtil.MoonFishingLocations.TryGetValue(territory, out var spots)
                              && spots.TryGetValue(info.MapPosition, out var list)
                              && list.Count > 0;
                if (!hasSpot)
                    missingFish[territory] = missingFish.GetValueOrDefault(territory) + 1;
            }
        }

        foreach (var (territory, count) in missingGather)
            IceLogging.Info($"區域 {territory} 有 {count} 個採集任務找不到對應的採集路線資料，" +
                            "這些任務只能手動處理。", "[資料盤點]");

        foreach (var (territory, count) in missingFish)
            IceLogging.Info($"區域 {territory} 有 {count} 個釣魚任務找不到對應的釣點座標，" +
                            "這些任務只能手動處理。", "[資料盤點]");
    }

    private static string GetClassAcronym(uint jobId)
    {
        // Map your job IDs to the acronyms used in the CSV
        // You'll need to determine what these mappings are based on your game data
        return jobId switch
        {
            8 => "CRP",  // Carpenter
            9 => "BSM",  // Blacksmith
            10 => "ARM", // Armorer
            11 => "GSM", // Goldsmith
            12 => "LTW", // Leatherworker
            13 => "WVR", // Weaver
            14 => "ALC", // Alchemist
            15 => "CUL", // Culinarian
            16 => "MIN", // Miner
            17 => "BTN", // Botanist
            18 => "FSH", // Fisher
            _ => ""
        };
    }
}
