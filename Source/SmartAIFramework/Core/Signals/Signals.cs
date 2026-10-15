using Verse;
using Verse.AI;

namespace SmartAIFramework.Core.Signals
{
    public interface ISignal { }

    public enum IncapacitationKind { Downed, Killed, Despawned }

    public readonly struct StanceChangedSignal : ISignal
    {
        public readonly Pawn pawn;
        public readonly Stance oldStance;
        public readonly Stance newStance;
        public StanceChangedSignal(Pawn pawn, Stance oldStance, Stance newStance)
        { this.pawn = pawn; this.oldStance = oldStance; this.newStance = newStance; }
    }

    public readonly struct PawnIncapacitatedSignal : ISignal
    {
        public readonly Pawn pawn;
        public readonly Map map;
        public readonly IncapacitationKind kind;
        public PawnIncapacitatedSignal(Pawn pawn, Map map, IncapacitationKind kind)
        { this.pawn = pawn; this.map = map; this.kind = kind; }
    }

    public readonly struct DamageTakenSignal : ISignal
    {
        public readonly Pawn victim;
        public readonly DamageInfo dinfo;
        public readonly float amount;
        public DamageTakenSignal(Pawn victim, DamageInfo dinfo, float amount)
        { this.victim = victim; this.dinfo = dinfo; this.amount = amount; }
    }

    public readonly struct NoiseSignal : ISignal
    {
        public readonly Thing source;
        public readonly Map map;
        public readonly IntVec3 position;
        public readonly float radius;
        public readonly ClamorDef def;
        public NoiseSignal(Thing source, Map map, IntVec3 position, float radius, ClamorDef def)
        { this.source = source; this.map = map; this.position = position; this.radius = radius; this.def = def; }
    }

    public readonly struct JobStartedSignal : ISignal
    {
        public readonly Pawn pawn;
        public readonly Job job;
        public JobStartedSignal(Pawn pawn, Job job) { this.pawn = pawn; this.job = job; }
    }

    public readonly struct ShotFiredSignal : ISignal
    {
        public readonly Pawn shooter;
        public readonly Verb verb;
        public readonly LocalTargetInfo target;
        public readonly bool success;
        public ShotFiredSignal(Pawn shooter, Verb verb, LocalTargetInfo target, bool success)
        { this.shooter = shooter; this.verb = verb; this.target = target; this.success = success; }
    }

    public readonly struct PawnEnteredCellSignal : ISignal
    {
        public readonly Pawn pawn;
        public readonly IntVec3 nextCell;
        public PawnEnteredCellSignal(Pawn pawn, IntVec3 nextCell) { this.pawn = pawn; this.nextCell = nextCell; }
    }

    public readonly struct TargetedSignal : ISignal
    {
        public readonly Pawn target;
        public readonly Pawn aggressor;
        public TargetedSignal(Pawn target, Pawn aggressor) { this.target = target; this.aggressor = aggressor; }
    }

    public readonly struct NoiseHeardSignal : ISignal
    {
        public readonly Pawn listener;
        public readonly Core_Engine.AcousticEvent evt;
        public NoiseHeardSignal(Pawn listener, Core_Engine.AcousticEvent evt) { this.listener = listener; this.evt = evt; }
    }
}
