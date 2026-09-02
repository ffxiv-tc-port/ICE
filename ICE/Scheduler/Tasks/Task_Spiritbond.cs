using ECommons.Automation;
using ECommons.GameHelpers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ICE.Utilities.Cosmic_Helper;
using YamlDotNet.Serialization;

namespace ICE.Scheduler.Tasks
{
    public unsafe static class Task_Spiritbond
    {
        public static ushort Weapon { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[0].SpiritbondOrCollectability; }
        public static ushort Offhand { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[1].SpiritbondOrCollectability; }
        public static ushort Helm { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[2].SpiritbondOrCollectability; }
        public static ushort Body { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[3].SpiritbondOrCollectability; }
        public static ushort Hands { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[4].SpiritbondOrCollectability; }
        public static ushort Legs { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[6].SpiritbondOrCollectability; }
        public static ushort Feet { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[7].SpiritbondOrCollectability; }
        public static ushort Earring { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[8].SpiritbondOrCollectability; }
        public static ushort Neck { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[9].SpiritbondOrCollectability; }
        public static ushort Wrist { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[10].SpiritbondOrCollectability; }
        public static ushort Ring1 { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[11].SpiritbondOrCollectability; }
        public static ushort Ring2 { get => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[12].SpiritbondOrCollectability; }
        public static bool IsSpiritbondReadyAny()
        {
            // 同步上游：精選（Spiritbond／萃取）功能要先做完解鎖任務(638)才會開放。
            // 沒解鎖就硬要萃取會卡在 step-moon，所以未解鎖直接回 false，不進入萃取流程。
            if (!SpiritbondUnlocked())
                return false;

            if (Weapon == 10000) return true;
            if (Offhand == 10000) return true;
            if (Helm == 10000) return true;
            if (Body == 10000) return true;
            if (Hands == 10000) return true;
            if (Legs == 10000) return true;
            if (Feet == 10000) return true;
            if (Earring == 10000) return true;
            if (Neck == 10000) return true;
            if (Wrist == 10000) return true;
            if (Ring1 == 10000) return true;
            if (Ring2 == 10000) return true;

            return false;
        }

        // 同步上游：任務 638 為精選材料（萃取）解鎖任務；完成後才允許萃取。
        public static bool SpiritbondUnlocked()
        {
            return QuestManager.IsQuestComplete(638);
        }

        public static void Enqueue()
        {
            P.TaskManager.Enqueue(() => ExtractMateria(), "Extracting materia");
        }

        /// <summary>
        /// 逐層取出「精製度」文字節點。
        /// GetNodeById / GetAsAtkComponent* / GetComponent / GetTextNodeById 全都是
        /// [MemberFunction] 原生呼叫，對 null 接收者會直接 AccessViolationException；
        /// AVE 是 corrupted-state exception，try/catch 與任何例外隔離都攔不到，
        /// 所以每一層都必須在呼叫「之前」驗證 —— 原本那種先串完整條鏈、
        /// 事後才檢查 null 的寫法已經來不及了。
        /// 保留原本「分類節點也必須存在」的前提：任一層取不到就回 false，本次不動作。
        /// </summary>
        private static unsafe bool TryGetSpiritbondTextNode(AtkUnitBase* addonMaterialize, out AtkTextNode* spiritbondTextNode)
        {
            spiritbondTextNode = null;

            if (addonMaterialize == null)
                return false;

            var listNode = addonMaterialize->GetNodeById(12);
            if (listNode == null)
                return false;

            var list = listNode->GetAsAtkComponentList();
            if (list == null)
                return false;

            if (list->UldManager.NodeList == null || list->UldManager.NodeListCount <= 2)
                return false;

            var spiritbondNode = list->UldManager.NodeList[2];
            if (spiritbondNode == null)
                return false;

            var spiritbondComponent = spiritbondNode->GetComponent();
            if (spiritbondComponent == null)
                return false;

            var spiritbondText = spiritbondComponent->GetTextNodeById(5);
            if (spiritbondText == null)
                return false;

            var spiritbondTyped = spiritbondText->GetAsAtkTextNode();
            if (spiritbondTyped == null)
                return false;

            // 分類節點：原本只拿來當「介面已就緒」的前提檢查，維持同樣語意。
            var dropdownNode = addonMaterialize->GetNodeById(4);
            if (dropdownNode == null)
                return false;

            var dropdown = dropdownNode->GetAsAtkComponentDropdownList();
            if (dropdown == null)
                return false;

            if (dropdown->UldManager.NodeList == null || dropdown->UldManager.NodeListCount <= 1)
                return false;

            var categoryNode = dropdown->UldManager.NodeList[1];
            if (categoryNode == null)
                return false;

            var categoryCheckBox = categoryNode->GetAsAtkComponentCheckBox();
            if (categoryCheckBox == null)
                return false;

            var categoryText = categoryCheckBox->GetTextNodeById(3);
            if (categoryText == null || categoryText->GetAsAtkTextNode() == null)
                return false;

            spiritbondTextNode = spiritbondTyped;
            return true;
        }

        public static unsafe bool? ExtractMateria()
        {
            if (InventoryManager.Instance()->GetEmptySlotsInBag() < 1 || !IsSpiritbondReadyAny())
            {
                if (GenericHelpers.TryGetAddonByName("Materialize", out AtkUnitBase* materialize))
                {
                    // -1 是關窗：關閉中的那幾幀仍拿得到實例、本步每幀重跑，500ms 節流不是防護。擋下＝這一幀不送。
                    if (EzThrottler.Throttle("Closing the materialize window")
                        && AddonPressGuard.TryBeginPress("精製：關閉精製視窗", "Materialize", materialize, AddonPressGuard.BuildPressKey(true, -1)))
                        ECommons.Automation.Callback.Fire(materialize, true, -1);
                }
                else
                {
                    IceLogging.Info("Materia Extraction is completed, continuing back to the start state");
                    SchedulerMain.State = IceState.Start;
                    return true;
                }
            }
            else
            {
                if (Player.Mounted)
                {
                    if (EzThrottler.Throttle("Dismounting"))
                        Utils.Dismount();
                }
                else if (EzThrottler.Throttle("Attempting to extract materia") && !Player.IsBusy)
                {
                    if (GenericHelpers.TryGetAddonByName("MaterializeDialog", out AtkUnitBase* addonMaterializeDialog) && GenericHelpers.IsAddonReady(addonMaterializeDialog))
                    {
                        // MaterializeDialog 按下確定即關（單答終結窗，守衛內併 key）：擋下時這一幀不按，照舊 return false 下一幀再來。
                        if (AddonPressGuard.TryBeginPress("精製：精製確認", "MaterializeDialog", addonMaterializeDialog))
                            new AddonMaster.MaterializeDialog(addonMaterializeDialog).Materialize();
                        return false;
                    }
                    if (!GenericHelpers.TryGetAddonByName("Materialize", out AtkUnitBase* addonMaterialize))
                    {
                        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 14);
                        return false;
                    }
                    else if (GenericHelpers.IsAddonReady(addonMaterialize))
                    {
                        if (!TryGetSpiritbondTextNode(addonMaterialize, out var spiritbondTextNode))
                            return false;

                        var spiritbondText = spiritbondTextNode->NodeText.ToString();
                        // 讀到 U+FFFD ＝ 視窗記憶體正在變動（多半是關閉中），這一幀不碰。
                        if (AddonPressGuard.IsTextCorrupt("Materialize", spiritbondText))
                            return false;

                        // 選第一件裝備後精製視窗不關（開出 MaterializeDialog），屬多次互動窗：逃生口 15 幀。
                        if (spiritbondText.Replace(" ", string.Empty) == "100%"
                            && AddonPressGuard.TryBeginPress("精製：選擇第一件裝備", "Materialize", addonMaterialize, AddonPressGuard.BuildPressKey(true, 2, 0), AddonPressGuard.RoutineRePressEscapeFrames))
                            ECommons.Automation.Callback.Fire(addonMaterialize, true, 2, 0);
                    }
                }
            }

            return false;
        }

        public unsafe static bool TryExtractMateria()
        {
            if (!EzThrottler.Throttle("Extract", 250))
                return false;

            if (InventoryManager.Instance()->GetEmptySlotsInBag() < 1 || !IsSpiritbondReadyAny() || !C.SelfSpiritbondGather || !Player.Job.IsDol())
            {
                if (GenericHelpers.TryGetAddonByName("Materialize", out AtkUnitBase* materialize))
                {
                    if (EzThrottler.Throttle("Closing the materialize window"))
                        ECommons.Automation.Callback.Fire(materialize, true, -1);
                }
                else
                {
                    SchedulerMain.State = IceState.Start;
                    return true;
                }
            }

            if (Player.IsBusy)
                return false;

            if (GenericHelpers.TryGetAddonByName("MaterializeDialog", out AtkUnitBase* addonMaterializeDialog) && GenericHelpers.IsAddonReady(addonMaterializeDialog))
            {
                new AddonMaster.MaterializeDialog(addonMaterializeDialog).Materialize();
                return false;
            }
            if (!GenericHelpers.TryGetAddonByName("Materialize", out AtkUnitBase* addonMaterialize))
            {
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 14);
                return false;
            }
            else if (GenericHelpers.IsAddonReady(addonMaterialize))
            {
                if (!TryGetSpiritbondTextNode(addonMaterialize, out var spiritbondTextNode))
                    return false;

                if (spiritbondTextNode->NodeText.ToString().Replace(" ", string.Empty) == "100%")
                    ECommons.Automation.Callback.Fire(addonMaterialize, true, 2, 0);
            }
            else
            {
                addonMaterialize->Close(true);
                SchedulerMain.State = IceState.Start;
                return true;
            }
            return false;
        }
    }
}
