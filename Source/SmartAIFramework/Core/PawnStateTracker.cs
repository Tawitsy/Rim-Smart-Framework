using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Compat;
using SmartAIFramework.Decisions;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Data
{
    public enum TacticalActionType
    {
        None = 0,
        Sidestep,
        SeekCover,
        SelfTriage
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

        public int cachedRole = -1;
        public int roleComputedTick = -99999;
        public int roleVersion = -1;

        public int nextCoverEvalTick;
        public int nextKiteEvalTick;
        public int nextClamorEvalTick;
        public int nextSidestepTick;
        public int nextPredatorAlertTick;

        public int quietUntilTick;
        public IntVec3 quietPawnCell = IntVec3.Invalid;
        public IntVec3 quietThreatCell = IntVec3.Invalid;
        public int lastStartJobOverrideTick = -99999;

        public Proposal pendingProposal;

        public int lastIssuedJobId = -1;
        public string lastIssuedBehaviorId;
        public DecisionTier lastIssuedTier = DecisionTier.Routine;

        public int committedUntilTick;
        public DecisionTier committedTier = DecisionTier.Routine;
        public float committedScore;
        public string committedBehaviorId;

        public const int CooldownSlots = 8;
        public string[] cooldownIds;
        public int[] cooldownUntil;

        public DecisionEntry[] decisionRing;
        public int decisionNext;
        public int decisionCount;

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

        public static bool ObserveOnly => SmartAIMod.Settings != null && SmartAIMod.Settings.observeOnly;

        public static int Now => Find.TickManager?.TicksGame ?? 0;

        public static bool HasPlayerOrderNowOrQueued(Pawn pawn)
        {
            return PawnUtility.PlayerForcedJobNowOrSoon(pawn) || (pawn.jobs?.jobQueue != null && pawn.jobs.jobQueue.AnyPlayerForced);
        }

        public PawnStateTracker(Game game)
        {
            ResetAll();
        }

        public static void ResetAll()
        {
            states.Clear();
            suppressedTargets.Clear();
            JobDirector.Reset();
            Arbiter.Reset();
            DecisionLog.Reset();
            ThingProfileCache.Clear();
            VerbProfileCache.Clear();
            Work.WorkUtility.ClearAll();
            Work.WorkDelegationUtility.ClearCache();
            ColonyAlert.Reset();
        }

        public static bool CanActAutonomously(Pawn pawn)
        {
            if (ObserveOnly || pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
                return false;
            if (pawn.Drafted || pawn.InMentalState)
                return false;
            if (pawn.Faction != Faction.OfPlayer)
                return false;
            return !PerspectiveShiftCompat.IsDirectlyControlled(pawn);
        }

        public static bool CanAssist(Pawn pawn)
        {
            if (ObserveOnly || pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
                return false;
            if (pawn.InMentalState)
                return false;
            if (pawn.Faction != Faction.OfPlayer)
                return false;
            return !PerspectiveShiftCompat.IsDirectlyControlled(pawn);
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
            JobDirector.Forget(pawn);
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

        public static int TrackedPawnCount => states.Count;
        public static int SuppressedTargetCount => suppressedTargets.Count;

        public static void ForgetTarget(Thing target)
        {
            if (target != null && suppressedTargets.Count > 0)
                suppressedTargets.Remove(target.thingIDNumber);
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
    }
}
