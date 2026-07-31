using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using Callback = ECommons.Automation.Callback;

namespace ICE.Utilities.AddonMasters;

/// <summary>
/// ⚠️ 原本在 ECommons 的 <c>UIHelpers/AddonMasterImplementations/InclusionShop.cs</c>，**上游已刪除**。
///
/// 2026-07-31 全艦隊 ECommons pin 統一到 <c>10364c4</c> 時，ICE 是唯一編譯失敗的 repo。
/// 缺的共三個類別：<see cref="Shop"/>、<see cref="ShopExchangeCurrency"/>、以及本類別。
/// 全部原封不動搬進 ICE；上游已刪除它們，沒有「未來與上游分岔」的問題。
///
/// 📌 已知缺陷（沿用原樣，未修）：AtkValues 索引 297/298/300/305/311 與 stride 18 全是寫死的，
/// 且沒有對 <c>AtkValuesCount</c> 做邊界檢查。遊戲改版後會靜默指到錯的位置。
/// </summary>
public unsafe class InclusionShop : AddonMasterBase<AtkUnitBase>
{
    public InclusionShop(nint addon) : base(addon) { }
    public InclusionShop(void* addon) : base(addon) { }

    public uint CurrencyAmount => Addon->AtkValues[297].UInt;
    public uint NumEntries => Addon->AtkValues[298].UInt;

    public class ShopItemInfo(InclusionShop master, int index)
    {
        public uint ItemId;
        public uint CurrencyId;
        public uint Cost;

        public void Select(int amount = 1)
        {
            Callback.Fire(master.Base, true, 14, index, amount);
        }
    }

    public ShopItemInfo[] ShopItems
    {
        get
        {
            var ret = new List<ShopItemInfo>();
            for (int i = 0; i < NumEntries; i++)
            {
                var itemId = Addon->AtkValues[300 + (i * 18)].UInt;

                if (itemId == 0)
                    continue;

                var costItemId = Addon->AtkValues[305 + (i * 18)].UInt;
                var costAmount = Addon->AtkValues[311 + (i * 18)].UInt;

                ret.Add(new ShopItemInfo(this, i)
                {
                    ItemId = itemId,
                    CurrencyId = costItemId,
                    Cost = costAmount,
                });
            }
            return [.. ret];
        }
    }

    public override string AddonDescription { get; } = "Crafter/Gathering Script Shop Window";
}
