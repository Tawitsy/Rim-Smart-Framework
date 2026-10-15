using SmartAIFramework.Core.Time;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Core.Decisions
{
    public enum IntentKind
    {
        Reposition,
        SeekCover,
        Retreat,
        TendSelf,
        Shelter,
        Rest,
        Custom,
    }

    public sealed class Intent
    {
        public readonly Pawn pawn;
        public readonly IntentKind kind;
        public readonly float utility;
        public readonly bool urgent;
        public readonly JobDef jobDef;
        public readonly LocalTargetInfo target;
        public readonly int jobExpiryTicks;
        public readonly int expiresAtTick;
        public readonly string source;
        public readonly string reason;

        public Intent(Pawn pawn, IntentKind kind, float utility, bool urgent, JobDef jobDef, LocalTargetInfo target,
            string source, string reason, int validForTicks = 250, int jobExpiryTicks = 0)
        {
            this.pawn = pawn; this.kind = kind; this.utility = utility; this.urgent = urgent;
            this.jobDef = jobDef; this.target = target; this.jobExpiryTicks = jobExpiryTicks;
            this.source = source; this.reason = reason;
            expiresAtTick = GameClock.ExpiresIn(validForTicks);
        }

        public bool Expired => GameClock.IsExpired(expiresAtTick);

        public Job MakeJob()
        {
            Job job = target.IsValid ? JobMaker.MakeJob(jobDef, target) : JobMaker.MakeJob(jobDef);
            if (jobExpiryTicks > 0)
            {
                job.expiryInterval = jobExpiryTicks;
                job.checkOverrideOnExpire = true;
            }
            return job;
        }

        public override string ToString() => $"{kind}({jobDef?.defName} -> {target}) u={utility:F1}{(urgent ? " URGENT" : "")} by {source}: {reason}";
    }
}
