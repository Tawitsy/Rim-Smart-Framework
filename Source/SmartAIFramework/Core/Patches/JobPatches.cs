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

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_JobTracker_StartJob
    {
        private const int ReconsiderTicks = 120;

        // Returning false skips the original StartJob: the pawn keeps its current job, or vanilla looks for
        // another one next tick. Nothing is substituted, so a vetoed job can never park the pawn.
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

                    // A pawn already standing in the hot room should get out; the escape is taken up at its next decision.
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

            // Not throttled: every veto suppresses its target, so vanilla cannot pick the same one again, and letting
            // the second pick through sent pawns into a shooter's reach (soak: 2 cases in 3 days).
            bool inThreatReach = SmartAIMod.Settings.commonSenseHazardJobs && WorkUtility.IsRoutineJobInThreatReach(___pawn, newJob);
            if (!inThreatReach)
            {
                if (!WorkUtility.ShouldCancelHazardousJob(___pawn, newJob))
                    return true;
                if (!PawnStateTracker.CanOverrideStartJob(___pawn))
                    return true;
            }

            if (SmartLog.IsEnabled)
                SmartLog.Decision("HazardJob", ___pawn, $"cancelled {newJob.def.defName} at {newJob.targetA.Cell}");

            return !JobDirector.VetoStartJob(___pawn, newJob, "hazardous job in combat zone, fire or extreme temperature");
        }
    }
}
