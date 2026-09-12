using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;
using Pictomancy;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.DebugWindowTabs
{
    internal class Ui_NpcViewer
    {
        public static void Draw()
        {
            var territoryid = Player.Territory;

            // 🔴 原本是 NpcData.MoonNpcs[territoryid] —— 字典索引子查不到鍵時是
            //    <b>擲 KeyNotFoundException</b>，不是回 null。MoonNpcs 只有 1237／1291
            //    兩個鍵，所以在月面以外打開這個分頁，每一幀都會擲例外；Draw() 擲例外會被
            //    Dalamud 的視窗例外隔離接住，把這扇除錯視窗換成錯誤面板（要手動點重試）。
            //    🔑 下面那兩行的 `moonNpcs != null` 從來沒有生效過 —— 索引子不會回 null，
            //    例外在前一行就已經發生。原作者的意圖顯然是「查不到就顯示無效」，
            //    這裡改用 TryGetValue 把那個意圖真的實作出來（行為不變，只是不再炸）。
            //    同 repo 的 NpcInfo.cs 對 TryGetMoonNpc 的說明逐字寫著「不要直接索引 MoonNpcs」，
            //    並記錄了 2026-08-03 因為 Player.Territory 中途改變而觸發的事故。
            var hasMoonNpcs = NpcData.MoonNpcs.TryGetValue(territoryid, out var moonNpcs);
            ImGui.Text($"Territory Id: {territoryid}");
            ImGui.Text($"Valid Moon NPC Info: {hasMoonNpcs}");
            if (hasMoonNpcs && moonNpcs != null)
            {
                foreach (var npcEntry in moonNpcs)
                {
                    if (ImGui.CollapsingHeader($"NPC: {npcEntry.Name}"))
                    {
                        Vector3 randomPoint = RandomUtil.GetRandomPointInBounds(npcEntry.Corner1, npcEntry.Corner2, npcEntry.Corner3, npcEntry.Corner4, npcEntry.NpcLocation.Y);
                        if (ImGui.Button($"Path to random point##{npcEntry.Name}"))
                        {
                            IceLogging.DestinationLogs.Log(randomPoint);
                            P.Navmesh.PathfindAndMoveTo(randomPoint, false);
                        }

                        using (var drawList = PictoService.Draw())
                        {
                            drawList.AddQuadFilled(npcEntry.Corner1, npcEntry.Corner2, npcEntry.Corner3, npcEntry.Corner4, C.PictoColor_Circle);
                        }
                    }
                }
            }
        }
    }
}
