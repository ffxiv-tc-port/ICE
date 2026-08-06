using Dalamud.Game.ClientState.Objects.Enums;
using System.Text;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 機甲行動顯示／紀錄裡出現的**其他玩家角色名**的遮蔽。
///
/// 🔑 為什麼需要這個：機甲行動是多人內容，附近幾乎一定有其他玩家。
/// 他們的角色名會從兩個地方外流：
///  1. <see cref="MechaAoeOverlay"/> 把目標名字畫在世界疊加層上（截圖就帶出去了）；
///  2. <see cref="Cosmic_Helper.IceLogging"/> 的 <c>LogSystem</c> —— 那是一份 3000 筆的
///     **歷史紀錄**，而且 UI 上有一顆「複製記錄到剪貼簿」的按鈕
///     （<c>Ui/MainUi/HelpFolder/helpSelect_Logs.cs</c>）。使用者回報問題時整份貼出去，
///     裡面就有別人的角色名。
///
/// 所以**預設遮蔽**，要看完整名字得自己去開 <c>C.MechaShowFullPlayerNames</c>。
///
/// 📌 遮蔽範圍刻意只涵蓋 <see cref="ObjectKind.Player"/>：
/// 怪物／場景物件的名字是遊戲內容，不是個人資訊，遮掉只會讓畫面與診斷變得無法解讀。
/// 本機玩家自己那一筆也不遮（<paramref name="isSelf"/>）——遮自己沒有意義，
/// 而且使用者要靠它確認「這一行講的是我」。
/// </summary>
internal static class MechaPrivacy
{
    /// <summary>拿不到名字時的顯示。刻意不是空字串，讓「沒有名字」在畫面上看得見。</summary>
    public const string Unknown = "?";

    /// <summary>
    /// 依設定把名字轉成可以安全顯示／記錄的形式。
    /// </summary>
    /// <param name="name">原始角色名／物件名。</param>
    /// <param name="kind">物件種類。只有 <see cref="ObjectKind.Player"/> 會被遮。</param>
    /// <param name="isSelf">是不是本機玩家自己（自己不遮）。</param>
    public static string Sanitize(string? name, ObjectKind kind, bool isSelf = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Unknown;

        if (kind != ObjectKind.Player || isSelf || C.MechaShowFullPlayerNames)
            return name;

        return Abbreviate(name);
    }

    /// <summary>
    /// 「Firstname Lastname」→「F. L.」。
    ///
    /// 選縮寫而不是整串換成「玩家」，是因為使用者仍然需要能**區分**畫面上的兩個人
    /// （「綠圈那個是誰」），只是不需要把完整名字帶出遊戲。
    ///
    /// ⚠️ 這是不可逆的單向處理，但**不是**保密機制：同一個縮寫在同一個場合仍然
    /// 指得出是誰。它要解決的是「無意間把別人的角色名貼到公開的地方」。
    /// </summary>
    public static string Abbreviate(string name)
    {
        var sb = new StringBuilder(name.Length);
        var wroteAny = false;

        foreach (var part in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (wroteAny)
                sb.Append(' ');

            // ⚠️ 用 StringInfo 的字素叢集而不是 part[0]：角色名可能含代理對
            //    （表情符號／罕用字），直接取 char[0] 會切出半個字。
            var first = FirstTextElement(part);
            sb.Append(first).Append('.');
            wroteAny = true;
        }

        return wroteAny ? sb.ToString() : Unknown;
    }

    private static string FirstTextElement(string s)
    {
        if (s.Length == 0)
            return Unknown;

        var e = System.Globalization.StringInfo.GetTextElementEnumerator(s);
        return e.MoveNext() ? e.GetTextElement() : s[..1];
    }
}
