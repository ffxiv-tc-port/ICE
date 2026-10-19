using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ICE.Scheduler.Handlers
{
    internal class GenericHandlers
    {
        internal static bool? Throttle(string name, int ms)
        {
            return EzThrottler.Throttle(name, ms);
        }

        internal static bool? WaitFor(string name)
        {
            return EzThrottler.Check(name);
        }

        internal static unsafe bool? FireCallback(string AddonName, bool visibilty, params int[] callback_fires)
        {
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(AddonName, out var addon) && GenericHelpers.IsAddonReady(addon))
            {
                // 🔴 全 repo 經此 helper 的按法都是關窗（-1）：關閉中的那幾幀 IsAddonReady 仍過，呼叫端
                //    （Task_BuyCosmoItems.CloseShop／PlayerHandlers 的 WKSReward）每幀重跑、只有 500ms 節流，
                //    再送一次就是攔不到的 AccessViolation。守衛下沉在這裡一處罩住兩站（粒度＝窗＋位址＋參數組）。
                //    被擋回 false 與「視窗還沒 ready」走同一條既有路徑；兩個呼叫端都是敘述式呼叫、不看回傳值，
                //    不是 Enqueue lambda，所以回傳值語意不動也不會改綁任務。
                if (!AddonPressGuard.TryBeginPress($"FireCallback：{AddonName}", AddonName, addon, AddonPressGuard.BuildPressKey(visibilty, callback_fires)))
                    return false;

                ECommons.Automation.Callback.Fire(addon, visibilty, callback_fires.Cast<object>().ToArray());
                return true;
            }
            return false;
        }
    }
}

