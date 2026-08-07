using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ICE.Utilities.Cosmic_Helper;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Handlers
{
    internal static class GearsetHandler // Borrowed from Artisan
    {
        internal unsafe static void TaskClassChange(Job job)
        {
            if (job == Player.Job || !EzThrottler.Throttle("Gearset", 250) || Player.IsBusy)
                return;
            var gearsets = RaptureGearsetModule.Instance();
            foreach (ref var gs in gearsets->Entries)
            {
                if (!RaptureGearsetModule.Instance()->IsValidGearset(gs.Id)) continue;
                if ((Job)gs.ClassJob == job)
                {
                    if (gs.Flags.HasFlag(RaptureGearsetModule.GearsetFlag.MainHandMissing))
                    {
                        if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
                        {
                            select.Yes();
                        }
                        else
                        {
                            gearsets->EquipGearset(gs.Id);
                        }
                    }

                    var result = gearsets->EquipGearset(gs.Id);
                    IceLogging.Debug($"Tried to equip gearset {gs.Id} for {job}, result={result}, flags={gs.Flags}");
                    return;
                }
            }
            return;
        }

        /// <summary>
        /// 這個職業有沒有「可以直接換過去」的套裝。
        /// </summary>
        /// <remarks>
        /// 判定沿用 <see cref="TaskClassChange"/> 用的兩個條件：套裝要有效，而且不能是
        /// <c>MainHandMissing</c> —— 主手武器不在身上就換不了職業，<c>EquipGearset</c> 只會跳一個
        /// 確認視窗然後失敗。呼叫端要先問過這裡，才不會「換不過去又不講話」。<br/>
        /// 🔴 只回傳 bool，不外流任何原生指標；<c>Entries</c> 只在這個呼叫的堆疊框內走訪。
        /// <c>RaptureGearsetModule.Instance()</c> 在還沒登入時可能是 null，所以先判空 ——
        /// 「不知道」要回 false，不能讓呼叫端以為換得過去。
        /// </remarks>
        internal unsafe static bool HasUsableGearset(Job job)
        {
            var gearsets = RaptureGearsetModule.Instance();
            if (gearsets == null)
                return false;

            foreach (ref var gs in gearsets->Entries)
            {
                if (!gearsets->IsValidGearset(gs.Id)) continue;
                if ((Job)gs.ClassJob != job) continue;
                if (gs.Flags.HasFlag(RaptureGearsetModule.GearsetFlag.MainHandMissing)) continue;

                return true;
            }

            return false;
        }
    }
}