using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Combat;
using SmartAIFramework.Data;
using SmartAIFramework.Evaluation;
using SmartAIFramework.Work;
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
            if (PawnStateTracker.TryTakeQueuedJob(___pawn, __result.Job, out Job replacementJob))
            {
                __result = new ThinkResult(replacementJob, __result.SourceNode, __result.Tag, fromQueue: false);
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnThing))]
    public static class Patch_WorkGiver_Scanner_HasJobOnThing
    {
        [HarmonyPriority(Priority.Low)]
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            return WorkGateFilter.Run(__instance, pawn, t, forced, ref __result);
        }
    }

    // Most WorkGivers override HasJobOnThing without calling base, so the base patch above never sees
    // them. These are the routine (non-emergency) overrides the delegation/suppression logic is meant for.
    [HarmonyPatch]
    public static class Patch_WorkGiver_Overrides_HasJobOnThing
    {
        private static readonly string[] TargetTypeNames =
        {
            "RimWorld.WorkGiver_CleanFilth",
            "RimWorld.WorkGiver_Repair",
            "RimWorld.WorkGiver_Deconstruct",
            "RimWorld.WorkGiver_ConstructDeliverResourcesToFrames",
            "RimWorld.WorkGiver_ConstructDeliverResourcesToBlueprints",
        };

        public static IEnumerable<MethodBase> TargetMethods()
        {
            for (int i = 0; i < TargetTypeNames.Length; i++)
            {
                System.Type type = AccessTools.TypeByName(TargetTypeNames[i]);
                MethodInfo method = type != null ? AccessTools.DeclaredMethod(type, nameof(WorkGiver_Scanner.HasJobOnThing)) : null;
                if (method != null)
                    yield return method;
            }
        }

        [HarmonyPriority(Priority.Low)]
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            return WorkGateFilter.Run(__instance, pawn, t, forced, ref __result);
        }
    }

    internal static class WorkGateFilter
    {
        public static bool Run(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            if (forced) return true;

            if (PawnStateTracker.IsTargetSuppressed(t))
            {
                __result = false;
                return false;
            }

            if (ColonistCareUtility.ShouldAvoidWakingSleeper(pawn, t))
            {
                __result = false;
                return false;
            }

            if (WorkDelegationUtility.ShouldYieldToCloserColonist(__instance, pawn, t))
            {
                if (SmartLog.IsEnabled)
                    SmartLog.Decision("Delegation", pawn, $"yielded {__instance.def?.defName} on {t.LabelShort} to a closer colonist");
                __result = false;
                return false;
            }

            if (WorkDelegationUtility.ShouldPrioritizeCurrentRoom(__instance, pawn, t))
            {
                if (SmartLog.IsEnabled)
                    SmartLog.Decision("Delegation", pawn, $"skipped {t.LabelShort} outside the current dirty room");
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
            if (newJob == null || ___pawn == null || !___pawn.Spawned)
                return;

            if (SmartLog.IsEnabled && ___pawn.Faction == Faction.OfPlayer)
            {
                SmartLog.Message("Colonist-Job", $"{___pawn.LabelShort} starting {newJob.def.defName} at {newJob.targetA.Cell}");
            }

            if (newJob.def == JobDefOf.PredatorHunt &&
                newJob.targetA.Thing is Pawn prey &&
                prey.Faction == Faction.OfPlayer &&
                !prey.Drafted)
            {
                if (___pawn.Position.InHorDistOf(prey.Position, 50f) ||
                    GenSight.LineOfSight(___pawn.Position, prey.Position, ___pawn.Map, skipFirstCell: true))
                {
                    CombatUtility.NotifyPredatorHuntingColonist(___pawn, prey);
                }
                return;
            }

            if (!PawnStateTracker.CanActAutonomously(___pawn) || newJob.playerForced)
                return;

            if (SmartAIMod.Settings.selfPreservationMedicalRest &&
                !WorkUtility.IsMedicalOrRestJob(newJob.def) &&
                SurvivalUtility.IsInBleedOutCrisis(___pawn, SmartAIMod.Settings.crisisHorizonTicks))
            {
                if (PawnStateTracker.CanOverrideStartJob(___pawn) &&
                    WorkUtility.TryMakeSelfTriageJob(___pawn, out Job triageJob))
                {
                    if (SmartLog.IsEnabled)
                        SmartLog.Decision("SelfTriage", ___pawn, $"replaced {newJob.def.defName} with {triageJob.def.defName} (bleed-out in {SurvivalUtility.TicksUntilBleedOut(___pawn)} ticks)");
                    newJob = triageJob;
                    return;
                }
            }

            if (newJob.def == JobDefOf.BeatFire)
            {
                if (WorkUtility.IsSuperheatedFireHazard(___pawn, newJob.targetA.Cell, out string fireReason) &&
                    PawnStateTracker.CanOverrideStartJob(___pawn))
                {
                    if (newJob.targetA.Thing != null)
                    {
                        PawnStateTracker.SuppressTarget(newJob.targetA.Thing, 600);
                    }

                    Messages.Message(
                        $"{___pawn.LabelShort} refused to enter superheated room to fight fire ({fireReason})!",
                        ___pawn,
                        MessageTypeDefOf.CautionInput);

                    if (___pawn.Position.GetRoom(___pawn.Map)?.Temperature > 60f &&
                        StructureUtility.TryFindNearestSafeRoom(___pawn, out IntVec3 fireSafeCell))
                    {
                        newJob = JobMaker.MakeJob(JobDefOf.Goto, fireSafeCell);
                        newJob.expiryInterval = ReconsiderTicks;
                        newJob.checkOverrideOnExpire = true;
                        return;
                    }

                    newJob = JobMaker.MakeJob(JobDefOf.Wait, ReconsiderTicks);
                    return;
                }
            }

            if (!WorkUtility.ShouldCancelHazardousJob(___pawn, newJob))
                return;

            if (!PawnStateTracker.CanOverrideStartJob(___pawn))
                return;

            if (SmartLog.IsEnabled)
                SmartLog.Decision("HazardJob", ___pawn, $"cancelled {newJob.def.defName} at {newJob.targetA.Cell}");

            if (newJob.targetA.Thing != null)
            {
                PawnStateTracker.SuppressTarget(newJob.targetA.Thing, SmartAIMod.Settings.jobSuppressionDurationTicks);
            }

            if (WorkUtility.TryMakeSelfTriageJob(___pawn, out Job emergencyJob))
            {
                newJob = emergencyJob;
                return;
            }

            if (StructureUtility.TryFindNearestSafeRoom(___pawn, out IntVec3 safeCell))
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
            if (__result != null || finalizerJob != null || ___pawn == null || job == null)
                return;
            if (!PawnStateTracker.CanActAutonomously(___pawn))
                return;
            if (___pawn.jobs?.jobQueue != null && ___pawn.jobs.jobQueue.Count > 0)
                return;

            if (WorkUtility.TryMakePreTripJob(___pawn, job, out Job prepJob))
            {
                __result = prepJob;
                return;
            }

            if (WorkUtility.TryMakeOpportunisticHaulJob(___pawn, job, out Job haulJob))
            {
                __result = haulJob;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_WorkGiver_FightFires_JobOnThing
    {
        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.WorkGiver_FightFires");
            return AccessTools.Method(type, "JobOnThing");
        }

        public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (__result == null || forced || pawn == null || t == null || PawnStateTracker.ObserveOnly)
                return;

            if (WorkUtility.IsSuperheatedFireHazard(pawn, t.Position, out _))
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
            if (PawnStateTracker.ObserveOnly) return;

            __result += WorkUtility.GetFoodFreshnessOffset(foodSource);

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
            if (!SmartAIMod.Settings.comfortSeating || PawnStateTracker.ObserveOnly || pawn?.Map == null)
                return;

            if (cell.IsValid)
            {
                Building edifice = cell.GetEdifice(pawn.Map);
                if (edifice?.def?.building != null && edifice.def.building.isSittable)
                    return;
            }

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
                    pawn.Position,
                    pawn.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
                    PathEndMode.OnCell,
                    TraverseParms.For(pawn),
                    12f,
                    t => IsValidComfortChair(t, pawn));
            }

            if (chair != null && Toils_Ingest.TryFindFreeSittingSpotOnThing(chair, pawn, out IntVec3 chairCell))
            {
                cell = chairCell;
                __result = true;
            }
        }

        private static bool IsValidComfortChair(Thing t, Pawn pawn)
        {
            if (t?.def?.building == null || !t.def.building.isSittable) return false;
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
        public static IEnumerable<Toil> Postfix(IEnumerable<Toil> __result, JobDriver_DoBill __instance)
        {
            bool insertedCleanToil = false;

            foreach (Toil t in __result)
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
            Toil cleanToil = ToilMaker.MakeToil("CleanImmediateWorkArea");
            cleanToil.initAction = delegate
            {
                if (PawnStateTracker.ObserveOnly) return;

                Pawn actor = driver.pawn;
                if (actor == null || actor.WorkTypeIsDisabled(WorkTypeDefOf.Cleaning)) return;

                Job curJob = driver.job;
                if (curJob == null) return;

                IntVec3 cell = curJob.targetA.Cell;
                Map map = actor.Map;
                if (map == null || !cell.InBounds(map)) return;
                if (!actor.Position.InHorDistOf(cell, 3.5f)) return;

                Room room = cell.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors) return;

                int cleaned = 0;
                for (int i = 0; i < 9; i++)
                {
                    IntVec3 c = cell + GenAdj.AdjacentCellsAndInside[i];
                    if (!c.InBounds(map)) continue;
                    List<Thing> thingList = c.GetThingList(map);
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
