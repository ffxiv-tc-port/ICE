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
/// 📌 AtkValue 的索引 4／84／85／454／1064 全是寫死的（來源是上游 ECommons，照國際服寫的）。
/// 2026-08-07 補上邊界檢查：<c>Addon->AtkValues[i]</c> 是**沒有任何邊界檢查的原始指標索引**，
/// 遊戲改版或台服佈局不同時，<c>NumEntries</c> 會讀到垃圾值，接著
/// <c>for (i &lt; NumEntries) AtkValues[1064 + i]</c> 就會一路讀到配置外 —— 那是 AVE，
/// <c>try/catch</c> 攔不到。現在超出 <see cref="AtkUnitBase.AtkValuesCount"/> 一律當「讀不到」，
/// 並把 <see cref="AtkValueCount"/> 開出來讓診斷 log 能定錨真正的佈局。
/// </summary>
public unsafe class ShopExchangeCurrency : AddonMasterBase<AtkUnitBase>
{
    public ShopExchangeCurrency(nint addon) : base(addon) { }
    public ShopExchangeCurrency(void* addon) : base(addon) { }

    // 寫死的 AtkValue 佈局（上游值）。改動前請先用偵錯視窗的「宇宙商店」分頁對照實機。
    private const int IdxNumEntries = 4;
    private const int IdxCurrencyAmount = 84;
    private const int IdxCurrencyIcon = 85;
    private const int IdxCostStart = 454;
    private const int IdxItemIdStart = 1064;

    /// <summary>這個 addon 目前實際有幾個 AtkValue。診斷用：寫死的索引有沒有超界一看就知道。</summary>
    public int AtkValueCount => Addon == null ? 0 : Addon->AtkValuesCount;

    /// <summary>有邊界檢查的 AtkValue 讀取。超界／指標為空一律回 false，呼叫端自己決定退化行為。</summary>
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

    public uint CurrencyAmount => TryGetUInt(IdxCurrencyAmount, out var v) ? v : 0;
    public uint NumEntries => TryGetUInt(IdxNumEntries, out var v) ? v : 0;

    public uint CurrencyId
    {
        get
        {
            if (!TryGetUInt(IdxCurrencyIcon, out var iconId) || iconId == 0)
                return 0;
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
            var count = NumEntries;
            for (int i = 0; i < count; i++)
            {
                // 讀不到（＝索引已經超出這個 addon 的 AtkValue 數量）就停手，不要繼續往外讀。
                if (!TryGetUInt(IdxItemIdStart + i, out var itemId))
                    break;

                if (itemId == 0)
                    continue;

                if (!TryGetUInt(IdxCostStart + i, out var costAmount))
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

    public override string AddonDescription { get; } = "Item Exchange Window";
}
