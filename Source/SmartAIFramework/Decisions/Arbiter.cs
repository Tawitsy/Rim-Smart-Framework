using System;
using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Compat;
using SmartAIFramework.Data;
using SmartAIFramework.Work;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Decisions
{
    public static class Arbiter
    {
        public static int MaxHeavyEvaluationsPerTick => SmartAIMod.Settings?.advHeavyEvaluationsPerTick ?? SmartAISettings.DefaultHeavyEvaluationsPerTick;

        private const float HysteresisFactor = 1.25f;
        private const float HysteresisMargin = 10f;
        private const int DefaultCooldownTicks = 30;

        private struct PullBehavior
        {
            public string id;
            public Func<Pawn, Job, Proposal> propose;
        }

        private static readonly List<PullBehavior> pullBehaviors = new List<PullBehavior>(4);

        private static int budgetTick = -1;
        private static int budgetUsed;

        public static void Reset()
        {
            budgetTick = -1;
            budgetUsed = 0;
        }

        public static void RegisterDecisionPointBehavior(string behaviorId, Func<Pawn, Job, Proposal> propose)
        {
            pullBehaviors.Add(new PullBehavior { id = behaviorId, propose = propose });
        }

        public static bool TryConsumeHeavyBudget(bool urgent)
        {
            int now = PawnStateTracker.Now;
            if (now != budgetTick)
            {
                budgetTick = now;
                budgetUsed = 0;
            }

            if (!urgent && budgetUsed >= MaxHeavyEvaluationsPerTick)
                return false;

            budgetUsed++;
            return true;
        }

        public static int GetCooldownTicks(string behaviorId)
        {
            switch (behaviorId)
            {
                case BehaviorIds.Sidestep: return 75;
                case BehaviorIds.Kite:
                case BehaviorIds.KiteAssist: return 35;
                case BehaviorIds.PredatorRetreat: return 600;
                case BehaviorIds.BlastEvac: return 0;
                default: return DefaultCooldownTicks;
            }
        }

        public static bool IsOnCooldown(Pawn pawn, string behaviorId)
        {
            PawnTacticalState state = PawnStateTracker.TryGet(pawn);
            return state != null && IsOnCooldown(state, behaviorId, PawnStateTracker.Now);
        }

        public static bool Submit(Proposal p)
        {
            Pawn pawn = p?.pawn;
            if (pawn == null || p.jobDef == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
                return false;
            if (!(p.assistDrafted ? PawnStateTracker.CanAssist(pawn) : PawnStateTracker.CanActAutonomously(pawn)))
                return false;

            int now = PawnStateTracker.Now;
            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);

            if (IsOnCooldown(state, p.behaviorId, now))
            {
                DecisionLog.Record(pawn, p, DecisionOutcome.RejectedCooldown, "behavior on cooldown");
                return false;
            }

            if (p.jobDef == JobDefOf.Goto && p.target.IsValid && !p.target.HasThing && p.target.Cell == pawn.Position)
            {
                DecisionLog.Record(pawn, p, DecisionOutcome.RejectedTier, "already at the target cell");
                return false;
            }

            if (!PassesCommitment(state, p.tier, p.score, now))
            {
                DecisionLog.Record(pawn, p, DecisionOutcome.RejectedCommitment, "pawn is committed to another decision");
                return false;
            }

            bool direct = p.assistDrafted && pawn.Drafted;
            Proposal pending = state.pendingProposal;

            if (!direct && pending != null && now < pending.validUntilTick && !IsBetter(p, pending))
            {
                DecisionLog.Record(pawn, p, DecisionOutcome.RejectedCommitment, "a pending proposal ranks at least as high");
                return false;
            }

            DecisionLog.Record(pawn, p, DecisionOutcome.Accepted);
            JobDirector.Execute(p);
            return true;
        }

        public static bool TryResolveAtDecision(Pawn pawn, Job vanillaChoice, out Job replacement)
        {
            replacement = null;
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
                return false;
            if (PawnStateTracker.ObserveOnly || pawn.Drafted || pawn.Faction != Faction.OfPlayer)
                return false;

            int now = PawnStateTracker.Now;
            PawnTacticalState state = PawnStateTracker.TryGet(pawn);
            Proposal pending = state?.pendingProposal;

            if (pending != null && now >= pending.validUntilTick)
            {
                DecisionLog.Record(pawn, pending, DecisionOutcome.Expired);
                state.pendingProposal = null;
                pending = null;
            }

            DecisionTier vanillaTier = ClassifyVanillaChoice(pawn, vanillaChoice);
            if ((int)vanillaTier <= (int)DecisionTier.VanillaEmergency)
            {
                if (pending != null)
                {
                    DecisionLog.Record(pawn, pending, DecisionOutcome.RejectedTier, "vanilla choice outranks it");
                    state.pendingProposal = null;
                }
                return false;
            }

            Proposal best = pending;
            for (int i = 0; i < pullBehaviors.Count; i++)
            {
                PullBehavior pull = pullBehaviors[i];
                if (state != null && IsOnCooldown(state, pull.id, now))
                    continue;

                Proposal candidate = pull.propose(pawn, vanillaChoice);
                if (candidate != null && (best == null || IsBetter(candidate, best)))
                    best = candidate;
            }

            if (best == null)
                return false;

            if (state == null)
                state = PawnStateTracker.GetOrCreate(pawn);
            bool isPending = best == state.pendingProposal;

            if ((int)best.tier >= (int)vanillaTier)
            {
                DecisionLog.Record(pawn, best, DecisionOutcome.RejectedTier, "does not outrank the vanilla choice");
                if (isPending) state.pendingProposal = null;
                return false;
            }

            if (WorkUtility.IsMedicalOrRestJob(vanillaChoice?.def) && WorkUtility.IsMedicalOrRestJob(best.jobDef))
            {
                DecisionLog.Record(pawn, best, DecisionOutcome.RejectedTier, "vanilla already chose a medical or rest job");
                if (isPending) state.pendingProposal = null;
                return false;
            }

            if (!PassesCommitment(state, best.tier, best.score, now))
            {
                DecisionLog.Record(pawn, best, DecisionOutcome.RejectedCommitment, "pawn is committed to another decision");
                if (isPending) state.pendingProposal = null;
                return false;
            }

            Job job = best.CreateJob();
            state.pendingProposal = null;
            if (job == null)
                return false;

            replacement = job;
            OnExecuted(best);
            return true;
        }

        public static DecisionTier ClassifyVanillaChoice(Pawn pawn, Job vanillaChoice)
        {
            return ClassifyJob(pawn, vanillaChoice);
        }

        public static DecisionTier ClassifyJob(Pawn pawn, Job job)
        {
            if (pawn.Drafted || (job != null && job.playerForced) || PerspectiveShiftCompat.IsDirectlyControlled(pawn))
                return DecisionTier.PlayerOrder;
            if (pawn.InMentalState || pawn.IsBurning() || (job != null && WorkUtility.IsSurvivalCriticalJob(job.def)))
                return DecisionTier.VanillaEmergency;
            if (job != null && WorkUtility.IsVitalNeedJob(job.def))
                return DecisionTier.VanillaNeed;
            return DecisionTier.Routine;
        }

        internal static void OnExecuted(Proposal p)
        {
            Pawn pawn = p.pawn;
            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);
            int now = PawnStateTracker.Now;

            if (p.commitTicks > 0)
            {
                state.committedUntilTick = now + p.commitTicks;
                state.committedTier = p.tier;
                state.committedScore = p.score;
                state.committedBehaviorId = p.behaviorId;
            }
            else
            {
                state.committedUntilTick = 0;
            }

            int cooldown = GetCooldownTicks(p.behaviorId);
            if (cooldown > 0)
                SetCooldown(state, p.behaviorId, now + cooldown);

            DecisionLog.Record(pawn, p, DecisionOutcome.Executed);

            if (p.executedMessage != null)
                Messages.Message(p.executedMessage, pawn, p.executedMessageType ?? MessageTypeDefOf.NeutralEvent);
        }

        internal static void OnBlocked(Proposal p, string reason)
        {
            PawnTacticalState state = PawnStateTracker.GetOrCreate(p.pawn);
            int cooldown = GetCooldownTicks(p.behaviorId);
            if (cooldown > 0)
                SetCooldown(state, p.behaviorId, PawnStateTracker.Now + cooldown);

            DecisionLog.Record(p.pawn, p, DecisionOutcome.BlockedProtected, reason);
        }

        private static bool IsBetter(Proposal a, Proposal b)
        {
            return a.tier < b.tier || (a.tier == b.tier && a.score > b.score);
        }

        private static bool PassesCommitment(PawnTacticalState state, DecisionTier tier, float score, int now)
        {
            if (now >= state.committedUntilTick)
                return true;
            if (tier < state.committedTier)
                return true;
            return tier == state.committedTier && score >= state.committedScore * HysteresisFactor + HysteresisMargin;
        }

        private static bool IsOnCooldown(PawnTacticalState state, string behaviorId, int now)
        {
            string[] ids = state.cooldownIds;
            if (ids == null) return false;

            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == behaviorId)
                    return now < state.cooldownUntil[i];
            }
            return false;
        }

        private static void SetCooldown(PawnTacticalState state, string behaviorId, int until)
        {
            if (state.cooldownIds == null)
            {
                state.cooldownIds = new string[PawnTacticalState.CooldownSlots];
                state.cooldownUntil = new int[PawnTacticalState.CooldownSlots];
            }

            int slot = 0;
            int oldest = int.MaxValue;
            for (int i = 0; i < state.cooldownIds.Length; i++)
            {
                if (state.cooldownIds[i] == behaviorId)
                {
                    slot = i;
                    break;
                }
                int expiry = state.cooldownIds[i] == null ? int.MinValue : state.cooldownUntil[i];
                if (expiry < oldest)
                {
                    oldest = expiry;
                    slot = i;
                }
            }

            state.cooldownIds[slot] = behaviorId;
            state.cooldownUntil[slot] = until;
        }
    }
}
