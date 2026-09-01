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
            // RaptureGearsetModule.Instance() 走 UIModule，未登入／UI 尚未建立時回 null
            //（CS 手寫實作逐字是 uiModule == null ? null : uiModule->GetRaptureGearsetModule()）。
            // 取不到就直接 return——與上面三道閘門相同的失敗形式（這次不換裝，下個節流視窗再試）。
            if (gearsets == null)
                return;

            foreach (ref var gs in gearsets->Entries)
            {
                if (!gearsets->IsValidGearset(gs.Id)) continue;
                if ((Job)gs.ClassJob == job)
                {
                    if (gs.Flags.HasFlag(RaptureGearsetModule.GearsetFlag.MainHandMissing))
                    {
                        if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
                        {
                            // 閘門預設是「一律按下確定」＝與原本完全相同（見 YesnoGuard）。
                            // 🔴 YesnoPressGuard 放在最後（它有副作用：記下這一次按壓）。
                            //    這個按窗點是全 ICE 唯一「完全不看視窗內容、只要 SelectYesno 開著
                            //    就按下確定」而且**會被別的按窗點直接接在後面**的地方：
                            //    Task_AbandonMission.Enqueue() 排的順序就是
                            //      AbandonMission（按下放棄確認）→ Task_TurninMission.JobSwapCheck → 本方法，
                            //    而 NeoTaskManager 一個 framework tick 只跑一個任務，
                            //    所以兩次按壓最短可以只差一幀 —— 正好落在「視窗已按下、還在關閉中」
                            //    那幾幀（GetAddonByName 拿得到、IsAddonReady 三關全過），
                            //    此時再送一次 callback 就是原生 AccessViolation，try/catch 攔不到。
                            //    上面那把 "Gearset" 250ms 節流擋不住它：節流記的是「這把 key 上次放行的
                            //    時刻」，跨呼叫點的第一次呼叫一律放行。守衛認的是**視窗位址**，才擋得住。
                            // ⚠️ 擋下來時的行為變化只有「這一個 250ms 視窗不按確定」：下面那行
                            //    EquipGearset 照舊執行、本方法照舊回傳，呼叫端（都是回 false 重試的任務）
                            //    下一輪就會再來一次。控制流完全沒有改變。
                            if (YesnoGuard.ShouldConfirm(YesnoSituation.GearsetMainHand)
                                && YesnoPressGuard.MayPress("更換套裝：主手替換確認", select))
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