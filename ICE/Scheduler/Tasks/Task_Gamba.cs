using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using YamlDotNet.Core.Tokens;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal static class Task_Gamba
    {
        public static readonly List<Config.Gamba> DefaultGambaItems = new()
        {
            // Mounts
            new Config.Gamba { ItemId = 44505, Weight = 200, Type = GambaType.Mount }, // Vacuum Suit Identification Key
            new Config.Gamba { ItemId = 47973, Weight = 200, Type= GambaType.Mount }, // Warp Loader Identification Key

            // Emotes
            new Config.Gamba { ItemId = 44509, Weight = 25, Type = GambaType.Emote }, // Ballroom Etiquette - Personal Perfection
            new Config.Gamba { ItemId = 46795, Weight = 25, Type = GambaType.Emote }, // Ballroom Etiquette - Anticipating Exertion

            // Outfits
            new Config.Gamba { ItemId = 47937, Weight = 50, Type = GambaType.Outfit }, // Cosmosuit Coffer
            new Config.Gamba { ItemId = 47095, Weight = 50, Type = GambaType.Outfit }, // Star Pilot Attire Coffer

            // Minions
            new Config.Gamba { ItemId = 47966, Weight = 25, Type = GambaType.Minion }, // Micro Rover
            new Config.Gamba { ItemId = 46782, Weight = 25, Type = GambaType.Minion }, // Model Suit

            // Accessories
            new Config.Gamba { ItemId = 48154, Weight = 5, Type = GambaType.Accessory }, // The Faces We Wear - Tinted Sunglasses
            new Config.Gamba { ItemId = 48160, Weight = 5, Type = GambaType.Accessory }, // Loparasol
            new Config.Gamba { ItemId = 46840, Weight = 5, Type = GambaType.Accessory }, // The Faces We Wear - Scaevan Headgear

            // Orchestration
            new Config.Gamba { ItemId = 48210, Weight = 0, Type = GambaType.Orchestrion }, // Stargazers Orchestrion Roll
            new Config.Gamba { ItemId = 48220, Weight = 0, Type = GambaType.Orchestrion }, // Echoes in the Distance Orchestrion Roll
            new Config.Gamba { ItemId = 48221, Weight = 0, Type = GambaType.Orchestrion }, // Close in the Distance (Instrumental) Orchestrion Roll
            new Config.Gamba { ItemId = 46155, Weight = 0, Type = GambaType.Orchestrion }, // Kaleidoscope Orchestrion Roll

            // Housing Items
            new Config.Gamba { ItemId = 23892, Weight = 0, Type = GambaType.Housing }, // Verdant Partition
            new Config.Gamba { ItemId = 48733, Weight = 0, Type = GambaType.Housing }, // Cosmotable
            new Config.Gamba { ItemId = 48734, Weight = 0, Type = GambaType.Housing }, // Cosmolamp
            new Config.Gamba { ItemId = 48136, Weight = 0, Type = GambaType.Housing }, // Drafting Table
            new Config.Gamba { ItemId = 32215, Weight = 0, Type = GambaType.Housing }, // Spring Meadow Partition
            new Config.Gamba { ItemId = 46175, Weight = 0, Type = GambaType.Housing }, // Portable Exoterminal
            new Config.Gamba { ItemId = 46174, Weight = 0, Type = GambaType.Housing }, // Cosmokitchen Partition
            new Config.Gamba { ItemId = 46173, Weight = 0, Type = GambaType.Housing }, // Cosmoseat


            // Dyes
            new Config.Gamba { ItemId = 48169, Weight = 0, Type = GambaType.Dye }, // Metallic Pink Dye
            new Config.Gamba { ItemId = 48170, Weight = 0, Type = GambaType.Dye }, // Metallic Ruby Red Dye
            new Config.Gamba { ItemId = 48171, Weight = 0, Type = GambaType.Dye }, // Metallic Cobalt Green Dye
            new Config.Gamba { ItemId = 48172, Weight = 0, Type = GambaType.Dye }, // Metallic Dark Blue Dye

            // Materia
            new Config.Gamba { ItemId = 41762, Weight = 0, Type = GambaType.Materia }, // Gatherer's Guerdon Materia XI
            new Config.Gamba { ItemId = 41763, Weight = 0, Type = GambaType.Materia }, // Gatherer's Guile Materia XI
            new Config.Gamba { ItemId = 41764, Weight = 0, Type = GambaType.Materia }, // Gatherer's Grasp Materia XI
            new Config.Gamba { ItemId = 41765, Weight = 0, Type = GambaType.Materia }, // Craftsman's Competence Materia XI
            new Config.Gamba { ItemId = 41766, Weight = 0, Type = GambaType.Materia }, // Craftsman's Cunning Materia XI
            new Config.Gamba { ItemId = 41767, Weight = 0, Type = GambaType.Materia }, // Craftsman's Command Materia XI
            new Config.Gamba { ItemId = 41775, Weight = 0, Type = GambaType.Materia }, // Gatherer's Guerdon Materia XII
            new Config.Gamba { ItemId = 41776, Weight = 0, Type = GambaType.Materia }, // Gatherer's Guile Materia XII
            new Config.Gamba { ItemId = 41777, Weight = 0, Type = GambaType.Materia }, // Gatherer's Grasp Materia XII
            new Config.Gamba { ItemId = 41778, Weight = 0, Type = GambaType.Materia }, // Craftsman's Competence Materia XII
            new Config.Gamba { ItemId = 41779, Weight = 0, Type = GambaType.Materia }, // Craftsman's Cunning Materia XII
            new Config.Gamba { ItemId = 41780, Weight = 0, Type = GambaType.Materia }, // Craftsman's Command Materia XII

            // Other
            new Config.Gamba { ItemId = 43943, Weight = 0, Type = GambaType.Other }, // Cracked Prismaticrystal
            new Config.Gamba { ItemId = 43944, Weight = 0, Type = GambaType.Other }, // Cracked Novacrystal
            new Config.Gamba { ItemId = 28724, Weight = 0, Type = GambaType.Other }, // Crafter's Delineation
            new Config.Gamba { ItemId = 6141,  Weight = 0, Type = GambaType.Other }, // Cordial HQ
            new Config.Gamba { ItemId = 48158, Weight = 0, Type = GambaType.Other }, // Magicked Prism (Cosmic Exploration)
        };

        public static void EnsureGambaWeightsInitialized(bool force = false)
        {
            bool changed = false;
            if (force)
                C.GambaItemWeights.Clear();
            foreach (var item in DefaultGambaItems)
            {
                if (C.GambaItemWeights.Any(x => x.ItemId == item.ItemId))
                    continue;
                C.GambaItemWeights.Add(new Config.Gamba { ItemId = item.ItemId, Weight = item.Weight, Type = item.Type });
                changed = true;
            }
            if (changed)
                C.Save();
        }

        public static void Enqueue()
        {
            EnsureGambaWeightsInitialized();
            if (GenericHelpers.TryGetAddonMaster<WKSLottery>("WKSLottery", out var gamba) && gamba.IsAddonReady)
            {
                P.TaskManager.EnqueueMulti
                    (
                        new(GamblingTime, "Time to go gambling!", Utils.TaskConfig),
                        new(CloseTalk, "Closing the talk window"),
                        new(() => SchedulerMain.State = IceState.Idle)
                    );
            }
            else
            {
                // If this is the case, then we're here to initalize the gamba
                P.TaskManager.EnqueueMulti
                    (
                        new(PathToGambaNpc, "Pathing to the gamba NPC"),
                        new(TalkToGambaNpc, "Talk to the Gamba NPC"),
                        new(SelectGamba, "Selecting the options to go to gamba"),
                        new(GamblingTime, "Time to go gambling!", Utils.TaskConfig),
                        new(CloseTalk, "Closing the talk window")
                    );
            }
        }

        private static bool? PathToGambaNpc()
        {
            var zoneId = Player.Territory;
            var npcEntry = NpcData.MoonNpcs[zoneId].Where(x => x.type == NpcData.NpcType.Gamba).FirstOrDefault();

            if (Player.DistanceTo(npcEntry.NpcLocation) <= 6.75f)
            {
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                }
                else if (P.Navmesh.IsRunning())
                {
                    if (Player.DistanceTo(npcEntry.NpcLocation) < 5)
                    {
                        IceLogging.Debug("Pathing to NPC has reached the distance thresh, stopping");
                        P.Navmesh.Stop();
                        return true;
                    }
                }
                else
                {
                    IceLogging.Debug($"Distance to the npc is correct, commending gamba");
                    return true;
                }
            }
            else
            {
                if (!P.Navmesh.IsReady())
                {
                    Utils.VnavBuildInfo();
                }
                else if (!P.Navmesh.IsRunning())
                {
                    if (EzThrottler.Throttle("Pathing to repair NPC"))
                    {
                        IceLogging.Debug($"Pathing to: {npcEntry.Name}");

                        Vector3 randomPoint = RandomUtil.GetRandomPointInBounds(npcEntry.Corner1, npcEntry.Corner2, npcEntry.Corner3, npcEntry.Corner4, npcEntry.NpcLocation.Y);
                        IceLogging.DestinationLogs.Log(randomPoint);
                        P.Navmesh.PathfindAndMoveTo(randomPoint, false);
                    }
                }
            }

            return false;
        }
        private static bool? TalkToGambaNpc()
        {
            if (GenericHelpers.TryGetAddonMaster<SelectString>("SelectString", out var selectString) && selectString.IsAddonReady)
            {
                IceLogging.Info("We've gotten to selecting the npc dialog (woo!). Selecting gamba");
                return true;
            }
            else if (GenericHelpers.TryGetAddonMaster<Talk>("Talk", out var talk) && talk.IsAddonReady)
            {
                if (EzThrottler.Throttle("Closing Talk Window", 250))
                    talk.Click();
            }
            else
            {
                var npcEntry = NpcData.MoonNpcs[Player.Territory]
                    .First(x => x.type == NpcData.NpcType.Gamba);
                Utils.TryGetNpcObject(npcEntry, out var researchNpc);
                if (EzThrottler.Throttle("Interacting with gambaNpc!"))
                {
                    Utils.TargetgameObject(researchNpc);
                    Utils.InteractWithObject(researchNpc);
                }
            }

            return false;
        }
        private static bool? SelectGamba()
        {
            if (GenericHelpers.TryGetAddonMaster<SelectIconString>("SelectIconString", out var iconString) && iconString.IsAddonReady)
            {
                if (EzThrottler.Throttle("Selecting Materia Selection"))
                {
                    var select = iconString.Entries[0];
                    IceLogging.Debug($"Selecting: {select.Text}");
                    select.Select();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<SelectString>("SelectString", out var selectString) && selectString.IsAddonReady)
            {
                if (EzThrottler.Throttle("Selecting yes to gamba"))
                {
                    selectString.Entries[0].Select();
                }
            }
            else if (GenericHelpers.TryGetAddonMaster<WKSLottery>("WKSLottery", out var gamba) && gamba.IsAddonReady)
            {
                return true;
            }

            return false;
        }
        private static unsafe bool? GamblingTime()
        {
            string tag = "Gambling Time Task";

            if (GenericHelpers.TryGetAddonMaster<WKSLottery>("WKSLottery", out var gamba) && gamba.IsAddonReady)
            {
                uint[] currencies = [45691, 48146, 48147, 48148];
                var manager = WKSManager.Instance();
                var zoneId = *((byte*)manager + 0x5D);
                var itemId = currencies[zoneId];
                PlayerHelper.GetItemCount(itemId, out var credits);

                bool confirmEnabled, leftWheelEnabled, rightWheelEnabled;
                unsafe
                {
                    confirmEnabled = gamba.SpinWheelButton->IsEnabled;
                    leftWheelEnabled = gamba.WheelLeftButton->IsEnabled;
                    rightWheelEnabled = gamba.WheelRightButton->IsEnabled;
                }

                if (GenericHelpers.TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
                {
                    if (credits >= 1000 + C.GambaCreditsMinimum)
                        select.Yes();
                    else
                        select.No();
                }
                else if (confirmEnabled)
                    gamba.ConfirmButton();
                else if (leftWheelEnabled || rightWheelEnabled)
                {
                    float leftWeight = gamba.LeftWheelItems.Sum(item => C.GambaItemWeights.FirstOrDefault(x => x.ItemId == item.itemId)?.Weight ?? 0);
                    float rightWeight = gamba.RightWheelItems.Sum(item => C.GambaItemWeights.FirstOrDefault(x => x.ItemId == item.itemId)?.Weight ?? 0);

                    if (C.GambaPreferSmallerWheel)
                    {
                        leftWeight = gamba.LeftWheelItems.Length > 0 ? leftWeight / gamba.LeftWheelItems.Length : 0;
                        rightWeight = gamba.RightWheelItems.Length > 0 ? rightWeight / gamba.RightWheelItems.Length : 0;

                        if (leftWeight == rightWeight && leftWeight > 0)
                        {
                            leftWeight += 1.0f / Math.Max(1, gamba.LeftWheelItems.Length);
                            rightWeight += 1.0f / Math.Max(1, gamba.RightWheelItems.Length);
                        }
                    }

                    if (gamba.LeftWheelItems.Length == 0)
                    {
                        IceLogging.Info($"Found a pure stellar mission gamba. Choosing left wheel", tag);
                        SelectWheelLeft(gamba);
                    }
                    else if (gamba.RightWheelItems.Length == 0)
                    {
                        IceLogging.Info($"Found a pure stellar mission gamba. Choosing right wheel", tag);
                        SelectWheelRight(gamba);
                    }
                    else if (leftWeight > rightWeight)
                    {
                        IceLogging.Info($"[Gamba] First wheel is better with total weight: {leftWeight}");
                        SelectWheelLeft(gamba);
                    }
                    else if (rightWeight > leftWeight)
                    {
                        IceLogging.Info($"[Gamba] Second wheel is better with total weight: {rightWeight}");
                        SelectWheelRight(gamba);
                    }
                    else
                    {
                        IceLogging.Info("[Gamba] Both wheels are equal in weight. Randomly selecting one.");
                        if (new Random().Next(2) == 0)
                            SelectWheelLeft(gamba);
                        else
                            SelectWheelRight(gamba);
                    }
                }

                return false;
            }
            else
            {
                return true;
            }

        }
        private static unsafe bool HasEnoughCredits()
        {
            uint[] currencies = [45691, 48146, 48147, 48148];
            var manager = WKSManager.Instance();
            var zoneId = *((byte*)manager + 0x5D);
            var itemId = currencies[zoneId];

            PlayerHelper.GetItemCount(itemId, out var credits);
            return credits >= 1000;
        }
        private static bool? CloseTalk()
        {
            if (GenericHelpers.TryGetAddonMaster<Talk>("Talk", out var talk) && talk.IsAddonReady)
            {
                if (EzThrottler.Throttle("Closing Talk Window", 250))
                    talk.Click();
                return false;
            }
            else
            {
                return true;
            }
        }
        public static unsafe void SelectWheelLeft(WKSLottery gamba)
        {
            gamba.WheelLeftButton->Flags = 327936U; // Checked, Enabled, Selected
            gamba.WheelRightButton->Flags = 65792U; // Not Checked, Enabled, Not Selected
            IceLogging.Debug($"[Gamba] Selecting Left Wheel");
        }
        public static unsafe void SelectWheelRight(WKSLottery gamba)
        {
            gamba.WheelLeftButton->Flags = 65792U; // Not Checked, Enabled, Not Selected
            gamba.WheelRightButton->Flags = 327936U; // Checked, Enabled, Selected
            IceLogging.Debug($"[Gamba] Selecting Right Wheel");
        }
        public static bool BigBangGamba()
        {
            // Big Bang Tickets are earned from doing the fates... and this kind fucks with things? 
            // Name of the item is "Bing Bang Fortune (Planet Name)

            return false;
        }
    }
}
