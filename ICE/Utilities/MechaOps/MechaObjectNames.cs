using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 機甲行動裡「這個物件到底叫什麼、算不算任務目標」的**資料表**後備。
///
/// 🔑 <b>為什麼需要這一層</b>（2026-08-06 使用者回報）：機甲行動「有害菌床驅除指令」的
/// 目標物件在 ObjectTable 裡<b>名字是空的</b>，畫面上就只剩一個沒有標籤的圈。
/// 唯一的補救是關掉「只顯示可選取的物件」，但那會把整片場景與 NPC 一起灌進來。
///
/// 📌 <b>離線已證實</b>（<c>exd-tc/7.20</c>，台服繁中）：那不是我們讀錯，
/// 是<b>遊戲資料本身就沒有名字</b>。
/// <code>
///   WKSMechaEventData 第 1 列 = 巨型偏屬性水晶破壞指令
///   WKSMechaEventData 第 5 列 = 有害菌床驅除指令        ← 使用者截圖的橫幅逐字相符
///
///   WKSMechaEventObject（欄 0 = 佈局實例 id、欄 1 = EObj 的 DataId）
///     47..49   DataId 2014717   EObjName[2014717] = "野外探測器"
///     56..77   DataId 2014720   EObjName[2014720] = "巨型偏屬性水晶"
///     91..115  DataId 2014722   EObjName[2014722] = ""      ← 有害菌床，**空字串**
///     53..55   DataId 2014721   EObjName[2014721] = ""
///     87..90   DataId 2014723   EObjName[2014723] = ""
/// </code>
/// 所以「查資料表拿名字」對這一場事件必定失敗，<b>後備標籤是唯一的解</b>——
/// 而且不能畫成空白（「不知道」要在畫面上看得見）。
///
/// 這個類別提供兩件事：
///  1. <see cref="FromSheet"/>：<c>BaseId</c> → <c>EObjName</c> 的名字（拿得到就用，這是最好的情況）。
///  2. <see cref="KnownEventObjectIds"/>：機甲事件用得到的那幾個 <c>DataId</c> 白名單，
///     讓過濾器分得出「無名的任務目標」與「無名的場景裝飾」。
///
/// 🔴 <b>自我驗證</b>：(2) 讀的是 <c>WKSMechaEventObject.Unknown1</c>——那個欄位的語意是
/// 我們自己推出來的，不是官方文件。所以每一個值都要通過「落在 EObj 的 id 區間」＋
/// 「<c>EObjName</c> 真的有這一列」兩關才收下。推論若是錯的，結果是<b>白名單空掉</b>
/// （＝退回純執行期配對，也就是這個檔案出現以前的行為），不會冒出一堆錯的目標。
/// </summary>
internal static class MechaObjectNames
{
    /// <summary>EObj／EObjName 的列 id 區間。台服 7.20 實測 EObjName 是 2000000..2014999。</summary>
    private const uint EObjIdMin = 2_000_000;
    private const uint EObjIdMax = 2_099_999;

    /// <summary>BaseId → 資料表名字。空字串＝查過了，資料表就是沒給名字（不要再查一次）。</summary>
    private static readonly Dictionary<uint, string> sheetNames = [];

    private static HashSet<uint>? knownEventObjectIds;
    private static Dictionary<uint, uint>? layoutToBaseId;

    /// <summary>共用的空集合。資料表還沒準備好時回這一份，**不快取**，下一幀會再試。</summary>
    private static readonly HashSet<uint> EmptyIds = [];

    private static readonly Dictionary<uint, uint> EmptyMap = [];

    /// <summary>
    /// <c>BaseId</c>（＝<c>IGameObject.BaseId</c>，EventObj 的話就是 EObj 的列 id）→ 物件名。
    /// 拿不到（不在 EObj 區間、沒有這一列、或名字是空的）一律回 <c>null</c>，
    /// 由呼叫端決定要畫什麼後備標籤。
    /// </summary>
    public static string? FromSheet(uint baseId)
    {
        if (baseId < EObjIdMin || baseId > EObjIdMax)
            return null;

        if (sheetNames.TryGetValue(baseId, out var cached))
            return cached.Length == 0 ? null : cached;

        var text = string.Empty;
        try
        {
            var row = Svc.Data.GetExcelSheet<EObjName>()?.GetRowOrDefault(baseId);
            if (row != null)
            {
                // 任務／物件名開頭可能夾私用區圖示字元，ImGui 畫不出來（見 GameTextUtil）。
                text = GameTextUtil.StripGameIcons(row.Value.Singular.ExtractText());
            }
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaObjectNameSheetFailed", 60_000))
                IceLogging.Info($"讀 EObjName 失敗，物件名改用後備標籤：{ex.Message}", "[MechaOps]");
            return null;   // 這次不快取，下次還可以再試
        }

        sheetNames[baseId] = text;
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// 機甲事件會用到的物件 <c>DataId</c> 白名單（台服 7.20 應為 2014717／2014720／2014722）。
    ///
    /// 🔑 用途是**過濾**，不是識別：清單裡的東西即使沒有名字、也不可選取，
    /// 仍然要當成「疑似任務目標」列出來；反過來不在清單裡的無名場景裝飾才是雜訊。
    /// 讀不到就是空集合＝這條線索不生效，過濾完全退回原本的行為。
    ///
    /// 🔴 <b>這一份沒有分身份</b>，直接拿去過濾會讓協助員看到駕駛員的目標
    /// （2026-08-06 實機回報）。過濾一律走 <see cref="ObjectIdsForRole"/>。
    /// </summary>
    public static IReadOnlySet<uint> KnownEventObjectIds
    {
        get
        {
            EnsureBuilt();
            return knownEventObjectIds ?? EmptyIds;
        }
    }

    // ---- 身份相關（2026-08-06 新增）----

    /// <summary>上一次做身份分類時用的事件列 id。換事件就要重算。</summary>
    private static uint roleSplitRowId;
    private static HashSet<uint>? groundSupportIds;
    private static HashSet<uint>? pilotIds;

    /// <summary>
    /// 這個身份**該看到**的機甲事件物件 <c>DataId</c>。
    ///
    /// 分類方式刻意<b>不</b>依賴 <c>WKSMechaEventObject</c> 那幾個語意不明的數值欄，
    /// 而是拿物件自己的名字去比對這一場事件的兩段指示文字（見 <see cref="EventObjectiveText"/>）：
    /// <list type="bullet">
    ///   <item>名字出現在<b>協助員</b>指示文字裡 → 協助員該看的（例如「野外探測器」）；</item>
    ///   <item>其餘一律歸<b>駕駛員</b>——包含沒有名字、比對不出來的。</item>
    /// </list>
    ///
    /// 🔑 <b>預設倒向保守那一邊是刻意的</b>：分不出來就歸給駕駛員，代表協助員拿到的是一份
    /// <b>偏小</b>的白名單。這個方向的失敗是「少畫幾個，還有遊戲自己的標記兜底」；
    /// 反過來把駕駛員的巨型目標塞給協助員，失敗形式就是使用者回報的「目標不一樣」。
    ///
    /// ⚠️ <see cref="MechaRole.Unknown"/> 一律回空集合——判不出身份時只信遊戲自己的標記。
    ///
    /// 📌 台服 7.20 的實際分類（離線核對）：
    /// <code>
    ///   2014717 野外探測器      → 出現在兩場事件的協助員指示文字裡 → 協助員
    ///   2014720 巨型偏屬性水晶  → 沒出現在協助員文字裡（在駕駛員文字裡）→ 駕駛員
    ///   2014722 （無名，即巨型變異菌床）→ 沒有名字可比對 → 駕駛員（保守預設）
    /// </code>
    /// 第三筆正是使用者回報的那一個，而保守預設剛好給了正確答案。
    /// </summary>
    public static IReadOnlySet<uint> ObjectIdsForRole(uint dataRowId, MechaRole role)
    {
        if (role == MechaRole.Unknown)
            return EmptyIds;

        EnsureRoleSplit(dataRowId);

        if (role == MechaRole.GroundSupport)
            return groundSupportIds ?? EmptyIds;

        // 駕駛員：自己的目標 ＋ 協助員也看得到的共用物件（野外探測器之類）。
        // 多畫一個探測器不會造成「打錯批」，所以這一邊不必收緊。
        return pilotIds ?? EmptyIds;
    }

    /// <summary>診斷用：這一場事件分到協助員那一邊的有幾個。</summary>
    public static int RoleIdCount(uint dataRowId, MechaRole role)
    {
        if (role == MechaRole.Unknown)
            return 0;
        EnsureRoleSplit(dataRowId);
        return (role == MechaRole.GroundSupport ? groundSupportIds?.Count : pilotIds?.Count) ?? 0;
    }

    private static void EnsureRoleSplit(uint dataRowId)
    {
        if (groundSupportIds != null && roleSplitRowId == dataRowId)
            return;

        EnsureBuilt();
        var all = knownEventObjectIds;
        if (all == null)
            return;   // 白名單本身還沒建起來；不要把空結果快取住，下次再試

        var support = new HashSet<uint>();
        var pilot = new HashSet<uint>();

        var supportText = EventObjectiveText(dataRowId, MechaRole.GroundSupport) ?? string.Empty;

        foreach (var id in all)
        {
            var name = FromSheet(id);

            // 🔑 名字要有兩個字以上才拿去做子字串比對：一個字的名字在長句子裡太容易誤命中，
            //    而誤命中的方向剛好是「把駕駛員目標判給協助員」——正是要避免的那一種。
            var isSupport = name is { Length: >= 2 }
                            && supportText.Contains(name, StringComparison.Ordinal);

            if (isSupport)
                support.Add(id);
            else
                pilot.Add(id);
        }

        // 駕駛員看得到全部（自己的＋共用的）。
        pilot.UnionWith(support);

        groundSupportIds = support;
        pilotIds = pilot;
        roleSplitRowId = dataRowId;
    }

    /// <summary>
    /// 這一場事件對**這個身份**的指示文字。
    ///
    /// 📌 離線核對（<c>exd-tc/7.20</c>，兩場事件都吻合）：
    /// <code>
    ///   WKSMechaEventData 欄 Unknown2 ＝ 駕駛員指示
    ///     第 1 列「駕駛動力裝甲，粉碎巨型偏屬性水晶。」
    ///     第 5 列「駕駛輪式鏟裝車，剷除巨型變異菌床。」
    ///   WKSMechaEventData 欄 Unknown3 ＝ 協助員指示
    ///     第 1 列「使用宇宙鑽頭粉碎小型偏屬性水晶。將獲取的資源投入野外探測器分析。」
    ///     第 5 列「使用宇宙火焰噴射器焚燒小型變異菌床。將燃燒後留下的灰燼投入野外探測器分析。」
    /// </code>
    /// 🔑 兩欄的歸屬不是猜的：欄 Unknown2 每一列都以「駕駛…」開頭，而欄 Unknown3 點名的
    /// 宇宙鑽頭／宇宙火焰噴射器正好就是 <c>MechaActionShapes</c> 早就離線驗證過、
    /// 標記為「協助員」的 42150／42258 兩個技能。兩份獨立的資料互相印證。
    /// </summary>
    public static string? EventObjectiveText(uint dataRowId, MechaRole role)
    {
        if (dataRowId == 0 || role == MechaRole.Unknown)
            return null;

        try
        {
            var row = Svc.Data.GetExcelSheet<WKSMechaEventData>()?.GetRowOrDefault(dataRowId);
            if (row == null)
                return null;

            var raw = role == MechaRole.GroundSupport ? row.Value.Unknown3 : row.Value.Unknown2;
            var text = GameTextUtil.StripGameIcons(raw.ExtractText());
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaObjectiveTextFailed", 60_000))
                IceLogging.Info($"讀 WKSMechaEventData 的指示文字失敗，這一段不顯示：{ex.Message}", "[MechaOps]");
            return null;
        }
    }

    /// <summary>
    /// 佈局實例 id → 物件 <c>DataId</c>。
    ///
    /// ⚠️ <b>只給診斷用，不參與任何顯示判斷</b>：這裡假設
    /// <c>WKSMechaEventMapMarker.LayoutId</c>（marker +0xB8）與
    /// <c>WKSMechaEventObject</c> 第 0 欄是同一套編號——兩邊的數值範圍吻合
    /// （皆為 1.09e7~1.18e7），但沒有離線證明。假設不成立時診斷那一行會查不到值，
    /// 顯示與過濾完全不受影響。
    /// </summary>
    public static IReadOnlyDictionary<uint, uint> LayoutToBaseId
    {
        get
        {
            EnsureBuilt();
            return layoutToBaseId ?? EmptyMap;
        }
    }

    /// <summary>上一次嘗試建表的時刻。⚠️ 沒有它的話，資料表還沒好時會**每個物件每幀**重試一次。</summary>
    private static long lastBuildAttemptTick;

    private static void EnsureBuilt()
    {
        if (knownEventObjectIds != null)
            return;

        // 建不起來時最多 5 秒重試一次（呼叫端在每幀的物件迴圈裡）。
        var now = Environment.TickCount64;
        if (lastBuildAttemptTick != 0 && now - lastBuildAttemptTick < 5_000)
            return;
        lastBuildAttemptTick = now;

        HashSet<uint> ids;
        Dictionary<uint, uint> map;

        try
        {
            var sheet = Svc.Data.GetExcelSheet<WKSMechaEventObject>();
            var nameSheet = Svc.Data.GetExcelSheet<EObjName>();
            if (sheet == null || nameSheet == null)
                return;   // 資料還沒好，不快取，下次再來

            ids = [];
            map = [];

            foreach (var row in sheet)
            {
                var raw = row.Unknown1;
                if (raw <= 0)
                    continue;

                var baseId = (uint)raw;

                // 🔑 自我驗證的兩關。沒過就當這一欄不是我們以為的東西，直接跳過。
                if (baseId < EObjIdMin || baseId > EObjIdMax)
                    continue;
                if (nameSheet.GetRowOrDefault(baseId) == null)
                    continue;

                // 🔑 第三關：要有佈局實例 id 才算「真的擺在場景裡的物件」。
                //    台服 7.20 的資料裡，2014721／2014723 這兩個 DataId 的列**沒有**實例 id
                //    （欄 0 全是 0，欄 2 也跟其他列不同），看起來是出生點之類的佔位資料，
                //    不是玩家看得到的目標。收進白名單只會在地上多出幾個看不懂的圈。
                //    ⚠️ 這一關偏嚴：漏掉的東西仍然可以靠執行期的標記配對補回來。
                var layoutId = row.Unknown0;
                if (layoutId == 0)
                    continue;

                ids.Add(baseId);
                map[layoutId] = baseId;
            }
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaObjectWhitelistFailed", 60_000))
                IceLogging.Info($"讀 WKSMechaEventObject 失敗，機甲物件白名單這一輪不生效：{ex.Message}", "[MechaOps]");
            return;
        }

        knownEventObjectIds = ids;
        layoutToBaseId = map;

        // 📌 Information 而不是 Debug：使用者跑 LogLevel 2，而這一行正是
        //    「白名單有沒有生效」唯一問得出答案的地方（空的＝欄位推論不成立）。
        IceLogging.Info(
            $"機甲事件物件白名單：{ids.Count} 個 DataId"
            + (ids.Count == 0
                ? "（空的＝WKSMechaEventObject 的欄位語意跟預期不同，過濾退回純執行期配對）"
                : "：" + string.Join(", ", ids.OrderBy(x => x).Select(id => $"{id}({FromSheet(id) ?? "無名"})")))
            + $"；佈局對照 {map.Count} 筆",
            "[MechaOps]");
    }

    /// <summary>
    /// 目前這場機甲事件的名稱（例如「有害菌床驅除指令」）。取不到回 <c>null</c>。
    /// </summary>
    /// <param name="dataRowId">
    /// <c>WKSMechaEvent.WKSMechaEventDataRowId</c>（+0x50D8 的純量，由取樣端帶過來）。
    /// ⚠️ 「這個欄位是 <c>WKSMechaEventData</c> 的列 id」沿用 CS 的欄位命名，台服沒有另外證明；
    /// 錯的話這裡不是查無此列（回 null＝不顯示）就是顯示另一場事件的名字，兩種都只是顯示問題。
    /// </param>
    public static string? EventName(uint dataRowId)
    {
        if (dataRowId == 0)
            return null;

        try
        {
            var row = Svc.Data.GetExcelSheet<WKSMechaEventData>()?.GetRowOrDefault(dataRowId);
            if (row == null)
                return null;

            var text = GameTextUtil.StripGameIcons(row.Value.Unknown0.ExtractText());
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaEventNameFailed", 60_000))
                IceLogging.Info($"讀 WKSMechaEventData 失敗，事件名不顯示：{ex.Message}", "[MechaOps]");
            return null;
        }
    }
}
