using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Numerics; // Add this for Vector2 and Vector3

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
