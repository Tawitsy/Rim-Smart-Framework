using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Modules
{
    public static class Module_Delegation
    {
        public static bool ShouldYieldToCloserColonist(WorkGiver_Scanner scanner, Pawn pawn, Thing target)
        {
            if (!SmartAIMod.Settings.smartTaskDelegation || pawn?.Map == null || target == null)
                return false;

            if (!pawn.IsColonist || pawn.Drafted)
                return false;

            if (scanner?.def != null && scanner.def.emergency)
                return false;

            WorkTypeDef workType = scanner?.def?.workType;

            float myDistSq = pawn.Position.DistanceToSquared(target.Position);

            if (myDistSq <= 196f)
                return false;

            Map map = pawn.Map;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            if (colonists == null || colonists.Count <= 1)
                return false;

            Room targetRoom = target.Position.GetRoom(map);
            bool targetInRoom = targetRoom != null && targetRoom.ProperRoom && !targetRoom.PsychologicallyOutdoors;
            Room pawnRoom = pawn.Position.GetRoom(map);
            bool pawnInDifferentRoom = targetInRoom && (pawnRoom != targetRoom);

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn other = colonists[i];
                if (other == pawn || other.Dead || other.Downed || other.Drafted || other.InMentalState)
                    continue;

                if (!IsPawnCapableAndAssigned(other, workType))
                    continue;

                float otherDistSq = other.Position.DistanceToSquared(target.Position);

                if (SmartAIMod.Settings.localVisualCourtesy && otherDistSq <= 56.25f && (otherDistSq * 6.25f < myDistSq))
                {
                    if (other.CanReserveAndReach(target, PathEndMode.Touch, Danger.Some) && IsAvailableForLocalTask(other, workType))
                    {
                        if (other.CurJob == null || other.CurJob.def == JobDefOf.Wait_Wander || other.CurJob.def == JobDefOf.GotoWander)
                        {
                            other.jobs.CheckForJobOverride();
                        }
                        return true;
                    }
                }

                if (SmartAIMod.Settings.roomCentricWorkFocus && targetInRoom && pawnInDifferentRoom)
                {
                    if (other.Position.GetRoom(map) == targetRoom && other.CanReserveAndReach(target, PathEndMode.Touch, Danger.Some) && IsAvailableForLocalTask(other, workType))
                    {
                        if (other.CurJob == null || other.CurJob.def == JobDefOf.Wait_Wander || other.CurJob.def == JobDefOf.GotoWander)
                        {
                            other.jobs.CheckForJobOverride();
                        }
                        return true;
                    }
                }

                if (SmartAIMod.Settings.antiSwarmingRedundancy && other.CurJob != null && other.CurJob.targetA.Thing == target)
                {
                    if (!(target is Corpse) && !(target.def.stackLimit > 1 && target.stackCount > 75))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool isCheckingRoomWork;

        public static bool ShouldPrioritizeCurrentRoom(WorkGiver_Scanner scanner, Pawn pawn, Thing target)
        {
            if (isCheckingRoomWork) return false;
            if (!SmartAIMod.Settings.roomCentricWorkFocus || pawn?.Map == null || target == null)
                return false;

            if (!pawn.IsColonist || pawn.Drafted)
                return false;

            if (scanner?.def != null && scanner.def.emergency)
                return false;

            Map map = pawn.Map;
            Room pawnRoom = pawn.Position.GetRoom(map);
            if (pawnRoom == null || !pawnRoom.ProperRoom || pawnRoom.PsychologicallyOutdoors)
                return false;

            Room targetRoom = target.Position.GetRoom(map);
            if (targetRoom != pawnRoom)
            {
                isCheckingRoomWork = true;
                try
                {
                    return HasWorkRemainingInRoom(pawn, pawnRoom, scanner);
                }
                finally
                {
                    isCheckingRoomWork = false;
                }
            }

            return false;
        }

        private static bool HasWorkRemainingInRoom(Pawn pawn, Room room, WorkGiver_Scanner scanner)
        {
            if (room == null || scanner == null) return false;

            var things = room.ContainedAndAdjacentThings;
            if (things == null) return false;

            int count = things.Count;
            for (int i = 0; i < count; i++)
            {
                Thing t = things[i];
                if (t != null && !t.Destroyed && !t.IsForbidden(pawn) && t.Position.GetRoom(pawn.Map) == room)
                {
                    if (t.Position.DistanceToSquared(pawn.Position) <= 256 &&
                        scanner.HasJobOnThing(pawn, t, false) &&
                        pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool IsPawnCapableAndAssigned(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || pawn.Downed || pawn.Dead || pawn.Drafted || pawn.InMentalState)
                return false;

            if (workType == null)
                return true;

            if (pawn.WorkTypeIsDisabled(workType))
                return false;

            return pawn.workSettings != null && pawn.workSettings.WorkIsActive(workType);
        }

        private static bool IsAvailableForLocalTask(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn.CurJob == null) return true;
            JobDef def = pawn.CurJob.def;

            if (def == JobDefOf.Wait_Wander || def == JobDefOf.GotoWander || def == JobDefOf.Wait_MaintainPosture)
                return true;

            if (pawn.CurJob.workGiverDef?.workType == workType)
                return true;

            return false;
        }
    }
}
