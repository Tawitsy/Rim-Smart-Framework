using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Compat;
using SmartAIFramework.Data;
using SmartAIFramework.Decisions;
using SmartAIFramework.Evaluation;
using SmartAIFramework.Grid;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Work
{
    public static class WorkUtility
    {
        private static readonly Dictionary<int, int> nextPreTripTick = new Dictionary<int, int>(32);
        private static readonly Dictionary<int, int> nextOppHaulTick = new Dictionary<int, int>(32);
        private const int PreTripCooldownTicks = 2500;
        private const int OppHaulCooldownTicks = 1200;
        private const int FailedSearchRetryTicks = 300;

        public static bool ShouldCancelHazardousJob(Pawn pawn, Job job)
        {
            if (!SmartAIMod.Settings.commonSenseHazardJobs || pawn == null || job == null || pawn.Map == null)
                return false;
            if (job.playerForced || pawn.Drafted || !pawn.IsColonist)
                return false;

            if (IsRoutineJobInThreatReach(pawn, job))
                return true;

            if (!IsRoutineWork(job.def))
                return false;

            LocalTargetInfo target = job.targetA;
            if (!target.IsValid)
                return false;

            return IsHazardousAt(pawn, target.Cell, target.Thing);
        }

        public static bool IsRoutineJobInThreatReach(Pawn pawn, Job job)
        {
            Thing target = job.targetA.Thing;
            LocalTargetInfo targetB = job.targetB;
            bool aUsable = target != null && target.Spawned && target.Map == pawn.Map;
            bool bUsable = targetB.IsValid && (!targetB.HasThing || (targetB.Thing.Spawned && targetB.Thing.Map == pawn.Map));
            IntVec3 cellA = job.targetA.Cell;
            bool aCell = job.targetA.IsValid && !job.targetA.HasThing && job.workGiverDef != null && !job.workGiverDef.emergency && cellA.InBounds(pawn.Map);
            if (!aUsable && !bUsable && !aCell)
                return false;

            ThreatMap threats = ThreatMap.TryGet(pawn.Map);
            if (threats == null || !threats.AnyActive || !ThreatMap.IsEligible(pawn))
                return false;

            if (job.def == JobDefOf.AttackMelee || job.def == JobDefOf.AttackStatic)
                return false;

            if (pawn.carryTracker?.CarriedThing != null)
                return false;
            if (Arbiter.ClassifyJob(pawn, job) != DecisionTier.Routine)
                return false;

            if (aUsable && threats.IsInReach(target.Position, pawn))
                return true;
            if (aCell && threats.IsInReach(cellA, pawn))
                return true;
            if (!bUsable)
                return false;
            IntVec3 cellB = targetB.HasThing ? targetB.Thing.Position : targetB.Cell;
            return cellB.InBounds(pawn.Map) && threats.IsInReach(cellB, pawn);
        }

        public static bool IsHazardousWorkTarget(Pawn pawn, Thing target, JobDef jobDefOrNull)
        {
            if (pawn == null || target == null || pawn.Map == null || !target.Spawned)
                return false;
            return IsHazardousAt(pawn, target.Position, target);
        }

        public static bool IsRainWashedFilth(Thing t)
        {
            if (!(t is Filth) || t.def.filth == null || !t.def.filth.rainWashes || !t.Spawned)
                return false;
            Map map = t.Map;
            if (map == null || t.Position.Roofed(map))
                return false;
            return map.weatherManager.RainRate > 0.1f;
        }

        private static bool IsHazardousAt(Pawn pawn, IntVec3 targetCell, Thing targetThing)
        {
            Map map = pawn.Map;
            if (!targetCell.InBounds(map))
                return false;

            if (SmartAIMod.Settings.environmentalHazardProtection && !targetCell.Roofed(map))
            {
                float envRisk = SurvivalUtility.GetEnvironmentalRisk(pawn, targetCell, map);
                if (envRisk >= SmartAIMod.Settings.hazardSensitivity)
                    return true;
            }

            float targetDanger = UtilityEvaluator.GetDangerAt(map, targetCell, pawn);
            if (targetDanger < SmartAIMod.Settings.hazardSensitivity)
                return false;

            float urgency = 20f;
            if (targetThing != null)
            {
                ThingProfile profile = ThingProfileCache.Get(targetThing.def);
                if (profile != null)
                {
                    if (profile.isPerishable)
                        urgency = Mathf.Max(urgency, SurvivalUtility.GetSpoilageUrgency(targetThing));
                    else if (profile.isMedicine)
                        urgency = 60f;
                }
            }

            var evalInput = new ActionEvalInput
            {
                urgency = urgency,
                selfCapability = UtilityEvaluator.GetSelfCapability(pawn),
                efficiency = UtilityEvaluator.GetDistanceEfficiency(pawn.Position, targetCell),
                danger = targetDanger,
                currentDanger = UtilityEvaluator.GetDangerAt(map, pawn.Position, pawn),
                negativeConsequence = 0f
            };

            return UtilityEvaluator.Evaluate(evalInput) < 0f;
        }

        public static bool IsSuperheatedFireHazard(Pawn pawn, IntVec3 firePos, out string reason)
        {
            reason = null;
            if (!SmartAIMod.Settings.superheatedFirefightingSafety || pawn?.Map == null)
                return false;

            Map map = pawn.Map;
            if (!firePos.InBounds(map) || map.roofGrid.RoofAt(firePos) == null)
                return false;

            Room fireRoom = firePos.GetRoom(map);
            if (fireRoom == null || fireRoom.PsychologicallyOutdoors)
                return false;

            float roomTemp = fireRoom.Temperature;
            if (roomTemp <= 60f)
                return false;

            float safeMax = pawn.SafeTemperatureRange().max;
            if (roomTemp <= safeMax)
                return false;

            Room pawnRoom = pawn.Position.GetRoom(map);
            if (pawnRoom != fireRoom)
            {
                reason = $"Superheated room ({roomTemp:F0} C) is lethal to enter";
                return true;
            }

            if (roomTemp > 80f)
            {
                reason = $"Room is inferno ({roomTemp:F0} C) - must evacuate";
                return true;
            }

            return false;
        }

        public static bool TryMakeSelfTriageJob(Pawn pawn, out Job triageJob)
        {
            triageJob = null;
            if (!SmartAIMod.Settings.selfPreservationMedicalRest || pawn?.Map == null || pawn.IsBurning())
                return false;
            if (!SurvivalUtility.IsInBleedOutCrisis(pawn, SmartAIMod.Settings.crisisHorizonTicks))
                return false;

            if (pawn.playerSettings != null && pawn.playerSettings.selfTend && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
            {
                if (pawn.health?.hediffSet?.HasTendableHediff() == true)
                {
                    triageJob = JobMaker.MakeJob(JobDefOf.TendPatient, pawn);
                    triageJob.expiryInterval = 400;
                    triageJob.checkOverrideOnExpire = true;
                    return true;
                }
            }

            Building_Bed bed = RestUtility.FindBedFor(pawn);
            if (bed != null)
            {
                triageJob = JobMaker.MakeJob(JobDefOf.LayDown, bed);
                triageJob.expiryInterval = 500;
                triageJob.checkOverrideOnExpire = true;
                return true;
            }

            return false;
        }

        public static bool IsMedicalOrRestJob(JobDef def)
        {
            if (def == null) return false;
            return def == JobDefOf.TendPatient || def == JobDefOf.LayDown ||
                   def == JobDefOf.LayDownAwake || def == JobDefOf.LayDownResting ||
                   def == JobDefOf.Rescue || def == JobDefOf.TakeToBedToOperate ||
                   IsSurvivalCriticalJob(def);
        }

        public static bool IsVitalNeedJob(JobDef def)
        {
            if (def == null) return false;
            return def == JobDefOf.LayDown || def == JobDefOf.LayDownAwake || def == JobDefOf.LayDownResting ||
                   def == JobDefOf.Ingest || def == JobDefOf.TendPatient || def == JobDefOf.Rescue ||
                   def == JobDefOf.TakeToBedToOperate || def == JobDefOf.FeedPatient || def == JobDefOf.BeatFire;
        }

        public static bool IsSurvivalCriticalJob(JobDef def)
        {
            if (def == null) return false;
            return def == JobDefOf.ExtinguishSelf || def == JobDefOf.Flee ||
                   def == JobDefOf.FleeAndCower || def == JobDefOf.GotoSafeTemperature;
        }

        private static bool IsRoutineWork(JobDef def)
        {
            return def == JobDefOf.HaulToCell || def == JobDefOf.HaulToContainer ||
                   def == JobDefOf.Clean || def == JobDefOf.Repair ||
                   def == JobDefOf.FinishFrame || def == JobDefOf.Mine;
        }

        public static float GetFoodFreshnessOffset(Thing food)
        {
            if (!SmartAIMod.Settings.commonSenseFifoFood || food == null)
                return 0f;

            CompRottable rot = food.TryGetComp<CompRottable>();
            if (rot == null) return 0f;

            return Mathf.Clamp01(rot.RotProgressPct) * 12f;
        }

        public static bool TryMakePreTripJob(Pawn pawn, Job travelJob, out Job prepJob)
        {
            prepJob = null;
            if (!SmartAIMod.Settings.preTripNeedsPreparation || pawn?.Map == null || travelJob == null)
                return false;
            if (!pawn.IsColonist || pawn.Drafted || travelJob.playerForced)
                return false;
            if (pawn.carryTracker?.CarriedThing != null)
                return false;

            int now = PawnStateTracker.Now;
            if (nextPreTripTick.TryGetValue(pawn.thingIDNumber, out int nextTick) && now < nextTick)
                return false;

            IntVec3 dest = travelJob.targetA.Cell;
            if (!dest.IsValid || pawn.Position.DistanceToSquared(dest) < 1225)
                return false;

            JobDef def = travelJob.def;
            if (def == JobDefOf.Ingest || def == JobDefOf.LayDown ||
                def == JobDefOf.LayDownAwake || def == JobDefOf.LayDownResting ||
                def == JobDefOf.TendPatient || def == JobDefOf.Flee ||
                def == JobDefOf.ExtinguishSelf || def == JobDefOf.Rescue ||
                def == JobDefOf.TakeToBedToOperate || def == JobDefOf.DoBill ||
                def == JobDefOf.FinishFrame || def == JobDefOf.Research || def == JobDefOf.Repair)
                return false;

            bool searched = false;
            if (pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < 0.40f)
            {
                searched = true;
                if (FoodUtility.TryFindBestFoodSourceFor(
                    pawn, pawn, desperate: false, out Thing foodSource, out ThingDef foodDef,
                    canRefillDispenser: false, canUseInventory: true, canUsePackAnimalInventory: false,
                    allowForbidden: false, allowCorpse: false, allowSociallyImproper: false,
                    allowHarvest: false, forceScanWholeMap: false))
                {
                    if (pawn.Position.InHorDistOf(foodSource.Position, 25f))
                    {
                        nextPreTripTick[pawn.thingIDNumber] = now + PreTripCooldownTicks;
                        prepJob = JobMaker.MakeJob(JobDefOf.Ingest, foodSource);
                        prepJob.count = FoodUtility.WillIngestStackCountOf(
                            pawn, foodDef, FoodUtility.GetNutrition(pawn, foodSource, foodDef));
                        return true;
                    }
                }
            }

            if (pawn.needs?.rest != null && pawn.needs.rest.CurLevelPercentage < 0.28f)
            {
                searched = true;
                Building_Bed bed = RestUtility.FindBedFor(pawn);
                if (bed != null && pawn.Position.InHorDistOf(bed.Position, 25f) && RestUtility.CanUseBedEver(pawn, bed.def))
                {
                    nextPreTripTick[pawn.thingIDNumber] = now + PreTripCooldownTicks;
                    prepJob = JobMaker.MakeJob(JobDefOf.LayDown, bed);
                    return true;
                }
            }

            if (searched)
                nextPreTripTick[pawn.thingIDNumber] = now + FailedSearchRetryTicks;
            return false;
        }

        public static bool TryMakeOpportunisticHaulJob(Pawn pawn, Job travelJob, out Job haulJob)
        {
            haulJob = null;
            if (CompatRegistry.YieldOpportunisticHauling)
                return false;
            if (!SmartAIMod.Settings.opportunisticHaulingOnRoute || pawn?.Map == null || travelJob == null)
                return false;
            if (!pawn.IsColonist || pawn.Drafted || travelJob.playerForced)
                return false;
            if (pawn.WorkTagIsDisabled(WorkTags.Hauling | WorkTags.ManualDumb))
                return false;
            if (ModsConfig.BiotechActive && pawn.IsWorkTypeDisabledByAge(WorkTypeDefOf.Hauling, out _))
                return false;
            if (pawn.carryTracker?.CarriedThing != null)
                return false;

            int now = PawnStateTracker.Now;
            if (nextOppHaulTick.TryGetValue(pawn.thingIDNumber, out int nextTick) && now < nextTick)
                return false;

            IntVec3 dest = travelJob.targetA.Cell;
            if (!dest.IsValid)
                return false;

            int directDistSq = pawn.Position.DistanceToSquared(dest);
            if (directDistSq < 256)
                return false;

            JobDef def = travelJob.def;
            if (def == JobDefOf.HaulToCell || def == JobDefOf.HaulToContainer ||
                def == JobDefOf.TendPatient || def == JobDefOf.ExtinguishSelf ||
                def == JobDefOf.Flee || def == JobDefOf.DoBill ||
                def == JobDefOf.FinishFrame || def == JobDefOf.Research || def == JobDefOf.Repair)
                return false;

            Map map = pawn.Map;
            IntVec3 pawnPos = pawn.Position;
            float directDist = Mathf.Sqrt(directDistSq);

            CellIndices indices = map.cellIndices;
            ThingGrid thingGrid = map.thingGrid;
            IntVec3[] pattern = GenRadial.RadialPattern;
            int searchCells = GenRadial.NumCellsInRadius(5.5f);
            int evaluatedItems = 0;

            for (int i = 0; i < searchCells && evaluatedItems < 10; i++)
            {
                IntVec3 c = pawnPos + pattern[i];
                if (!c.InBounds(map)) continue;

                List<Thing> things = thingGrid.ThingsListAtFast(indices.CellToIndex(c));
                for (int j = 0; j < things.Count && evaluatedItems < 10; j++)
                {
                    Thing item = things[j];
                    if (item == null || item.Destroyed || !item.def.EverHaulable || item.IsForbidden(pawn))
                        continue;

                    evaluatedItems++;

                    if (map.reservationManager.FirstRespectedReserver(item, pawn) != null)
                        continue;
                    if (!HaulAIUtility.PawnCanAutomaticallyHaulFast_NewTemp(pawn, item, forced: false))
                        continue;

                    StoragePriority curPriority = StoreUtility.CurrentStoragePriorityOf(item, false);
                    if (!StoreUtility.TryFindBestBetterStorageFor(item, pawn, map, curPriority, pawn.Faction, out IntVec3 foundCell, out IHaulDestination haulDest))
                        continue;

                    IntVec3 storeCell = (haulDest is ISlotGroupParent) ? foundCell : (haulDest as Thing)?.Position ?? IntVec3.Invalid;
                    if (!storeCell.IsValid) continue;

                    float detourDist = pawnPos.DistanceTo(item.Position) + item.Position.DistanceTo(storeCell) + storeCell.DistanceTo(dest);
                    float storeToDestDist = storeCell.DistanceTo(dest);

                    if (detourDist <= directDist * 1.35f && storeToDestDist <= directDist * 0.75f)
                    {
                        haulJob = HaulAIUtility.HaulToStorageJob(pawn, item, forced: false);
                        if (haulJob != null)
                        {
                            nextOppHaulTick[pawn.thingIDNumber] = now + OppHaulCooldownTicks;
                            return true;
                        }
                    }
                }
            }

            if (evaluatedItems > 0)
                nextOppHaulTick[pawn.thingIDNumber] = now + FailedSearchRetryTicks;
            return false;
        }

        public static void ClearPawn(int pawnId)
        {
            nextPreTripTick.Remove(pawnId);
            nextOppHaulTick.Remove(pawnId);
        }

        public static void ClearAll()
        {
            nextPreTripTick.Clear();
            nextOppHaulTick.Clear();
        }
    }
}
