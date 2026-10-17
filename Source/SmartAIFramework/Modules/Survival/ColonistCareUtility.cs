using RimWorld;
using SmartAIFramework.Data;
using SmartAIFramework.Evaluation;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Work
{
    public static class ColonistCareUtility
    {
        private static int cachedTick = -1;
        private static int cachedRoomId = -1;
        private static int cachedObserverId = -1;
        private static bool cachedRoomResult;

        private static int sleeperFlagTick = -1;
        private static Map sleeperFlagMap;
        private static bool sleeperFlagValue;

        private static bool AnyColonistAsleepInBed(Map map)
        {
            int now = PawnStateTracker.Now;
            if (now == sleeperFlagTick && map == sleeperFlagMap)
                return sleeperFlagValue;

            bool any = false;
            var colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p.InBed() && (p.CurJob?.def == JobDefOf.LayDown || p.jobs?.curDriver?.asleep == true))
                {
                    any = true;
                    break;
                }
            }

            sleeperFlagTick = now;
            sleeperFlagMap = map;
            sleeperFlagValue = any;
            return any;
        }

        public static bool NeedsEarlyRescue(Pawn patient)
        {
            if (!SmartAIMod.Settings.buddyEmpathyAssistance || PawnStateTracker.ObserveOnly ||
                patient == null || !patient.Spawned || patient.Dead)
                return false;

            if (patient.Faction != Faction.OfPlayer || patient.InBed() || patient.Drafted)
                return false;

            JobDef curDef = patient.CurJob?.def;
            if (patient.InMentalState || curDef == JobDefOf.AttackMelee || curDef == JobDefOf.AttackStatic ||
                curDef == JobDefOf.TendPatient || curDef == JobDefOf.ExtinguishSelf)
                return false;

            if (SurvivalUtility.IsInBleedOutCrisis(patient, SmartAIMod.Settings.crisisHorizonTicks))
                return true;

            float movingCap = patient.health?.capacities?.GetLevel(PawnCapacityDefOf.Moving) ?? 1f;
            if (movingCap >= 0.35f)
                return false;

            float summaryHealth = patient.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (summaryHealth < 0.60f)
                return true;

            HediffSet hediffSet = patient.health?.hediffSet;
            if (hediffSet != null)
            {
                Hediff hypo = hediffSet.GetFirstHediffOfDef(HediffDefOf.Hypothermia);
                if (hypo != null && hypo.Severity > 0.40f) return true;

                Hediff heat = hediffSet.GetFirstHediffOfDef(HediffDefOf.Heatstroke);
                if (heat != null && heat.Severity > 0.40f) return true;
            }

            return false;
        }

        public static bool HasSleepingOccupant(Room room, Pawn observer)
        {
            if (!SmartAIMod.Settings.sleepingCourtesy || room == null || !room.ProperRoom || room.PsychologicallyOutdoors)
                return false;

            RoomRoleDef role = room.Role;
            if (role != RoomRoleDefOf.Bedroom && role != RoomRoleDefOf.Barracks)
                return false;

            if (observer != null && (observer.RaceProps == null || !observer.RaceProps.Humanlike))
                return false;

            int now = PawnStateTracker.Now;
            int obsId = observer?.thingIDNumber ?? 0;
            if (now == cachedTick && room.ID == cachedRoomId && obsId == cachedObserverId)
            {
                return cachedRoomResult;
            }

            bool result = EvaluateRoomSleepers(room, observer);
            cachedTick = now;
            cachedRoomId = room.ID;
            cachedObserverId = obsId;
            cachedRoomResult = result;
            return result;
        }

        private static bool EvaluateRoomSleepers(Room room, Pawn observer)
        {
            foreach (Building_Bed bed in room.ContainedThings<Building_Bed>())
            {
                if (bed == null) continue;
                var occupants = bed.CurOccupants;
                if (occupants == null) continue;

                foreach (Pawn sleeper in occupants)
                {
                    if (sleeper == null || sleeper.Dead || !sleeper.Spawned)
                        continue;

                    if (observer != null)
                    {
                        if (sleeper == observer) continue;
                        if (observer.relations != null && sleeper.relations != null &&
                            LovePartnerRelationUtility.LovePartnerRelationExists(observer, sleeper))
                        {
                            continue;
                        }
                    }

                    if (sleeper.InBed() && (sleeper.CurJob?.def == JobDefOf.LayDown || sleeper.jobs?.curDriver?.asleep == true))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool ShouldAvoidWakingSleeper(Pawn pawn, Thing targetThing)
        {
            if (!SmartAIMod.Settings.sleepingCourtesy || PawnStateTracker.ObserveOnly ||
                pawn == null || targetThing == null)
                return false;

            Map map = pawn.Map;
            if (map == null || pawn.Drafted || !targetThing.Spawned)
                return false;

            if (targetThing is Pawn)
                return false;

            if (!AnyColonistAsleepInBed(map))
                return false;

            IntVec3 pos = targetThing.Position;
            if (!pos.IsValid || map.roofGrid.RoofAt(pos) == null)
                return false;

            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return false;

            Job curJob = pawn.CurJob;
            if (curJob != null)
            {
                JobDef d = curJob.def;
                if (d == JobDefOf.TendPatient || d == JobDefOf.ExtinguishSelf ||
                    d == JobDefOf.Rescue || d == JobDefOf.TakeToBedToOperate ||
                    d == JobDefOf.Flee || d == JobDefOf.BeatFire)
                    return false;
            }

            Room targetRoom = pos.GetRoom(map);
            return HasSleepingOccupant(targetRoom, pawn);
        }
    }
}
