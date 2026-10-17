using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Compat;
using SmartAIFramework.Data;
using SmartAIFramework.Work;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Decisions
{
    // The only code allowed to start, end, replace or interrupt a pawn's job on the mod's behalf.
    public static class JobDirector
    {
        private static readonly List<Pawn> interruptQueue = new List<Pawn>(16);
        private static bool isFlushing;

        // Set when a shot is blocked inside TryCastShot. The flush is deferred until the verb has
        // finished its burst bookkeeping, because vanilla applies Stance_Cooldown after TryCastShot
        // returns false and that cooldown would otherwise pin the pawn in place.
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

        // Ending the job of a pawn that is on fire, fleeing, or carrying something (a downed ally,
        // a haul) does more harm than the tactical move gains: vanilla drops carried things on interrupt.
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

            // A drafted assist classifies as PlayerOrder by definition, so only autonomous pawns are compared.
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

            // The Arbiter swaps the pending proposal in at the pawn's next decision, whether or not it also interrupts.
            state.pendingProposal = p;
            if (p.actuation != Actuation.Interrupt)
                return;

            if (!interruptQueue.Contains(pawn))
                interruptQueue.Add(pawn);

            if (!p.deferFlush)
                FlushInterrupts();
        }

        // The pawn is drafted, so there is no think-tree decision to wait for: start the short move directly.
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
                        // The proposal stays pending: it can still be swapped in at the pawn's next decision.
                        Arbiter.OnBlocked(proposal, blockedReason);
                        continue;
                    }

                    Job curJob = p.jobs?.curJob;
                    if (curJob != null && curJob.def == proposal.jobDef && curJob.targetA == proposal.target)
                        continue;

                    // The job ends here and the Arbiter's decision-point hook supplies the replacement
                    // while vanilla looks for the next job, which is when the proposal counts as executed.
                    p.stances?.CancelBusyStanceHard();
                    p.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);
                }
            }
            finally
            {
                isFlushing = false;
            }
        }

        public static void ApplyAtDecision(Pawn pawn, ref ThinkResult result)
        {
            Job discarded = result.Job;
            if (Arbiter.TryResolveAtDecision(pawn, discarded, out Job replacement))
            {
                if (discarded != null)
                {
                    if (result.FromQueue)
                    {
                        // A queued job is usually one vanilla suspended, and it still holds its reservations.
                        // Put it back so the pawn resumes it after the replacement, as after a vanilla interrupt.
                        pawn.jobs.jobQueue.EnqueueFirst(discarded, result.Tag);
                    }
                    else
                    {
                        // Job givers such as JobGiver_Wander reserve the destination when they build the job.
                        pawn.ClearReservationsForJob(discarded);
                    }
                }
                result = new ThinkResult(replacement, result.SourceNode, result.Tag, fromQueue: false);
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

        // Refuses a job without starting a replacement: the pawn keeps what it has, or vanilla looks again next tick.
        // Suppressing the target stops the work search from re-picking the same job immediately.
        public static bool VetoStartJob(Pawn pawn, Job newJob, string reason, int suppressTicks = -1)
        {
            if (pawn == null || newJob == null || newJob.playerForced)
                return false;

            Thing target = newJob.targetA.Thing;
            if (target == null)
                return false;

            PawnStateTracker.SuppressTarget(target, suppressTicks >= 0 ? suppressTicks : SmartAIMod.Settings.jobSuppressionDurationTicks);
            // A job resumed from the queue arrives here still holding its reservations, and it will never run now.
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
