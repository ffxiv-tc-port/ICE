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
/// 📌 AtkValues 索引 2/75/441 全是寫死的（上游值，照國際服寫的）。
/// 2026-08-07 補上邊界檢查：<c>Addon->AtkValues[i]</c> 是沒有邊界檢查的原始指標索引，
/// <c>NumEntries</c> 讀到垃圾值時迴圈會一路讀到配置外 —— 那是 AVE，<c>try/catch</c> 攔不到。
/// 索引本身**沒有改**，只是超出 <see cref="AtkUnitBase.AtkValuesCount"/> 時當成「讀不到」。
/// </summary>
public unsafe class Shop : AddonMasterBase<AtkUnitBase>
{
    public Shop(nint addon) : base(addon) { }
    public Shop(void* addon) : base(addon) { }

    /// <summary>這個 addon 目前實際有幾個 AtkValue。診斷用。</summary>
    public int AtkValueCount => Addon == null ? 0 : Addon->AtkValuesCount;

    private bool TryGetUInt(int index, out uint value)
    {
        value = 0;
        if (Addon == null || Addon->AtkValues == null)
            return false;
        if (index < 0 || index >= Addon->AtkValuesCount)
            return false;
        value = Addon->AtkValues[index].UInt;
        return true;
    }

    public uint NumEntries => TryGetUInt(2, out var v) ? v : 0;

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
            var count = NumEntries;
            for (int i = 0; i < count; i++)
            {
                if (!TryGetUInt(441 + i, out var itemId))
                    break;

                if (itemId == 0)
                    continue;

                if (!TryGetUInt(75 + i, out var costAmount))
                    break;

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
