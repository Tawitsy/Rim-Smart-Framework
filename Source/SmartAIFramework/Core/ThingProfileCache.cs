using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.API;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Data
{
    public sealed class ThingProfile
    {
        public bool isNutritious;
        public bool isPerishable;
        public bool isMedicine;
        public bool isBallisticCover;
        public float coverPercent;
        public bool isFlammable;
        public bool isExplosive;
        public bool isTrap;
        public float marketValue;
        public float valueDensity;

        public AIThreatExtension threatExtension;
        public short hazardWeight = 1;
    }

    public static class ThingProfileCache
    {
        private static readonly Dictionary<int, ThingProfile> byDefHash = new Dictionary<int, ThingProfile>(512);

        public static ThingProfile Get(Thing thing) => thing?.def == null ? null : Get(thing.def);

        public static ThingProfile Get(ThingDef def)
        {
            if (def == null) return null;
            int key = def.shortHash;
            if (byDefHash.TryGetValue(key, out ThingProfile profile))
                return profile;

            profile = new ThingProfile
            {
                isNutritious = def.IsIngestible,
                isPerishable = def.HasComp(typeof(CompRottable)),
                isMedicine = def.IsMedicine,
                coverPercent = def.fillPercent,
                isBallisticCover = def.fillPercent >= 0.35f,
                isFlammable = def.BaseFlammability > 0.4f,
                isExplosive = def.HasComp(typeof(CompExplosive)) || def == ThingDefOf.Chemfuel,
                isTrap = def.thingClass != null && typeof(Building_Trap).IsAssignableFrom(def.thingClass),
                marketValue = def.BaseMarketValue,
                valueDensity = def.BaseMarketValue / Mathf.Max(0.1f, def.BaseMass)
            };

            AIThreatExtension ext = def.GetModExtension<AIThreatExtension>();
            if (ext != null)
            {
                profile.threatExtension = ext;
                profile.hazardWeight = (short)Mathf.Clamp(Mathf.RoundToInt(ext.dangerScore / 30f), 1, 4);
            }

            byDefHash[key] = profile;
            return profile;
        }

        private static bool[] hasThreatExtension;

        public static bool HasThreatExtension(ThingDef def)
        {
            bool[] table = hasThreatExtension;
            int idx = def.index;
            if (table == null || idx >= table.Length)
                table = BuildThreatTable();
            return table[idx];
        }

        private static bool[] BuildThreatTable()
        {
            System.Collections.Generic.List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            int size = DefDatabase<ThingDef>.DefCount + 1;
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].index >= size) size = defs[i].index + 1;
            }

            bool[] table = new bool[size];
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef d = defs[i];
                AIThreatExtension ext = d.GetModExtension<AIThreatExtension>();
                table[d.index] = ext != null && ext.hazardRadius > 0f;
            }
            hasThreatExtension = table;
            return table;
        }

        public static void Clear()
        {
            byDefHash.Clear();
            hasThreatExtension = null;
        }
    }
}
