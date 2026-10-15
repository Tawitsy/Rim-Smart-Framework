using System;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Brain;
using SmartAIFramework.Modules;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsGoodStoreCell))]
    public static class Patch_StoreUtility_IsGoodStoreCell
    {
        public static void Postfix(IntVec3 c, Map map, ref bool __result)
        {
            if (__result && SmartAIMod.Settings.structureDoorDiscipline && c.GetDoor(map) != null)
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(GenPlace), nameof(GenPlace.HaulPlaceBlockerIn))]
    public static class Patch_GenPlace_HaulPlaceBlockerIn
    {
        public static void Postfix(IntVec3 c, Map map, ref Thing __result)
        {
            if (__result == null && SmartAIMod.Settings.structureDoorDiscipline && map != null)
            {
                Building_Door door = c.GetDoor(map);
                if (door != null)
                {
                    __result = door;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryDropCarriedThing),
        new[] { typeof(IntVec3), typeof(ThingPlaceMode), typeof(Thing), typeof(Action<Thing, int>) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    public static class Patch_CarryTracker_TryDropCarriedThing
    {
        public static void Prefix(Pawn ___pawn, ref IntVec3 dropLoc)
        {
            if (!SmartAIMod.Settings.structureDoorDiscipline) return;
            Map map = ___pawn?.MapHeld;
            if (map == null || !dropLoc.InBounds(map)) return;

            if (dropLoc.GetDoor(map) != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 adj = dropLoc + GenAdj.CardinalDirections[i];
                    if (adj.InBounds(map) && adj.Standable(map) && adj.GetDoor(map) == null)
                    {
                        dropLoc = adj;
                        return;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryDropCarriedThing),
        new[] { typeof(IntVec3), typeof(int), typeof(ThingPlaceMode), typeof(Thing), typeof(Action<Thing, int>) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    public static class Patch_CarryTracker_TryDropCarriedThing_Count
    {
        public static void Prefix(Pawn ___pawn, ref IntVec3 dropLoc)
        {
            Patch_CarryTracker_TryDropCarriedThing.Prefix(___pawn, ref dropLoc);
        }
    }

    [HarmonyPatch(typeof(JobGiver_Wander), "GetExactWanderDest")]
    public static class Patch_JobGiver_Wander_GetExactWanderDest
    {
        public static void Postfix(Pawn pawn, ref IntVec3 __result)
        {
            if (pawn?.Map == null || !__result.IsValid) return;

            if (SmartAIMod.Settings.structureDoorDiscipline && __result.GetDoor(pawn.Map) != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 adj = __result + GenAdj.CardinalDirections[i];
                    if (adj.InBounds(pawn.Map) && adj.Standable(pawn.Map) && adj.GetDoor(pawn.Map) == null)
                    {
                        __result = adj;
                        break;
                    }
                }
            }

            if (SmartAIMod.Settings.sleepingCourtesy && __result.IsValid)
            {
                Room room = __result.GetRoom(pawn.Map);
                if (Module_Social.IsPrivateBedroomWithActiveSleeper(room, pawn))
                {
                    __result = IntVec3.Invalid;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Building_Door), "DoorTryClose")]
    public static class Patch_Building_Door_DoorTryClose
    {
        public static void Prefix(Building_Door __instance, ref bool ___holdOpenInt)
        {
            if (___holdOpenInt && SmartAIMod.Settings.structureDoorDiscipline && __instance.Spawned)
            {
                Map map = __instance.Map;
                if (map != null && map.dangerWatcher.DangerRating == StoryDanger.High)
                {
                    if (!__instance.BlockedOpenMomentary)
                    {
                        ___holdOpenInt = false;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(RCellFinder), nameof(RCellFinder.TryFindGoodAdjacentSpotToTouch))]
    public static class Patch_RCellFinder_TryFindGoodAdjacentSpotToTouch
    {
        public static void Postfix(Pawn toucher, Thing touchee, ref IntVec3 result, ref bool __result)
        {
            if (!SmartAIMod.Settings.structureAntiTrapping || !__result || toucher?.Map == null || touchee == null) return;

            bool isImpassable = false;
            if (touchee is Frame frame && frame.BuildDef != null && frame.BuildDef.passability == Traversability.Impassable)
            {
                isImpassable = true;
            }
            else if (touchee is Blueprint blueprint && blueprint.def.entityDefToBuild is ThingDef td && td.passability == Traversability.Impassable)
            {
                isImpassable = true;
            }
            else if (touchee is Building b && b.def != null && b.def.passability == Traversability.Impassable)
            {
                isImpassable = true;
            }

            if (!isImpassable) return;

            Map map = toucher.Map;
            bool isDeconstructing = toucher.CurJob?.def == JobDefOf.Deconstruct;
            bool isCurrentCellRoofed = map.roofGrid.RoofAt(result) != null;
            int currentOpenNeighbors = CountOpenCardinalNeighbors(result, map, touchee.Position);

            if (currentOpenNeighbors <= 1 || (isDeconstructing && isCurrentCellRoofed))
            {
                IntVec3 bestSafeCell = result;
                float bestScore = currentOpenNeighbors * 10f;
                if (isDeconstructing && !isCurrentCellRoofed) bestScore += 50f;

                foreach (IntVec3 adj in GenAdj.CellsAdjacent8Way(touchee))
                {
                    if (adj != result && adj.InBounds(map) && adj.WalkableBy(map, toucher) && toucher.CanReach(adj, PathEndMode.OnCell, Danger.Some))
                    {
                        int openCount = CountOpenCardinalNeighbors(adj, map, touchee.Position);
                        bool isAdjRoofed = map.roofGrid.RoofAt(adj) != null;

                        float score = openCount * 10f;

                        if (isDeconstructing && !isAdjRoofed)
                        {
                            score += 50f;
                        }

                        if (score > bestScore && openCount >= 1)
                        {
                            bestScore = score;
                            bestSafeCell = adj;
                        }
                    }
                }

                if (bestSafeCell != result)
                {
                    result = bestSafeCell;
                }
            }
        }

        private static int CountOpenCardinalNeighbors(IntVec3 c, Map map, IntVec3 buildingPos)
        {
            int count = 0;
            for (int i = 0; i < 4; i++)
            {
                IntVec3 neighbor = c + GenAdj.CardinalDirections[i];
                if (neighbor == buildingPos) continue;
                if (neighbor.InBounds(map) && neighbor.Standable(map))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
