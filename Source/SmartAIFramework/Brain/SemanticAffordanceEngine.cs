using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Brain
{
    public sealed class ThingAffordanceProfile
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

        public override string ToString() =>
            $"[Affordance] Food:{isNutritious} Perish:{isPerishable} Med:{isMedicine} Cover:{coverPercent:P0} Flam:{isFlammable} Expl:{isExplosive} Trap:{isTrap} Val/kg:{valueDensity:F1}";
    }

    public static class SemanticAffordanceEngine
    {
        private static readonly Dictionary<ThingDef, ThingAffordanceProfile> cache =
            new Dictionary<ThingDef, ThingAffordanceProfile>(512);

        public static ThingAffordanceProfile Analyze(Thing thing) => thing?.def == null ? null : Analyze(thing.def);

        public static ThingAffordanceProfile Analyze(ThingDef def)
        {
            if (def == null) return null;
            if (cache.TryGetValue(def, out var cached)) return cached;

            var profile = new ThingAffordanceProfile
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

            cache[def] = profile;
            return profile;
        }

        public static void ClearCache() => cache.Clear();
    }
}
