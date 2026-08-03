using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Numerics; // Add this for Vector2 and Vector3
using System.Diagnostics.CodeAnalysis;

namespace ICE.Utilities;

internal static class NpcData // Renamed the class to avoid conflict
{
    public enum NpcType
    {
        Repair,
        Credit,
        Relic,
        Gamba
    }

    public class NPCInfo // Keep this class for the dictionary
    {
        public NpcType type { get; set; }
        public uint NpcId { get; set; }
        public uint[] AlternateNpcIds { get; set; } = [];
        public string Name { get; set; }
        public Vector2 BoxCorner1 { get; set; }
        public Vector2 BoxCorner2 { get; set; }
        public Vector3 NpcLocation { get; set; }
        public Vector3 Corner1 { get; set; }
        public Vector3 Corner2 { get; set; }
        public Vector3 Corner3 { get; set; }
        public Vector3 Corner4 { get; set; }
    }

    /// <summary>
    /// 取得某個月面區域裡指定用途的 NPC。查不到就回 false —— <b>不要直接索引 MoonNpcs</b>。
    /// </summary>
    /// <remarks>
    /// 🔴 <c>MoonNpcs</c> 只有兩個 key（1237 Sinus Ardorum／1291 Phaenna），而所有呼叫端
    /// 傳進來的都是 <c>Player.Territory</c> —— 也就是<b>會在任務排隊到一半突然改變</b>的值。
    /// 2026-08-03 的事故就是這樣觸發的：機甲行動抽中駕駛員把玩家直接傳送走。
    /// 佇列裡已經排好的 <c>PathToRepair</c>／<c>RepairAtNpc</c>／<c>PathToRelicNPC</c> 等等，
    /// 在下一個 tick 讀到的 <c>Player.Territory</c> 就不是月面了，直接索引＝KeyNotFoundException。<br/>
    /// 另外原本幾處用 <c>.First(...)</c>（找不到就丟例外）、幾處用
    /// <c>.FirstOrDefault()</c>（找不到回 <c>null</c>，下一行解參考就是 NullReferenceException），
    /// 兩種寫法都沒有處理「找不到」，這裡一併收斂成同一個閘門。
    /// </remarks>
    public static bool TryGetMoonNpc(uint zoneId, NpcType type, [MaybeNullWhen(false)] out NPCInfo npc)
    {
        npc = null;
        if (!MoonNpcs.TryGetValue(zoneId, out var list))
            return false;

        npc = list.FirstOrDefault(x => x.type == type);
        return npc != null;
    }

    public static Dictionary<uint, List<NPCInfo>> MoonNpcs = new() // Use NPCInfo instead of NpcInfo
    {
        [1237] = new List<NPCInfo>
        {
            new NPCInfo // Repair | Gil Gear Vendor
            {
                type = NpcType.Repair,
                NpcId = 1052610,
                AlternateNpcIds = [1052589, 1052601, 1052609],
                Name = "Godgyth",
                NpcLocation = new Vector3(19.46f, 1.69f, 18.11f),
                Corner1 = new Vector3(18.50f, 1.69f, 15.25f),
                Corner2 = new Vector3(15.05f, 1.69f, 18.70f),
                Corner3 = new Vector3(14.52f, 1.69f, 17.49f),
                Corner4 = new Vector3(17.97f, 1.69f, 14.20f),
            },
            new NPCInfo // Credit Exchange Vendor
            {
                type = NpcType.Credit,
                NpcId = 1052608,
                AlternateNpcIds = [1052588, 1052600, 1052607],
                Name = "Mesouaidonque",
                NpcLocation = new Vector3(18.23f, 1.69f, 19.42f),
                Corner1 = new Vector3(17.80f, 1.69f, 15.94f),
                Corner2 = new Vector3(15.13f, 1.69f, 18.62f),
                Corner3 = new Vector3(14.78f, 1.69f, 17.21f),
                Corner4 = new Vector3(17.40f, 1.69f, 15.17f),
            },
            new NPCInfo // Relic NPC
            {
                type = NpcType.Relic,
                NpcId = 1052605,
                AlternateNpcIds = [1052583, 1052586, 1052598],
                Name = "Researchingway",
                NpcLocation = new Vector3(-18.91f, 2.15f, 18.84f),
                Corner1 = new Vector3(-14.67f, 1.69f, 19.09f),
                Corner2 = new Vector3(-18.64f, 1.69f, 15.11f),
                Corner3 = new Vector3(-16.89f, 1.69f, 14.18f),
                Corner4 = new Vector3(-13.74f, 1.69f, 17.70f),
            },
            new NPCInfo // Cosmic Fortune aka Gamba Wheel
            {
                type = NpcType.Gamba,
                NpcId = 1052612,
                AlternateNpcIds = [1052590, 1052602, 1052611],
                Name = "Orbitingway",
                NpcLocation = new Vector3(18.84f, 2.24f, -18.91f),
                Corner1 = new Vector3(14.97f, 1.69f, -18.79f),
                Corner2 = new Vector3(18.74f, 1.69f, -15.02f),
                Corner3 = new Vector3(17.23f, 1.69f, -14.45f),
                Corner4 = new Vector3(13.96f, 1.69f, -17.90f),
            }
        },
        // ⚠️ 第二顆星 Phaenna。整組資料都是上游照國際服寫的，台服**目前一筆也驗不了**：
        //    這四個 DataId 在台服 ENpcResident 裡有列，但 Singular/Name 全部是空字串
        //    （＝尚未開放），而 WKSTerritoryInfo 對應那一列也整列是 0，拿不到台服自己的值。
        //
        //    🔴 已知會重演的雷：上面 [1237] 的每個 NPC 都帶 AlternateNpcIds，
        //    因為**同一個 NPC 會依據據點的建設階段換成不同的 DataId**，而上游只寫死了
        //    國際服當時那一階的值，台服對不上（commit 3cec4b0 就是在修這件事）。
        //    這裡的 [1291] **沒有任何 AlternateNpcIds**，所以台服開放 Phaenna 時
        //    幾乎一定會再中一次同一顆雷。
        //
        //    緩解已經在 Utils.TryGetNpcObject：ID 全部找不到時會退回「配置座標 5 公尺內的
        //    可指定 NPC」並記一筆 Warning。⚠️ 但那個退路依賴下面的 NpcLocation 是對的，
        //    而座標同樣是國際服資料、同樣無法離線驗證。
        //    → 開放當天請先看 log 有沒有 "[NPC Resolver]" 的 Warning：
        //      有 Warning 但功能正常＝只是 DataId 要補；連退路都失敗＝座標也要重測。
        [1291] = new List<NPCInfo>
        {
            new NPCInfo()
            {
                type = NpcType.Repair,
                NpcId = 1052641,
                Name = "Godgyth",
                NpcLocation = new Vector3(359.52f, 52.75f, -401.72f),
                Corner1 = new Vector3(359.40f, 52.69f, -405.65f),
                Corner2 = new Vector3(354.36f, 52.69f, -400.62f),
                Corner3 = new Vector3(352.80f, 52.61f, -402.19f),
                Corner4 = new Vector3(357.93f, 52.64f, -406.99f),
            },
            new NPCInfo // Credit Exchange Vendor
            {
                type = NpcType.Credit,
                NpcId = 1052640,
                Name = "Mesouaidonque",
                NpcLocation = new Vector3(358.33f, 52.75f, -400.44f),
                Corner1 = new Vector3(359.40f, 52.69f, -405.65f),
                Corner2 = new Vector3(354.36f, 52.69f, -400.62f),
                Corner3 = new Vector3(352.80f, 52.61f, -402.19f),
                Corner4 = new Vector3(357.93f, 52.64f, -406.99f),
            },
            new NPCInfo // Relic NPC
            {
                type = NpcType.Relic,
                NpcId = 1052629,
                Name = "Researchingway",
                NpcLocation = new Vector3(321.22f, 53.19f, -401.24f),
                Corner1 = new Vector3(325.77f, 52.69f, -400.47f),
                Corner2 = new Vector3(320.52f, 52.69f, -405.73f),
                Corner3 = new Vector3(322.31f, 52.69f, -406.82f),
                Corner4 = new Vector3(326.88f, 52.69f, -402.41f),
            },
            new NPCInfo // Cosmic Fortune aka Gamba Wheel
            {
                type = NpcType.Gamba,
                NpcId = 1052642,
                Name = "Orbitingway",
                NpcLocation = new Vector3(358.82f, 53.19f, -438.86f),
                Corner1 = new Vector3(354.66f, 52.69f, -439.08f),
                Corner2 = new Vector3(358.74f, 52.69f, -435.01f),
                Corner3 = new Vector3(357.67f, 52.69f, -433.95f),
                Corner4 = new Vector3(353.90f, 52.69f, -437.59f),
            }
        },
    };
}
