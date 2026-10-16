using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Data;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Combat
{
    public enum ClamorCategory
    {
        None = 0,
        Gunfire,
        Impact,
        Construction,
        DistressCall
    }

    public static class SoundAwarenessUtility
    {
        public static ClamorCategory Classify(ClamorDef def, Thing source)
        {
            if (def == null) return ClamorCategory.None;
            if (def == ClamorDefOf.Harm)
            {
                if (source is Pawn victim &&
                    victim.Faction == Faction.OfPlayer &&
                    victim.Map?.dangerWatcher?.DangerRating == StoryDanger.High)
                {
                    return ClamorCategory.DistressCall;
                }
                return ClamorCategory.Gunfire;
            }
            if (def == ClamorDefOf.Impact) return ClamorCategory.Impact;
            if (def == ClamorDefOf.Construction) return ClamorCategory.Construction;
            return ClamorCategory.None;
        }

        public static void OnClamor(Thing source, IntVec3 position, float radius, ClamorDef def)
        {
            Map map = source?.Map;
            if (map == null) return;

            ClamorCategory category = Classify(def, source);
            if (category == ClamorCategory.None) return;

            Thing instigator = (source is Projectile proj) ? (proj.Launcher ?? source) : source;
            if (instigator == null) return;

            bool isDistress = category == ClamorCategory.DistressCall;
            if (!isDistress && instigator.Faction == Faction.OfPlayer)
                return;

            List<Pawn> colonists = map.mapPawns?.FreeColonistsSpawned;
            if (colonists == null || colonists.Count == 0) return;

            float baseRadius = radius * SmartAIMod.Settings.acousticRadiusMultiplier;
            if (baseRadius <= 0.5f) return;

            // A distress call comes from the hurt colonist, not from the danger. Listeners react to the
            // attacker nearest the victim; if none is known, the call carries no usable threat position.
            IntVec3 threatPos = position;
            if (isDistress)
            {
                threatPos = FindNearestColonyThreat(map, position, DistressThreatSearchRadius);
                if (!threatPos.IsValid) return;
                if (SmartLog.IsEnabled)
                    SmartLog.Message("Distress", $"{source.LabelShort} hurt at {position}; nearest attacker at {threatPos}");
            }

            int now = PawnStateTracker.Now;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn listener = colonists[i];
                if (listener == null || listener == source || listener == instigator || listener.Dead || listener.Downed)
                    continue;
                if (listener.Drafted)
                    continue;

                if (!isDistress && !instigator.HostileTo(listener))
                    continue;

                PawnTacticalState state = PawnStateTracker.GetOrCreate(listener);
                if (now < state.nextClamorEvalTick)
                    continue;

                float hearing = listener.health?.capacities?.GetLevel(PawnCapacityDefOf.Hearing) ?? 1f;
                if (hearing <= 0.05f) continue;

                float maxDist = baseRadius * hearing;
                if (!listener.Position.InHorDistOf(position, maxDist))
                    continue;

                state.nextClamorEvalTick = now + 90;
                state.RecordThreat(threatPos, 250, now);

                if (category == ClamorCategory.Gunfire || category == ClamorCategory.Impact || isDistress)
                {
                    CombatUtility.NotifyThreatPerceived(listener, threatPos);
                }
                else if (category == ClamorCategory.Construction && listener.CurJob?.def?.joyKind != null)
                {
                    listener.jobs?.CheckForJobOverride();
                }
            }
        }

        private const float DistressThreatSearchRadius = 30f;

        private static IntVec3 FindNearestColonyThreat(Map map, IntVec3 center, float maxDist)
        {
            HashSet<IAttackTarget> hostiles = map.attackTargetsCache?.TargetsHostileToColony;
            if (hostiles == null || hostiles.Count == 0)
                return IntVec3.Invalid;

            IntVec3 best = IntVec3.Invalid;
            float bestDistSq = maxDist * maxDist;
            foreach (IAttackTarget target in hostiles)
            {
                Thing t = target?.Thing;
                if (t == null || !t.Spawned || target.ThreatDisabled(null))
                    continue;

                float distSq = t.Position.DistanceToSquared(center);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = t.Position;
                }
            }
            return best;
        }
    }
}
