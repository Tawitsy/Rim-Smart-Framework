using System.Collections.Generic;
using SmartAIFramework.Core.Decisions;
using SmartAIFramework.Core.Time;
using SmartAIFramework.Core_Engine;
using Verse;

namespace SmartAIFramework.Core.Knowledge
{
    public readonly struct PawnSituation
    {
        public readonly bool known;
        public readonly int version;

        public readonly bool threatActive;
        public readonly IntVec3 threatPosition;

        public readonly int ticksSinceDamaged;
        public readonly float recentDamage;

        public readonly int aimedAtCount;

        public readonly bool heardDangerRecently;
        public readonly AcousticKind lastNoiseKind;
        public readonly IntVec3 lastNoisePosition;

        public readonly bool hasPendingIntent;

        public PawnSituation(bool known, int version, bool threatActive, IntVec3 threatPosition,
            int ticksSinceDamaged, float recentDamage, int aimedAtCount,
            bool heardDangerRecently, AcousticKind lastNoiseKind, IntVec3 lastNoisePosition, bool hasPendingIntent)
        {
            this.known = known; this.version = version;
            this.threatActive = threatActive; this.threatPosition = threatPosition;
            this.ticksSinceDamaged = ticksSinceDamaged; this.recentDamage = recentDamage;
            this.aimedAtCount = aimedAtCount;
            this.heardDangerRecently = heardDangerRecently; this.lastNoiseKind = lastNoiseKind; this.lastNoisePosition = lastNoisePosition;
            this.hasPendingIntent = hasPendingIntent;
        }

        public static readonly PawnSituation Unknown = new PawnSituation(false, 0, false, IntVec3.Invalid,
            int.MaxValue, 0f, 0, false, AcousticKind.Unknown, IntVec3.Invalid, false);

        public bool IsUnderFire => aimedAtCount > 0 || ticksSinceDamaged < 300;
    }

    public interface IPawnMemoryWriter
    {
        void RecordThreat(IntVec3 position, int ttlTicks);
        void RecordDamage(float amount);
        void RecordNoise(AcousticKind kind, IntVec3 position, int ttlTicks);
        void AddAimer(Pawn aggressor);
        void RemoveAimer(Pawn aggressor);
        Intent PendingIntent { get; set; }
    }

    public sealed class PawnMemory : IPawnMemoryWriter
    {
        private const int RecentDamageWindow = 600;

        public readonly Pawn pawn;
        public int Version { get; private set; }

        private Expiring<IntVec3> threat;
        private Expiring<AcousticKind> noiseKind;
        private IntVec3 noisePos = IntVec3.Invalid;
        private int lastDamagedTick = -1;
        private float recentDamage;
        private readonly HashSet<Pawn> aimers = new HashSet<Pawn>();
        private Intent pending;

        public LazyCache<float> selfCapability;

        public PawnMemory(Pawn pawn) { this.pawn = pawn; }

        private static readonly System.Predicate<Pawn> IsStaleAimer = a => a == null || a.Dead || a.Downed || !a.Spawned;

        public PawnSituation Snapshot()
        {
            aimers.RemoveWhere(IsStaleAimer);

            int sinceDmg = GameClock.TicksSince(lastDamagedTick);
            return new PawnSituation(
                known: true,
                version: Version,
                threatActive: threat.TryGet(out var tPos),
                threatPosition: tPos,
                ticksSinceDamaged: sinceDmg,
                recentDamage: sinceDmg < RecentDamageWindow ? recentDamage : 0f,
                aimedAtCount: aimers.Count,
                heardDangerRecently: noiseKind.IsActive,
                lastNoiseKind: noiseKind.GetOrDefault(AcousticKind.Unknown),
                lastNoisePosition: noisePos,
                hasPendingIntent: pending != null && !pending.Expired);
        }

        void IPawnMemoryWriter.RecordThreat(IntVec3 position, int ttlTicks)
        {
            threat.Set(position, ttlTicks);
            Version++;
        }

        void IPawnMemoryWriter.RecordDamage(float amount)
        {
            if (GameClock.TicksSince(lastDamagedTick) >= RecentDamageWindow) recentDamage = 0f;
            recentDamage += amount;
            lastDamagedTick = GameClock.Now;
            Version++;
        }

        void IPawnMemoryWriter.RecordNoise(AcousticKind kind, IntVec3 position, int ttlTicks)
        {
            noiseKind.Set(kind, ttlTicks);
            noisePos = position;
            Version++;
        }

        void IPawnMemoryWriter.AddAimer(Pawn aggressor)
        {
            if (aimers.Add(aggressor)) Version++;
        }

        void IPawnMemoryWriter.RemoveAimer(Pawn aggressor)
        {
            if (aimers.Remove(aggressor)) Version++;
        }

        Intent IPawnMemoryWriter.PendingIntent
        {
            get => pending;
            set { pending = value; Version++; }
        }
    }
}
