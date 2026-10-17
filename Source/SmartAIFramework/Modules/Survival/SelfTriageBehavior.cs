using SmartAIFramework.Data;
using SmartAIFramework.Decisions;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Work
{
    public static class SelfTriageBehavior
    {
        private const float Score = 90f;
        private const int CommitTicks = 90;
        private const int ValidTicks = 120;

        public static void Register()
        {
            Arbiter.RegisterDecisionPointBehavior(BehaviorIds.SelfTriage, ProposeAtDecision);
        }

        private static Proposal ProposeAtDecision(Pawn pawn, Job vanillaChoice)
        {
            if (WorkUtility.IsMedicalOrRestJob(vanillaChoice?.def))
                return null;

            if (!WorkUtility.TryMakeSelfTriageJob(pawn, out Job triageJob))
                return null;

            return FromJob(pawn, triageJob, Actuation.AtNextDecision, "bleeding out soon: tend or rest");
        }

        public static void SubmitInterrupt(Pawn pawn)
        {
            if (!WorkUtility.TryMakeSelfTriageJob(pawn, out Job triageJob))
                return;

            Arbiter.Submit(FromJob(pawn, triageJob, Actuation.Interrupt, "bleeding out soon: interrupt for triage"));
        }

        private static Proposal FromJob(Pawn pawn, Job job, Actuation actuation, string reason)
        {
            return new Proposal
            {
                pawn = pawn,
                behaviorId = BehaviorIds.SelfTriage,
                tier = DecisionTier.Survival,
                score = Score,
                jobDef = job.def,
                target = job.targetA,
                jobExpiryTicks = job.expiryInterval,
                actuation = actuation,
                validUntilTick = PawnStateTracker.Now + ValidTicks,
                commitTicks = CommitTicks,
                reason = reason
            };
        }
    }
}
