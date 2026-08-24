using ECommons.ExcelServices.TerritoryEnumeration;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Ui_IPCTesting
    {
        private static int Radius = 10;
        private static int XLoc = 0;
        private static int YLoc = 0;
        private static string PandoraFeature = "";
        private static int amount = 1000;

        private static string importString = new string('\0', 2048); // Pre-allocate buffer
        private static string SwapToPreset = string.Empty;
        private static uint missionId = 0;
        private static uint baitId = 0;
        private static bool baitSwapped = false;

        private static string SettingChange = "";
        private static bool SettingState = false;

        public static unsafe void Draw()
        {
            ImGui.Text($"Artisan Is Busy? {P.Artisan.IsBusy()}");
            ImGui.Text($"{EzThrottler.GetRemainingTime("[Main Item(s)] Starting Main Craft")}");
            if (ImGui.Button("Artisan, craft this".Loc()))
            {
                P.Artisan.CraftItem(36026, 1);
            }

            ImGui.SetNextItemWidth(125);
            ImGui.InputInt("Radius", ref Radius);
            ImGui.SetNextItemWidth(125);
            ImGui.InputInt("X Location", ref XLoc);
            ImGui.SetNextItemWidth(125);
            ImGui.InputInt("Y Location", ref YLoc);

            if (ImGui.Button($"Test Radius"))
            {
                // AgentMap 取得器合法回 null；這裡要讀 CurrentTerritoryId。
                var agent = AgentMap.Instance();
                if (agent == null)
                    IceLogging.Info("AgentMap 尚未就緒，不執行採集圈測試。", "[Ui_IPCTesting]");
                else
                    Utils.SetGatheringRing(agent->CurrentTerritoryId, XLoc, YLoc, Radius);
            }

            ImGui.Separator();
            ImGui.InputText("Pandora Feature", ref PandoraFeature);
            if (ImGui.Button("Pause Feature".Loc()))
            {
                P.Pandora.PauseFeature(PandoraFeature, amount);
            }

            ImGui.Separator();
            ImGui.Text("AutoHook");
            ImGui.SetNextItemWidth(150);
            ImGui.InputText("Preset String", ref importString, 2048);
            if (ImGui.Button("Import".Loc()))
            {
                P.AutoHook.ImportAndSelectPreset(importString);
                importString = string.Empty;
            }
            ImGui.SetNextItemWidth(150);
            ImGui.InputText("Swap to preset", ref SwapToPreset);
            if (ImGui.Button("Swap".Loc()))
            {
                P.AutoHook.SetPreset(SwapToPreset);
            }
            if (ImGui.Button("Apply Temp".Loc()))
            {
                P.AutoHook.CreateAndSelectAnonymousPreset(importString);
            }
            ImGui.SetNextItemWidth(200);
            ImGui.InputUInt("Select mission to import", ref missionId);
            ImGui.InputUInt("Bait ID", ref baitId);
            if (ImGui.Button("Swap to bait".Loc()))
            {
                 SwapBait(baitId);
            }
            if (ImGui.Button("Swap Bait... simple".Loc()))
            {
                if (CosmicHelper.CurrentBait == 0)
                {
                    IceLogging.Debug("Bait is not currently equipped");
                }

                P.AutoHook.SwapBaitById(baitId);
            }
            if (ImGui.Button("Stupid Test".Loc()))
            {
                if (CosmicHelper.CurrentBait == 0)
                {
                    IceLogging.Debug($"No bait is equipped");
                }
                else if (CosmicHelper.CurrentBait == null)
                {
                    IceLogging.Debug("Bait is null... aka not in the middle of a mission");
                }
                else
                {
                    IceLogging.Debug($"Current bait: {CosmicHelper.CurrentBait}");
                }
            }

            if (ImGui.Button("Enable AutoHook".Loc()))
            {
                P.AutoHook.SetPluginState(true);
            }
            if (ImGui.Button("Disable Autohook".Loc()))
            {
                P.AutoHook.SetPluginState(false);
            }

            ImGui.Separator();
            ImGui.Text($"Is ICE Running? | {P.IceIpc.IsRunning()}");
            if (ImGui.Button("Only Missions Via IPC".Loc()))
            {
                HashSet<uint> missionListIds = new() { 1, 3, 4, 7, 9, 11 };
                P.IceIpc.OnlyMissions(missionListIds);
            }
            if (ImGui.Button("Change to gamba".Loc()))
            {
                SchedulerMain.State = IceState.Gambling;
            }

            ImGui.Separator();

            ImGui.SetNextItemWidth(150);
            ImGui.InputText("Setting Name", ref SettingChange);
            ImGui.Checkbox("Setting Bool".Loc(), ref SettingState);

            if (ImGui.Button("Toggle Setting".Loc()))
            {
                P.IceIpc.ChangeSetting(SettingChange, SettingState);
            }

            // ⚠️ 這裡原本有一顆「Assign Artisan Food Test」按鈕，呼叫 P.Artisan.AssignArtisanRecipe()。
            //    已於 2026-08-03 連同底層的 Artisan.AssignRecipie 訂閱端一起移除：
            //    Artisan 根本沒有註冊那個 IPC，按下去完全沒反應也不會有任何 log。
        }

        private static void SwapBait(uint baitId)
        {
            _ = Task.Run(async () =>
            {
                baitSwapped = await TaskSwapBait(baitId);
            });

            _ = Task.Run(async () =>
            {
                await P.AutoHook.SwapBaitById(baitId);
            });
        }

        private static async Task<bool> TaskSwapBait(uint bait)
        {
            return await P.AutoHook.SwapBaitById(bait);
        }
    }
}
