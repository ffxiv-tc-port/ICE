using ICE.Utilities.Cosmic_Helper;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using System.Collections.Generic;
using System.Text;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Utilities
{
    /// <summary>ICE 會自動按下確定的那些確認框，各自是什麼情境。</summary>
    internal enum YesnoSituation
    {
        /// <summary>修理裝備的花費確認（NPC 委託修理與自行修理是同一族確認框）。</summary>
        Repair,

        /// <summary>換套裝時「主手武器不在身上」的替換確認。</summary>
        GearsetMainHand,

        /// <summary>釣起收藏品時的「收為收藏品」確認。</summary>
        FishingCollect,

        /// <summary>宇宙好運道（轉盤）的消耗點數確認。</summary>
        Lottery,

        /// <summary>研究材料繳交的確認。</summary>
        RelicTurnin,
    }

    /// <summary>
    /// 「不是預期的確認框就不要按確定」的共用閘門。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>為什麼需要它</b>：ICE 有好幾個地方是「只要 <c>SelectYesno</c> 開著就按下確定」，
    /// 完全不看上面寫什麼。無人值守時只要有別的確認框剛好跳出來（組隊邀請、交易、
    /// 使用者自己開的視窗），就會被一起按掉。<br/>
    /// 既有的 <c>RejectUnknownYesno</c> 只蓋住兩個地方
    /// （接任務 <c>Task_FindMission.GrabMission</c>、放棄任務 <c>Task_AbandonMission</c>），
    /// 而且它的比對基準是<b>寫死的七國語言字串</b>（<c>CosmicHandler.commenceStrings</c>／
    /// <c>abandonStrings</c>）。這裡蓋的是其餘那些完全沒有防護的地方。<br/>
    /// <br/>
    /// 🔴 <b>預設是 <see cref="UnexpectedYesnoAction.AlwaysConfirm"/>＝現行行為</b>，
    /// 既有使用者升級之後一個位元都不會變。<c>RejectUnknownYesno</c> 預設是
    /// <c>true</c>（已經開著），所以<b>不能</b>把這些新地方掛到那個旗標上 ——
    /// 那等於未經同意就改了所有人的行為。
    /// </remarks>
    internal static class YesnoGuard
    {
        private const string Handle = "[Yesno Guard]";

        /// <summary>
        /// 每個情境的比對基準取自遊戲自己的 <c>Addon</c> 表列。
        /// </summary>
        /// <remarks>
        /// ⚠️ 用遊戲資料而不是寫死字串，是為了自動跟著客戶端語言走
        /// （<c>CosmicHandler</c> 那兩份寫死字串每加一種語言就要補一次）。<br/>
        /// 📌 台服 7.20 的 <c>exd-tc/7.20/Addon.csv</c> 逐列核對過：<br/>
        /// 860「要修理下列裝備嗎？／費用：…」、861「要修理目前所有裝備嗎？／費用：…」、<br/>
        /// 862／863 是同兩句的「缺少觸媒」變體（自行修理走這一族）。<br/>
        /// 4384／4385「沒有發現該套裝中主手上的〔道具名〕。要用…替換該裝備嗎？」、<br/>
        /// 4388「該套裝中主手上的〔道具名〕無法進行裝備。要用…」。<br/>
        /// 1463／3815「確定要將下列道具收為收藏品嗎？／收藏價值：…」。<br/>
        /// 16915「是否消耗〔數量〕再次挑戰宇宙好運道？／[持有：…]」。<br/>
        /// ⚠️ <see cref="YesnoSituation.RelicTurnin"/> 刻意留空：台服 Addon 表裡找不到
        /// 能對應「研究材料繳交」的列（12579「確定要繳交〔道具〕嗎？」後面接的是
        /// <b>持有上限</b>警告，正常情況根本不會出現）。留空的情境只會記錄、不會擋 ——
        /// 猜一個對不上的基準會讓 <see cref="UnexpectedYesnoAction.Decline"/> 把正常流程擋死。
        /// </remarks>
        private static readonly Dictionary<YesnoSituation, uint[]> ExpectedAddonRows = new()
        {
            [YesnoSituation.Repair] = [860, 861, 862, 863],
            [YesnoSituation.GearsetMainHand] = [4384, 4385, 4388],
            [YesnoSituation.FishingCollect] = [1463, 3815],
            [YesnoSituation.Lottery] = [16915],
            [YesnoSituation.RelicTurnin] = [],
        };

        private static readonly Dictionary<YesnoSituation, string[]> MarkerCache = [];

        /// <summary>已經記過 log 的確認框文字。有上限，免得長時間掛機把它撐大。</summary>
        private static readonly HashSet<string> ReportedPrompts = [];
        private const int ReportedPromptsCap = 64;

        /// <summary>
        /// 這一段靜態文字要多長才拿來當比對基準。
        /// </summary>
        /// <remarks>
        /// 太短的片段拿去做「包含」比對必然誤判（例如只剩「費用：」）。
        /// 寧可讓某個情境完全沒有基準（＝只記錄不擋），也不要拿一個會誤判的基準去擋。
        /// </remarks>
        private const int MinMarkerLength = 6;

        /// <summary>設定畫面用：這個情境到底有沒有可以比對的基準。</summary>
        public static bool HasMarkers(YesnoSituation situation) => GetMarkers(situation).Length > 0;

        /// <summary>
        /// 這一次可不可以按下確定。
        /// </summary>
        /// <returns>
        /// <c>true</c>＝呼叫端照原本的邏輯按下確定。<br/>
        /// <c>false</c>＝這一次不要按確定；<b>取消已經由本方法按掉了</b>（或者這一幀確認框文字讀壞／
        /// 那扇窗剛被按過還沒收掉，什麼都沒按），呼叫端什麼都不用做，下一輪再來。
        /// </returns>
        /// <remarks>
        /// 🔴 <see cref="UnexpectedYesnoAction.AlwaysConfirm"/>（預設）會在讀任何東西之前就回
        /// <c>true</c> —— 沒開這個功能的人連確認框的文字都不會被讀，成本是零。
        /// </remarks>
        public static bool ShouldConfirm(YesnoSituation situation)
        {
            var mode = C.UnexpectedYesno;
            if (mode == UnexpectedYesnoAction.AlwaysConfirm)
                return true;

            // 呼叫端都是在「SelectYesno 開著」的分支裡才進來的；真的拿不到就放行，
            // 這個閘門的職責是「擋掉認不出來的」，不是「多製造一種卡住的方式」。
            if (!GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var master) || !master.IsAddonReady)
                return true;

            var promptText = SafePromptText(master);

            // 讀到 U+FFFD ＝ 視窗記憶體正在變動（多半是關閉中），這一幀不碰：不按確定也不按取消，
            // 呼叫端照舊「這一輪沒按到」下一幀再來。
            if (AddonPressGuard.IsTextCorrupt("SelectYesno", promptText))
                return false;

            var markers = GetMarkers(situation);

            if (markers.Length == 0)
            {
                // 這個情境沒有登記基準（或遊戲資料讀不到）。一律放行，但把文字留下來 ——
                // 那行 log 就是之後要補基準時唯一的依據。
                ReportOnce($"{DescribeSituation(situation)}：這個情境沒有可用的比對基準，照常按下確定。確認框文字：「{promptText}」");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(promptText))
            {
                var normalized = StripWhitespace(promptText);
                foreach (var marker in markers)
                {
                    if (normalized.Contains(marker, StringComparison.Ordinal))
                        return true;
                }
            }

            if (mode == UnexpectedYesnoAction.LogOnly)
            {
                ReportOnce($"{DescribeSituation(situation)}：確認框文字與預期不符，但目前設定是「只記錄」，仍然照常按下確定。文字：「{promptText}」");
                return true;
            }

            ReportOnce($"{DescribeSituation(situation)}：確認框文字與預期不符，已按下取消。文字：「{promptText}」。" +
                       "如果這其實是正常的確認框，請把這行回報出來，我們把它加進比對基準。");

            // 按取消而不是「什麼都不做」：真的是別人的確認框時，按掉它才能讓 ICE 繼續跑；
            // 什麼都不做的話那個框會一直擋在那裡，變成無聲的卡死。
            // 這也與既有的 RejectUnknownYesno 在 GrabMission／AbandonMission 的處理一致。
            // 🔴 這一下取消也要過 YesnoPressGuard：呼叫端的 `ShouldConfirm(...) && MayPress(...)` 鏈裡本方法在
            //    MayPress 之前求值，沒有這一行的話這次按壓既不受擋、也不會被記下，同一扇窗之後被別的接點再按就沒有依據。
            if (EzThrottler.Throttle($"ICE: yesno guard decline {situation}", 500)
                && YesnoPressGuard.MayPress("未預期的確認框：取消", master))
                master.No();

            return false;
        }

        private static void ReportOnce(string message)
        {
            if (ReportedPrompts.Count >= ReportedPromptsCap)
            {
                // 到頂之後就只靠節流，不再無限長大。
                if (!EzThrottler.Throttle("ICE: yesno guard report overflow", 60000))
                    return;
            }
            else if (!ReportedPrompts.Add(message))
            {
                return;
            }

            IceLogging.Info(message, Handle);
        }

        private static string DescribeSituation(YesnoSituation situation) => situation switch
        {
            YesnoSituation.Repair => "修理裝備",
            YesnoSituation.GearsetMainHand => "更換套裝",
            YesnoSituation.FishingCollect => "收藏品收取",
            YesnoSituation.Lottery => "宇宙好運道",
            YesnoSituation.RelicTurnin => "研究材料繳交",
            _ => situation.ToString(),
        };

        /// <summary>
        /// 把一列 <c>Addon</c> 文字變成一個可以拿來比對的基準。
        /// </summary>
        /// <remarks>
        /// 🔴 <b>為什麼不能像「已經學會」那組一樣整列比對</b>：那三列（4937／11501／11506）
        /// 是純文字，這裡這幾列中間夾著參數（費用、道具名、收藏價值），而且 4388 的道具名
        /// 就插在句子<b>中間</b>（「該套裝中主手上的〔斧槍〕無法進行裝備。」）——
        /// 整列比對、甚至「第一句」比對，在執行期都對不上。<br/>
        /// 所以改成：走一遍 payload，取<b>第一段</b>長度夠的純文字當基準，用「包含」比對。
        /// 參數被 payload 型別擋掉，剩下的就是那句話真正固定不變的部分。<br/>
        /// ⚠️ 862／863 後面那句「缺少所需的觸媒…」是條件式出現的，所以<b>只能</b>取第一段，
        /// 不能要求每一段都命中。
        /// </remarks>
        private static string ExtractMarker(ReadOnlySeString text)
        {
            foreach (var payload in text)
            {
                if (payload.Type != ReadOnlySePayloadType.Text)
                    continue;

                var fragment = StripWhitespace(payload.ToString());
                if (fragment.Length >= MinMarkerLength)
                    return fragment;
            }

            return string.Empty;
        }

        private static string[] GetMarkers(YesnoSituation situation)
        {
            if (MarkerCache.TryGetValue(situation, out var cached))
                return cached;

            var markers = new List<string>();
            if (ExpectedAddonRows.TryGetValue(situation, out var rowIds) && rowIds.Length > 0)
            {
                var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Addon>();
                if (sheet != null)
                {
                    foreach (var rowId in rowIds)
                    {
                        // 🔴 GetRow 查無此列會擲 ArgumentOutOfRangeException。
                        if (!sheet.TryGetRow(rowId, out var row))
                            continue;

                        var marker = ExtractMarker(row.Text);
                        if (marker.Length > 0 && !markers.Contains(marker))
                            markers.Add(marker);
                    }
                }
            }

            var result = markers.ToArray();
            MarkerCache[situation] = result;

            IceLogging.Info(
                result.Length == 0
                    ? $"{DescribeSituation(situation)}：沒有可用的比對基準（登記的 Addon 列：" +
                      $"{(rowIds == null || rowIds.Length == 0 ? "（未登記）" : string.Join("／", rowIds))}），" +
                      "這個情境只會記錄、不會擋下任何確認框。"
                    : $"{DescribeSituation(situation)}：比對基準共 {result.Length} 筆：{string.Join(" / ", result)}",
                Handle);

            return result;
        }

        /// <summary>
        /// 安全地讀 <c>SelectYesno</c> 的提示文字。
        /// </summary>
        /// <remarks>
        /// 🔴 ECommons 的 <c>SelectYesno.Text</c> 是 <c>ReadSeString(&amp;Addon-&gt;PromptText-&gt;NodeText)</c>，
        /// <c>PromptText</c> 為 null 時會從 null 加偏移再去讀 —— 那是 AVE，<c>try/catch</c> 攔不到。
        /// 這些呼叫點都在每幀／每 500ms 的路徑上，所以自己補判空。<br/>
        /// 📌 與 <c>Task_BuyCosmoItems.SafePromptText</c> 是同一套做法（那邊刻意不動，
        /// 它自己那條路徑已經在出貨中驗過）。
        /// </remarks>
        private static unsafe string SafePromptText(SelectYesno master)
        {
            var addon = master.Addon;
            if (addon == null)
                return string.Empty;

            var prompt = addon->PromptText;
            if (prompt == null)
                return string.Empty;

            return GenericHelpers.ReadSeString(&prompt->NodeText).GetText();
        }

        private static string StripWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                // char.IsWhiteSpace 一併涵蓋全形空白（U+3000）與不斷行空白（U+00A0）。
                if (char.IsWhiteSpace(c))
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
