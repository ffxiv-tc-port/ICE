using FFXIVClientStructs.FFXIV.Component.GUI;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace ICE.Ui;

/// <summary>
/// 從 <c>WKSMissionInfomation</c> 面板讀出「每個目標的完成進度」。
///
/// <para><b>為什麼用節點文字而不是 AtkValue 索引</b><br/>
/// 這個 addon 在台服 7.20 沒有可離線查證的 AtkValue 佈局：ECommons 的 AddonMaster 只記錄了
/// 0 / 2 / 3 / 4 / 5（名稱、目前分數、銀、金、緊急進度），再往後全是未知；而遊戲資料裡
/// <c>ui/uld/WKSMissionInfomation.uld</c> <b>不存在</b>（已用 Lumina 直接查 index1 的 hash 表確認），
/// 所以連 ULD 都沒得對照。硬猜索引會顯示錯誤數字 —— 那比不顯示更糟。
/// </para>
///
/// <para><b>改用的作法</b><br/>
/// 走訪 addon 自己的節點樹，把文字節點依「同一個父節點」分組（遊戲的每一列目標就是
/// 一個 Res 父節點底下掛著「說明文字」＋「n/m」兩個文字節點），只挑出<b>剛好含有一個
/// <c>n/m</c> 分數字串</b>的那些組別。顯示出來的字串<b>原封不動來自遊戲</b>，不做任何解讀或
/// 重新配對，所以不會出現「配錯欄位」這種錯誤資訊。找不到符合形狀的組別就回傳空清單
/// （疊加層那邊會什麼都不畫），不會退而求其次去猜。
/// </para>
///
/// <para>⚠️ 節點指標一律不跨影格保存 —— 每次刷新都重新走訪，快取的只有字串。</para>
///
/// <para><b>評價型任務（實機 2026-08-02 dump 確認）</b><br/>
/// 有些任務面板完全沒有「n/m」形式的逐項目標列（例如採集特殊裝甲板材料任務），只有
/// 「當前評價／銀星達成條件／金星達成條件」三個分數欄位——這種任務下 <see cref="Get"/>
/// 回傳空清單是<b>正確行為</b>，不是掃描漏抓。分數改由 <c>OverlayWindow</c> 直接讀
/// ECommons AddonMaster 的 <c>WKSMissionInfomation.CurrentScore/SilverScore/GoldScore</c>
/// （全是 <c>uint?</c>，這幾個欄位已加固過型別/邊界檢查）。<see cref="TimeRemaining"/>
/// 則是「時間限制」那一列，跟目標列共用同一次節點走訪，用內容比對（找含「時間限制」
/// 字樣的節點群組）取得，不寫死索引。</para>
/// </summary>
internal static class MissionObjectiveReader
{
    /// <summary>單一目標列。<see cref="Text"/> 是遊戲原字串，<see cref="Progress"/> 是那一列的 n/m。</summary>
    internal readonly record struct ObjectiveLine(string Text, string Progress, int Current, int Required)
    {
        public bool Done => Required > 0 && Current >= Required;
    }

    /// <summary>只認「純粹是 n/m」的字串（允許千分位逗號與前後空白）。時間是 12:34，不會誤中。</summary>
    private static readonly Regex FractionOnly = new(@"^\s*([\d,]+)\s*/\s*([\d,]+)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 備援形狀：說明文字與數字被塞在<b>同一個</b>文字節點裡（「…數量　0/1」）。
    /// 只在該組沒有獨立的 n/m 節點、而且整組只有一個字串時才套用。
    /// </summary>
    private static readonly Regex LabelWithFraction = new(@"^(.*\S)\s+([\d,]+\s*/\s*[\d,]+)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 只認「M:SS/M:SS」這種時間格式，跟目標進度的純數字 n/m 分開判斷，不會互相誤中。
    /// 四個擷取群組依序是「剩餘分、剩餘秒、總分、總秒」。
    /// </summary>
    private static readonly Regex TimeFraction = new(@"^(\d{1,2}):(\d{2})\s*/\s*(\d{1,2}):(\d{2})$", RegexOptions.Compiled);

    /// <summary>
    /// 銀星／金星達成條件裡的「剩餘時間 25:10以上」那個門檻。
    /// </summary>
    /// <remarks>
    /// 刻意用<b>半形</b>冒號的「數字:數字」形狀來認：評價型任務的條件是純數字（"2,510"）
    /// 或帶全形冒號的敘述，不會誤中。抓不到就代表這不是時間型條件，呼叫端要原樣顯示字串。
    /// </remarks>
    private static readonly Regex MedalTimeRequirement = new(@"(\d{1,2}):(\d{2})", RegexOptions.Compiled);

    /// <summary>銀星達成條件那一列的標題字樣。</summary>
    /// <remarks>
    /// 🔴 <b>刻意只放台服原文，不放裸英文 "Silver"/"Gold"</b>：那兩個字會出現在道具名裡
    /// （"Gold Ore" 之類），拿去比對目標列會<b>誤判成達成條件列</b>並顯示錯的數字。
    /// 比對不到不是問題——呼叫端會退回資料表的旗標與門檻，那條路是對的；
    /// 誤判才是問題。台服的「銀星」「金星」兩字組不會出現在道具名裡（「金屬」不含「金星」）。
    /// </remarks>
    private static readonly string[] SilverLabels = ["銀星"];

    /// <summary>金星達成條件那一列的標題字樣。理由同 <see cref="SilverLabels"/>。</summary>
    private static readonly string[] GoldLabels = ["金星"];

    /// <summary>時間限制那一列的標題字樣。</summary>
    private static readonly string[] TimeLimitLabels = ["時間限制", "時間限定", "Time Limit"];

    private const string AddonName = "WKSMissionInfomation";

    /// <summary>走訪上限，純粹是防呆用的天花板，正常的 addon 遠遠用不到。</summary>
    private const int MaxNodes = 512;
    private const int MaxDepth = 8;

    private static readonly List<ObjectiveLine> Cache = new();
    private static uint cachedMissionId;

    /// <summary>已經為哪個任務印過「抓不到目標列」的提示，避免每 250ms 洗一次記錄檔。</summary>
    private static uint loggedEmptyForMission;

    /// <summary>
    /// 「時間限制」那一列的原文（例如 <c>"9:45/10:00"</c>），來源與目標列同一次節點走訪，
    /// 用內容比對（找含「時間限制」字樣的節點群組）取得，不靠寫死索引。抓不到就是 null。
    /// </summary>
    internal static string? TimeRemaining { get; private set; }

    /// <summary>
    /// 「時間限制」那一列左邊那個數字換算成秒（<b>剩餘</b>時間，不是已經過的時間）。抓不到是 null。
    /// </summary>
    /// <remarks>
    /// 🔑 <b>「左邊是剩餘而不是已經過」是離線推出來的，不是猜的</b>：使用者 2026-08-06 的面板傾印裡
    /// 面板顯示 <c>26:34/30:00</c>，而同一份傾印的 <c>AtkValues[7] = 1785995121</c>
    /// 換算是 UTC 2026-08-06 05:45:21 —— 那個時間點在傾印被讀到的當下<b>還沒發生</b>，
    /// 所以它只能是任務的截止時刻。把 26:34 當「剩餘」推回去，截圖時刻 = 截止 − 26:34
    /// 落在傾印之前幾分鐘（合理）；當「已經過」推回去則會落在傾印之後十幾分鐘（不可能）。
    /// 同一份傾印的 <c>AtkValues[8] = 1800</c> 也剛好等於 <c>WKSMissionUnit</c> 第 470 列的
    /// <c>MissionTime</c>，交叉印證「右邊那個是總時限」。
    /// </remarks>
    internal static int? RemainingSeconds { get; private set; }

    /// <summary>「時間限制」那一列右邊那個數字換算成秒（總時限）。抓不到是 null。</summary>
    internal static int? TotalSeconds { get; private set; }

    /// <summary>
    /// 銀星／金星「達成條件」那一列的內容。<see cref="MedalCondition.Raw"/> 是遊戲原字串
    /// （只剝掉畫不出來的圖示字元，不改寫），<see cref="MedalCondition.RequiredRemainingSeconds"/>
    /// 只有在條件是「剩餘時間 M:SS 以上」時才有值。
    /// </summary>
    internal readonly record struct MedalCondition(string? Raw, int? RequiredRemainingSeconds)
    {
        /// <summary>這一列到底有沒有讀到東西。沒讀到時呼叫端要顯示「不知道」，不是顯示 0。</summary>
        internal bool HasText => !string.IsNullOrWhiteSpace(Raw);

        /// <summary>條件是不是「剩餘時間要多少以上」這種時間型。</summary>
        internal bool IsTimeBased => RequiredRemainingSeconds is not null;
    }

    /// <summary>銀星達成條件。沒讀到時是 <c>default</c>（<see cref="MedalCondition.HasText"/> 為 false）。</summary>
    internal static MedalCondition SilverCondition { get; private set; }

    /// <summary>金星達成條件。</summary>
    internal static MedalCondition GoldCondition { get; private set; }

    /// <summary>
    /// 取得目前任務的目標進度。面板沒開、或找不到符合形狀的資料時回傳空清單。
    /// </summary>
    internal static IReadOnlyList<ObjectiveLine> Get(uint missionId)
    {
        if (missionId == 0)
        {
            if (Cache.Count > 0)
                Cache.Clear();
            ClearParsed();
            cachedMissionId = 0;
            return Cache;
        }

        // 任務換了就立刻重算，否則節流到 ~4 次/秒（走訪節點樹不算貴，但沒必要每幀做）。
        if (missionId != cachedMissionId)
        {
            cachedMissionId = missionId;
            Refresh();
        }
        else if (EzThrottler.Throttle("ICE_MissionObjectiveReader", 250))
        {
            Refresh();
        }

        return Cache;
    }

    /// <summary>
    /// 把所有「這一輪從面板讀出來的」欄位歸零。面板關掉／讀不到時一定要走這裡，
    /// 否則畫面會留著上一個任務的舊數字——那比什麼都不顯示更糟。
    /// </summary>
    private static void ClearParsed()
    {
        TimeRemaining = null;
        RemainingSeconds = null;
        TotalSeconds = null;
        SilverCondition = default;
        GoldCondition = default;
    }

    private static unsafe void Refresh()
    {
        var found = new List<ObjectiveLine>();

        try
        {
            var ptr = Svc.GameGui.GetAddonByName(AddonName, 1);
            if (ptr.Address == nint.Zero)
            {
                Cache.Clear();
                ClearParsed();
                return;
            }

            var addon = (AtkUnitBase*)ptr.Address;
            if (!addon->IsVisible || !addon->IsReady)
            {
                Cache.Clear();
                ClearParsed();
                return;
            }

            // owner 指標 -> 那一組收集到的文字（保留走訪順序）。
            var groups = new List<(nint Owner, float ScreenY, List<string> Texts)>();
            var index = new Dictionary<nint, int>();
            var budget = MaxNodes;

            CollectTexts(&addon->UldManager, groups, index, ref budget, 0);

            // 時間限制列與銀星／金星達成條件列跟目標列是不同形狀（"M:SS/M:SS" 含冒號，
            // FractionOnly 不會誤中），獨立掃一輪、用內容比對（找含標題字樣的節點群組）取值，
            // 不靠寫死索引。刻意跟下面的目標列迴圈分開：目標列迴圈遇到形狀不符會 continue，
            // 會把這幾個群組跳過。
            //
            // ⚠️ 這裡一律先過 GameTextUtil.StripGameIcons：遊戲的列文字會夾帶私用區圖示字元
            //    （實測任務名開頭是 \uE0BE），沒剝掉的話下面錨定的正規表示式全部落空，
            //    失敗形式是「整段不顯示」而不是報錯。
            ClearParsed();

            foreach (var group in groups)
            {
                var texts = new List<string>(group.Texts.Count);
                foreach (var raw in group.Texts)
                {
                    var cleaned = GameTextUtil.StripGameIcons(raw);
                    if (cleaned.Length > 0)
                        texts.Add(cleaned);
                }

                if (texts.Count == 0)
                    continue;

                if (TimeRemaining == null && HasLabel(texts, TimeLimitLabels))
                {
                    foreach (var text in texts)
                    {
                        var match = TimeFraction.Match(text);
                        if (!match.Success)
                            continue;

                        TimeRemaining = text;
                        RemainingSeconds = ToSeconds(match.Groups[1].Value, match.Groups[2].Value);
                        TotalSeconds = ToSeconds(match.Groups[3].Value, match.Groups[4].Value);
                        break;
                    }
                }

                if (!SilverCondition.HasText)
                    SilverCondition = ReadCondition(texts, SilverLabels);

                if (!GoldCondition.HasText)
                    GoldCondition = ReadCondition(texts, GoldLabels);
            }

            foreach (var group in groups.OrderBy(g => g.ScreenY))
            {
                // 一列目標 = 一個「n/m」＋至少一段說明文字。
                string? progress = null;
                var labels = new List<string>();

                foreach (var raw in group.Texts)
                {
                    // 同上：先剝掉畫不出來的圖示字元，否則 FractionOnly 這種錨定樣式會落空，
                    // 而且顯示出來的說明文字會夾著一個「�」。
                    var text = GameTextUtil.StripGameIcons(raw);
                    var match = FractionOnly.Match(text);
                    if (match.Success)
                    {
                        // 同一組出現兩個 n/m 就代表這不是我們認得的形狀，整組丟掉。
                        if (progress != null)
                        {
                            progress = null;
                            break;
                        }
                        progress = text;
                        continue;
                    }

                    if (text.Length > 0)
                        labels.Add(text);
                }

                // 備援：說明文字與 n/m 被塞在同一個節點裡。
                if (progress == null && labels.Count == 1)
                {
                    var combined = LabelWithFraction.Match(labels[0]);
                    if (combined.Success)
                    {
                        progress = combined.Groups[2].Value.Trim();
                        labels[0] = combined.Groups[1].Value.Trim();
                    }
                }

                if (progress == null || labels.Count == 0)
                    continue;

                var m = FractionOnly.Match(progress);
                if (!m.Success)
                    continue;
                if (!int.TryParse(m.Groups[1].Value.Replace(",", ""), out var current))
                    continue;
                if (!int.TryParse(m.Groups[2].Value.Replace(",", ""), out var required))
                    continue;

                found.Add(new ObjectiveLine(string.Join(" ", labels), progress, current, required));
            }
        }
        catch (Exception ex)
        {
            IceLogging.Debug($"讀取任務目標進度時發生例外：{ex.Message}", "[MissionObjectiveReader]");
            Cache.Clear();
            ClearParsed();
            return;
        }

        // 面板是開著的卻一列都沒抓到 —— 這是要人回報的情況，所以寫在 Information
        // （使用者的記錄等級會濾掉 Debug/Verbose）。每個任務只講一次，不洗版。
        if (found.Count == 0 && loggedEmptyForMission != cachedMissionId)
        {
            loggedEmptyForMission = cachedMissionId;
            IceLogging.Info(
                $"任務 {cachedMissionId}：WKSMissionInfomation 已就緒，但沒有解析到任何「n/m」目標列。" +
                "請到除錯視窗的 Mission Info 分頁展開 \"Objective progress raw dump\" 並回報內容。",
                "[MissionObjectiveReader]");
        }
        else if (found.Count > 0)
        {
            loggedEmptyForMission = 0;
        }

        Cache.Clear();
        Cache.AddRange(found);
    }

    /// <summary>這一組文字裡有沒有任何一段含有指定的標題字樣。</summary>
    private static bool HasLabel(List<string> texts, string[] labels)
    {
        foreach (var text in texts)
        {
            foreach (var label in labels)
            {
                if (text.Contains(label, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 從一個節點群組裡讀出「達成條件」那一列的內容。
    /// </summary>
    /// <remarks>
    /// 遊戲的一列是「條件內容」＋「銀星達成條件／金星達成條件」兩個文字節點，
    /// 所以<b>標題以外的那一段就是內容</b>。只有標題、沒有內容時回 <c>default</c>
    /// ——寧可讓呼叫端顯示「不知道」，也不要拿標題自己充當內容。
    /// </remarks>
    private static MedalCondition ReadCondition(List<string> texts, string[] labels)
    {
        var labelIndex = -1;
        for (var i = 0; i < texts.Count && labelIndex < 0; i++)
        {
            foreach (var label in labels)
            {
                if (texts[i].Contains(label, StringComparison.OrdinalIgnoreCase))
                {
                    labelIndex = i;
                    break;
                }
            }
        }

        if (labelIndex < 0)
            return default;

        for (var i = 0; i < texts.Count; i++)
        {
            if (i == labelIndex)
                continue;

            var value = texts[i];
            if (value.Length == 0)
                continue;

            var match = MedalTimeRequirement.Match(value);
            int? required = null;
            if (match.Success)
                required = ToSeconds(match.Groups[1].Value, match.Groups[2].Value);

            return new MedalCondition(value, required);
        }

        return default;
    }

    /// <summary>「分:秒」兩段字串換算成秒。任一段不是數字就回 null。</summary>
    private static int? ToSeconds(string minutes, string seconds)
    {
        if (!int.TryParse(minutes, out var m) || !int.TryParse(seconds, out var s))
            return null;
        return (m * 60) + s;
    }

    /// <summary>
    /// 把一個 UldManager 底下的文字節點收進 <paramref name="groups"/>，並遞迴進入元件節點。
    /// 分組鍵是「文字節點的父節點指標」；元件內部的節點則統一掛在該元件節點底下。
    /// </summary>
    private static unsafe void CollectTexts(
        AtkUldManager* uld,
        List<(nint Owner, float ScreenY, List<string> Texts)> groups,
        Dictionary<nint, int> index,
        ref int budget,
        int depth)
    {
        if (uld == null || depth > MaxDepth)
            return;
        if (uld->NodeList == null || uld->NodeListCount <= 0)
            return;

        for (var i = 0; i < uld->NodeListCount; i++)
        {
            if (budget-- <= 0)
                return;

            var node = uld->NodeList[i];
            if (node == null)
                continue;

            // 不可見的節點（例如沒用到的預留列）不要收，否則會混進空白/舊資料。
            // ⚠️ 這裡刻意不用 AtkResNode.IsVisible()：那是特徵碼綁定的成員函式，台服簽名沒中就會炸。
            //    改成純欄位讀取自己往上走父節點鏈，行為等價且不依賴任何簽名。
            if (!IsVisibleChain(node))
                continue;

            if (node->Type == NodeType.Text)
            {
                var textNode = (AtkTextNode*)node;
                var text = textNode->NodeText.GetText();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var owner = (nint)(node->ParentNode != null ? node->ParentNode : node);
                if (!index.TryGetValue(owner, out var slot))
                {
                    slot = groups.Count;
                    index[owner] = slot;
                    groups.Add((owner, node->ScreenY, new List<string>()));
                }
                groups[slot].Texts.Add(text);
            }
            else if ((int)node->Type >= 1000)
            {
                var componentNode = (AtkComponentNode*)node;
                if (componentNode->Component == null)
                    continue;

                CollectTexts(&componentNode->Component->UldManager, groups, index, ref budget, depth + 1);
            }
        }
    }

    /// <summary>
    /// 除錯用：把 addon 的 AtkValues 與分組後的文字節點全部倒出來。
    /// 之後若要把來源改成寫死的 AtkValue 索引（比走訪節點便宜），就靠這份輸出來校準。
    /// </summary>
    internal static unsafe List<string> DumpDiagnostics()
    {
        var lines = new List<string>();
        try
        {
            var ptr = Svc.GameGui.GetAddonByName(AddonName, 1);
            if (ptr.Address == nint.Zero)
            {
                lines.Add($"{AddonName} 未載入");
                return lines;
            }

            var addon = (AtkUnitBase*)ptr.Address;
            lines.Add($"IsVisible={addon->IsVisible} IsReady={addon->IsReady} AtkValuesCount={addon->AtkValuesCount} NodeListCount={addon->UldManager.NodeListCount}");

            lines.Add("--- AtkValues ---");
            // AtkValuesCount 非 0 不保證 AtkValues 指標有效：addon 已建立但值陣列尚未配置
            // （或已釋放）時，計數仍讀得到舊值而指標是 null，裸索引就是對 null 解參考。
            // AVE 不是 .NET 攔得到的例外，下面那層 try/catch 接不住，所以必須先判空。
            if (addon->AtkValues == null)
            {
                lines.Add($"(AtkValues 指標為 null，AtkValuesCount={addon->AtkValuesCount}，本節略過)");
            }
            else
            {
                for (var i = 0; i < addon->AtkValuesCount; i++)
                {
                    var value = addon->AtkValues[i];
                    var rendered = value.Type switch
                    {
                        ValueType.String or ValueType.ManagedString or ValueType.String8 =>
                            value.String.Value != null ? $"\"{GameTextUtil.EscapeGameIcons(Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).GetText())}\"" : "\"\"",
                        ValueType.Int => value.Int.ToString(),
                        ValueType.UInt => value.UInt.ToString(),
                        ValueType.Bool => value.Byte.ToString(),
                        _ => value.Type.ToString(),
                    };
                    if (rendered is "\"\"" or "0")
                        continue; // 空值太多，只印有內容的
                    lines.Add($"[{i}] {value.Type}: {rendered}");
                }
            }

            lines.Add("--- 文字節點（依父節點分組，按畫面 Y 排序）---");
            var groups = new List<(nint Owner, float ScreenY, List<string> Texts)>();
            var index = new Dictionary<nint, int>();
            var budget = MaxNodes;
            CollectTexts(&addon->UldManager, groups, index, ref budget, 0);
            // 🔑 傾印是要拿去回報的，所以圖示字元跳脫成 \uE0BE 而不是剝掉——
            //    「本來有哪個圖示字元」正是排查時要知道的事，畫成「�」等於沒情報。
            foreach (var group in groups.OrderBy(g => g.ScreenY))
                lines.Add($"Y={group.ScreenY:F0} | {string.Join(" ǀ ", group.Texts.Select(GameTextUtil.EscapeGameIcons))}");

            lines.Add("--- 解析結果 ---");
            lines.Add($"TimeRemaining={TimeRemaining ?? "(無)"} RemainingSeconds={RemainingSeconds?.ToString() ?? "(無)"} TotalSeconds={TotalSeconds?.ToString() ?? "(無)"}");
            lines.Add($"SilverCondition={(SilverCondition.HasText ? SilverCondition.Raw : "(無)")} 門檻秒={SilverCondition.RequiredRemainingSeconds?.ToString() ?? "(非時間型)"}");
            lines.Add($"GoldCondition={(GoldCondition.HasText ? GoldCondition.Raw : "(無)")} 門檻秒={GoldCondition.RequiredRemainingSeconds?.ToString() ?? "(非時間型)"}");
        }
        catch (Exception ex)
        {
            lines.Add($"例外：{ex.Message}");
        }
        return lines;
    }

    /// <summary>
    /// 節點自己以及所有祖先都掛著 <see cref="NodeFlags.Visible"/> 才算看得見。
    /// 純欄位讀取（不呼叫任何特徵碼函式），迴圈上限固定避免壞掉的鏈造成無窮迴圈。
    /// </summary>
    private static unsafe bool IsVisibleChain(AtkResNode* node)
    {
        var current = node;
        for (var up = 0; up < 16 && current != null; up++)
        {
            if ((current->NodeFlags & NodeFlags.Visible) == 0)
                return false;
            current = current->ParentNode;
        }
        return true;
    }
}
