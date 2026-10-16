using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Data;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Work
{
    public static class WorkDelegationUtility
    {
        private static int cachedTick = -1;
        private static int cachedPawnId = -1;
        private static Room cachedPawnRoom;
        private static bool cachedPawnRoomIsIndoor;
        private static bool cachedPawnRoomDirty;

        public static void ClearCache()
        {
            cachedTick = -1;
            cachedPawnId = -1;
            cachedPawnRoom = null;
        }

        private static void UpdatePawnCacheIfNeeded(Pawn pawn, Map map)
        {
            int now = PawnStateTracker.Now;
            int id = pawn.thingIDNumber;
            if (now == cachedTick && id == cachedPawnId)
                return;

            cachedTick = now;
            cachedPawnId = id;
            cachedPawnRoom = pawn.Position.GetRoom(map);
            cachedPawnRoomIsIndoor = cachedPawnRoom != null && cachedPawnRoom.ProperRoom && !cachedPawnRoom.PsychologicallyOutdoors;
            cachedPawnRoomDirty = cachedPawnRoomIsIndoor && cachedPawnRoom.GetStat(RoomStatDefOf.Cleanliness) < 0f;
        }

        public static bool ShouldYieldToCloserColonist(WorkGiver_Scanner scanner, Pawn pawn, Thing target)
        {
            if (!SmartAIMod.Settings.smartTaskDelegation || PawnStateTracker.ObserveOnly ||
                pawn?.Map == null || target == null || !target.Spawned)
                return false;

            if (!pawn.IsColonist || pawn.Drafted)
                return false;

            if (scanner?.def != null && scanner.def.emergency)
                return false;

            int myDistSq = pawn.Position.DistanceToSquared(target.Position);
            if (myDistSq <= 196)
                return false;

            Map map = pawn.Map;
            List<Pawn> colonists = map.mapPawns?.FreeColonistsSpawned;
            if (colonists == null || colonists.Count <= 1)
                return false;

            UpdatePawnCacheIfNeeded(pawn, map);

            WorkTypeDef workType = scanner?.def?.workType;
            District targetDistrict = target.GetDistrict();
            Room targetRoom = targetDistrict?.Room;
            bool targetInEnclosedRoom = targetRoom != null && targetRoom.ProperRoom && !targetRoom.PsychologicallyOutdoors;
            bool pawnInDifferentRoom = targetInEnclosedRoom && cachedPawnRoom != targetRoom;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn other = colonists[i];
                if (other == null || other == pawn || other.Dead || other.Downed || other.Drafted || other.InMentalState)
                    continue;

                if (SmartAIMod.Settings.antiSwarmingRedundancy && other.CurJob != null && other.CurJob.targetA.Thing == target)
                {
                    if (!(target is Corpse) && !(target.def.stackLimit > 1 && target.stackCount > 75))
                        return true;
                }

                if (!IsCapableAndAssigned(other, workType) || !IsAvailableForLocalTask(other))
                    continue;

                int otherDistSq = other.Position.DistanceToSquared(target.Position);

                if (SmartAIMod.Settings.localVisualCourtesy && otherDistSq <= 56 && (otherDistSq * 6 < myDistSq))
                {
                    if (other.GetDistrict() == targetDistrict &&
                        !target.IsForbidden(other) &&
                        map.reservationManager.CanReserve(other, target))
                    {
                        return true;
                    }
                }

                if (SmartAIMod.Settings.roomCentricWorkFocus && pawnInDifferentRoom)
                {
                    if (other.GetDistrict() == targetDistrict &&
                        !target.IsForbidden(other) &&
                        map.reservationManager.CanReserve(other, target))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool ShouldPrioritizeCurrentRoom(WorkGiver_Scanner scanner, Pawn pawn, Thing target)
        {
            if (!SmartAIMod.Settings.roomCentricWorkFocus || PawnStateTracker.ObserveOnly ||
                pawn?.Map == null || target == null || !target.Spawned)
                return false;

            if (!pawn.IsColonist || pawn.Drafted)
                return false;

            if (scanner?.def != null && scanner.def.emergency)
                return false;

            if (!(scanner is WorkGiver_CleanFilth))
                return false;

            Map map = pawn.Map;
            UpdatePawnCacheIfNeeded(pawn, map);

            if (!cachedPawnRoomIsIndoor || !cachedPawnRoomDirty)
                return false;

            Room targetRoom = target.GetDistrict()?.Room;
            return targetRoom != cachedPawnRoom;
        }

        public static bool IsCapableAndAssigned(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || pawn.Downed || pawn.Dead || pawn.Drafted || pawn.InMentalState)
                return false;
            if (workType == null)
                return true;
            if (pawn.WorkTypeIsDisabled(workType))
                return false;
            return pawn.workSettings != null && pawn.workSettings.WorkIsActive(workType);
        }

        // Only yield to someone who will actually pick the task up: idle, and scheduled to work right now.
        // A colonist busy with other work of the same type, or wandering during recreation/sleep hours,
        // would never take it and the task would starve.
        private static bool IsAvailableForLocalTask(Pawn pawn)
        {
            TimeAssignmentDef assignment = pawn.timetable?.CurrentAssignment;
            if (assignment != null && assignment != TimeAssignmentDefOf.Work && assignment != TimeAssignmentDefOf.Anything)
                return false;

            Job curJob = pawn.CurJob;
            if (curJob == null) return true;

            JobDef def = curJob.def;
            return def == JobDefOf.Wait_Wander || def == JobDefOf.GotoWander || def == JobDefOf.Wait_MaintainPosture;
        }
    }
}
