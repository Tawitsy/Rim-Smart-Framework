using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Data;
using SmartAIFramework.Work;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Patches
{
    [HarmonyPatchCategory("Survival")]
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

    [HarmonyPatchCategory("Survival")]
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

    [HarmonyPatchCategory("Survival")]
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

    [HarmonyPatchCategory("Survival")]
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
                    Building_Bed bed = FindBedForEarlyRescue(pawn, patient);
                    __result = bed != null && patient.CanReserve(bed);
                }
            }
        }

        internal static Building_Bed FindBedForEarlyRescue(Pawn rescuer, Pawn patient)
        {
            return RestUtility.FindBedFor(patient, rescuer, checkSocialProperness: false,
                ignoreOtherReservations: false, patient.GuestStatus);
        }
    }

    [HarmonyPatchCategory("Survival")]
    [HarmonyPatch(typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.JobOnThing))]
    public static class Patch_WorkGiver_RescueDowned_JobOnThing
    {
        public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (__result == null || !SmartAIMod.Settings.buddyEmpathyAssistance)
                return;

            if (t is Pawn patient && !patient.Downed && ColonistCareUtility.NeedsEarlyRescue(patient))
            {
                Building_Bed bed = Patch_WorkGiver_RescueDowned_HasJobOnThing.FindBedForEarlyRescue(pawn, patient);
                if (bed == null)
                {
                    __result = null;
                    return;
                }

                Job job = JobMaker.MakeJob(JobDefOf.TakeToBedToOperate, patient, bed);
                job.count = 1;
                __result = job;
                if (Data.SmartLog.IsEnabled)
                    Data.SmartLog.Decision("EarlyRescue", pawn, $"carrying {patient.LabelShort} to {bed.LabelShort} at {bed.Position}");
            }
        }
    }

    [HarmonyPatchCategory("Survival")]
    [HarmonyPatch]
    public static class Patch_WorkGiver_FightFires_JobOnThing
    {
        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.WorkGiver_FightFires");
            return AccessTools.Method(type, "JobOnThing");
        }

        public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (__result == null || forced || pawn == null || t == null || PawnStateTracker.ObserveOnly)
                return;

            if (WorkUtility.IsSuperheatedFireHazard(pawn, t.Position, out _))
            {
                __result = null;
            }
        }
    }
}
