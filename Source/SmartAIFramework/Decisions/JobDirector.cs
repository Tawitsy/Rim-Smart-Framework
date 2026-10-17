using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Compat;
using SmartAIFramework.Data;
using SmartAIFramework.Work;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Decisions
{
    public static class JobDirector
    {
        private static readonly List<Pawn> interruptQueue = new List<Pawn>(16);
        private static bool isFlushing;

        private static bool flushAfterCast;

        public static void Reset()
        {
            interruptQueue.Clear();
            isFlushing = false;
            flushAfterCast = false;
        }

        public static void Forget(Pawn pawn)
        {
            interruptQueue.Remove(pawn);
        }

        public static bool IsProtectedFromInterrupt(Pawn pawn, Job curJob)
        {
            if (pawn.IsBurning() || pawn.carryTracker?.CarriedThing != null)
                return true;
            return curJob != null && WorkUtility.IsSurvivalCriticalJob(curJob.def);
        }

        public static bool CanInterrupt(Pawn pawn, Proposal p, out string blockedReason)
        {
            blockedReason = null;
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                blockedReason = "pawn is not active";
                return false;
            }
            if (PawnStateTracker.ObserveOnly)
            {
                blockedReason = "observe-only mode";
                return false;
            }
            if (pawn.InMentalState)
            {
                blockedReason = "pawn is in a mental state";
                return false;
            }
            if (pawn.Faction != Faction.OfPlayer)
            {
                blockedReason = "pawn is not in the player faction";
                return false;
            }
            if (PerspectiveShiftCompat.IsDirectlyControlled(pawn))
            {
                blockedReason = "under direct player control";
                return false;
            }
            if (pawn.stances?.stunner?.Stunned == true)
            {
                blockedReason = "pawn is stunned";
                return false;
            }

            Job curJob = pawn.jobs?.curJob;
            if (pawn.Drafted)
            {
                if (!p.assistDrafted)
                {
                    blockedReason = "pawn is drafted";
                    return false;
                }
                if (curJob != null && curJob.playerForced && curJob.def == JobDefOf.Goto)
                {
                    blockedReason = "player gave a move order";
                    return false;
                }
            }
            else if (curJob != null && curJob.playerForced)
            {
                blockedReason = "current job is player-forced";
                return false;
            }

            if (!(p.assistDrafted && pawn.Drafted) && (int)Arbiter.ClassifyJob(pawn, curJob) <= (int)p.tier)
            {
                blockedReason = "current job outranks the proposal";
                return false;
            }

            Stance curStance = pawn.stances?.curStance;
            if (!p.allowBusyStance && curStance is Stance_Busy busy && !(busy is Stance_Cooldown))
            {
                blockedReason = "pawn is in a busy stance";
                return false;
            }

            if (IsProtectedFromInterrupt(pawn, curJob))
            {
                blockedReason = "burning, carrying, or in a vanilla emergency job";
                return false;
            }

            return true;
        }

        public static void Execute(Proposal p)
        {
            Pawn pawn = p.pawn;
            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);

            if (p.assistDrafted && pawn.Drafted)
            {
                ExecuteDraftedAssist(p);
                return;
            }

            state.pendingProposal = p;
            if (p.actuation != Actuation.Interrupt)
                return;

            if (!interruptQueue.Contains(pawn))
                interruptQueue.Add(pawn);

            if (!p.deferFlush)
                FlushInterrupts();
        }

        private static void ExecuteDraftedAssist(Proposal p)
        {
            Pawn pawn = p.pawn;
            if (!CanInterrupt(pawn, p, out string blockedReason))
            {
                Arbiter.OnBlocked(p, blockedReason);
                return;
            }

            Job job = p.CreateJob();
            job.playerForced = false;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            Arbiter.OnExecuted(p);
        }

        public static void RequestFlushAfterCast() => flushAfterCast = true;

        public static void FlushIfRequestedAfterCast()
        {
            if (!flushAfterCast) return;
            flushAfterCast = false;
            FlushInterrupts();
        }

        public static void FlushInterrupts()
        {
            if (interruptQueue.Count == 0 || isFlushing)
                return;

            isFlushing = true;
            int now = PawnStateTracker.Now;
            try
            {
                for (int i = interruptQueue.Count - 1; i >= 0; i--)
                {
                    Pawn p = interruptQueue[i];
                    interruptQueue.RemoveAt(i);

                    if (p == null || !p.Spawned || p.Dead || p.Downed)
                        continue;

                    PawnTacticalState state = PawnStateTracker.TryGet(p);
                    Proposal proposal = state?.pendingProposal;
                    if (proposal == null || now >= proposal.validUntilTick || proposal.actuation != Actuation.Interrupt)
                        continue;

                    if (!CanInterrupt(p, proposal, out string blockedReason))
                    {
                        Arbiter.OnBlocked(proposal, blockedReason);
                        continue;
                    }

                    Job curJob = p.jobs?.curJob;
                    if (curJob != null && curJob.def == proposal.jobDef && curJob.targetA == proposal.target)
                        continue;

                    p.stances?.CancelBusyStanceHard();
                    p.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);
                }
            }
            finally
            {
                isFlushing = false;
            }
        }

        public const string ThreatWaitReason = "routine job inside a shooter's reach";
        public static int ThreatWaitTicks => SmartAIMod.Settings?.advThreatWaitTicks ?? SmartAISettings.DefaultThreatWaitTicks;

        public static void ApplyAtDecision(Pawn pawn, ref ThinkResult result)
        {
            Job discarded = result.Job;
            if (Arbiter.TryResolveAtDecision(pawn, discarded, out Job replacement))
            {
                ReleaseDiscarded(pawn, discarded, result);
                result = new ThinkResult(replacement, result.SourceNode, result.Tag, fromQueue: false);
                return;
            }

            if (discarded != null && ModuleFlags.Work && SmartAIMod.Settings.commonSenseHazardJobs &&
                WorkUtility.IsRoutineJobInThreatReach(pawn, discarded))
            {
                Job wait = JobMaker.MakeJob(JobDefOf.Wait_Wander);
                wait.expiryInterval = ThreatWaitTicks;
                wait.checkOverrideOnExpire = true;
                ReleaseDiscarded(pawn, discarded, result);
                DecisionLog.Record(pawn, BehaviorIds.ThreatWait, DecisionTier.Survival, 0f, DecisionOutcome.Vetoed, ThreatWaitReason);
                result = new ThinkResult(wait, result.SourceNode, result.Tag, fromQueue: false);
            }
        }

        private static void ReleaseDiscarded(Pawn pawn, Job discarded, ThinkResult result)
        {
            if (discarded == null)
                return;
            if (result.FromQueue)
            {
                pawn.jobs.jobQueue.EnqueueFirst(discarded, result.Tag);
            }
            else
            {
                pawn.ClearReservationsForJob(discarded);
            }
        }

        public static void RequestRethink(Pawn pawn)
        {
            if (pawn == null || !PawnStateTracker.CanActAutonomously(pawn))
                return;

            Job curJob = pawn.jobs?.curJob;
            if (curJob == null || curJob.playerForced || IsProtectedFromInterrupt(pawn, curJob))
                return;

            pawn.jobs.CheckForJobOverride();
        }

        public static bool VetoStartJob(Pawn pawn, Job newJob, string reason, int suppressTicks = -1, bool suppressTarget = true)
        {
            if (pawn == null || newJob == null || newJob.playerForced)
                return false;

            if (suppressTarget)
            {
                Thing target = newJob.targetA.Thing;
                if (target == null)
                    return false;
                PawnStateTracker.SuppressTarget(target, suppressTicks >= 0 ? suppressTicks : SmartAIMod.Settings.jobSuppressionDurationTicks);
            }

            Map map = pawn.Map;
            if (map != null)
            {
                map.reservationManager.ReleaseClaimedBy(pawn, newJob);
                map.pawnDestinationReservationManager.ReleaseClaimedBy(pawn, newJob);
                map.physicalInteractionReservationManager.ReleaseClaimedBy(pawn, newJob);
                map.attackTargetReservationManager.ReleaseClaimedBy(pawn, newJob);
            }
            DecisionLog.Record(pawn, BehaviorIds.StartJobVeto, DecisionTier.Survival, 0f, DecisionOutcome.Vetoed, reason);
            return true;
        }
    }
}
