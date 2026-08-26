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
/// 📌 AtkValues 索引 297/298/300/305/311 與 stride 18 全是寫死的（上游值）。
/// 2026-08-07 補上邊界檢查：<c>Addon->AtkValues[i]</c> 是沒有邊界檢查的原始指標索引，
/// <c>NumEntries</c> 讀到垃圾值時迴圈會一路讀到配置外 —— 那是 AVE，<c>try/catch</c> 攔不到。
/// 索引本身**沒有改**，只是超出 <see cref="AtkUnitBase.AtkValuesCount"/> 時當成「讀不到」。
/// </summary>
public unsafe class InclusionShop : AddonMasterBase<AtkUnitBase>
{
    public InclusionShop(nint addon) : base(addon) { }
    public InclusionShop(void* addon) : base(addon) { }

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

    public uint CurrencyAmount => TryGetUInt(297, out var v) ? v : 0;
    public uint NumEntries => TryGetUInt(298, out var v) ? v : 0;

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
            var count = NumEntries;
            for (int i = 0; i < count; i++)
            {
                if (!TryGetUInt(300 + (i * 18), out var itemId))
                    break;

                if (itemId == 0)
                    continue;

                if (!TryGetUInt(305 + (i * 18), out var costItemId) ||
                    !TryGetUInt(311 + (i * 18), out var costAmount))
                    break;

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
