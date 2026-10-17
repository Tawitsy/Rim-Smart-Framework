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
            snapshot.map = null;
            snapshot.tick = -1;
            snapshot.idleHelpers.Clear();
            snapshot.targetedBy.Clear();
        }

        private sealed class ColonySnapshot
        {
            public Map map;
            public int tick = -1;
            public int activeColonists;
            public readonly List<Pawn> idleHelpers = new List<Pawn>(16);
            public readonly Dictionary<Thing, int> targetedBy = new Dictionary<Thing, int>(64);
        }

        private static readonly ColonySnapshot snapshot = new ColonySnapshot();

        private static ColonySnapshot GetSnapshot(Map map)
        {
            int now = PawnStateTracker.Now;
            if (snapshot.map == map && snapshot.tick == now)
                return snapshot;

            snapshot.map = map;
            snapshot.tick = now;
            snapshot.activeColonists = 0;
            snapshot.idleHelpers.Clear();
            snapshot.targetedBy.Clear();

            List<Pawn> colonists = map.mapPawns?.FreeColonistsSpawned;
            if (colonists == null)
                return snapshot;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn other = colonists[i];
                if (other == null || other.Dead || other.Downed || other.Drafted || other.InMentalState)
                    continue;
                snapshot.activeColonists++;

                Thing targeted = other.CurJob?.targetA.Thing;
                if (targeted != null)
                {
                    snapshot.targetedBy.TryGetValue(targeted, out int n);
                    snapshot.targetedBy[targeted] = n + 1;
                }

                if (IsAvailableForLocalTask(other))
                    snapshot.idleHelpers.Add(other);
            }
            return snapshot;
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

            if (target is Pawn)
                return false;

            int myDistSq = pawn.Position.DistanceToSquared(target.Position);
            if (myDistSq <= 196)
                return false;

            Map map = pawn.Map;
            ColonySnapshot snap = GetSnapshot(map);
            if (snap.activeColonists <= 1)
                return false;

            if (SmartAIMod.Settings.antiSwarmingRedundancy && snap.targetedBy.TryGetValue(target, out int targeting))
            {
                if (pawn.CurJob?.targetA.Thing == target) targeting--;
                if (targeting > 0 && !(target is Corpse) && !(target.def.stackLimit > 1 && target.stackCount > 75))
                    return true;
            }

            List<Pawn> helpers = snap.idleHelpers;
            if (helpers.Count == 0)
                return false;

            UpdatePawnCacheIfNeeded(pawn, map);

            WorkTypeDef workType = scanner?.def?.workType;
            District targetDistrict = null;
            bool districtResolved = false;
            bool pawnInDifferentRoom = false;

            for (int i = 0; i < helpers.Count; i++)
            {
                Pawn other = helpers[i];
                if (other == pawn)
                    continue;

                int otherDistSq = other.Position.DistanceToSquared(target.Position);
                bool closeEnough = SmartAIMod.Settings.localVisualCourtesy && otherDistSq <= 56 && otherDistSq * 6 < myDistSq;
                if (!closeEnough && !SmartAIMod.Settings.roomCentricWorkFocus)
                    continue;

                if (!districtResolved)
                {
                    districtResolved = true;
                    targetDistrict = target.GetDistrict();
                    Room targetRoom = targetDistrict?.Room;
                    bool targetInEnclosedRoom = targetRoom != null && targetRoom.ProperRoom && !targetRoom.PsychologicallyOutdoors;
                    pawnInDifferentRoom = targetInEnclosedRoom && cachedPawnRoom != targetRoom;
                }
                if (!closeEnough && !pawnInDifferentRoom)
                    continue;

                if (workType != null && (other.WorkTypeIsDisabled(workType) || other.workSettings == null || !other.workSettings.WorkIsActive(workType)))
                    continue;

                if (closeEnough)
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
