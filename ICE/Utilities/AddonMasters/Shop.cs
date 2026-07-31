using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using Callback = ECommons.Automation.Callback;

namespace ICE.Utilities.AddonMasters;

/// <summary>
/// ⚠️ 原本在 ECommons 的 <c>UIHelpers/AddonMasterImplementations/Shop.cs</c>，
/// **上游已刪除**（新版只留 CollectablesShop / FreeCompanyCreditShop / ShopCardDialog /
/// ShopExchangeCurrencyDialog / ShopExchangeItemDialog，沒有基本的 Gil 商店視窗）。
///
/// 2026-07-31 全艦隊 ECommons pin 統一到 <c>10364c4</c> 時，ICE 是唯一編譯失敗的 repo，
/// 缺的就是這個與 <see cref="ShopExchangeCurrency"/> 兩個類別。原封不動搬進 ICE。
/// 上游已刪除它，所以沒有「未來會與上游分岔」的問題。
///
/// 📌 已知缺陷（沿用原樣，未修）：AtkValues 索引 2/75/441 全是寫死的，且沒有對
/// <c>AtkValuesCount</c> 做邊界檢查。遊戲改版後會靜默指到錯的位置。
/// </summary>
public unsafe class Shop : AddonMasterBase<AtkUnitBase>
{
    public Shop(nint addon) : base(addon) { }
    public Shop(void* addon) : base(addon) { }

    public uint NumEntries => Addon->AtkValues[2].UInt;

    public class ShopItemInfo(Shop master, int index)
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
    public ShopItemInfo[] ShopItems
    {
        get
        {
            var ret = new List<ShopItemInfo>();
            for (int i = 0; i < NumEntries; i++)
            {
                var itemId = Addon->AtkValues[441 + (i * 1)].UInt;

                if (itemId == 0)
                    continue;

                var costAmount = Addon->AtkValues[75 + (i * 1)].UInt;
                ret.Add(new ShopItemInfo(this, i)
                {
                    ItemId = itemId,
                    CostAmount = costAmount
                });
            }
            return [.. ret];
        }
    }

    public override string AddonDescription { get; } = "Basic Gil Shop Window";
}
