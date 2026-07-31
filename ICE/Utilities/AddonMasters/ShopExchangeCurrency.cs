using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Linq;
using Callback = ECommons.Automation.Callback;

namespace ICE.Utilities.AddonMasters;

/// <summary>
/// ⚠️ 這個類別原本在 ECommons 的 <c>UIHelpers/AddonMasterImplementations/ShopExchangeCurrency.cs</c>，
/// 但**上游在 2026-07 之後把它整個刪除**（新版只留下 <c>ShopExchangeCurrencyDialog</c>，
/// 那是數量輸入對話框、不是商店視窗本體，兩者是不同的 addon）。
///
/// ICE 有 6 處依賴它，而 2026-07-31 把全艦隊的 ECommons pin 統一到 <c>10364c4</c> 時，
/// ICE 是唯一因此編譯失敗的 repo。為了讓 ICE 能跟上統一 pin，把這個類別原封不動搬進 ICE 自己。
///
/// 「複製第三方程式碼未來會分岔」的顧慮在這裡不成立 —— 上游已經刪掉它，沒有上游可以分岔。
///
/// 📌 已知缺陷（沿用原樣，未修）：AtkValues 的索引 84/85/454/1064 全是寫死的，而且
/// 沒有對 <c>AtkValuesCount</c> 做邊界檢查。遊戲改版後這些索引會靜默指到錯的地方。
/// 要修的話得先確認台服當前的 AtkValue 佈局。
/// </summary>
public unsafe class ShopExchangeCurrency : AddonMasterBase<AtkUnitBase>
{
    public ShopExchangeCurrency(nint addon) : base(addon) { }
    public ShopExchangeCurrency(void* addon) : base(addon) { }

    public uint CurrencyAmount => Addon->AtkValues[84].UInt;
    public uint NumEntries => Addon->AtkValues[4].UInt;

    public uint CurrencyId
    {
        get
        {
            var iconId = Addon->AtkValues[85].UInt;
            // 註：這裡是依 Icon 欄位（非主鍵）過濾，線性掃描是必要的，不能換成 GetRow。
            var row = Svc.Data.GetExcelSheet<Item>().Where(x => x.Icon == iconId).FirstOrDefault().RowId;
            return row != 0 ? row : 0;
        }
    }

    // 1064 - Start of ItemIds
    // 454  - Start of Shop Price

    public class ShopItemInfo(ShopExchangeCurrency master, int index)
    {
        public uint ItemId;
        public uint CostAmount;

        public void Select(int amount = 1)
        {
            Callback.Fire(master.Base, true, 0, index, amount);
        }
    }

    public class CostInfo
    {
        public uint itemId;
        public uint cost;
    }

    /// <summary>
    /// This exist as it is because "ShopExchangeCurrency" covers...  alot of different shop types that exist. <br></br>
    /// This just covers the basic "Item -> Gil/Currency Exchange", since ones where you need to exchange items are coded differently
    /// </summary>
    public ShopItemInfo[] BasicShopItems
    {
        get
        {
            var ret = new List<ShopItemInfo>();
            for (int i = 0; i < NumEntries; i++)
            {
                var itemId = Addon->AtkValues[1064 + (i * 1)].UInt;

                if (itemId == 0)
                    continue;

                var costAmount = Addon->AtkValues[454 + (i * 1)].UInt;
                ret.Add(new ShopItemInfo(this, i)
                {
                    ItemId = itemId,
                    CostAmount = costAmount
                });
            }
            return [.. ret];
        }
    }

    public override string AddonDescription { get; } = "Item Exchange Window";
}
