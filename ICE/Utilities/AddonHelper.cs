using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;

namespace ICE.Utilities;

public static class AddonHelper
{
    public static unsafe void OpenRecipeNote()
    {
        // 冪等守衛：台服上對已開啟的 RecipeNote 重複呼叫 OpenRecipeByRecipeId 會關閉重建視窗
        // (國際服則是無害的重新選取)。已開啟就不要再呼叫。
        if (IsAddonActive("RecipeNote"))
            return;

        int[] basicCrafts = [1008, 1, 170, 663, 302, 464, 1101, 901];
        uint recipeId = (uint)basicCrafts[Player.JobId-8];

        AgentRecipeNote.Instance()->OpenRecipeByRecipeId(ExcelHelper.RecipeSheet.GetRow(recipeId).RowId);
    }
    public static unsafe bool IsAddonActive(string AddonName) // Used to see if the addon is active/ready to be fired on
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(AddonName);
        return addon != null && addon->IsVisible && addon->IsReady;
    }

    public static unsafe string GetNodeText(string addonName, params int[] nodeNumbers)
    {
        try
        {
            var ptr = Svc.GameGui.GetAddonByName(addonName, 1);
            if (ptr.Address == IntPtr.Zero)
                return String.Empty;

            var addon = (AtkUnitBase*)ptr.Address;
            if (addon->UldManager.NodeList == null || addon->UldManager.NodeListCount == 0)
                return String.Empty;

            var uld = addon->UldManager;
            AtkResNode* currentNode = null;

            for (var i = 0; i < nodeNumbers.Length; i++)
            {
                int nodeIndex = nodeNumbers[i];

                if (nodeIndex < 0 || nodeIndex >= uld.NodeListCount)
                    return $"IndexOutOfRange: {nodeIndex}";

                currentNode = uld.NodeList[nodeIndex];

                // More nodes to traverse
                if (i < nodeNumbers.Length - 1)
                {
                    if (currentNode->Type != NodeType.Component || ((AtkComponentNode*)currentNode)->Component == null)
                        return $"InvalidComponentNode: {currentNode->Type}";

                    var component = ((AtkComponentNode*)currentNode)->Component;
                    if (component->UldManager.NodeList == null ||
                        component->UldManager.NodeListCount == 0)
                        return "EmptyChildUld";

                    uld = component->UldManager;
                }
            }

            if (currentNode == null)
                return String.Empty;

            return currentNode->Type switch
            {
                NodeType.Counter => GetCounterNodeText((AtkCounterNode*)currentNode),
                NodeType.Text => GetTextNodeText((AtkTextNode*)currentNode),
                _ => $"UnsupportedNodeType: {currentNode->Type}"
            };
        }
        catch (Exception ex) when (ex is AccessViolationException or NullReferenceException)
        {
            PluginLog.Error($"Memory access violation in GetNodeText: {ex.Message}");
            return string.Empty;
        }
    }

    private static unsafe string GetCounterNodeText(AtkCounterNode* node)
    {
        return node != null ? node->NodeText.ToString() : string.Empty;
    }

    private static unsafe string GetTextNodeText(AtkTextNode* node)
    {
        return node != null ? node->NodeText.GetText() : string.Empty;
    }

    /// <summary>
    /// 逐層走訪節點清單取出文字節點；任何一層取不到就回 <c>null</c>。
    /// 🔴 呼叫端必須自己判空後才可解參考。
    /// 原本的寫法對 NodeList 索引完全沒有邊界檢查，也沒有檢查
    /// <c>((AtkComponentNode*)node)-&gt;Component</c> 是否為 null，兩者都是
    /// AccessViolationException 入口；AVE 是 corrupted-state exception，try/catch 攔不到。
    /// 守衛內容與本檔已加固的 <see cref="GetNodeText"/> 一致。
    /// </summary>
    public static unsafe AtkTextNode* GetAtkTextNode(string addonName, params int[] nodeNumbers)
    {
        var ptr = Svc.GameGui.GetAddonByName(addonName, 1);
        if (ptr.Address == IntPtr.Zero)
            return null;

        var addon = (AtkUnitBase*)ptr.Address;
        if (addon->UldManager.NodeList == null || addon->UldManager.NodeListCount == 0)
            return null;

        var uld = addon->UldManager;
        AtkResNode* node = null;

        for (var i = 0; i < nodeNumbers.Length; i++)
        {
            var nodeNumber = nodeNumbers[i];

            if (nodeNumber < 0 || nodeNumber >= uld.NodeListCount)
                return null;

            node = uld.NodeList[nodeNumber];
            if (node == null)
                return null;

            // More nodes to traverse
            if (i < nodeNumbers.Length - 1)
            {
                if (node->Type != NodeType.Component)
                    return null;

                var component = ((AtkComponentNode*)node)->Component;
                if (component == null ||
                    component->UldManager.NodeList == null ||
                    component->UldManager.NodeListCount == 0)
                    return null;

                uld = component->UldManager;
            }
        }

        return (AtkTextNode*)node;
    }

    private static unsafe AtkResNode* GetNodeByIDChain(AtkResNode* node, params int[] ids)
    {
        if (node == null || ids.Length <= 0)
            return null;

        if (node->NodeId == ids[0])
        {
            if (ids.Length == 1)
                return node;

            var newList = new List<int>(ids);
            newList.RemoveAt(0);

            var childNode = node->ChildNode;
            if (childNode != null)
                return GetNodeByIDChain(childNode, [.. newList]);

            if ((int)node->Type >= 1000)
            {
                // Component 是指標欄位，元件尚未建立完成時為 null；
                // NodeList 也可能是空的。兩者不擋都會 AccessViolationException。
                var componentNode = node->GetAsAtkComponentNode();
                if (componentNode == null)
                    return null;

                var component = componentNode->Component;
                if (component == null)
                    return null;

                var uldManager = component->UldManager;
                if (uldManager.NodeList == null || uldManager.NodeListCount == 0)
                    return null;

                childNode = uldManager.NodeList[0];
                return childNode == null ? null : GetNodeByIDChain(childNode, [.. newList]);
            }

            return null;
        }

        //check siblings
        var sibNode = node->PrevSiblingNode;
        return sibNode != null ? GetNodeByIDChain(sibNode, ids) : null;
    }
}