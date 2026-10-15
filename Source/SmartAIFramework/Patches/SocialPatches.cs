using HarmonyLib;
using RimWorld;
using SmartAIFramework.Modules;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.WantsToBeRescued))]
    public static class Patch_HealthAIUtility_WantsToBeRescued
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result) return;
            if (!SmartAIMod.Settings.buddyEmpathyAssistance || pawn == null || !pawn.Spawned || pawn.Dead) return;

            if (Module_Social.ShouldBeRescuedEarly(pawn))
            {
                __result = true;
                SmartAIFramework.Core.SmartAILog.Log("SmartAI-Social", $"Buddy Empathy: Authorized early rescue for limping/critical pawn {pawn.LabelShort} before collapse.");
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.ShouldSkip))]
    public static class Patch_WorkGiver_RescueDowned_ShouldSkip
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result) return;
            if (!SmartAIMod.Settings.buddyEmpathyAssistance || pawn?.Map == null) return;
            var colonists = pawn.Map.mapPawns.FreeColonistsSpawned;
            if (colonists == null) return;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn c = colonists[i];
                if (c != null && c != pawn && !c.Downed && Module_Social.ShouldBeRescuedEarly(c))
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
        public static void Postfix(Pawn pawn, ref System.Collections.Generic.IEnumerable<Thing> __result)
        {
            if (!SmartAIMod.Settings.buddyEmpathyAssistance || pawn?.Map == null) return;
            var colonists = pawn.Map.mapPawns.FreeColonistsSpawned;
            if (colonists == null || colonists.Count == 0) return;

            System.Collections.Generic.List<Thing> merged = null;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn c = colonists[i];
                if (c != null && c != pawn && !c.Downed && Module_Social.ShouldBeRescuedEarly(c))
                {
                    if (merged == null)
                    {
                        merged = new System.Collections.Generic.List<Thing>(__result ?? System.Linq.Enumerable.Empty<Thing>());
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
            if (__result) return;
            if (!SmartAIMod.Settings.buddyEmpathyAssistance || pawn?.Map == null) return;
            if (t is Pawn patient && !patient.Downed && Module_Social.ShouldBeRescuedEarly(patient))
            {
                if (!patient.InBed() && pawn.CanReserve(patient, 1, -1, null, forced) && !GenAI.EnemyIsNear(patient, 25f))
                {
                    __result = true;
                }
            }
        }
    }
}
