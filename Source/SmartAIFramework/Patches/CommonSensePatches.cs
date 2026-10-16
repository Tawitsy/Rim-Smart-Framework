using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Brain;
using SmartAIFramework.Core.Decisions;
using SmartAIFramework.Core.Knowledge;
using SmartAIFramework.Modules;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(Pawn_JobTracker), "DetermineNextJob")]
    public static class Patch_JobTracker_DetermineNextJob
    {
        public static void Postfix(Pawn ___pawn, ref ThinkResult __result)
        {
            if (___pawn == null) return;
            if (IntentArbiter.TryConsume(___pawn, __result.Job, out Job smartJob))
            {
                __result = new ThinkResult(smartJob, __result.SourceNode, __result.Tag, false);
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnThing))]
    public static class Patch_WorkGiver_Scanner_HasJobOnThing
    {
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            if (!forced && CognitiveBlacklist.IsSuppressed(t))
            {
                __result = false;
                return false;
            }

            if (!forced && Module_Social.ShouldAvoidWakingSleeper(pawn, t))
            {
                __result = false;
                return false;
            }

            if (!forced && Module_Delegation.ShouldYieldToCloserColonist(__instance, pawn, t))
            {
                __result = false;
                return false;
            }

            if (!forced && Module_Delegation.ShouldPrioritizeCurrentRoom(__instance, pawn, t))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_JobTracker_StartJob
    {
        private const int ReconsiderTicks = 120;

        public static void Prefix(Pawn ___pawn, ref Job newJob)
        {
            if (newJob == null || ___pawn == null) return;

            if (___pawn.Faction?.IsPlayer == true)
            {
                SmartAIFramework.Core.SmartAILog.Log("Colonist-Job", $"{___pawn.LabelShort} started {newJob.def.defName} at {newJob.targetA.Cell}");
            }

            if (SmartAIMod.Settings.selfPreservationMedicalRest && !Module_CommonSense.IsMedicalOrRestJob(newJob.def))
            {
                if (DeterministicPredictor.IsInImmediatePhysiologicalCrisis(___pawn, SmartAIMod.Settings.crisisHorizonTicks))
                {
                    if (!(___pawn.Drafted && newJob.playerForced))
                    {
                        if (Module_CommonSense.TryGetEmergencyLifePreservationJob(___pawn, out Job emergencyJob) &&
                            ActionGate.ApplyVeto(___pawn, assistLevel: false, $"Emergency life preservation override for {newJob.def.defName}"))
                        {
                            newJob = emergencyJob;
                            return;
                        }
                    }
                }
            }

            if (newJob.def == JobDefOf.BeatFire && !newJob.playerForced)
            {
                if (Module_CommonSense.IsSuperheatedFireHazard(___pawn, newJob.targetA.Cell, out string fireReason))
                {
                    if (!ActionGate.ApplyVeto(___pawn, assistLevel: false, $"Refused BeatFire in superheated room ({fireReason})"))
                        return;

                    if (newJob.targetA.Thing != null)
                    {
                        CognitiveBlacklist.Suppress(newJob.targetA.Thing, 600);
                    }

                    Messages.Message(
                        $"{___pawn.LabelShort} refused to enter superheated room to fight fire ({fireReason})!",
                        ___pawn,
                        MessageTypeDefOf.CautionInput
                    );

                    if (___pawn.Position.GetRoom(___pawn.Map)?.Temperature > 60f)
                    {
                        if (Module_Structure.TryFindNearestSanctuary(___pawn, out IntVec3 fireSafeCell))
                        {
                            newJob = JobMaker.MakeJob(JobDefOf.Goto, fireSafeCell);
                            newJob.expiryInterval = 120;
                            newJob.checkOverrideOnExpire = true;
                            return;
                        }
                    }

                    newJob = JobMaker.MakeJob(JobDefOf.Wait, 120);
                    return;
                }
            }

            if (newJob.def == JobDefOf.PredatorHunt && newJob.targetA.Thing is Pawn prey &&
                prey.Faction == Faction.OfPlayer && !prey.Drafted)
            {
                if (___pawn.Position.DistanceTo(prey.Position) <= 50f || GenSight.LineOfSight(___pawn.Position, prey.Position, ___pawn.Map, true))
                {
                    Module_Combat.Notify_PredatorHuntingColonist(___pawn, prey);
                }
            }

            if (!Module_CommonSense.ShouldRefuseJob(___pawn, newJob)) return;

            if (!ActionGate.ApplyVeto(___pawn, assistLevel: false, $"Refused {newJob.def.defName} in hazard zone"))
                return;

            if (newJob.targetA.Thing != null)
            {
                CognitiveBlacklist.Suppress(newJob.targetA.Thing, SmartAIMod.Settings.jobSuppressionDurationTicks);
            }

            if (Module_CommonSense.TryGetEmergencyLifePreservationJob(___pawn, out Job emergencyJob2))
            {
                newJob = emergencyJob2;
                return;
            }

            if (Module_Structure.TryFindNearestSanctuary(___pawn, out IntVec3 safeCell))
            {
                newJob = JobMaker.MakeJob(JobDefOf.Goto, safeCell);
                newJob.expiryInterval = ReconsiderTicks;
                newJob.checkOverrideOnExpire = true;
            }
            else
            {
                newJob = JobMaker.MakeJob(JobDefOf.Wait, ReconsiderTicks);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryOpportunisticJob))]
    public static class Patch_JobTracker_TryOpportunisticJob
    {
        public static void Postfix(Pawn ___pawn, Job finalizerJob, Job job, ref Job __result)
        {
            if (__result != null || finalizerJob != null || ___pawn == null || job == null) return;
            if (___pawn.jobs?.jobQueue != null && ___pawn.jobs.jobQueue.Count > 0) return;

            if (Module_CommonSense.TryGetPreTripPreparationJob(___pawn, job, out Job prepJob))
            {
                if (ActionGate.ApplyVeto(___pawn, assistLevel: false, $"Pre-trip preparation before {job.def.defName}"))
                {
                    __result = prepJob;
                    return;
                }
            }

            if (Module_CommonSense.TryGetOpportunisticHaulJob(___pawn, job, out Job haulJob))
            {
                if (ActionGate.ApplyVeto(___pawn, assistLevel: false, $"Opportunistic haul before {job.def.defName}"))
                {
                    __result = haulJob;
                }
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_WorkGiver_FightFires_JobOnThing
    {
        public static System.Reflection.MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.WorkGiver_FightFires");
            return AccessTools.Method(type, "JobOnThing");
        }

        public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (__result == null || forced || pawn == null || t == null || ActionGate.ObserveOnly) return;
            if (Module_CommonSense.IsSuperheatedFireHazard(pawn, t.Position, out _))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality))]
    public static class Patch_FoodUtility_FoodOptimality
    {
        public static void Postfix(Pawn eater, Thing foodSource, float dist, ref float __result)
        {
            if (ActionGate.ObserveOnly) return;
            __result += Module_CommonSense.FifoBonus(foodSource);

            if (dist > 45f && eater != null && !eater.Drafted)
            {
                __result -= (dist - 45f) * 2.5f;
            }
        }
    }

    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.TryFindChairOrSpot))]
    public static class Patch_Toils_Ingest_TryFindChairOrSpot
    {
        public static void Postfix(Pawn pawn, Thing ingestible, ref IntVec3 cell, ref bool __result)
        {
            if (!SmartAIMod.Settings.comfortSeating || ActionGate.ObserveOnly || pawn?.Map == null) return;

            bool sittingOnChair = false;
            if (cell.IsValid)
            {
                Building edifice = cell.GetEdifice(pawn.Map);
                sittingOnChair = edifice != null && edifice.def.building != null && edifice.def.building.isSittable;
            }

            if (!sittingOnChair)
            {
                Thing chair = null;
                Room currentRoom = pawn.Position.GetRoom(pawn.Map);

                if (currentRoom != null && currentRoom.ProperRoom && !currentRoom.PsychologicallyOutdoors)
                {
                    foreach (Building b in currentRoom.ContainedThings<Building>())
                    {
                        if (IsValidComfortChair(b, pawn))
                        {
                            chair = b;
                            break;
                        }
                    }
                }

                if (chair == null)
                {
                    chair = GenClosest.ClosestThingReachable(
                        pawn.Position, pawn.Map,
                        ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
                        PathEndMode.OnCell, TraverseParms.For(pawn), 12f,
                        t => IsValidComfortChair(t, pawn));
                }

                if (chair != null && Toils_Ingest.TryFindFreeSittingSpotOnThing(chair, pawn, out IntVec3 chairCell))
                {
                    cell = chairCell;
                    __result = true;
                }
            }
        }

        private static bool IsValidComfortChair(Thing t, Pawn pawn)
        {
            if (t == null || t.def?.building == null || !t.def.building.isSittable) return false;
            if (t.Faction != pawn.Faction && pawn.Faction != null && pawn.Faction.IsPlayer) return false;
            if (t.IsForbidden(pawn) || t.IsBurning() || t.HostileTo(pawn)) return false;
            if (pawn.IsColonist && t.Position.Fogged(t.Map)) return false;
            if (!pawn.CanReserve(t) || !t.IsSociallyProper(pawn)) return false;
            if (t.Position.GetDangerFor(pawn, t.Map) != Danger.None) return false;
            return Toils_Ingest.TryFindFreeSittingSpotOnThing(t, pawn, out _);
        }
    }

    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    public static class Patch_JobDriver_DoBill_MakeNewToils
    {
        public static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_DoBill __instance)
        {
            bool insertedCleanToil = false;

            foreach (Toil t in toils)
            {
                if (!insertedCleanToil && SmartAIMod.Settings.commonSenseSanitation && __instance != null &&
                    (t.debugName == "MakeUnfinishedThingIfNeeded" || t.debugName == "DoRecipeWork"))
                {
                    insertedCleanToil = true;
                    yield return CreateWorkbenchCleanToil(__instance);
                }

                yield return t;
            }
        }

        private static Toil CreateWorkbenchCleanToil(JobDriver_DoBill driver)
        {
            Toil cleanToil = ToilMaker.MakeToil("SmartSanitation");
            cleanToil.initAction = delegate
            {
                if (ActionGate.ObserveOnly) return;

                Pawn actor = driver.pawn;
                if (actor == null || actor.WorkTypeIsDisabled(WorkTypeDefOf.Cleaning)) return;

                Job curJob = driver.job;
                if (curJob == null) return;

                IntVec3 cell = curJob.targetA.Cell;
                Map map = actor.Map;
                if (map == null || !cell.InBounds(map)) return;

                // Ensure the pawn has physically arrived at the workbench before cleaning around it
                if (!actor.Position.InHorDistOf(cell, 3.5f)) return;

                Room room = cell.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors) return;

                int cleaned = 0;
                for (int i = 0; i < 9; i++)
                {
                    IntVec3 c = cell + GenAdj.AdjacentCellsAndInside[i];
                    if (!c.InBounds(map)) continue;
                    var thingList = c.GetThingList(map);
                    for (int j = thingList.Count - 1; j >= 0; j--)
                    {
                        if (thingList[j] is Filth filth && !filth.Destroyed && !filth.IsForbidden(actor))
                        {
                            filth.ThinFilth();
                            cleaned++;
                            if (cleaned >= 2) break;
                        }
                    }
                    if (cleaned >= 2) break;
                }

                if (cleaned > 0)
                {
                    SoundDefOf.Interact_CleanFilth?.PlayOneShot(new TargetInfo(cell, map));
                    FleckMaker.ThrowDustPuff(cell, map, 0.5f);
                }
            };
            cleanToil.defaultCompleteMode = ToilCompleteMode.Instant;
            return cleanToil;
        }
    }
}
