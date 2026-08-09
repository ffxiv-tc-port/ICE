using Dalamud.Interface.Textures;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ICE.Enums;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace ICE.Utilities;

public static unsafe partial class CosmicHelper
{

    public static readonly HashSet<uint> Ranks = [1, 2, 3, 4];
    public static readonly HashSet<uint> ARankIds = [4, 5, 6];

    public static readonly HashSet<uint> CrafterJobList = [8, 9, 10, 11, 12, 13, 14, 15];
    public static readonly HashSet<uint> GatheringJobList = [16, 17, 18];


    /// <summary>
    /// Currently contains all the WKSMissionLotterySpecialCond values that are weather based
    /// MAKE SURE. TO UPDATE THIS. COME NEW MOON
    /// </summary>
    public static readonly HashSet<uint> WeatherSelection = new() { 13, 14, 15, 16 };

    public static List<int> GreyIconList = new List<int>() { 91031, 91032, 91033, 91034, 91035, 91036, 91037, 91038, 91039, 91040, 91041 };
    public static Dictionary<CosmicWeather, int> WeatherIds = new()
    {
        [CosmicWeather.UmbralWind] = 60219,
        [CosmicWeather.MoonDust] = 60222,
        [CosmicWeather.Clouds] = 60203,
        [CosmicWeather.Rain] = 60207,
    };

    public static readonly int MinimumLevel = 10;
    // 📌 這裡原本有 `public static readonly int MaximumLevel = Player.MaxLevel;` ——
    //    零讀取者的死碼，已刪。它是三個同形快照裡最糟的一個：static 初始化器只跑一次，
    //    而取的是 `Player.MaxLevel` —— 登入前那個值未必可用，所以它不只是「可能過期」，
    //    是「可能一開始就取到不對的值，然後永遠是那個值」。
    //    要用等級上限的話當場問 Player，不要再放一個模組層級的快照。

    public static readonly int MaxRelicLevel = 14;

    #region Dictionaries

    public class CraftingInfo
    {
        public uint ItemId { get; set; } 
        public uint Durability { get; set; }
        public uint Quality { get; set; }
        public uint Progress { get; set; }
        public int RequiredAmount { get; set; } 
        public Dictionary<uint, int> RequiredItems { get; set; } = new();
    }

    /// <summary>
    /// Some things to note that I didn't realize until after I really dug into the sheet a bit more/cleaned this up. <br />
    /// Sheet is: WKSMissionUnit <br />
    /// <b>- Row 0:</b> Mission Name <br />
    /// <b>- Row 2:</b> JobId attached to the quest (so 8 is CRP, 9 is BSM, etc.) <br />
    /// <b>- Row 3:</b> 2nd Required job??? <br />
    /// <b>- Row 4:</b> 3rd Required job??? <br />
    /// <b>- Row 5:</b> Bool → Is it a critical mission? <br />
    /// <b>- Row 6:</b> Rank → D = 1 | C = 2 | B = 3 | 4 = A-1 | 5 = A-2 | 6 = A-3 <br />
    /// <b>- Row 7:</b> Mission time limit (seconds) <br />
    /// <b>- Row 18:</b> Recipe # → Corresponds to the RecipeID
    /// </summary>
    public class CosmicInfo
    {
        // - - - Crafter Specific - - - //
        public Dictionary<ushort, CraftingInfo> Crafts_Main { get; set; } = new();
        public Dictionary<ushort, CraftingInfo> Crafts_Pre { get; set; } = new();
        public bool IsExpert { get; set; } = false;

        // - - - BTN | MIN Specific - - - //
        public Dictionary<uint, int> Gathering_Min { get; set; } = new();

        // - - - Map Related - - - // 
        public Vector2 MapPosition { get; set; } = new();
        public int Radius { get; set; } = 0;
        public uint TerritoryId { get; set; }
        public uint MarkerId { get; set; }

        // - - - Universal Info - - - //
        public string Name { get; set; }
        public HashSet<uint> Jobs { get; set; }
        public uint ToDoId { get; set; } = 0;
        public uint Rank { get; set; } = 1;
        public MissionAttributes Attributes { get; set; }
        public CosmicWeather Weather { get; set; }
        public uint StartTime { get; set; }
        public uint EndTime { get; set; }
        public uint ClassScore { get; set; } = 0;
        public uint CosmoCredit { get; set; } = 0;
        public uint LunarCredit { get; set; } = 0;
        public uint RewardItem { get; set; } = 0;
        public uint RewardItemAmount { get; set; } = 0;
        public HashSet<uint> PreviousMissions { get; set; } = new();
        public Dictionary<int, int> RelicXpInfo { get; set; } = new();
        public uint BronzeScore { get; set; } = 0;
        public uint SilverScore { get; set; } = 0;
        public uint GoldScore { get; set; } = 0;

        /// <summary><c>WKSMissionUnit.MissionTime</c>（秒）。0 代表這個任務沒有時間限制。</summary>
        public uint TimeLimitSeconds { get; set; } = 0;

        /// <summary>
        /// 這個任務的銀星／金星是不是用「交件時還剩多少時間」評的（而不是評價分數）。
        /// </summary>
        /// <remarks>
        /// 📌 直接沿用既有的 <see cref="MissionAttributes.ScoreTimeRemaining"/>，<b>不另立判斷</b>——
        /// 排程器的交件邏輯（<c>Task_CheckScore</c>）已經在用同一個旗標，兩邊共用才不會出現
        /// 「顯示說是時間型、交件卻按分數走」的分岔。<br/><br/>
        /// 🔑 <b>離線交叉驗證過</b>（<c>exd-tc/7.20</c>）：這個旗標（由 <c>WKSMissionText</c>
        /// ∈ {104,110,113,114,115} 且職業是採集／釣魚推出來）與另一條完全獨立的路徑
        /// —— <c>WKSMissionToDo.MissionType == 8</c> —— <b>命中同樣的 34 個任務，一個不差</b>。<br/>
        /// 這一型任務的 <see cref="SilverScore"/>／<see cref="GoldScore"/> 單位是
        /// <b>「剩餘秒數 × 10」而不是分數</b>：第 470 列（30:00 時限）的 15100 / 15500 換算是
        /// 25:10 / 25:50，跟使用者 2026-08-06 實機面板上的「剩餘時間 25:10以上」
        /// 「剩餘時間 25:50以上」逐字相符；34 個任務的門檻除以 10 也全部小於各自的
        /// <see cref="TimeLimitSeconds"/>。<br/>
        /// ⚠️ 但光看「門檻除以 10 塞得進時限」<b>不足以</b>判定型別——MissionType 3／4／9 也都
        /// 通過那個測試，它們卻是評價型。所以不要拿數字範圍當判別依據。
        /// </remarks>
        public bool IsTimeGraded => Attributes.HasFlag(MissionAttributes.ScoreTimeRemaining);
    }

    public static Dictionary<uint, CosmicInfo> SheetMissionDict = new();

    /// <summary>
    /// 任務類型分類鍵——跟主視窗任務分頁（<c>modeSelect_Standard.Draw</c> 的 Standard 分類，也就是
    /// <c>C.GrindProvisionals</c> 關閉時那個 if/else-if 鏈）用完全同一套互斥、依序判斷的優先序：<br/>
    /// Critical → ProvisionalWeather → ProvisionalTimed → ProvisionalSequential → Rank(A/B/C/D)。<br/>
    /// 回傳的字串沿用既有 zh-TW 字典裡已經存在的裸詞條（"Critical"／"Weather"／"Timed"／"Sequence"／
    /// "A Rank"…——主視窗的任務分頁按鈕已經在用同一批鍵），呼叫端只需要
    /// <c>.Loc()</c> 再自行決定要不要加框、上色。理論上每個任務都會落在某一類（Rank 保底是 1），
    /// 但仍以 <c>null</c> 表示「查不到」，呼叫端要能不畫。
    /// </summary>
    public static string? GetMissionCategoryKey(CosmicInfo info)
    {
        if (info.Attributes.HasFlag(MissionAttributes.Critical))
            return "Critical";
        if (info.Attributes.HasFlag(MissionAttributes.ProvisionalWeather))
            return "Weather";
        if (info.Attributes.HasFlag(MissionAttributes.ProvisionalTimed))
            return "Timed";
        if (info.Attributes.HasFlag(MissionAttributes.ProvisionalSequential))
            return "Sequence";
        if (info.Rank > 3)
            return "A Rank";
        if (info.Rank == 3)
            return "B Rank";
        if (info.Rank == 2)
            return "C Rank";
        if (info.Rank == 1)
            return "D Rank";
        return null;
    }

    public class GatheringInfo
    {
        public Dictionary<uint, int> MinGatherItems = [];
    }

    public static Dictionary<uint, GatheringInfo> GatheringItemDict = new();

    public static Dictionary<uint, ISharedImmediateTexture> GreyTexture = new Dictionary<uint, ISharedImmediateTexture>();

    public static Dictionary<uint, ISharedImmediateTexture> JobIconDict = new Dictionary<uint, ISharedImmediateTexture>();
    public static Dictionary<CosmicWeather, ISharedImmediateTexture> WeatherIconDict = new();

    public static Dictionary<uint, uint> MissionScoreDict = new(); // MissionID -> Score

    // Load the CSV file
    public static void LoadMissionScores()
    {
        MissionScoreDict.Clear();

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "ICE.Resources.MissionScores.csv";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            PluginLog.Error($"Failed to find embedded CSV: {resourceName}");
            return;
        }

        using var reader = new StreamReader(stream);
        bool headerSkipped = false;
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (!headerSkipped)
            {
                headerSkipped = true;
                continue; // Skip header
            }

            var parts = line.Split(',');
            if (parts.Length >= 4 &&
                uint.TryParse(parts[0].Trim(), out uint missionId) &&
                uint.TryParse(parts[3].Trim(), out uint score))
            {
                MissionScoreDict[missionId] = score;
            }
        }
    }

    public class GatherItemInfo
    {
        public HashSet<uint> itemIds { get; set; } = new();
        public uint Type { get; set; } = 0;
    }
    public static Dictionary<string, GatherItemInfo> GatheringItems = new();

    public class XPType
    {
        public int CurrentXP { get; set; }
        public int NeededXP { get; set; }
    }

    public static Dictionary<uint, List<uint>> MissionUnlock = new()
    {
        [499] = new() { 82, 397 },
        [500] = new() { 217, 397 },
        [501] = new() { 262, 397 },
        [505] = new() { 37, 442 },
        [506] = new() { 127, 442 },
        [507] = new() { 307, 442 },
        [510] = new() { 172, 487 },
        [511] = new() { 352, 487 }
    };

    #endregion
}