using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Work;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.WantsToBeRescued))]
    public static class Patch_HealthAIUtility_WantsToBeRescued
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result || !SmartAIMod.Settings.buddyEmpathyAssistance || pawn == null || !pawn.Spawned || pawn.Dead)
                return;

            if (ColonistCareUtility.NeedsEarlyRescue(pawn))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.ShouldSkip))]
    public static class Patch_WorkGiver_RescueDowned_ShouldSkip
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result || !SmartAIMod.Settings.buddyEmpathyAssistance || pawn?.Map == null)
                return;

            List<Pawn> colonists = pawn.Map.mapPawns?.FreeColonistsSpawned;
            if (colonists == null) return;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn c = colonists[i];
                if (c != null && c != pawn && !c.Downed && ColonistCareUtility.NeedsEarlyRescue(c))
                {
                    __result = false;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.PotentialWorkThingsGlobal))]
    public static class Patch_WorkGiver_RescueDowned_PotentialWorkThingsGlobal
    {
        public static void Postfix(Pawn pawn, ref IEnumerable<Thing> __result)
        {
            if (!SmartAIMod.Settings.buddyEmpathyAssistance || pawn?.Map == null)
                return;

            List<Pawn> colonists = pawn.Map.mapPawns?.FreeColonistsSpawned;
            if (colonists == null || colonists.Count == 0)
                return;

            List<Thing> merged = null;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn c = colonists[i];
                if (c != null && c != pawn && !c.Downed && ColonistCareUtility.NeedsEarlyRescue(c))
                {
                    if (merged == null)
                    {
                        merged = new List<Thing>(__result ?? Enumerable.Empty<Thing>());
                    }
                    merged.Add(c);
                }
            }

            if (merged != null)
            {
                __result = merged;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.HasJobOnThing))]
    public static class Patch_WorkGiver_RescueDowned_HasJobOnThing
    {
        public static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            if (__result || !SmartAIMod.Settings.buddyEmpathyAssistance || pawn?.Map == null)
                return;

            if (t is Pawn patient && !patient.Downed && ColonistCareUtility.NeedsEarlyRescue(patient))
            {
                if (!patient.InBed() && pawn.CanReserve(patient, 1, -1, null, forced) && !GenAI.EnemyIsNear(patient, 25f))
                {
                    __result = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.JobOnThing))]
    public static class Patch_WorkGiver_RescueDowned_JobOnThing
    {
        public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (__result == null || !SmartAIMod.Settings.buddyEmpathyAssistance)
                return;

            if (t is Pawn patient && !patient.Downed && ColonistCareUtility.NeedsEarlyRescue(patient))
            {
                if (__result.def == JobDefOf.Rescue)
                {
                    __result.def = JobDefOf.TakeToBedToOperate;
                }
            }
        }
    }
}
