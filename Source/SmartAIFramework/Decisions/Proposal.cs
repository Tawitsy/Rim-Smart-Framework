using Verse;
using Verse.AI;

namespace SmartAIFramework.Decisions
{
    public static class BehaviorIds
    {
        public const string Sidestep = "Combat.Sidestep";
        public const string Kite = "Combat.Kite";
        public const string KiteAssist = "Combat.KiteAssist";
        public const string YieldLane = "Combat.YieldLane";
        public const string Cover = "Combat.Cover";
        public const string Hold = "Combat.Hold";
        public const string KiteFromCover = "Combat.KiteFromCover";
        public const string SelfTriage = "Survival.SelfTriage";
        public const string PredatorRetreat = "Survival.PredatorRetreat";
        public const string BlastEvac = "Survival.BlastEvac";
        public const string EscapeHeat = "Survival.EscapeHeat";
        public const string StartJobVeto = "StartJob.Veto";
        public const string ThreatWait = "Work.ThreatWait";
    }

    public sealed class Proposal
    {
        public Pawn pawn;
        public string behaviorId;
        public DecisionTier tier;
        public float score;
        public JobDef jobDef;
        public LocalTargetInfo target;
        public int jobExpiryTicks;
        public Actuation actuation;
        public int validUntilTick;
        public int commitTicks;
        public string reason;

        public bool allowBusyStance;

        public bool assistDrafted;

        public bool deferFlush;

        public string executedMessage;
        public MessageTypeDef executedMessageType;

        public Job CreateJob()
        {
            Job job = target.IsValid ? JobMaker.MakeJob(jobDef, target) : JobMaker.MakeJob(jobDef);
            if (jobExpiryTicks > 0)
            {
                job.expiryInterval = jobExpiryTicks;
                job.checkOverrideOnExpire = true;
            }
            return job;
        }
    }
}
