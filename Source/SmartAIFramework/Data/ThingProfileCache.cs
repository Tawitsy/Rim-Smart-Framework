using System.Collections.Generic;
using RimWorld;
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

            byDefHash[key] = profile;
            return profile;
        }

        public static void Clear() => byDefHash.Clear();
    }
}
