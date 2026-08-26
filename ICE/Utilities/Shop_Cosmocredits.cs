using ECommons.DalamudServices;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ICE.Utilities;

/// <summary>
/// 宇宙點數商店的商品清單。
///
/// 🔴 原本這裡是 49 筆寫死的字典（照國際服的商品寫的）。2026-07-31 拿台服的遊戲資料比對，
/// 台服實際只賣 39 件：其中 21 件是「ICE 有列、台服根本買不到」（例如各種魔晶石），
/// 另有 11 件是「台服有賣、ICE 完全漏掉」；兩邊都有的價格則完全一致 ——
/// 也就是純粹的內容進度差異。使用者回報的「清單和實際商店對不上」就是這個。
///
/// 改為執行期從遊戲的 SpecialShop 表讀取，判準是「這筆交易的付款道具是宇宙點數」。
/// 這樣任何客戶端（台服／國際服／日後開新商品）都會自動正確，不需要再手動維護清單。
/// </summary>
public class Shop_Cosmocredits
{
    /// <summary>
    /// 宇宙點數（Cosmocredits）的貨幣道具 ID。
    /// 整份清單現在只剩這一個常數，其餘全部由遊戲資料決定。
    /// </summary>
    public const uint CosmocreditItemId = 45690;

    private static Dictionary<uint, ItemInfo>? cachedShop;
    private static HashSet<string>? cachedShopNames;

    /// <summary>目前客戶端實際販售的宇宙點數商品（首次存取時建立，之後快取）。</summary>
    public static Dictionary<uint, ItemInfo> CosmocreditShop => cachedShop ??= BuildFromGameData();

    /// <summary>
    /// 「用宇宙點數付款」的 SpecialShop 的名稱集合（台服只有一筆：「宇宙信用點數交易」）。
    /// </summary>
    /// <remarks>
    /// 用途是 <c>Task_BuyCosmoItems.SelectShop</c> 要在 NPC 的商店選單裡挑對的那一項。
    /// 🔴 兌換 NPC（ENpcBase 1052588／1052600／1052607／1052608）掛了**兩個** SpecialShop：
    /// <c>1770945</c>「宇宙信用點數交易」（60 筆商品）與 <c>1770977</c>（台服整列空白、零筆商品）。
    /// 上游寫死選 <c>Entries[1]</c>＝第二項，在台服會挑到那個空的。改成用名字對，對不到才退回舊行為。
    /// </remarks>
    public static HashSet<string> CosmocreditShopNames
    {
        get
        {
            if (cachedShop == null)
                cachedShop = BuildFromGameData();
            return cachedShopNames ?? [];
        }
    }

    /// <summary>Excel 表在執行期不會變，這個只留給需要重建時用。</summary>
    public static void Invalidate()
    {
        cachedShop = null;
        cachedShopNames = null;
    }

    private static Dictionary<uint, ItemInfo> BuildFromGameData()
    {
        var result = new Dictionary<uint, ItemInfo>();
        var names = new HashSet<string>();
        cachedShopNames = names;

        var sheet = Svc.Data?.GetExcelSheet<SpecialShop>();
        if (sheet == null)
        {
            IceLogging.Error("讀不到 SpecialShop 表，宇宙點數商店清單會是空的", "[Shop]");
            return result;
        }

        foreach (var shop in sheet)
        {
            foreach (var entry in shop.Item)
            {
                // 這一筆是不是用宇宙點數付款？順便取得單價。
                uint cost = 0;
                foreach (var c in entry.ItemCosts)
                {
                    if (c.ItemCost.RowId != CosmocreditItemId)
                        continue;
                    cost = c.CurrencyCost;
                    break;
                }

                if (cost == 0)
                    continue;

                var shopName = shop.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(shopName))
                    names.Add(shopName);

                foreach (var receive in entry.ReceiveItems)
                {
                    var itemRef = receive.Item;
                    if (itemRef.RowId == 0)
                        continue;

                    // 名稱走 Lumina（台服自帶繁中）。取不到就留空，UI 端本來就會自己再查一次。
                    var name = itemRef.ValueNullable?.Name.ExtractText() ?? string.Empty;
                    result[itemRef.RowId] = new ItemInfo { Name = name, Cost = cost };
                }
            }
        }

        IceLogging.Info($"宇宙點數商店：從遊戲資料讀到 {result.Count} 件商品，" +
                        $"商店名稱 {names.Count} 筆：{string.Join(" / ", names)}", "[Shop]");
        return result;
    }

    public class ItemInfo
    {
        public string Name { get; set; } = string.Empty;
        public uint Cost { get; set; }
    }
}
