using ECommons.EzSharedDataManager;
using System.Collections.Generic;

namespace ICE.Scheduler.Handlers
{
    internal static class TextAdvancedManager
    {
        private static bool WasChanged = false;
        private static long nextReassertAt;

        /// <summary>條件與補放節奏同 <see cref="YesAlreadyManager.Tick"/>（TextAdvance 重載時會清空清單）。</summary>
        internal static void Tick()
        {
            if (YesAlreadyManager.ShouldSuppress)
            {
                var now = Environment.TickCount64;
                if (!WasChanged || now >= nextReassertAt)
                {
                    WasChanged = true;
                    nextReassertAt = now + YesAlreadyManager.ReassertIntervalMs;
                    LockTA();
                }
            }
            else if (WasChanged)
            {
                WasChanged = false;
                UnlockTA();
            }
        }
        internal static void LockTA()
        {
            if (EzSharedData.TryGet<HashSet<string>>("TextAdvance.StopRequests", out var data))
            {
                data.Add(Name);
            }
        }

        internal static void UnlockTA()
        {
            if (EzSharedData.TryGet<HashSet<string>>("TextAdvance.StopRequests", out var data))
            {
                data.Remove(Name);
            }
        }
    }
}

