using ECommons.EzSharedDataManager;
using System.Collections.Generic;

namespace ICE.Scheduler.Handlers
{
    internal static class YesAlreadyManager
    {
        private static bool WasChanged = false;
        private static long nextReassertAt;

        /// <summary>ICE 排程在跑（含手動模式）＝整段暫停 YesAlready／TextAdvance；修理、轉盤、商店等站都會按它們也會按的窗。</summary>
        internal static bool ShouldSuppress => SchedulerMain.State != IceState.Idle;

        /// <summary>每秒補放一次的間隔：對方重載會清空清單、YesAlready 看門狗也會移除掛太久的名字，只在邊緣加一次會默默失效。</summary>
        internal const long ReassertIntervalMs = 1000;

        internal static void Tick()
        {
            if (ShouldSuppress)
            {
                var now = Environment.TickCount64;
                if (!WasChanged || now >= nextReassertAt)
                {
                    WasChanged = true;
                    nextReassertAt = now + ReassertIntervalMs;
                    Lock();
                }
            }
            else if (WasChanged)
            {
                WasChanged = false;
                Unlock();
            }
        }
        internal static void Lock()
        {
            if (EzSharedData.TryGet<HashSet<string>>("YesAlready.StopRequests", out var data))
            {
                data.Add(Name);
            }
        }

        internal static void Unlock()
        {
            if (EzSharedData.TryGet<HashSet<string>>("YesAlready.StopRequests", out var data))
            {
                data.Remove(Name);
            }
        }
    }
}
