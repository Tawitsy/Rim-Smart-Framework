using RimWorld;
using SmartAIFramework.Brain;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Modules
{
    public static class Module_CommonSense
    {
        private const float HazardRefuseThreshold = 25f;

        public static bool ShouldRefuseJob(Pawn pawn, Job job)
        {
            if (!SmartAIMod.Settings.commonSenseHazardJobs) return false;
            if (pawn == null || job == null || pawn.Map == null) return false;
            if (job.playerForced || pawn.Drafted) return false;
            if (!pawn.IsColonist) return false;

            int horizon = SmartAIMod.Settings.crisisHorizonTicks;
            if (DeterministicPredictor.IsInImmediatePhysiologicalCrisis(pawn, horizon) && !IsMedicalOrRestJob(job.def))
            {
                return true;
            }

            if (!IsRoutineWork(job.def)) return false;

            LocalTargetInfo t = job.targetA;
            if (!t.IsValid) return false;

            float jobUrgency = 20f;
            Thing targetThing = t.Thing;
            if (targetThing != null)
            {
                var affordance = SemanticAffordanceEngine.Analyze(targetThing);
                if (affordance != null)
                {
                    if (affordance.isPerishable)
                    {
                        jobUrgency = Mathf.Max(jobUrgency, DeterministicPredictor.PredictSpoilageUrgency(targetThing));
                    }
                    else if (affordance.isMedicine)
                    {
                        jobUrgency = 60f;
                    }
                }
            }

            var input = new UtilityInput
            {
                urgency = jobUrgency,
                selfCapability = CognitiveBrain.SelfCapability(pawn),
                efficiency = CognitiveBrain.Efficiency(pawn.Position, t.Cell),
                danger = CognitiveBrain.DangerAt(pawn.Map, t.Cell, pawn),
                negativeConsequence = 0f,
            };

            if (SmartAIMod.Settings.environmentalHazardProtection && !t.Cell.Roofed(pawn.Map))
            {
                float envHazard = DeterministicPredictor.PredictEnvironmentalHazard(pawn, t.Cell, pawn.Map, job.def);
                if (envHazard >= SmartAIMod.Settings.hazardSensitivity) return true;
            }

            return input.danger >= SmartAIMod.Settings.hazardSensitivity && CognitiveBrain.Evaluate(input) < 0f;
        }

        private static readonly System.Collections.Generic.Dictionary<int, int> lastSanitationTicks =
            new System.Collections.Generic.Dictionary<int, int>();

        public static bool TryGetSanitationJob(Pawn pawn, Job workJob, out Job cleanJob)
        {
            cleanJob = null;
            if (!SmartAIMod.Settings.commonSenseSanitation || pawn?.Map == null) return false;
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Cleaning)) return false;
            if (workJob == null || !IsSterileSensitiveJob(workJob)) return false;

            if (lastSanitationTicks.TryGetValue(pawn.thingIDNumber, out int lastTick))
            {
                if (Find.TickManager.TicksGame - lastTick < 1500)
                    return false;
            }

            IntVec3 targetCell = workJob.targetA.Cell;
            Map map = pawn.Map;
            Room room = targetCell.GetRoom(map);

            System.Collections.Generic.List<Thing> filthList = null;

            if (room != null && room.ProperRoom && !room.PsychologicallyOutdoors)
            {
                float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
                bool isHospital = room.Role == RoomRoleDefOf.Hospital;

                if (cleanliness < 0f || isHospital)
                {
                    foreach (Filth f in room.ContainedThings<Filth>())
                    {
                        if (f != null && !f.Destroyed && !f.IsForbidden(pawn) && f.Position.DistanceTo(targetCell) <= 3.8f && pawn.CanReserveAndReach(f, PathEndMode.Touch, Danger.Some))
                        {
                            if (filthList == null) filthList = new System.Collections.Generic.List<Thing>();
                            filthList.Add(f);
                            if (filthList.Count >= 3) break;
                        }
                    }

                    filthList?.Sort((a, b) => a.Position.DistanceToSquared(targetCell).CompareTo(b.Position.DistanceToSquared(targetCell)));
                }
            }
            else
            {
                for (int i = 0; i < 8; i++)
                {
                    IntVec3 c = targetCell + GenAdj.AdjacentCells[i];
                    if (!c.InBounds(map)) continue;
                    var things = c.GetThingList(map);
                    for (int j = 0; j < things.Count; j++)
                    {
                        if (things[j] is Filth f && !f.Destroyed && !f.IsForbidden(pawn) && pawn.CanReserveAndReach(f, PathEndMode.Touch, Danger.Some))
                        {
                            if (filthList == null) filthList = new System.Collections.Generic.List<Thing>();
                            filthList.Add(f);
                        }
                    }
                }
            }

            if (filthList != null && filthList.Count > 0)
            {
                lastSanitationTicks[pawn.thingIDNumber] = Find.TickManager.TicksGame;
                cleanJob = JobMaker.MakeJob(JobDefOf.Clean, filthList[0]);
                for (int i = 1; i < filthList.Count; i++)
                {
                    cleanJob.AddQueuedTarget(TargetIndex.A, filthList[i]);
                }
                SmartAIFramework.Core.SmartAILog.Log("SmartAI-Sanitation", $"{pawn.LabelShort} cleaning {filthList.Count} filth in room before {workJob.def.defName}");
                return true;
            }

            return false;
        }

        private static bool IsSterileSensitiveJob(Job job)
        {
            if (job == null) return false;
            if (job.def == JobDefOf.TendPatient) return true;
            if (job.def == JobDefOf.DoBill)
            {
                Thing t = job.targetA.Thing;
                if (t is Building_WorkTable || t is Building_Bed)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsSuperheatedFireHazard(Pawn pawn, IntVec3 firePos, out string reason)
        {
            reason = null;
            if (!SmartAIMod.Settings.superheatedFirefightingSafety || pawn?.Map == null) return false;

            Map map = pawn.Map;
            Room fireRoom = firePos.GetRoom(map);
            if (fireRoom == null || fireRoom.PsychologicallyOutdoors) return false;

            float roomTemp = fireRoom.Temperature;
            float safeMax = pawn.SafeTemperatureRange().max;

            if (roomTemp > 60f && roomTemp > safeMax)
            {
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
            }

            return false;
        }

        public static bool TryGetEmergencyLifePreservationJob(Pawn pawn, out Job emergencyJob)
        {
            emergencyJob = null;
            if (!SmartAIMod.Settings.selfPreservationMedicalRest || pawn?.Map == null) return false;
            if (!DeterministicPredictor.IsInImmediatePhysiologicalCrisis(pawn, SmartAIMod.Settings.crisisHorizonTicks))
                return false;

            if (pawn.playerSettings != null && pawn.playerSettings.selfTend && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
            {
                if (pawn.health?.hediffSet?.HasTendableHediff() == true)
                {
                    emergencyJob = JobMaker.MakeJob(JobDefOf.TendPatient, pawn);
                    emergencyJob.expiryInterval = 400;
                    emergencyJob.checkOverrideOnExpire = true;
                    return true;
                }
            }

            Building_Bed bed = RestUtility.FindBedFor(pawn);
            if (bed != null)
            {
                emergencyJob = JobMaker.MakeJob(JobDefOf.LayDown, bed);
                emergencyJob.expiryInterval = 500;
                emergencyJob.checkOverrideOnExpire = true;
                return true;
            }

            emergencyJob = JobMaker.MakeJob(JobDefOf.LayDown, pawn.Position);
            emergencyJob.expiryInterval = 300;
            emergencyJob.checkOverrideOnExpire = true;
            return true;
        }

        public static bool IsMedicalOrRestJob(JobDef def)
        {
            if (def == null) return false;
            return def == JobDefOf.TendPatient || def == JobDefOf.LayDown ||
                   def == JobDefOf.LayDownAwake || def == JobDefOf.LayDownResting ||
                   def == JobDefOf.GotoSafeTemperature || def == JobDefOf.Flee || def == JobDefOf.FleeAndCower;
        }

        private static bool IsRoutineWork(JobDef def)
        {
            return def == JobDefOf.HaulToCell || def == JobDefOf.HaulToContainer
                || def == JobDefOf.Clean || def == JobDefOf.Repair
                || def == JobDefOf.FinishFrame || def == JobDefOf.Mine;
        }

        public static float FifoBonus(Thing food)
        {
            if (!SmartAIMod.Settings.commonSenseFifoFood || food == null) return 0f;
            var rot = food.TryGetComp<CompRottable>();
            if (rot == null) return 0f;

            return Mathf.Clamp01(rot.RotProgressPct) * 12f;
        }

        private static readonly System.Collections.Generic.Dictionary<int, int> lastPreTripTicks =
            new System.Collections.Generic.Dictionary<int, int>();

        public static bool TryGetPreTripPreparationJob(Pawn pawn, Job job, out Job prepJob)
        {
            prepJob = null;
            if (!SmartAIMod.Settings.preTripNeedsPreparation || pawn?.Map == null) return false;
            if (!pawn.IsColonist || pawn.Drafted || job.playerForced) return false;
            if (pawn.carryTracker?.CarriedThing != null) return false;

            if (lastPreTripTicks.TryGetValue(pawn.thingIDNumber, out int lastTick))
            {
                if (Find.TickManager.TicksGame - lastTick < 2500)
                    return false;
            }

            IntVec3 dest = job.targetA.Cell;
            if (!dest.IsValid || pawn.Position.DistanceTo(dest) < 35f) return false;

            if (job.def == JobDefOf.Ingest || job.def == JobDefOf.LayDown ||
                job.def == JobDefOf.LayDownAwake || job.def == JobDefOf.LayDownResting ||
                job.def == JobDefOf.TendPatient || job.def == JobDefOf.Flee ||
                job.def == JobDefOf.ExtinguishSelf || job.def == JobDefOf.Rescue ||
                job.def == JobDefOf.DoBill || job.def == JobDefOf.FinishFrame ||
                job.def == JobDefOf.Research || job.def == JobDefOf.Repair)
                return false;

            if (pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < 0.40f)
            {
                if (FoodUtility.TryFindBestFoodSourceFor(pawn, pawn, desperate: false, out Thing foodSource, out ThingDef foodDef, canRefillDispenser: false, canUseInventory: true, canUsePackAnimalInventory: false, allowForbidden: false, allowCorpse: false, allowSociallyImproper: false, allowHarvest: false, forceScanWholeMap: false))
                {
                    if (pawn.Position.DistanceTo(foodSource.Position) <= 25f)
                    {
                        lastPreTripTicks[pawn.thingIDNumber] = Find.TickManager.TicksGame;
                        prepJob = JobMaker.MakeJob(JobDefOf.Ingest, foodSource);
                        prepJob.count = FoodUtility.WillIngestStackCountOf(pawn, foodDef, FoodUtility.GetNutrition(pawn, foodSource, foodDef));
                        SmartAIFramework.Core.SmartAILog.Log("SmartAI-Trip", $"{pawn.LabelShort} eating before distant {job.def.defName} ({pawn.Position.DistanceTo(dest):F0} cells away)");
                        return true;
                    }
                }
            }

            if (pawn.needs?.rest != null && pawn.needs.rest.CurLevelPercentage < 0.28f)
            {
                Building_Bed bed = RestUtility.FindBedFor(pawn);
                if (bed != null && pawn.Position.DistanceTo(bed.Position) <= 25f && RestUtility.CanUseBedEver(pawn, bed.def))
                {
                    lastPreTripTicks[pawn.thingIDNumber] = Find.TickManager.TicksGame;
                    prepJob = JobMaker.MakeJob(JobDefOf.LayDown, bed);
                    SmartAIFramework.Core.SmartAILog.Log("SmartAI-Trip", $"{pawn.LabelShort} resting before distant {job.def.defName} ({pawn.Position.DistanceTo(dest):F0} cells away)");
                    return true;
                }
            }

            return false;
        }

        private static readonly System.Collections.Generic.Dictionary<int, int> lastOpportunisticHaulTicks =
            new System.Collections.Generic.Dictionary<int, int>();

        public static bool TryGetOpportunisticHaulJob(Pawn pawn, Job travelJob, out Job haulJob)
        {
            haulJob = null;
            if (!SmartAIMod.Settings.opportunisticHaulingOnRoute || pawn?.Map == null) return false;
            if (!pawn.IsColonist || pawn.Drafted || travelJob.playerForced) return false;
            if (pawn.WorkTagIsDisabled(WorkTags.Hauling | WorkTags.ManualDumb)) return false;
            if (ModsConfig.BiotechActive && pawn.IsWorkTypeDisabledByAge(WorkTypeDefOf.Hauling, out _)) return false;
            if (pawn.carryTracker?.CarriedThing != null) return false;

            if (lastOpportunisticHaulTicks.TryGetValue(pawn.thingIDNumber, out int lastTick) &&
                Find.TickManager.TicksGame - lastTick < 1200)
            {
                return false;
            }

            IntVec3 dest = travelJob.targetA.Cell;
            if (!dest.IsValid) return false;

            float directDist = pawn.Position.DistanceTo(dest);
            if (directDist < 16f) return false;

            if (travelJob.def == JobDefOf.HaulToCell || travelJob.def == JobDefOf.HaulToContainer ||
                travelJob.def == JobDefOf.TendPatient || travelJob.def == JobDefOf.ExtinguishSelf ||
                travelJob.def == JobDefOf.Flee || travelJob.def == JobDefOf.DoBill ||
                travelJob.def == JobDefOf.FinishFrame || travelJob.def == JobDefOf.Research ||
                travelJob.def == JobDefOf.Repair)
                return false;

            Room currentRoom = pawn.Position.GetRoom(pawn.Map);
            System.Collections.Generic.IEnumerable<Thing> candidateThings = null;

            if (currentRoom != null && currentRoom.ProperRoom && !currentRoom.PsychologicallyOutdoors)
            {
                candidateThings = currentRoom.ContainedThings<Thing>();
            }
            else
            {
                candidateThings = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 6f, false);
            }

            if (candidateThings == null) return false;

            Map map = pawn.Map;
            foreach (Thing item in candidateThings)
            {
                if (item == null || item.Destroyed || item.IsForbidden(pawn) || !item.def.EverHaulable) continue;
                if (map.reservationManager.FirstRespectedReserver(item, pawn) != null) continue;
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast_NewTemp(pawn, item, forced: false)) continue;

                StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(item, false);
                if (!StoreUtility.TryFindBestBetterStorageFor(item, pawn, map, currentPriority, pawn.Faction, out IntVec3 foundCell, out IHaulDestination haulDestination))
                    continue;

                IntVec3 storeCell = (haulDestination is ISlotGroupParent) ? foundCell : (haulDestination as Thing)?.Position ?? IntVec3.Invalid;
                if (!storeCell.IsValid) continue;

                float detourDist = pawn.Position.DistanceTo(item.Position) + item.Position.DistanceTo(storeCell) + storeCell.DistanceTo(dest);
                float storeToDestDist = storeCell.DistanceTo(dest);

                if (detourDist <= directDist * 1.35f && storeToDestDist <= directDist * 0.75f)
                {
                    haulJob = HaulAIUtility.HaulToStorageJob(pawn, item, forced: false);
                    if (haulJob != null)
                    {
                        lastOpportunisticHaulTicks[pawn.thingIDNumber] = Find.TickManager.TicksGame;
                        SmartAIFramework.Core.SmartAILog.Log("SmartAI-Haul", $"{pawn.LabelShort} taking opportunistic haul of {item.LabelShort} on route to {dest}");
                        return true;
                    }
                }
            }

            return false;
        }

        public static void ClearPawn(int pawnId)
        {
            lastSanitationTicks.Remove(pawnId);
            lastPreTripTicks.Remove(pawnId);
            lastOpportunisticHaulTicks.Remove(pawnId);
        }

        public static void ClearAll()
        {
            lastSanitationTicks.Clear();
            lastPreTripTicks.Clear();
            lastOpportunisticHaulTicks.Clear();
        }
    }
}
