using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Brain;
using SmartAIFramework.Core.Decisions;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Modules
{
    public static class Module_Social
    {
        public static bool ShouldBeRescuedEarly(Pawn patient)
        {
            if (!SmartAIMod.Settings.buddyEmpathyAssistance || ActionGate.ObserveOnly || patient == null || !patient.Spawned || patient.Dead)
                return false;

            if (patient.Faction != Faction.OfPlayer || patient.InBed() || patient.Drafted)
                return false;

            JobDef curDef = patient.CurJob?.def;
            if (patient.InMentalState || curDef == JobDefOf.AttackMelee || curDef == JobDefOf.AttackStatic ||
                curDef == JobDefOf.TendPatient || curDef == JobDefOf.ExtinguishSelf)
                return false;

            bool isBleedingCrisis = DeterministicPredictor.IsInImmediatePhysiologicalCrisis(patient, SmartAIMod.Settings.crisisHorizonTicks);

            float movingCap = patient.health?.capacities?.GetLevel(PawnCapacityDefOf.Moving) ?? 1f;
            bool isCrippled = movingCap < 0.35f;

            bool isThermalCrisis = false;
            var hediffSet = patient.health?.hediffSet;
            var hypothermia = hediffSet?.GetFirstHediffOfDef(HediffDefOf.Hypothermia);
            var heatstroke = hediffSet?.GetFirstHediffOfDef(HediffDefOf.Heatstroke);
            if ((hypothermia != null && hypothermia.Severity > 0.40f) ||
                (heatstroke != null && heatstroke.Severity > 0.40f))
            {
                isThermalCrisis = true;
            }

            float summaryHealth = patient.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (isBleedingCrisis || (isCrippled && isThermalCrisis) || (isCrippled && summaryHealth < 0.60f))
            {
                return true;
            }

            return false;
        }

        public static bool IsPrivateBedroomWithActiveSleeper(Room room, Pawn observer)
        {
            if (!SmartAIMod.Settings.sleepingCourtesy || room == null || !room.ProperRoom || room.PsychologicallyOutdoors)
                return false;

            if (observer != null && (observer.RaceProps == null || !observer.RaceProps.Humanlike))
                return false;

            if (room.Role != RoomRoleDefOf.Bedroom && room.Role != RoomRoleDefOf.Barracks)
                return false;

            foreach (Building_Bed bed in room.ContainedThings<Building_Bed>())
            {
                if (bed == null) continue;
                var occupants = bed.CurOccupants;
                if (occupants == null) continue;

                foreach (Pawn sleeper in occupants)
                {
                    if (sleeper == null || sleeper.Dead || !sleeper.Spawned) continue;

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
            if (!SmartAIMod.Settings.sleepingCourtesy || ActionGate.ObserveOnly || pawn == null || targetThing == null || pawn.Map == null)
                return false;

            if (pawn.Drafted) return false;
            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return false;
            if (!targetThing.Spawned || !targetThing.Position.IsValid) return false;

            Job curJob = pawn.CurJob;
            if (curJob != null)
            {
                JobDef d = curJob.def;
                if (d == JobDefOf.TendPatient || d == JobDefOf.ExtinguishSelf ||
                    d == JobDefOf.Rescue || d == JobDefOf.TakeToBedToOperate || d == JobDefOf.Flee || d == JobDefOf.BeatFire)
                    return false;
            }

            Room targetRoom = targetThing.Position.GetRoom(pawn.Map);
            return IsPrivateBedroomWithActiveSleeper(targetRoom, pawn);
        }
    }
}
