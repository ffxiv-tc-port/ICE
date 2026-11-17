using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Collections.Generic;

namespace ICE.Utilities.GatheringHelper;

public static unsafe partial class GatheringUtil
{
    public class GatheringActions
    {
        /// <summary>
        /// Internal name for myself to know wtf this is
        /// </summary>
        public string ActionName { get; set; }
        public Dictionary<uint, ClassGathInfo> ClassAction { get; set; } = new();
        /// <summary>
        /// Sheet name
        /// </summary>
        public uint StatusId { get; set; }
        /// <summary>
        /// The status name attached to it (personal use)
        /// </summary>
        public string StatusName { get; set; }
        /// <summary>
        /// The amount of GP required for the skill
        /// </summary>
        public int RequiredGp { get; set; }
        public int RequiredLv { get; set; }
    }

    public class ClassGathInfo
    {
        public string SkillName { get; set; }
        public uint ActionId { get; set; }
    }

    public static Dictionary<string, GatheringActions> GathActionDict = new()
    {
        { "BoonIncrease1", new GatheringActions
        {
            ActionName = "Pioneer's Gift I",
            ClassAction = new()
            {
                [16] = new() { ActionId = 21177, SkillName = "", },
                [17] = new() { ActionId = 21178, SkillName = "", }
            },
            StatusId = 2666,
            StatusName = "Gift of the Land",
            RequiredGp = 50,
            RequiredLv = 15,
        }},
        { "BoonIncrease2", new GatheringActions
        {
            ActionName = "Pioneer's Gift II",
            ClassAction = new()
            {
                [16] = new() { ActionId = 25589, SkillName = "", },
                [17] = new() { ActionId = 25590, SkillName = "", }
            },
            StatusId = 759,
            StatusName = "Gift of the Land II",
            RequiredGp = 100,
            RequiredLv = 50,
        }},
        { "Tidings", new GatheringActions
        {
            ActionName = "Nophica's Tidings",
            ClassAction = new()
            {
                [16] = new() { ActionId = 21203, SkillName = "", },
                [17] = new() { ActionId = 21204, SkillName = "", }
            },
            StatusId = 2667,
            StatusName = "Gatherer's Bounty",
            RequiredGp = 200,
            RequiredLv = 81,
        }},
        { "YieldI", new GatheringActions
        {
            ActionName = "Blessed Harvest",
            ClassAction = new()
            {
                [16] = new() { ActionId = 239, SkillName = "", },
                [17] = new() { ActionId = 222, SkillName = "", }
            },
            StatusId = 219,
            StatusName = "Gathering Yield Up",
            RequiredGp = 400,
            RequiredLv = 30,
        }},
        { "YieldII", new GatheringActions
        {
            ActionName = "Blessed Harvest II",
            ClassAction = new()
            {
                [16] = new() { ActionId = 241, SkillName = "", },
                [17] = new() { ActionId = 224, SkillName = "", }
            },
            StatusId = 219,
            StatusName = "Gathering Yield Up",
            RequiredGp = 500,
            RequiredLv = 40,
        }},
        { "BonusIntegrity", new GatheringActions
        {
            ActionName = "Ageless Words",
            ClassAction = new()
            {
                [16] = new() { ActionId = 232, SkillName = "Solid Reason", },
                [17] = new() { ActionId = 215, SkillName = "Ageless Word", }
            },
            RequiredGp = 300,
            RequiredLv = 30,
        }},
        { "BonusIntegrityChance", new GatheringActions
        {
            ActionName = "Wise of the World",
            ClassAction = new()
            {
                [16] = new() { ActionId = 26521, SkillName = "", },
                [17] = new() { ActionId = 26522, SkillName = "", }
            },
            StatusId = 2765,
            StatusName = "",
            RequiredGp = 0,
            RequiredLv = 90,
        }},
        { "BountifulYieldII", new GatheringActions
        {
            ActionName = "Bountiful Yield/Harvest II",
            ClassAction = new()
            {
                [16] = new() { ActionId = 272, SkillName = "", },
                [17] = new() { ActionId = 273, SkillName = "", }
            },
            StatusId = 1286,
            StatusName = "",
            RequiredGp = 100,
            RequiredLv = 68,
        }},
        { "FieldMasteryIII", new GatheringActions
        {
            // 50% increase
            ActionName = "Field Mastery III",
            ClassAction = new()
            {
                [16] = new() { ActionId = 295, SkillName = "", },
                [17] = new() { ActionId = 294, SkillName = "", }
            },
            StatusId = 218,
            StatusName = "Gathering Rate Up",
            RequiredGp = 250,
            RequiredLv = 10,
        }},
        { "FieldMasteryII", new GatheringActions
        {
            // 15% increase
            ActionName = "Field Mastery II",
            ClassAction = new()
            {
                [16] = new() { ActionId = 237, SkillName = "", },
                [17] = new() { ActionId = 220, SkillName = "", }
            },
            StatusId = 218,
            StatusName = "Gathering Rate Up",
            RequiredGp = 100,
            RequiredLv = 5,
        }},
        { "FieldMasteryI", new GatheringActions
        {
            // 5% increase
            ActionName = "Field Mastery I",
            ClassAction = new()
            {
                [16] = new() { ActionId = 235, SkillName = "", },
                [17] = new() { ActionId = 218, SkillName = "", }
            },
            StatusId = 218,
            StatusName = "Gathering Rate Up",
            RequiredGp = 50,
            RequiredLv = 4,
        }},
        { "FieldMasteryTemp", new GatheringActions
        {
            // 15% increase [temp]
            ActionName = "Clear Vision | Flora Mastery",
            ClassAction = new()
            {
                [16] = new() { ActionId = 4072, SkillName = "", },
                [17] = new() { ActionId = 4086, SkillName = "", }
            },
            StatusId = 754,
            StatusName = "Gathering Rate Up (Limited)",
            RequiredGp = 50,
            RequiredLv = 23,
        }},
    };

    public static Dictionary<string, GatheringActions> GathCollectableBuffs = new()
    {
        { "Scrutiny", new GatheringActions
        {
            ActionName = "Scrutiny",
            ClassAction = new()
            {
                [16] = new() { ActionId = 22185 },
                [17] = new() { ActionId = 22189 }
            },
            StatusId = 757,
            StatusName = "",
            RequiredGp = 200,
        }},
        { "Focus", new GatheringActions
        {
            ActionName = "Collector's Focus",
            ClassAction = new()
            {
                [16] = new() { ActionId = 21205 },
                [17] = new() { ActionId = 21206 }
            },
            StatusId = 2668,
            StatusName = "",
            RequiredGp = 100,
        }},
        { "Priming", new GatheringActions
        {
            ActionName = "Priming Touch",
            ClassAction = new()
            {
                [16] = new() { ActionId = 21205 },
                [17] = new() { ActionId = 34872 }
            },
            StatusId = 2668,
            StatusName = "",
            RequiredGp = 100,
        }},
        { "CollectorsHigh", new GatheringActions
        {
            // Only available in certain missions... *-sighs-*
            // 📌 這一條是宇宙採集任務的專屬臨時技能 Action 41307「極致強化洞察」。
            // 🔴 它掛在任務指令槽上，所以這裡填的 ActionId 是 27（GeneralAction 27 =「任務指令2」）而不是 41307 ——
            //    要找這個能力請搜 GeneralAction 27 或狀態 3911（下面的 StatusId，就是它賦予的「強化洞察」）。
            // 📌 受它加成的是 GathCollectableActions 裡的 "Brazen"（大膽提煉）與 "Meticulous"（慎重提煉）。
            // ⚠️ 只有「有 GP 的輪替」(Mission_Settings.SelectedRotation == 1) 會用到它；NoGpRotation 刻意不碰任務指令槽。
            ActionName = "Collectors High Standard",
            ClassAction = new()
            {
                [16] = new() { ActionId = 27 },
                [17] = new() { ActionId = 27 }
            },
            StatusId = 3911,
            StatusName = "",
            RequiredGp = 0,
        }},
        { "BonusIntegrity", new GatheringActions
        {
            ActionName = "Ageless Words",
            ClassAction = new()
            {
                [16] = new() { ActionId = 232, SkillName = "Solid Reason", },
                [17] = new() { ActionId = 215, SkillName = "Ageless Word", }
            },
            RequiredGp = 300,
        }},
        { "BonusIntegrityChance", new GatheringActions
        {
            ActionName = "Wise of the World",
            ClassAction = new()
            {
                [16] = new() { ActionId = 26521, SkillName = "", },
                [17] = new() { ActionId = 26522, SkillName = "", }
            },
            StatusId = 2765,
            StatusName = "",
            RequiredGp = 0,
        }},
    };

    public static Dictionary<string, GatheringActions> GathCollectableActions = new()
    {
        { "Scour", new GatheringActions
        {
            // Base general use skill
            ActionName = "Scour",
            ClassAction = new()
            {
                [16] = new() { ActionId = 22182 },
                [17] = new() { ActionId = 22186 }
            },
            StatusId = 0,
            StatusName = "n/a",
            RequiredGp = 0,
        }},
        { "Brazen", new GatheringActions
        {
            // 50 - 150% buff
            ActionName = "Brazen Woodsman",
            ClassAction = new()
            {
                [16] = new() { ActionId = 22183 },
                [17] = new() { ActionId = 22187 }
            },
            StatusId = 0,
            StatusName = "n/a",
            RequiredGp = 0,
        }},
        { "Meticulous", new GatheringActions
        {
            // Chance to not use durability/integrity
            ActionName = "Meticulous Woodsman",
            ClassAction = new()
            {
                [16] = new() { ActionId = 22184 },
                [17] = new() { ActionId = 22188 }
            },
            StatusId = 0,
            StatusName = "n/a",
            RequiredGp = 0,
        }},
        { "BonusIntegrity", new GatheringActions
        {
            ActionName = "Ageless Words",
            ClassAction = new()
            {
                [16] = new() { ActionId = 232, SkillName = "Solid Reason", },
                [17] = new() { ActionId = 215, SkillName = "Ageless Word", }
            },
            RequiredGp = 300,
        }},
        { "BonusIntegrityChance", new GatheringActions
        {
            ActionName = "Wise of the World",
            ClassAction = new()
            {
                [16] = new() { ActionId = 26521 },
                [17] = new() { ActionId = 26522 }
            },
            StatusId = 2765,
            StatusName = "",
            RequiredGp = 0,
        }},
        { "Collect", new GatheringActions
        {
            ActionName = "Collect",
            ClassAction = new()
            {
                [16] = new() { ActionId = 240},
                [17] = new() { ActionId = 815},
            },
            StatusId = 0,
            StatusName = "",
            RequiredGp = 0,
        } },
    };

    public unsafe static uint CollectStandardCharges()
    {
        try
        {
            if (DutyActionManager.GetInstanceIfReady() != null)
                return (uint)(DutyActionManager.GetInstanceIfReady()->CurCharges[1] + DutyActionManager.GetInstanceIfReady()->CurCharges[0]);

            return 0;
        }
        catch (Exception e)
        {
            e.Log();
            return 0;
        }
    }

    /* First things first, there's several types of missions for gathering
     * 1 Quantity Limited(Gather x amount on limited amount of nodes)
     * 2 Quantity(Gather x amount, gather more for increased score)
     * 3 Timed(Gather x amount in the time limit)
     * 4 Chain(Increase score based on chain)
     * 5 Gatherer's Boon (Increase score by hitting boon % chance)
     * 6 Chain + Boon(Get score from chain nodes + boon % chance)
     * 7 Collectables(This is going to be annoying)
     * 8 Time Steller Reduction(???) (Assuming Collectables -> Reducing for score...fuck)
     */

    // Sinus

    private static CriticalInfo AstromagneticStorm1 = new()
    {
        RawLocation = new Vector3(176.24f, 9.40f, 560.07f),
        MapInfo = new Vector2(24.7f, 32.5f)
    };
    private static CriticalInfo AstromagneticStorm2 = new()
    {
        RawLocation = new Vector3(-91.58f, 19.32f, -241.99f),
        MapInfo = new Vector2(19.7f, 16.6f)
    };
    private static CriticalInfo AstromagneticStorm3 = new()
    {
        RawLocation = new Vector3(-72.76f, 51.00f, 768.64f),
        MapInfo = new Vector2(19.9f, 36.9f)
    };
    private static CriticalInfo AstromagneticStorm4 = new()
    {
        RawLocation = new Vector3(-464.50f, 37.89f, -69.89f),
        MapInfo = new Vector2(12.2f, 20.1f)
    };

    private static CriticalInfo MeteorShower1 = new()
    {
        RawLocation = new Vector3(-219.93f, 24.16f, 209.98f),
        MapInfo = new(17.1f, 25.7f)
    };
    private static CriticalInfo MeteorShower2 = new()
    {
        RawLocation = new Vector3(34.86f, 34.38f, -349.75f),
        MapInfo = new(22.2f, 14.5f)
    };
    private static CriticalInfo MeteorShower3 = new()
    {
        RawLocation = new Vector3(845.90f, -58.44f, -390.45f),
        MapInfo = new(38.1f, 13.9f)
    };
    private static CriticalInfo MeteorShower4 = new()
    {
        RawLocation = new Vector3(497.36f, -115.32f, -845.65f),
        MapInfo = new Vector2(31.3f, 4.4f)
    };

    private static CriticalInfo SporingMist1 = new()
    {
        RawLocation = new Vector3(539.43f, 36.38f, 49.89f),
        MapInfo = new Vector2(32.3f, 22.5f)
    };
    private static CriticalInfo SporingMist2 = new()
    {
        RawLocation = new Vector3(654.32f, 52.00f, 100.13f),
        MapInfo = new Vector2(34.6f, 23.5f)
    };
    private static CriticalInfo SporingMist3 = new()
    {
        RawLocation = new Vector3(379.62f, 51.39f, 704.14f),
        MapInfo = new Vector2(29.1f, 35.6f)
    };
    private static CriticalInfo SporingMist4 = new()
    {
        RawLocation = new Vector3(99.66f, 18.11f, -209.80f),
        MapInfo = new Vector2(23.5f, 17.3f)
    };

    // Phaenna
    private static CriticalInfo Thunderstorms1 = new()
    {
        RawLocation = new Vector3(417.57f, 52.00f, -445.41f),
        MapInfo = new Vector2(29.9f, 12.6f),
    };
    private static CriticalInfo Thunderstorms2 = new()
    {
        RawLocation = new Vector3(432.76f, 54.13f, -169.80f),
        MapInfo = new Vector2(30.1f, 18.1f)
    };
    private static CriticalInfo Thunderstorms3 = new()
    {
        RawLocation = new Vector3(169.79f, 41.00f, -210.79f),
        MapInfo = new Vector2(24.9f, 17.3f)
    };
    private static CriticalInfo Thunderstorms4 = new()
    {
        RawLocation = new Vector3(-615.30f, 8.26f, -515.45f),
        MapInfo = new Vector2(9.2f, 11.2f)
    };

    private static CriticalInfo AnnealingWinds1 = new()
    {
        RawLocation = new Vector3(239.77f, 133.83f, -704.44f),
        MapInfo = new Vector2(26.3f, 7.4f)
    };
    private static CriticalInfo AnnealingWinds2 = new()
    {
        RawLocation = new Vector3(-506.27f, -8.42f, -751.29f),
        MapInfo = new Vector2(11.2f, 6.2f)
    };
    private static CriticalInfo AnnealingWinds3 = new()
    {
        RawLocation = new Vector3(410.29f, 18.90f, 25.14f),
        MapInfo = new Vector2(29.6f, 22.2f)
    };
    private static CriticalInfo AnnealingWinds4 = new()
    {
        RawLocation = new Vector3(10.10f, 7.98f, 339.70f),
        MapInfo = new Vector2(21.7f, 28.3f)
    };

    private static CriticalInfo GlassRain1 = new()
    {
        RawLocation = new Vector3(407.15f, -229.45f, 224.76f),
        MapInfo = new Vector2(29.3f, 25.6f)
    };
    private static CriticalInfo GlassRain2 = new()
    {
        RawLocation = new Vector3(544.42f, -251.07f, 634.55f),
        MapInfo = new Vector2(32.5f, 34.2f)
    };
    private static CriticalInfo GlassRain3 = new()
    {
        RawLocation = new Vector3(148.96f, -9.99f, 487.46f),
        MapInfo = new Vector2(24.6f, 31.4f)
    };
    private static CriticalInfo GlassRain4 = new()
    {
        RawLocation = new Vector3(-488.32f, 25.05f, 35.65f),
        MapInfo = new Vector2(11.8f, 22.3f)
    };

    public class CriticalInfo
    {
        public Vector3 RawLocation { get; set; }
        public Vector2 MapInfo { get; set; }
    }

    public static Dictionary<uint, CriticalInfo> CriticalLocations = new();

    public static void UpdateCriticalWeather()
    {
        // Sinus - Astromagnetic Storm 1/α
        AddKeys(AstromagneticStorm1, 518, 522, 530);
        AddKeys(AstromagneticStorm2, 537, 543);

        // Astromagnetic Storm 2/β
        AddKeys(AstromagneticStorm3, 512, 521, 527);
        AddKeys(AstromagneticStorm4, 533, 536, 542);

        // Meteor Showers 1/α
        AddKeys(MeteorShower1, 515, 524, 538);
        AddKeys(MeteorShower2, 519, 523);

        // Meteor Showers 2/β
        AddKeys(MeteorShower3, 516, 520, 525);
        AddKeys(MeteorShower4, 531, 534, 539);

        // Sporing Mist 1/α
        AddKeys(SporingMist1, 517, 532);
        AddKeys(SporingMist2, 514, 529, 541);

        // Sporing Mist 2/β
        AddKeys(SporingMist3, 513, 526, 528);
        AddKeys(SporingMist4, 535, 540, 544);

        // Phaenna - Thunderstorm 1/α
        AddKeys(Thunderstorms1, 1007, 1019, 1025);
        AddKeys(Thunderstorms2, 1016, 1022);

        // Thunderstorm 2/β
        AddKeys(Thunderstorms3, 1017, 1023, 1034);
        AddKeys(Thunderstorms4, 1010, 1026, 1031);

        // Annealing Winds 1/α
        AddKeys(AnnealingWinds1, 1028, 1032, 1037);
        AddKeys(AnnealingWinds2, 1011, 1020, 1035);

        // Annealing Winds 2/β
        AddKeys(AnnealingWinds3, 1013, 1029, 1038);
        AddKeys(AnnealingWinds4, 1008, 1036);

        // Glass Rain 1/α
        AddKeys(GlassRain1, 1009, 1014, 1021);
        AddKeys(GlassRain2, 1030, 1039);

        // Glass Rain 2/β
        AddKeys(GlassRain3, 1015, 1018, 1033);
        AddKeys(GlassRain4, 1012, 1024, 1027);
    }

    private static void AddKeys(CriticalInfo location, params uint[] keys)
    {
        foreach (var key in keys)
            CriticalLocations[key] = location;
    }
}
