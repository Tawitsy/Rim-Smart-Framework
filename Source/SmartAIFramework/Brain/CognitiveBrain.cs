using RimWorld;
using SmartAIFramework.Core_Engine;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Brain
{
    public struct UtilityInput
    {
        public float urgency;
        public float selfCapability;
        public float efficiency;
        public float danger;
        public float negativeConsequence;
    }

    public static class CognitiveBrain
    {
        public const float Max = 100f;

        public static float Evaluate(in UtilityInput i)
        {
            float benefit = (i.urgency / Max) * (i.selfCapability / Max) * (i.efficiency / Max) * Max;
            float cost = i.danger + i.negativeConsequence;
            float utility = benefit - cost;
            return Public_API.SmartAIApi.ApplyScoreOverrides(utility, i);
        }

        public static float SelfCapability(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed) return 0f;
            var mem = Core.Knowledge.KnowledgeBase.PeekMemory(pawn);
            if (mem != null)
            {
                return mem.selfCapability.Get(mem.Version, 600, () => ComputeSelfCapability(pawn));
            }
            return ComputeSelfCapability(pawn);
        }

        private static float ComputeSelfCapability(Pawn pawn)
        {
            var caps = pawn.health?.capacities;
            if (caps == null) return 0f;

            float consciousness = caps.GetLevel(PawnCapacityDefOf.Consciousness);
            float moving = caps.GetLevel(PawnCapacityDefOf.Moving);
            float healthPct = pawn.health.summaryHealth.SummaryHealthPercent;

            int ticksUntilDowned = DeterministicPredictor.PredictTicksUntilDowned(pawn);
            float physiologicalFactor = 1f;
            if (ticksUntilDowned < 6000)
            {
                physiologicalFactor = Mathf.Clamp01(ticksUntilDowned / 6000f);
            }

            float armor = Mathf.Clamp01(pawn.GetStatValue(StatDefOf.ArmorRating_Sharp));
            float weapon = 0.3f;
            var verb = pawn.TryGetAttackVerb(null, false, false);
            if (verb != null)
            {
                var profile = ModActionAnalyzer.Analyze(verb);
                if (profile != null) weapon = Mathf.Clamp01(0.3f + profile.damage / 40f + profile.range / 60f);
            }

            float score = consciousness * 0.30f + moving * 0.15f + healthPct * 0.25f + weapon * 0.20f + armor * 0.10f;
            score *= physiologicalFactor;
            return Mathf.Clamp(score * Max, 0f, Max);
        }

        public static float Efficiency(IntVec3 from, IntVec3 to, float maxSensibleDistance = 60f)
        {
            float dist = from.DistanceTo(to);
            return Mathf.Clamp01(1f - dist / maxSensibleDistance) * Max;
        }

        public static float DangerAt(Map map, IntVec3 cell, Pawn observer = null)
        {
            var grid = TacticalGridMapComponent.For(map);
            float d = grid?.DangerAt(cell) ?? 0f;

            if (observer != null && map != null)
            {
                float envRisk = DeterministicPredictor.PredictEnvironmentalHazard(observer, cell, map);
                d = Mathf.Max(d, envRisk);
            }

            return Mathf.Min(d, 100f);
        }

        public static void NotifyHeard(Pawn listener, AcousticEvent evt)
        {
            if (evt.kind == AcousticKind.Gunfire || evt.kind == AcousticKind.Impact)
                Modules.Module_Combat.OnThreatPerceived(listener, evt.position);
        }

        public static void NotifyDamaged(Pawn victim, DamageInfo dinfo)
        {
            Modules.Module_Combat.OnDamaged(victim, dinfo);
        }

        public static void NotifyTargeted(Pawn target, Pawn aggressor)
        {
            Modules.Module_Combat.OnThreatPerceived(target, aggressor.Position);
        }
    }
}
