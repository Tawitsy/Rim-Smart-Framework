using System.Text;
using RimWorld;
using SmartAIFramework.Data;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Decisions
{
    public enum DecisionOutcome
    {
        Accepted,
        Executed,
        RejectedTier,
        RejectedCommitment,
        RejectedCooldown,
        BlockedProtected,
        Vetoed,
        Expired
    }

    public struct DecisionEntry
    {
        public int tick;
        public int pawnId;
        public string behaviorId;
        public DecisionTier tier;
        public float score;
        public DecisionOutcome outcome;
        public string reason;
    }

    public static class DecisionLog
    {
        public const int PawnRingSize = 16;
        public const int GlobalRingSize = 256;

        private static readonly DecisionEntry[] globalRing = new DecisionEntry[GlobalRingSize];
        private static int globalNext;
        private static int globalCount;

        public static void Reset()
        {
            System.Array.Clear(globalRing, 0, globalRing.Length);
            globalNext = 0;
            globalCount = 0;
        }

        public static void Record(Pawn pawn, Proposal p, DecisionOutcome outcome, string reason = null)
        {
            Record(pawn, p.behaviorId, p.tier, p.score, outcome, reason ?? p.reason);
        }

        public static void Record(Pawn pawn, string behaviorId, DecisionTier tier, float score, DecisionOutcome outcome, string reason)
        {
            if (pawn == null) return;

            DecisionEntry entry;
            entry.tick = PawnStateTracker.Now;
            entry.pawnId = pawn.thingIDNumber;
            entry.behaviorId = behaviorId;
            entry.tier = tier;
            entry.score = score;
            entry.outcome = outcome;
            entry.reason = reason;

            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);
            if (state.decisionRing == null)
                state.decisionRing = new DecisionEntry[PawnRingSize];
            state.decisionRing[state.decisionNext] = entry;
            state.decisionNext = (state.decisionNext + 1) % PawnRingSize;
            if (state.decisionCount < PawnRingSize)
                state.decisionCount++;

            globalRing[globalNext] = entry;
            globalNext = (globalNext + 1) % GlobalRingSize;
            if (globalCount < GlobalRingSize)
                globalCount++;

            if (SmartLog.IsEnabled)
                SmartLog.Decision("Arbiter", pawn, $"{behaviorId} {outcome} (tier {tier}, score {score:F0}){(reason != null ? " - " + reason : "")}");
        }

        public static string Explain(Pawn pawn)
        {
            if (pawn == null) return "No pawn.";

            int now = PawnStateTracker.Now;
            var sb = new StringBuilder(512);
            sb.Append(pawn.LabelShort).Append(" (id ").Append(pawn.thingIDNumber).Append(") at tick ").Append(now).AppendLine();

            sb.Append("State: ");
            sb.Append(pawn.Spawned ? "spawned" : "not spawned");
            if (pawn.Dead) sb.Append(", dead");
            if (pawn.Downed) sb.Append(", downed");
            if (pawn.Drafted) sb.Append(", drafted");
            if (pawn.InMentalState) sb.Append(", mental state");
            if (pawn.IsBurning()) sb.Append(", burning");
            if (pawn.Faction != Faction.OfPlayer) sb.Append(", not player faction");
            if (PawnStateTracker.ObserveOnly) sb.Append(", observe-only mode");
            sb.AppendLine();

            Job cur = pawn.CurJob;
            sb.Append("Current job: ");
            if (cur == null) sb.Append("none");
            else sb.Append(cur.def.defName).Append(" -> ").Append(cur.targetA.ToString()).Append(cur.playerForced ? " (player forced)" : "");
            sb.AppendLine();

            PawnTacticalState state = PawnStateTracker.TryGet(pawn);
            if (state == null)
            {
                sb.AppendLine("No tracked state yet (the mod has not acted on this pawn).");
                return sb.ToString();
            }

            if (now < state.committedUntilTick)
            {
                sb.Append("Commitment: ").Append(state.committedBehaviorId).Append(" tier ").Append(state.committedTier)
                  .Append(" score ").Append(state.committedScore.ToString("F0")).Append(" for ")
                  .Append(state.committedUntilTick - now).AppendLine(" more ticks");
            }
            else
            {
                sb.AppendLine("Commitment: none");
            }

            sb.Append("Cooldowns: ");
            bool anyCooldown = false;
            if (state.cooldownIds != null)
            {
                for (int i = 0; i < state.cooldownIds.Length; i++)
                {
                    if (state.cooldownIds[i] != null && state.cooldownUntil[i] > now)
                    {
                        if (anyCooldown) sb.Append(", ");
                        sb.Append(state.cooldownIds[i]).Append(' ').Append(state.cooldownUntil[i] - now).Append(" ticks");
                        anyCooldown = true;
                    }
                }
            }
            sb.AppendLine(anyCooldown ? "" : "none");

            Proposal pending = state.pendingProposal;
            sb.Append("Pending proposal: ");
            if (pending == null)
            {
                sb.AppendLine("none");
            }
            else
            {
                sb.Append(pending.behaviorId).Append(" tier ").Append(pending.tier).Append(" score ").Append(pending.score.ToString("F0"))
                  .Append(' ').Append(pending.jobDef?.defName).Append(" -> ").Append(pending.target.ToString()).Append(' ').Append(pending.actuation);
                sb.AppendLine(now < pending.validUntilTick ? ", valid " + (pending.validUntilTick - now) + " more ticks" : ", expired");
            }

            var probe = new Proposal { pawn = pawn, behaviorId = "(probe)", tier = DecisionTier.Tactical };
            if (JobDirector.CanInterrupt(pawn, probe, out string blockedReason))
                sb.AppendLine("CanInterrupt (tactical): yes");
            else
                sb.Append("CanInterrupt (tactical): no - ").AppendLine(blockedReason);

            sb.AppendLine("Recent decisions (newest first):");
            if (state.decisionCount == 0 || state.decisionRing == null)
            {
                sb.AppendLine("  none");
            }
            else
            {
                for (int n = 0; n < state.decisionCount; n++)
                {
                    int idx = (state.decisionNext - 1 - n + PawnRingSize * 2) % PawnRingSize;
                    DecisionEntry e = state.decisionRing[idx];
                    sb.Append("  t").Append(e.tick).Append(" (").Append(now - e.tick).Append(" ago) ")
                      .Append(e.behaviorId).Append(" tier ").Append(e.tier).Append(" score ").Append(e.score.ToString("F0"))
                      .Append(" ").Append(e.outcome);
                    if (e.reason != null) sb.Append(" - ").Append(e.reason);
                    sb.AppendLine();
                }
            }

            sb.Append("Global ring holds ").Append(globalCount).Append(" of the last ").Append(GlobalRingSize).AppendLine(" decisions across all pawns.");
            return sb.ToString();
        }
    }
}
