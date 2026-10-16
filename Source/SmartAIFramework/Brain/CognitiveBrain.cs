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
        public float currentDanger;
        public float negativeConsequence;
    }

    public static class CognitiveBrain
    {
        public const float Max = 100f;

        public static float Evaluate(in UtilityInput i)
        {
            // 1. High urgency (life-saving/survival emergencies) overrides low capability:
            // A severely wounded/weak pawn still desperately desires to survive and escape.
            float capRatio = Mathf.Clamp01(i.selfCapability / Max);
            float emergencyBlend = Mathf.Clamp01((i.urgency - 40f) / 60f);
            float capFactor = Mathf.Lerp(0.50f + 0.50f * capRatio, 1.0f, emergencyBlend);

            // 2. Efficiency (distance) provides travel friction but has a protective base floor (no zero kill-switch):
            float effRatio = Mathf.Clamp01(i.efficiency / Max);
            float effFactor = 0.35f + 0.65f * effRatio;

            float benefit = i.urgency * capFactor * effFactor;

            // 3. Danger of Action vs Danger of Inaction:
            // If the pawn is already in a hazardous spot (e.g. 150C burning room or under fire),
            // moving to a safer cell (even with mild hazard) reduces net danger and is rewarded.
            float netDanger = Mathf.Max(0f, i.danger - (i.currentDanger * 0.85f));
            float cost = netDanger + i.negativeConsequence;

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
            float healthPct = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;

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

        public static float Efficiency(IntVec3 from, IntVec3 to, float maxSensibleDistance = 120f)
        {
            float dist = from.DistanceTo(to);
            float linear = Mathf.Clamp01(1f - dist / maxSensibleDistance);
            return Mathf.Max(20f, linear * Max);
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
            if (!evt.IsHostileTo(listener)) return;

            Core.Knowledge.KnowledgeBase.Writer(listener)?.RecordNoise(evt.kind, evt.position, 250);
            Core.Signals.SignalBus.Publish(new Core.Signals.NoiseHeardSignal(listener, evt));

            if (evt.kind == AcousticKind.Gunfire || evt.kind == AcousticKind.Impact || evt.kind == AcousticKind.CryForHelp)
                Modules.Module_Combat.OnThreatPerceived(listener, evt.position);
        }

        public static void NotifyDamaged(Pawn victim, DamageInfo dinfo)
        {
            Core.Signals.SignalBus.Publish(new Core.Signals.DamageTakenSignal(victim, dinfo, dinfo.Amount));
            Modules.Module_Combat.OnDamaged(victim, dinfo);
        }

        public static void NotifyTargeted(Pawn target, Pawn aggressor)
        {
            if (target == null || aggressor == null || !aggressor.HostileTo(target)) return;

            Core.Knowledge.KnowledgeBase.Writer(target)?.AddAimer(aggressor);
            Core.Signals.SignalBus.Publish(new Core.Signals.TargetedSignal(target, aggressor));
            Modules.Module_Combat.OnThreatPerceived(target, aggressor.Position);
        }
    }
}
