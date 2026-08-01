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

    private const string AddonName = "WKSMissionInfomation";

    /// <summary>走訪上限，純粹是防呆用的天花板，正常的 addon 遠遠用不到。</summary>
    private const int MaxNodes = 512;
    private const int MaxDepth = 8;

    private static readonly List<ObjectiveLine> Cache = new();
    private static uint cachedMissionId;

    /// <summary>已經為哪個任務印過「抓不到目標列」的提示，避免每 250ms 洗一次記錄檔。</summary>
    private static uint loggedEmptyForMission;

    /// <summary>
    /// 取得目前任務的目標進度。面板沒開、或找不到符合形狀的資料時回傳空清單。
    /// </summary>
    internal static IReadOnlyList<ObjectiveLine> Get(uint missionId)
    {
        if (missionId == 0)
        {
            if (Cache.Count > 0)
                Cache.Clear();
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

    private static unsafe void Refresh()
    {
        var found = new List<ObjectiveLine>();

        try
        {
            var ptr = Svc.GameGui.GetAddonByName(AddonName, 1);
            if (ptr.Address == nint.Zero)
            {
                Cache.Clear();
                return;
            }

            var addon = (AtkUnitBase*)ptr.Address;
            if (!addon->IsVisible || !addon->IsReady)
            {
                Cache.Clear();
                return;
            }

            // owner 指標 -> 那一組收集到的文字（保留走訪順序）。
            var groups = new List<(nint Owner, float ScreenY, List<string> Texts)>();
            var index = new Dictionary<nint, int>();
            var budget = MaxNodes;

            CollectTexts(&addon->UldManager, groups, index, ref budget, 0);

            foreach (var group in groups.OrderBy(g => g.ScreenY))
            {
                // 一列目標 = 一個「n/m」＋至少一段說明文字。
                string? progress = null;
                var labels = new List<string>();

                foreach (var text in group.Texts)
                {
                    var match = FractionOnly.Match(text);
                    if (match.Success)
                    {
                        // 同一組出現兩個 n/m 就代表這不是我們認得的形狀，整組丟掉。
                        if (progress != null)
                        {
                            progress = null;
                            break;
                        }
                        progress = text.Trim();
                        continue;
                    }

                    var trimmed = text.Trim();
                    if (trimmed.Length > 0)
                        labels.Add(trimmed);
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
            for (var i = 0; i < addon->AtkValuesCount; i++)
            {
                var value = addon->AtkValues[i];
                var rendered = value.Type switch
                {
                    ValueType.String or ValueType.ManagedString or ValueType.String8 =>
                        value.String.Value != null ? $"\"{Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).GetText()}\"" : "\"\"",
                    ValueType.Int => value.Int.ToString(),
                    ValueType.UInt => value.UInt.ToString(),
                    ValueType.Bool => value.Byte.ToString(),
                    _ => value.Type.ToString(),
                };
                if (rendered is "\"\"" or "0")
                    continue; // 空值太多，只印有內容的
                lines.Add($"[{i}] {value.Type}: {rendered}");
            }

            lines.Add("--- 文字節點（依父節點分組，按畫面 Y 排序）---");
            var groups = new List<(nint Owner, float ScreenY, List<string> Texts)>();
            var index = new Dictionary<nint, int>();
            var budget = MaxNodes;
            CollectTexts(&addon->UldManager, groups, index, ref budget, 0);
            foreach (var group in groups.OrderBy(g => g.ScreenY))
                lines.Add($"Y={group.ScreenY:F0} | {string.Join(" ǀ ", group.Texts)}");
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
