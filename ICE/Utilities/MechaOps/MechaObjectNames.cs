using Dalamud.Game.ClientState.Objects.Enums;
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

    /// <summary>
    /// <c>WKSMechaEventObject</c> 的列 id → (物件 <c>DataId</c>, 物件種類欄 <c>Unknown3</c>)。
    /// 這是 <see cref="WKSMechaEventObjectGroup"/> 的解參考表：群組表的值是**這張表的列號**，
    /// 不是 DataId，所以要先有這一份才查得動群組。
    /// </summary>
    private static Dictionary<uint, (uint BaseId, uint Type)>? objectRows;

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

    /// <summary>
    /// 這個 <c>BaseId</c> 是不是「機甲事件會用到的物件」——<b>不分身份、不分是哪一場事件</b>。
    ///
    /// 🔑 <b>用途只有一個：當雜訊過濾的豁免</b>。它回答的是「這東西屬不屬於機甲事件這個家族」，
    /// 不是「這東西是不是你該打的目標」（後者請用
    /// <see cref="ObjectIdsForRole"/> 或 <c>MechaObjectiveTracker.IsObjectiveBaseId</c>）。
    ///
    /// 🔴 <b>為什麼要有這一層</b>（2026-08-08 實機）：機甲事件的目標<b>普遍沒有名字、而且不可選取</b>
    /// ——菌床場連駕駛員的目標都無名（使用者原話：「有害菌床驅除指令 協助員的目標 沒名字
    /// 也是只有自己看得到　機甲得目標 也沒名字」）。水晶場的駕駛員路徑之所以看起來正常，
    /// 純粹是因為 2014720 巨型偏屬性水晶<b>剛好</b>有名字。
    /// 也就是說「無名＋不可選取＝場景裝飾」這條雜訊規則，對機甲目標是<b>系統性誤殺</b>，
    /// 只是先前被一個巧合遮住。這裡用 <c>DataId</c> 家族把它們整批擋在雜訊規則之外。
    /// </summary>
    public static bool IsKnownEventObjectId(uint baseId)
        => baseId != 0 && KnownEventObjectIds.Contains(baseId);

    /// <summary>
    /// 從實體的 <see cref="ObjectKind"/> 推它屬於哪一種身份的目標。
    /// ⚠️ <b>只對機甲事件家族的 <c>DataId</c> 有意義</b>——呼叫端必須先過
    /// <see cref="IsKnownEventObjectId"/>，否則場景裡一堆 EventObj 都會被判成駕駛員目標。
    ///
    /// 📌 <b>證據</b>（2026-08-08 兩場實機錄製，四個 DataId 全部吻合）：
    /// <code>
    ///   2014720 巨型偏屬性水晶 EventObj  → 駕駛員
    ///   2014722 巨型變異菌床   EventObj  → 駕駛員（對協助員 targetable=False 全程 1317 次）
    ///   2014721 小型偏屬性水晶 CardStand → 協助員（per-player 生成）
    ///   2014723 小型變異菌床   CardStand → 協助員（per-player 生成，同時可有多個實例）
    /// </code>
    /// 🔑 規律是<b>執行期生成的 per-player 目標會是 <c>CardStand</c>，靜態佈置的巨型目標是
    /// <c>EventObj</c></b>，跟 <c>WKSMechaEventObject.Unknown3</c> 的 4／3 完全對得起來。
    ///
    /// 🔴 <b>但這是觀察到的相關性，不是遊戲資料裡的角色標記</b>，所以它在
    /// <see cref="ObjectIdsForRole"/>（群組表，權威且分得出是哪一場事件）<b>之後</b>才問，
    /// 而且<b>只用來把分級往上調、絕不往下調</b>。這樣即使規律在未來的事件不成立，
    /// 失敗形式也只是「少補一個」，不會把群組表判對的目標踢掉。
    /// </summary>
    public static MechaRole RoleByObjectKind(ObjectKind kind) => kind switch
    {
        ObjectKind.CardStand => MechaRole.GroundSupport,
        ObjectKind.EventObj => MechaRole.Pilot,
        _ => MechaRole.Unknown,
    };

    // ---- 身份相關（2026-08-06 新增，2026-08-08 改為資料表群組驅動）----

    /// <summary>上一次做身份分類時用的事件列 id。換事件就要重算。</summary>
    private static uint roleSplitRowId;
    private static HashSet<uint>? groundSupportIds;
    private static HashSet<uint>? pilotIds;

    /// <summary>
    /// 這個身份**該看到**的機甲事件物件 <c>DataId</c>。
    ///
    /// 🔴🔴 <b>2026-08-08 重做：判定路徑完全不碰名字。</b>
    /// 舊版是拿物件名去比對事件的指示文字（名字出現在協助員那段就歸協助員，其餘歸駕駛員）。
    /// 那個做法有兩個致命問題，而且兩個都已被實機證據推翻：
    /// <list type="number">
    ///   <item><b>機甲目標普遍沒有名字。</b>2014721（協助員的小型偏屬性水晶）與菌床場的
    ///         駕駛員／協助員目標在 <c>EObjName</c> 裡都是空字串 ⇒ 比對必定落空。</item>
    ///   <item><b>落空時的預設是「歸駕駛員」</b> ⇒ 協助員的白名單只剩野外探測器一個
    ///         （實機 <c>wlRole=1</c> 全場逐字吻合），他真正要打的那個反而被排除。</item>
    /// </list>
    /// ⇒ <b>名字從此只准當顯示欄位</b>，歸屬一律走 <c>DataId</c>。
    ///
    /// 現在的資料鏈（純數值，離線可驗，台服 7.20 兩場事件都吻合）：
    /// <code>
    ///   WKSMechaEventData[事件列].Unknown18  → 協助員的物件群組
    ///   WKSMechaEventData[事件列].Unknown19  ┐
    ///   WKSMechaEventData[事件列].Unknown20  ┘ 駕駛員的物件群組（兩批，疑似兩波）
    ///     → WKSMechaEventObjectGroup[群組].Unknown0（subrow）＝ WKSMechaEventObject 的列號
    ///       → WKSMechaEventObject[列].Unknown1 ＝ EObj 的 DataId
    ///         WKSMechaEventObject[列].Unknown3 ＝ 物件種類（7＝野外探測器／3＝巨型／4＝小型）
    /// </code>
    /// 台服 7.20 展開後（<c>exd-tc/7.20</c> 離線核對）：
    /// <code>
    ///   事件 1 巨型偏屬性水晶破壞指令：群組 6／7／8
    ///     群組 6 （協助員）= 2014717 野外探測器 ×3(type 7) ＋ 2014721 小型水晶 ×3(type 4)
    ///     群組 7+8（駕駛員）= 2014720 巨型偏屬性水晶 ×22(type 3)
    ///   事件 5 有害菌床驅除指令：群組 9／10／11
    ///     群組 9  （協助員）= 2014717 野外探測器 ×3(type 7) ＋ 2014723 小型菌床 ×4(type 4)
    ///     群組 10+11（駕駛員）= 2014722 巨型變異菌床 ×25(type 3)
    /// </code>
    /// 🔑 <b>三條互相獨立的證據都指向同一組結論</b>，所以這個歸屬不是猜的：
    /// ①群組表本身的結構（兩場事件形狀完全對稱）；
    /// ②種類欄（協助員群組只有 4／7，駕駛員群組只有 3）；
    /// ③指示文字（協助員那段點名「小型…」＋「野外探測器」，駕駛員那段點名「巨型…」）
    /// ——③只拿來<b>驗證</b>，不參與執行期判定。
    /// ⚠️ 順帶更正一個容易猜錯的地方：菌床場<b>協助員</b>的目標是 <b>2014723</b>，
    /// 不是 2014722（後者是駕駛員的巨型目標）。
    ///
    /// ⚠️ <see cref="MechaRole.Unknown"/> 一律回空集合——判不出身份時只信遊戲自己的標記。
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

    /// <summary>診斷用：這一輪的身份分類是怎麼算出來的（群組表 or 後備）。</summary>
    public static string RoleSplitSource { get; private set; } = "未建立";

    private static void EnsureRoleSplit(uint dataRowId)
    {
        if (groundSupportIds != null && roleSplitRowId == dataRowId)
            return;

        EnsureBuilt();
        var all = knownEventObjectIds;
        if (all == null)
            return;   // 白名單本身還沒建起來；不要把空結果快取住，下次再試

        HashSet<uint> support;
        HashSet<uint> pilot;
        string source;

        if (TryBuildRoleSplitFromGroups(dataRowId, out var groupSupport, out var groupPilot))
        {
            support = groupSupport;
            pilot = groupPilot;
            source = "群組表";

            // 🔑 <b>這一場事件的群組沒提到的 DataId 一律「兩種身份都顯示」</b>。
            //    可能來源：別場事件的物件、或群組表沒收進去的新東西。
            //    兩個方向的失敗代價不對等——多顯示一個圈只是雜訊，
            //    少顯示的那個可能正是使用者要打的目標（這次的 bug 就是這樣來的），
            //    所以未知一律倒向「都顯示」，不猜邊。
            foreach (var id in all)
            {
                if (support.Contains(id) || pilot.Contains(id))
                    continue;
                support.Add(id);
                pilot.Add(id);
            }
        }
        else
        {
            // 後備：群組表讀不到或形狀不對（例如日後改版）。
            // 🔑 這裡刻意<b>不</b>退回舊的「比名字」做法——那個做法對無名目標必定落空，
            //    而落空的方向是「協助員少看到自己的目標」。改成整份都給兩邊：
            //    退化行為變成「多顯示」而不是「漏顯示」，跟上面未知 DataId 的處置同一個原則。
            support = [.. all];
            pilot = [.. all];
            source = "後備（群組表不可用，兩身份共用完整白名單）";
        }

        // 駕駛員仍然看得到協助員那一批（野外探測器是共用的）。
        // 多畫一個探測器不會造成「打錯批」，所以這一邊不必收緊。
        pilot.UnionWith(support);

        groundSupportIds = support;
        pilotIds = pilot;
        roleSplitRowId = dataRowId;
        RoleSplitSource = source;
    }

    /// <summary>
    /// 依 <c>WKSMechaEventData</c> 的三個群組欄把這一場事件的物件分給兩種身份。
    ///
    /// 🔴 <b>自我驗證</b>：這裡用到的欄位語意（Unknown18＝協助員群組、Unknown19/20＝駕駛員群組、
    /// <c>WKSMechaEventObject.Unknown3</c>＝物件種類）是我們自己從資料推出來的，不是官方文件。
    /// 所以展開之後要通過三關才收下：
    /// <list type="number">
    ///   <item>協助員群組不得是空的（空的代表欄位語意不對，不是「這場沒有協助員」）；</item>
    ///   <item>協助員那批裡<b>不能</b>出現種類 3（＝駕駛員的巨型目標）；</item>
    ///   <item>駕駛員那批裡<b>不能</b>出現種類 4（＝協助員的小型目標）。</item>
    /// </list>
    /// 任何一關沒過就整份放棄、回 <c>false</c>，由呼叫端走後備。
    /// 也就是說推論若是錯的，結果是「退回兩邊都顯示」，<b>不會</b>把某一邊的目標藏起來。
    /// </summary>
    private static bool TryBuildRoleSplitFromGroups(uint dataRowId, out HashSet<uint> support, out HashSet<uint> pilot)
    {
        support = [];
        pilot = [];

        if (dataRowId == 0 || objectRows == null)
            return false;

        try
        {
            var eventRow = Svc.Data.GetExcelSheet<WKSMechaEventData>()?.GetRowOrDefault(dataRowId);
            if (eventRow == null)
                return false;

            var groupSheet = Svc.Data.GetSubrowExcelSheet<WKSMechaEventObjectGroup>();
            if (groupSheet == null)
                return false;

            var supportGroup = (uint)eventRow.Value.Unknown18;
            var pilotGroupA = (uint)eventRow.Value.Unknown19;
            var pilotGroupB = (uint)eventRow.Value.Unknown20;

            // 種類欄的守衛（見方法註解的三關）。
            var supportTypes = new HashSet<uint>();
            var pilotTypes = new HashSet<uint>();

            CollectGroup(groupSheet, supportGroup, support, supportTypes);
            CollectGroup(groupSheet, pilotGroupA, pilot, pilotTypes);
            CollectGroup(groupSheet, pilotGroupB, pilot, pilotTypes);

            if (support.Count == 0)
                return false;
            if (supportTypes.Contains(PilotObjectType))
                return false;
            if (pilotTypes.Contains(GroundSupportObjectType))
                return false;

            return true;
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaRoleSplitGroupFailed", 60_000))
                IceLogging.Info($"讀機甲事件物件群組失敗，身份分類改走後備：{ex.Message}", "[MechaOps]");
            return false;
        }
    }

    /// <summary><c>WKSMechaEventObject.Unknown3</c>：駕駛員的巨型目標。</summary>
    private const uint PilotObjectType = 3;

    /// <summary><c>WKSMechaEventObject.Unknown3</c>：協助員的小型目標（執行期 per-player 生成）。</summary>
    private const uint GroundSupportObjectType = 4;

    /// <summary>
    /// 把一個群組展開成 <c>DataId</c> 集合。
    /// ⚠️ 群組表的值是 <c>WKSMechaEventObject</c> 的<b>列號</b>，不是 <c>DataId</c>——
    /// 直接當 DataId 用會查無此物件而且不報錯。
    /// </summary>
    private static void CollectGroup(
        Lumina.Excel.SubrowExcelSheet<WKSMechaEventObjectGroup> groupSheet,
        uint groupId,
        HashSet<uint> into,
        HashSet<uint> types)
    {
        if (groupId == 0 || objectRows == null)
            return;

        var subrows = groupSheet.GetRowOrDefault(groupId);
        if (subrows == null)
            return;

        foreach (var sub in subrows.Value)
        {
            var objectRowId = (uint)sub.Unknown0;
            if (objectRowId == 0)
                continue;
            if (!objectRows.TryGetValue(objectRowId, out var entry))
                continue;   // 那一列沒通過 EnsureBuilt 的自我驗證，跳過

            into.Add(entry.BaseId);
            types.Add(entry.Type);
        }
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
        Dictionary<uint, (uint BaseId, uint Type)> rowsById;

        try
        {
            var sheet = Svc.Data.GetExcelSheet<WKSMechaEventObject>();
            var nameSheet = Svc.Data.GetExcelSheet<EObjName>();
            if (sheet == null || nameSheet == null)
                return;   // 資料還沒好，不快取，下次再來

            ids = [];
            map = [];
            rowsById = [];

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

                // 🔴🔴 <b>2026-08-08 實機推翻</b>：這裡原本還有第三關「沒有佈局實例 id 就跳過」，
                //    理由寫的是「2014721／2014723 看起來是出生點之類的佔位資料，不是玩家看得到的目標」。
                //    **那個推論是錯的，而且它正是協助員目標判定失效的根因。**
                //
                //    使用者 2026-08-08 以協助員身份跑完一整場「巨型偏屬性水晶破壞指令」，
                //    <c>[MechaRec]</c> 錄到 <c>did=2014721</c> 的實體 <b>308 次</b>
                //    （<c>kind=CardStand</c>、無名、<c>targetable=False</c>、事件中段才 SPAWN），
                //    而使用者的宇宙鑽頭 11 發 <c>CAST-PRED-RAW</c> <b>每一發都蓋到它</b>（0.00~6.74 公尺）——
                //    它就是協助員實際在打的那個「小型偏屬性水晶」。
                //    被排除在白名單外的後果：它拿不到 <c>Likely</c> 分級，於是掉進
                //    <c>MechaOpsMonitor.IsKnownNoise</c>（無名＋不可選取）被靜默濾掉
                //    ——308 次裡有 <b>151 次</b> 的 <c>iceFilter</c> 就是 <c>known-noise</c>。
                //
                //    📌 <b>「沒有佈局實例 id」的真正語意是「執行期動態生成」而不是「佔位」</b>：
                //    協助員的小型目標是 per-player 生成的（使用者：「協助員的目標都只有自己看得到」），
                //    本來就不可能有靜態佈局實例 id。所以那一欄只能拿來建佈局對照，
                //    <b>不能拿來當「這個 DataId 算不算目標」的判準</b>。
                ids.Add(baseId);
                rowsById[row.RowId] = (baseId, (uint)row.Unknown3);

                // 佈局對照仍然只收得到實例 id 的那些（純診斷用，見 LayoutToBaseId）。
                var layoutId = row.Unknown0;
                if (layoutId != 0)
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
        objectRows = rowsById;

        // 📌 Information 而不是 Debug：使用者跑 LogLevel 2，而這一行正是
        //    「白名單有沒有生效」唯一問得出答案的地方（空的＝欄位推論不成立）。
        IceLogging.Info(
            $"機甲事件物件白名單：{ids.Count} 個 DataId"
            + (ids.Count == 0
                ? "（空的＝WKSMechaEventObject 的欄位語意跟預期不同，過濾退回純執行期配對）"
                : "：" + string.Join(", ", ids.OrderBy(x => x).Select(id => $"{id}({FromSheet(id) ?? "無名"})")))
            + $"；佈局對照 {map.Count} 筆；物件列 {rowsById.Count} 筆（身份分群用）",
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
