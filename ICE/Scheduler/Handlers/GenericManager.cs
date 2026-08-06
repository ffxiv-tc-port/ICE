using ECommons.Automation.LegacyTaskManager;
using ECommons.GameHelpers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;

namespace ICE.Scheduler.Handlers
{
    internal static unsafe class GenericManager
    {
        internal static TaskManager taskManager = new();
        static TaskManager TaskManager => taskManager;
        private static bool? ConfirmOrAbort(AddonRequest* addon)
        {
            // AtkComponentButton.IsEnabled 解的是 OwnerNode(0xA8),不是 AtkResNode(0xA0)。
            // 原本的 HandOverButton != null 只擋住按鈕本身，擋不到 OwnerNode 為 null，
            // 那是 AccessViolationException(corrupted-state，try/catch 無效)。
            if (GenericHelpers.IsComponentEnabled(addon->HandOverButton))
            {
                new AddonMaster.Request((IntPtr)addon).HandOver();
                return true;
            }
            return false;
        }

        private static bool? PandoraGatherState = false;
        private static bool? PandoraInteractState = false;
        private static bool? PandoraCordialState = false;

        internal static void Tick()
        {
            if (SchedulerMain.State == IceState.ManualMode)
            {
                // Ideally, we do nothing here. And this should catch it for people who are running manual mode to just return pandora back to normal
            }
            else if (SchedulerMain.State != IceState.Idle)
            {
                var timer = 30000; // 1 second = 1000

                var pandoraGatherEnabled = (P.Pandora.GetFeatureEnabled("Pandora Quick Gather") ?? false);
                if (pandoraGatherEnabled)
                {
                    if (EzThrottler.Throttle("Throttle Pandora Gathering", timer))
                        P.Pandora.PauseFeature("Pandora Quick Gather", timer);
                }

                var autoInteract = (P.Pandora.GetFeatureEnabled("Auto-interact with Gathering Nodes") ?? false);
                if (autoInteract)
                {
                    if (EzThrottler.Throttle("Throttle Pandora Auto-Interact w/ Nodes", timer))
                        P.Pandora.PauseFeature("Auto-interact with Gathering Nodes", timer);
                }

                var pandoraCordial = (P.Pandora.GetFeatureEnabled("Auto-Cordial") ?? false);
                if (C.AutoCordial && pandoraCordial && (SchedulerMain.State == IceState.Gather || SchedulerMain.State == IceState.DualClass))
                {
                    if (EzThrottler.Throttle("Throttling Pandora's Auto Cordial", timer))
                        P.Pandora.PauseFeature("Auto-Cordial", timer);
                }
            }


            if (EzThrottler.Throttle("DelayedTick"))
            {
                if (AddonHelper.IsAddonActive("WKSLottery") && C.GambaEnabled && SchedulerMain.State == IceState.Idle)
                    SchedulerMain.EnablePlugin();
            }
        }
    }
}
