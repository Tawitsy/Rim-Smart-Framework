using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Data;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Patches
{
    [HarmonyPatchCategory("Mind")]
    [HarmonyPatch(typeof(TantrumMentalStateUtility), nameof(TantrumMentalStateUtility.CanSmash))]
    public static class Patch_TantrumMentalStateUtility_CanSmash
    {
        public static void Postfix(Pawn pawn, Thing thing, ref bool __result)
        {
            if (!__result || !SmartAIMod.Settings.mindTantrumSparesExplosives) return;
            if (pawn?.Faction != Faction.OfPlayer) return;
            if (IsExplosive(thing) || HoldsExplosive(thing))
                __result = false;
        }

        private static bool IsExplosive(Thing thing)
        {
            ThingProfile profile = ThingProfileCache.Get(thing);
            return profile != null && profile.isExplosive;
        }

        private static bool HoldsExplosive(Thing thing)
        {
            if (!(thing is ISlotGroupParent parent)) return false;
            Map map = thing.Map;
            List<IntVec3> cells = parent.GetSlotGroup()?.CellsList;
            if (map == null || cells == null) return false;

            for (int i = 0; i < cells.Count; i++)
            {
                List<Thing> things = map.thingGrid.ThingsListAtFast(cells[i]);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing t = things[j];
                    if (t.def.category == ThingCategory.Item && IsExplosive(t))
                        return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatchCategory("Mind")]
    [HarmonyPatch]
    public static class Patch_MentalBreakWorker_BreakCanOccur
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();
            MethodInfo baseMethod = AccessTools.Method(typeof(MentalBreakWorker), nameof(MentalBreakWorker.BreakCanOccur));
            seen.Add(baseMethod);
            yield return baseMethod;

            foreach (System.Type type in AccessTools.AllTypes())
            {
                if (type == null || !typeof(MentalBreakWorker).IsAssignableFrom(type) || type == typeof(MentalBreakWorker))
                    continue;
                MethodInfo method = type.GetMethod(nameof(MentalBreakWorker.BreakCanOccur),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(Pawn) }, null);
                if (method == null || method.IsAbstract || method.GetMethodBody() == null || !seen.Add(method))
                    continue;
                yield return method;
            }
        }

        public static void Postfix(MentalBreakWorker __instance, Pawn pawn, ref bool __result)
        {
            if (!__result || pawn?.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike) return;
            HashSet<string> disabled = SmartAIMod.Settings.disabledMentalBreaks;
            if (disabled.Count > 0 && __instance.def != null && disabled.Contains(__instance.def.defName))
                __result = false;
        }
    }
}
