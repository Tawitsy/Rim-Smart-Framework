using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Data;
using SmartAIFramework.Decisions;
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

            // Every bullet impact lands here, so the per-listener work is ordered cheapest first: an integer
            // distance cut (generous enough for enhanced hearing), then flags, cooldown, and only then the
            // hostility and hearing-capacity lookups.
            float cutoff = baseRadius * MaxHearingFactor;
            float cutoffSq = cutoff * cutoff;
            bool alertRaised = false;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn listener = colonists[i];
                if (listener == null || listener == source || listener == instigator)
                    continue;
                if (listener.Position.DistanceToSquared(position) > cutoffSq)
                    continue;
                if (listener.Dead || listener.Downed || listener.Drafted)
                    continue;

                PawnTacticalState existing = PawnStateTracker.TryGet(listener);
                if (existing != null && now < existing.nextClamorEvalTick)
                    continue;

                if (!isDistress && !instigator.HostileTo(listener))
                    continue;

                float hearing = listener.health?.capacities?.GetLevel(PawnCapacityDefOf.Hearing) ?? 1f;
                if (hearing <= 0.05f) continue;

                float maxDist = baseRadius * hearing;
                if (!listener.Position.InHorDistOf(position, maxDist))
                    continue;

                // One raise per clamor, by the first colonist that actually hears hostile fire.
                if (!isDistress && !alertRaised)
                {
                    alertRaised = true;
                    ColonyAlert.Raise(map, AlertLevel.Alert);
                }

                PawnTacticalState state = existing ?? PawnStateTracker.GetOrCreate(listener);
                state.nextClamorEvalTick = now + 90;
                state.RecordThreat(threatPos, 250, now);

                if (category == ClamorCategory.Gunfire || category == ClamorCategory.Impact || isDistress)
                {
                    if (ModuleFlags.Combat)
                        CombatUtility.NotifyThreatPerceived(listener, threatPos, urgent: false);
                }
                else if (category == ClamorCategory.Construction && listener.CurJob?.def?.joyKind != null)
                {
                    JobDirector.RequestRethink(listener);
                }
            }
        }

        private const float DistressThreatSearchRadius = 30f;
        private const float MaxHearingFactor = 2f;

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
