using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Combat;
using SmartAIFramework.Compat;
using SmartAIFramework.Data;
using SmartAIFramework.Decisions;
using SmartAIFramework.Evaluation;
using SmartAIFramework.Grid;
using SmartAIFramework.Work;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace SmartAIFramework.Patches
{
    [HarmonyPatchCategory("Work")]
    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnThing))]
    public static class Patch_WorkGiver_Scanner_HasJobOnThing
    {
        [HarmonyPriority(Priority.Low)]
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            return WorkGateFilter.Run(__instance, pawn, t, forced, ref __result);
        }
    }

    [HarmonyPatchCategory("Work")]
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

    [HarmonyPatchCategory("Work")]
    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnCell))]
    public static class Patch_WorkGiver_Scanner_HasJobOnCell
    {
        [HarmonyPriority(Priority.Low)]
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            return WorkGateFilter.RunCell(__instance, pawn, c, forced, ref __result);
        }
    }

    [HarmonyPatchCategory("Work")]
    [HarmonyPatch]
    public static class Patch_WorkGiver_Overrides_HasJobOnCell
    {
        private static readonly string[] TargetTypeNames =
        {
            "RimWorld.WorkGiver_BuildRoof",
            "RimWorld.WorkGiver_ClearPollution",
            "RimWorld.WorkGiver_ClearSnowOrSand",
            "RimWorld.WorkGiver_ConstructAffectFloor",
            "RimWorld.WorkGiver_ConstructRemoveFloor",
            "RimWorld.WorkGiver_ConstructRemoveFoundation",
            "RimWorld.WorkGiver_ConstructSmoothWall",
            "RimWorld.WorkGiver_Fish",
            "RimWorld.WorkGiver_GrowerHarvest",
            "RimWorld.WorkGiver_PaintFloor",
            "RimWorld.WorkGiver_RemovePaintFloor",
            "RimWorld.WorkGiver_RemoveRoof",
        };

        public static IEnumerable<MethodBase> TargetMethods()
        {
            for (int i = 0; i < TargetTypeNames.Length; i++)
            {
                System.Type type = AccessTools.TypeByName(TargetTypeNames[i]);
                MethodInfo method = type != null ? AccessTools.DeclaredMethod(type, nameof(WorkGiver_Scanner.HasJobOnCell)) : null;
                if (method != null)
                    yield return method;
            }
        }

        [HarmonyPriority(Priority.Low)]
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            return WorkGateFilter.RunCell(__instance, pawn, c, forced, ref __result);
        }
    }

    internal static class WorkGateFilter
    {
        public static bool RunCell(WorkGiver_Scanner scanner, Pawn pawn, IntVec3 cell, bool forced, ref bool __result)
        {
            if (forced) return true;

            Map map = pawn.Map;
            ThreatMap threats = ThreatMap.TryGet(map);
            if (threats == null || !threats.AnyActive) return true;

            if (!SmartAIMod.Settings.commonSenseHazardJobs || PawnStateTracker.ObserveOnly) return true;

            WorkGiverDef def = scanner.def;
            if (def == null || def.emergency) return true;
            if (!ThreatMap.IsEligible(pawn) || !cell.InBounds(map)) return true;

            Region region = cell.GetRegion(map);
            bool inReach = region != null ? threats.RegionInReach(region) : threats.IsInReach(cell, pawn);
            if (!inReach) return true;

            if (SmartLog.IsEnabled)
                SmartLog.Decision("HazardFilter", pawn, $"refused {def.defName} on cell {cell}: inside a shooter's reach");
            __result = false;
            return false;
        }

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

            if (!PawnStateTracker.ObserveOnly && SmartAIMod.Settings.commonSenseHazardJobs && __instance.def != null && __instance.def.workType == WorkTypeDefOf.Cleaning &&
                WorkUtility.IsRainWashedFilth(t))
            {
                if (SmartLog.IsEnabled)
                    SmartLog.Decision("RainWash", pawn, $"skipped {t.LabelShort}: rain is washing it away");
                __result = false;
                return false;
            }

            if (ShouldRefuseHazardousWork(__instance, pawn, t))
            {
                if (SmartLog.IsEnabled)
                    SmartLog.Decision("HazardFilter", pawn, $"refused {__instance.def?.defName} on {t.LabelShort}: hazardous right now");
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

        private static bool ShouldRefuseHazardousWork(WorkGiver_Scanner scanner, Pawn pawn, Thing t)
        {
            if (!SmartAIMod.Settings.commonSenseHazardJobs)
                return false;

            WorkGiverDef def = scanner.def;
            if (def == null || def.emergency || t is Pawn)
                return false;

            Map map = pawn.Map;
            if (map == null)
                return false;

            ThreatMap threats = ThreatMap.TryGet(map);
            if (threats != null && threats.AnyActive && t.Spawned && ThreatMap.IsEligible(pawn) && threats.IsInReach(t.Position, pawn))
                return true;

            WorkTypeDef workType = def.workType;
            if (workType != WorkTypeDefOf.Hauling && workType != WorkTypeDefOf.Cleaning &&
                workType != WorkTypeDefOf.Construction && workType != WorkTypeDefOf.Mining)
                return false;

            bool combat = ColonyAlert.Get(map) == AlertLevel.Combat;
            if (!combat && !(SmartAIMod.Settings.environmentalHazardProtection && ColonyAlert.HasEnvironmentalHazard(map)))
                return false;

            if (!pawn.IsColonist || !PawnStateTracker.CanActAutonomously(pawn))
                return false;

            return WorkUtility.IsHazardousWorkTarget(pawn, t, null);
        }
    }

    [HarmonyPatchCategory("Work")]
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryOpportunisticJob))]
    public static class Patch_JobTracker_TryOpportunisticJob
    {
        public static void Postfix(Pawn ___pawn, Job finalizerJob, Job job, ref Job __result)
        {
            if (__result != null || finalizerJob != null || ___pawn == null || job == null)
                return;
            if (job.def == null || !job.def.allowOpportunisticPrefix || job.playerForced || ___pawn.Downed)
                return;
            if (!PawnStateTracker.CanActAutonomously(___pawn))
                return;
            PawnTacticalState issuer = PawnStateTracker.TryGet(___pawn);
            if (issuer != null && issuer.lastIssuedJobId == job.loadID)
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

    [HarmonyPatchCategory("Work")]
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

    [HarmonyPatchCategory("Work")]
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

    [HarmonyPatchCategory("Work")]
    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    public static class Patch_JobDriver_DoBill_MakeNewToils
    {
        private const int MinCleanTicks = 30;
        private const int MaxCleanTicks = 600;
        private const int MaxFilthPerPass = 2;

        private static RoomRoleDef kitchenRole;
        private static bool kitchenResolved;

        public static IEnumerable<Toil> Postfix(IEnumerable<Toil> __result, JobDriver_DoBill __instance)
        {
            bool insertedCleanToil = false;

            foreach (Toil t in __result)
            {
                if (!insertedCleanToil && SmartAIMod.Settings.commonSenseSanitation && __instance != null &&
                    !CommonSenseCompat.CleansBeforeWork &&
                    (t.debugName == "MakeUnfinishedThingIfNeeded" || t.debugName == "DoRecipeWork"))
                {
                    insertedCleanToil = true;
                    var picked = new List<Filth>(MaxFilthPerPass);
                    yield return CreateCleanPassToil(__instance, picked);
                    yield return CreateCleanApplyToil(__instance, picked);
                }

                yield return t;
            }
        }

        private static Toil CreateCleanPassToil(JobDriver_DoBill driver, List<Filth> picked)
        {
            Toil pass = ToilMaker.MakeToil("CleanImmediateWorkArea");
            pass.defaultCompleteMode = ToilCompleteMode.Delay;
            pass.defaultDuration = MinCleanTicks;
            pass.initAction = delegate
            {
                picked.Clear();
                int duration = TryPickFilth(driver, picked);
                if (duration <= 0)
                {
                    driver.ReadyForNextToil();
                    return;
                }

                pass.defaultDuration = duration;
                driver.ticksLeftThisToil = duration;
                driver.pawn.pather.StopDead();
            };
            pass.tickAction = delegate
            {
                driver.pawn.rotationTracker.FaceTarget(driver.job.targetA);
            };
            pass.WithProgressBarToilDelay(TargetIndex.A);
            return pass;
        }

        private static Toil CreateCleanApplyToil(JobDriver_DoBill driver, List<Filth> picked)
        {
            Toil apply = ToilMaker.MakeToil("CleanImmediateWorkAreaDone");
            apply.defaultCompleteMode = ToilCompleteMode.Instant;
            apply.initAction = delegate
            {
                if (picked.Count == 0) return;

                Pawn actor = driver.pawn;
                Map map = actor.Map;
                IntVec3 cell = driver.job.targetA.Cell;
                bool cleanedAny = false;
                for (int i = 0; i < picked.Count; i++)
                {
                    Filth filth = picked[i];
                    if (filth != null && !filth.Destroyed && filth.Spawned)
                    {
                        filth.Destroy();
                        cleanedAny = true;
                    }
                }
                picked.Clear();

                if (cleanedAny && map != null)
                {
                    SoundDefOf.Interact_CleanFilth?.PlayOneShot(new TargetInfo(cell, map));
                    FleckMaker.ThrowDustPuff(cell, map, 0.5f);
                }
            };
            return apply;
        }

        private static int TryPickFilth(JobDriver_DoBill driver, List<Filth> picked)
        {
            if (PawnStateTracker.ObserveOnly) return 0;

            Pawn actor = driver.pawn;
            Job job = driver.job;
            if (actor == null || job == null) return 0;
            if (actor.WorkTypeIsDisabled(WorkTypeDefOf.Cleaning) ||
                actor.workSettings == null || !actor.workSettings.WorkIsActive(WorkTypeDefOf.Cleaning))
                return 0;

            IntVec3 cell = job.targetA.Cell;
            Map map = actor.Map;
            if (map == null || !cell.InBounds(map)) return 0;
            if (!actor.Position.InHorDistOf(cell, 3.5f)) return 0;

            bool surgery = job.targetA.Thing is Pawn || job.bill?.recipe?.IsSurgery == true;
            if (!surgery)
            {
                Room room = cell.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors) return 0;
                RoomRoleDef role = room.Role;
                if (role != RoomRoleDefOf.Hospital && role != KitchenRole) return 0;
            }

            float work = 0f;
            for (int i = 0; i < 9 && picked.Count < MaxFilthPerPass; i++)
            {
                IntVec3 c = cell + GenAdj.AdjacentCellsAndInside[i];
                if (!c.InBounds(map)) continue;
                List<Thing> thingList = c.GetThingList(map);
                for (int j = thingList.Count - 1; j >= 0 && picked.Count < MaxFilthPerPass; j--)
                {
                    if (thingList[j] is Filth filth && !filth.Destroyed && !filth.IsForbidden(actor))
                    {
                        picked.Add(filth);
                        work += filth.def.filth.cleaningWorkToReduceThickness * filth.thickness;
                    }
                }
            }

            if (picked.Count == 0) return 0;

            float speed = actor.GetStatValue(StatDefOf.CleaningSpeed);
            if (speed < 0.1f) speed = 0.1f;
            int ticks = (int)(work / speed);
            if (ticks < MinCleanTicks) ticks = MinCleanTicks;
            else if (ticks > MaxCleanTicks) ticks = MaxCleanTicks;
            return ticks;
        }

        private static RoomRoleDef KitchenRole
        {
            get
            {
                if (!kitchenResolved)
                {
                    kitchenResolved = true;
                    kitchenRole = DefDatabase<RoomRoleDef>.GetNamedSilentFail("Kitchen");
                }
                return kitchenRole;
            }
        }
    }
}
