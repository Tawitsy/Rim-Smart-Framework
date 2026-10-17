using HarmonyLib;
using RimWorld;
using SmartAIFramework.Combat;
using SmartAIFramework.Data;
using SmartAIFramework.Decisions;
using SmartAIFramework.Work;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(Pawn_JobTracker), "DetermineNextJob")]
    public static class Patch_JobTracker_DetermineNextJob
    {
        public static void Postfix(Pawn ___pawn, ref ThinkResult __result)
        {
            if (___pawn == null) return;
            JobDirector.ApplyAtDecision(___pawn, ref __result);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryOpportunisticJob))]
    public static class Patch_JobTracker_TryOpportunisticJob_SkipModJobs
    {
        public static bool Prefix(Pawn ___pawn, Job job, ref Job __result)
        {
            if (job == null || ___pawn == null)
                return true;
            if (!JobDirector.IsVanillaExplosionFlee(job))
            {
                PawnTacticalState state = PawnStateTracker.TryGet(___pawn);
                if (state == null || state.lastIssuedJobId != job.loadID)
                    return true;
            }
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_JobTracker_StartJob
    {
        private const int ReconsiderTicks = 120;

        public static bool Prefix(Pawn ___pawn, Job newJob)
        {
            if (newJob == null || ___pawn == null || !___pawn.Spawned)
                return true;

            if (SmartLog.IsEnabled && ___pawn.Faction == Faction.OfPlayer)
            {
                SmartLog.Message("Colonist-Job", $"{___pawn.LabelShort} starting {newJob.def.defName} at {newJob.targetA.Cell}");
            }

            if (newJob.def == JobDefOf.PredatorHunt &&
                newJob.targetA.Thing is Pawn prey &&
                prey.Faction == Faction.OfPlayer &&
                !prey.Drafted)
            {
                if (ModuleFlags.Combat &&
                    (___pawn.Position.InHorDistOf(prey.Position, 50f) ||
                     GenSight.LineOfSight(___pawn.Position, prey.Position, ___pawn.Map, skipFirstCell: true)))
                {
                    CombatUtility.NotifyPredatorHuntingColonist(___pawn, prey);
                }
                return true;
            }

            if (!PawnStateTracker.CanActAutonomously(___pawn) || newJob.playerForced)
                return true;

            if (ModuleFlags.Survival && newJob.def == JobDefOf.BeatFire)
            {
                if (WorkUtility.IsSuperheatedFireHazard(___pawn, newJob.targetA.Cell, out string fireReason) &&
                    PawnStateTracker.CanOverrideStartJob(___pawn))
                {
                    Messages.Message(
                        $"{___pawn.LabelShort} refused to enter superheated room to fight fire ({fireReason})!",
                        ___pawn,
                        MessageTypeDefOf.CautionInput);

                    if (___pawn.Position.GetRoom(___pawn.Map)?.Temperature > 60f &&
                        StructureUtility.TryFindNearestSafeRoom(___pawn, out IntVec3 fireSafeCell))
                    {
                        Arbiter.Submit(new Proposal
                        {
                            pawn = ___pawn,
                            behaviorId = BehaviorIds.EscapeHeat,
                            tier = DecisionTier.Survival,
                            score = 90f,
                            jobDef = JobDefOf.Goto,
                            target = fireSafeCell,
                            jobExpiryTicks = ReconsiderTicks,
                            actuation = Actuation.AtNextDecision,
                            validUntilTick = PawnStateTracker.Now + ReconsiderTicks,
                            commitTicks = 60,
                            reason = "standing in a superheated room"
                        });
                    }

                    if (JobDirector.VetoStartJob(___pawn, newJob, "superheated fire room", 600))
                        return false;
                }
            }

            if (!ModuleFlags.Work)
                return true;

            if (SmartAIMod.Settings.commonSenseHazardJobs && WorkUtility.IsRoutineJobInThreatReach(___pawn, newJob))
            {
                if (SmartLog.IsEnabled)
                    SmartLog.Decision("HazardJob", ___pawn, $"cancelled {newJob.def.defName} at {newJob.targetA.Cell} (shooter's reach)");
                return !JobDirector.VetoStartJob(___pawn, newJob, JobDirector.ThreatWaitReason, -1, suppressTarget: false);
            }

            if (!WorkUtility.ShouldCancelHazardousJob(___pawn, newJob))
                return true;
            if (!PawnStateTracker.CanOverrideStartJob(___pawn))
                return true;

            if (SmartLog.IsEnabled)
                SmartLog.Decision("HazardJob", ___pawn, $"cancelled {newJob.def.defName} at {newJob.targetA.Cell}");

            return !JobDirector.VetoStartJob(___pawn, newJob, "hazardous job in combat zone, fire or extreme temperature");
        }
    }
}
