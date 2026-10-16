using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Data
{
    public enum TacticalActionType
    {
        None = 0,
        Sidestep,
        SeekCover,
        Retreat,
        SelfTriage
    }

    public struct PendingJobRequest
    {
        public TacticalActionType actionType;
        public JobDef jobDef;
        public LocalTargetInfo target;
        public float priorityScore;
        public int expiresAtTick;
        public int jobExpiryTicks;
        public bool cancelWarmup;

        public bool IsValid(int now) => jobDef != null && now < expiresAtTick;

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

    public sealed class PawnTacticalState
    {
        public readonly int pawnId;
        public int version;

        public IntVec3 lastThreatCell = IntVec3.Invalid;
        public int threatExpiresAtTick;

        public int lastDamagedTick = -99999;
        public float recentDamageAmount;

        public int activeAimerCount;

        public float cachedCapability = -1f;
        public int capabilityComputedTick = -99999;
        public int capabilityVersion = -1;

        public int nextCoverEvalTick;
        public int nextKiteEvalTick;
        public int nextClamorEvalTick;
        public int nextSidestepTick;
        public int nextPredatorAlertTick;
        public int lastStartJobOverrideTick = -99999;

        public PendingJobRequest pendingJob;

        public PawnTacticalState(int pawnId)
        {
            this.pawnId = pawnId;
        }

        public void RecordDamage(float amount, IntVec3 threatCell, int now)
        {
            if (now - lastDamagedTick > 600)
                recentDamageAmount = 0f;

            recentDamageAmount += amount;
            lastDamagedTick = now;

            if (threatCell.IsValid)
            {
                lastThreatCell = threatCell;
                threatExpiresAtTick = now + 300;
            }
            version++;
        }

        public void RecordThreat(IntVec3 threatCell, int ttlTicks, int now)
        {
            if (!threatCell.IsValid) return;
            lastThreatCell = threatCell;
            threatExpiresAtTick = now + ttlTicks;
            version++;
        }
    }

    public sealed class PawnStateTracker : GameComponent
    {
        private static readonly Dictionary<int, PawnTacticalState> states = new Dictionary<int, PawnTacticalState>(64);
        private static readonly Dictionary<int, int> suppressedTargets = new Dictionary<int, int>(128);
        private static readonly List<Pawn> interruptQueue = new List<Pawn>(16);
        private static bool isFlushing;

        public static bool ObserveOnly => SmartAIMod.Settings != null && SmartAIMod.Settings.observeOnly;

        public static int Now => Find.TickManager?.TicksGame ?? 0;

        public PawnStateTracker(Game game)
        {
            ResetAll();
        }

        public static void ResetAll()
        {
            states.Clear();
            suppressedTargets.Clear();
            interruptQueue.Clear();
            isFlushing = false;
            flushAfterCast = false;
            ThingProfileCache.Clear();
            VerbProfileCache.Clear();
            Work.WorkUtility.ClearAll();
            Work.WorkDelegationUtility.ClearCache();
        }

        public static bool CanActAutonomously(Pawn pawn)
        {
            if (ObserveOnly || pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
                return false;
            if (pawn.Drafted || pawn.InMentalState)
                return false;
            return pawn.Faction == Faction.OfPlayer;
        }

        public static bool CanAssist(Pawn pawn)
        {
            if (ObserveOnly || pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
                return false;
            if (pawn.InMentalState)
                return false;
            return pawn.Faction == Faction.OfPlayer;
        }

        public static PawnTacticalState GetOrCreate(Pawn pawn)
        {
            if (pawn == null) return null;
            int id = pawn.thingIDNumber;
            if (!states.TryGetValue(id, out PawnTacticalState state))
            {
                state = new PawnTacticalState(id);
                states[id] = state;
            }
            return state;
        }

        public static PawnTacticalState TryGet(Pawn pawn)
        {
            if (pawn == null || states.Count == 0) return null;
            states.TryGetValue(pawn.thingIDNumber, out PawnTacticalState state);
            return state;
        }

        public static void RemovePawn(Pawn pawn, bool forgetState)
        {
            if (pawn == null) return;
            interruptQueue.Remove(pawn);
            if (forgetState)
            {
                int id = pawn.thingIDNumber;
                states.Remove(id);
                Work.WorkUtility.ClearPawn(id);
            }
        }

        public static void SuppressTarget(Thing target, int durationTicks)
        {
            if (target == null) return;
            suppressedTargets[target.thingIDNumber] = Now + durationTicks;
        }

        public static bool IsTargetSuppressed(Thing target)
        {
            if (target == null || suppressedTargets.Count == 0)
                return false;

            int id = target.thingIDNumber;
            if (!suppressedTargets.TryGetValue(id, out int expiresAt))
                return false;

            if (Now >= expiresAt)
            {
                suppressedTargets.Remove(id);
                return false;
            }
            return true;
        }

        public static bool CanOverrideStartJob(Pawn pawn, int minIntervalTicks = 15)
        {
            if (pawn == null) return false;
            PawnTacticalState state = GetOrCreate(pawn);
            int now = Now;
            if (now - state.lastStartJobOverrideTick < minIntervalTicks)
                return false;

            state.lastStartJobOverrideTick = now;
            return true;
        }

        public static void QueueTacticalJob(
            Pawn pawn,
            TacticalActionType actionType,
            JobDef jobDef,
            LocalTargetInfo target,
            float priorityScore,
            int validForTicks = 120,
            int jobExpiryTicks = 45,
            bool cancelWarmup = false)
        {
            if (pawn == null || jobDef == null || !CanActAutonomously(pawn))
                return;

            PawnTacticalState state = GetOrCreate(pawn);
            int now = Now;

            if (state.pendingJob.IsValid(now) && state.pendingJob.priorityScore >= priorityScore)
                return;

            state.pendingJob = new PendingJobRequest
            {
                actionType = actionType,
                jobDef = jobDef,
                target = target,
                priorityScore = priorityScore,
                expiresAtTick = now + validForTicks,
                jobExpiryTicks = jobExpiryTicks,
                cancelWarmup = cancelWarmup
            };

            if (!interruptQueue.Contains(pawn))
            {
                interruptQueue.Add(pawn);
            }
        }

        public static void FlushInterrupts()
        {
            if (interruptQueue.Count == 0 || isFlushing)
                return;

            isFlushing = true;
            int now = Now;
            try
            {
                for (int i = interruptQueue.Count - 1; i >= 0; i--)
                {
                    Pawn p = interruptQueue[i];
                    interruptQueue.RemoveAt(i);

                    if (p == null || !p.Spawned || p.Dead || p.Downed)
                        continue;
                    if (p.stances?.stunner?.Stunned == true)
                        continue;

                    PawnTacticalState state = TryGet(p);
                    if (state == null || !state.pendingJob.IsValid(now))
                        continue;

                    if (!CanActAutonomously(p))
                        continue;

                    Stance curStance = p.stances?.curStance;
                    if (curStance is Stance_Busy busy && !(busy is Stance_Cooldown))
                    {
                        if (!state.pendingJob.cancelWarmup)
                            continue;
                    }

                    Job curJob = p.jobs?.curJob;
                    if (curJob != null && curJob.playerForced)
                        continue;
                    if (IsProtectedFromInterrupt(p, curJob))
                        continue;
                    if (curJob != null && curJob.def == state.pendingJob.jobDef && curJob.targetA == state.pendingJob.target)
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

        // Ending the job of a pawn that is on fire, fleeing, or carrying something (a downed ally,
        // a haul) does more harm than the tactical move gains: vanilla drops carried things on interrupt.
        public static bool IsProtectedFromInterrupt(Pawn pawn, Job curJob)
        {
            if (pawn.IsBurning() || pawn.carryTracker?.CarriedThing != null)
                return true;
            return curJob != null && Work.WorkUtility.IsSurvivalCriticalJob(curJob.def);
        }

        // Set when a shot is blocked inside TryCastShot. The flush is deferred until the verb has
        // finished its burst bookkeeping, because vanilla applies Stance_Cooldown after TryCastShot
        // returns false and that cooldown would otherwise pin the pawn in place.
        private static bool flushAfterCast;

        public static void RequestFlushAfterCast() => flushAfterCast = true;

        public static void FlushIfRequestedAfterCast()
        {
            if (!flushAfterCast) return;
            flushAfterCast = false;
            FlushInterrupts();
        }

        public static bool TryTakeQueuedJob(Pawn pawn, Job vanillaChoice, out Job replacementJob)
        {
            replacementJob = null;
            PawnTacticalState state = TryGet(pawn);
            if (state == null) return false;

            int now = Now;
            if (!state.pendingJob.IsValid(now))
            {
                state.pendingJob = default;
                return false;
            }

            if (!CanActAutonomously(pawn) || (vanillaChoice != null && vanillaChoice.playerForced))
            {
                state.pendingJob = default;
                return false;
            }

            if (pawn.IsBurning() || (vanillaChoice != null && Work.WorkUtility.IsSurvivalCriticalJob(vanillaChoice.def)))
            {
                state.pendingJob = default;
                return false;
            }

            PendingJobRequest req = state.pendingJob;
            state.pendingJob = default;
            replacementJob = req.CreateJob();
            return replacementJob != null;
        }
    }
}
