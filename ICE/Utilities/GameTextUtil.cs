using System.Text;

namespace ICE.Utilities;

/// <summary>
/// 遊戲字串在 ImGui 上顯示（或被正規表示式比對）之前的前處理。
/// </summary>
/// <remarks>
/// 🔴 <b>為什麼需要這個</b>：遊戲的字串常在開頭夾帶私用區（Private Use Area）的圖示字元。
/// 實測 <c>WKSMissionUnit</c> 第 470 列的名字是 <c>"\uE0BE 緊急籌備晚餐食材"</c>
/// （用 <c>exd-tc/7.20/WKSMissionUnit.csv</c> 逐字元核對過），而 ImGui 的字型畫不出
/// U+E0BE，使用者看到的是一個「�」。<br/><br/>
/// ⚠️ 更麻煩的是<b>比對</b>那一半：這些字元會讓錨定的正規表示式（例如
/// <c>^\d{1,2}:\d{2}/\d{1,2}:\d{2}$</c>）整條落空，而失敗形式是「那一列什麼都不顯示」——
/// 不是報錯。所以解析前一律先過這裡。
/// </remarks>
internal static class GameTextUtil
{
    /// <summary>私用區（Private Use Area）下界。</summary>
    private const char PrivateUseFirst = '\uE000';

    /// <summary>私用區上界。</summary>
    private const char PrivateUseLast = '\uF8FF';

    /// <summary>
    /// 剝掉字串裡的私用區字元並修掉因此多出來的頭尾空白。
    /// </summary>
    /// <remarks>
    /// 只處理 BMP 的私用區（U+E000..U+F8FF）—— FFXIV 的 <c>SeIconChar</c> 全部落在
    /// U+E020..U+E0DB，補充私用區（U+F0000 以上）遊戲沒在用。<br/>
    /// 📌 刻意<b>不</b>套用在整個外掛的所有字串上：疊加層底部的
    /// <c>SeIconChar.CrossWorld</c> 是畫得出來的，把它一起剝掉反而是回退。
    /// 這支只給「任務面板／任務表來的文字」用。
    /// </remarks>
    internal static string StripGameIcons(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // 絕大多數字串沒有私用區字元，先掃一遍避免無謂配置。
        var needsWork = false;
        foreach (var c in text)
        {
            if (IsPrivateUse(c))
            {
                needsWork = true;
                break;
            }
        }

        if (!needsWork)
            return text.Trim();

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!IsPrivateUse(c))
                sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 診斷傾印用：把私用區字元轉成 <c>\uE0BE</c> 這種看得懂的寫法，其餘原樣保留。
    /// </summary>
    /// <remarks>
    /// 🔑 傾印的用途是<b>拿去回報</b>，所以不能像 <see cref="StripGameIcons"/> 那樣直接剝掉
    /// ——「本來有哪個圖示字元」正是我們要知道的事。畫成「�」也一樣沒有情報。
    /// </remarks>
    internal static string EscapeGameIcons(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (IsPrivateUse(c))
                sb.Append("\\u").Append(((int)c).ToString("X4"));
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsPrivateUse(char c) => c >= PrivateUseFirst && c <= PrivateUseLast;

    /// <summary>
    /// 秒數轉 <c>M:SS</c>（超過一小時就 <c>H:MM:SS</c>）。負數一律夾到 0——
    /// 顯示「-0:03」只會讓人以為是 bug。
    /// </summary>
    internal static string FormatDuration(int seconds)
    {
        if (seconds < 0)
            seconds = 0;

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }
}
