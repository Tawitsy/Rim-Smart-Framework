using RimWorld;
using SmartAIFramework.Data;
using SmartAIFramework.Grid;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Evaluation
{
    public struct ActionEvalInput
    {
        public float urgency;
        public float selfCapability;
        public float efficiency;
        public float danger;
        public float currentDanger;
        public float negativeConsequence;
    }

    public static class UtilityEvaluator
    {
        public const float MaxScore = 100f;

        public static float Evaluate(in ActionEvalInput input)
        {
            float capRatio = Mathf.Clamp01(input.selfCapability / MaxScore);
            float emergencyBlend = Mathf.Clamp01((input.urgency - 40f) / 60f);
            float capFactor = Mathf.Lerp(0.50f + 0.50f * capRatio, 1.0f, emergencyBlend);

            float effRatio = Mathf.Clamp01(input.efficiency / MaxScore);
            float effFactor = 0.35f + 0.65f * effRatio;

            float benefit = input.urgency * capFactor * effFactor;
            float netDanger = Mathf.Max(0f, input.danger - (input.currentDanger * 0.85f));
            float cost = netDanger + input.negativeConsequence;

            float score = benefit - cost;
            return API.SmartAIApi.ApplyScoreOverrides(score, input);
        }

        public static float GetSelfCapability(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed) return 0f;

            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);
            if (state != null)
            {
                int now = PawnStateTracker.Now;
                if (state.cachedCapability >= 0f &&
                    state.capabilityVersion == state.version &&
                    now - state.capabilityComputedTick < 600)
                {
                    return state.cachedCapability;
                }

                float computed = ComputeSelfCapability(pawn);
                state.cachedCapability = computed;
                state.capabilityVersion = state.version;
                state.capabilityComputedTick = now;
                return computed;
            }

            return ComputeSelfCapability(pawn);
        }

        private static float ComputeSelfCapability(Pawn pawn)
        {
            PawnCapacitiesHandler caps = pawn.health?.capacities;
            if (caps == null) return 0f;

            float consciousness = caps.GetLevel(PawnCapacityDefOf.Consciousness);
            float moving = caps.GetLevel(PawnCapacityDefOf.Moving);
            float healthPct = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;

            int ticksUntilDowned = SurvivalUtility.TicksUntilBleedOut(pawn);
            float bleedFactor = ticksUntilDowned < 6000 ? Mathf.Clamp01(ticksUntilDowned / 6000f) : 1f;

            float armor = Mathf.Clamp01(pawn.GetStatValue(StatDefOf.ArmorRating_Sharp));
            float weaponScore = 0.3f;
            Verb verb = pawn.TryGetAttackVerb(null, false, false);
            if (verb != null)
            {
                VerbProfile profile = VerbProfileCache.Get(verb);
                if (profile != null)
                {
                    weaponScore = Mathf.Clamp01(0.3f + profile.damage / 40f + profile.range / 60f);
                }
            }

            float raw = consciousness * 0.30f + moving * 0.15f + healthPct * 0.25f + weaponScore * 0.20f + armor * 0.10f;
            return Mathf.Clamp(raw * bleedFactor * MaxScore, 0f, MaxScore);
        }

        public static float GetDistanceEfficiency(IntVec3 from, IntVec3 to, float maxSensibleDistance = 120f)
        {
            float dist = from.DistanceTo(to);
            float linear = Mathf.Clamp01(1f - dist / maxSensibleDistance);
            return Mathf.Max(20f, linear * MaxScore);
        }

        public static float GetDangerAt(Map map, IntVec3 cell, Pawn observer = null)
        {
            if (map == null || !cell.InBounds(map)) return 0f;

            TacticalGrid grid = TacticalGrid.Get(map);
            float danger = grid != null ? grid.DangerAt(cell) : 0f;

            if (observer != null)
            {
                float envRisk = SurvivalUtility.GetEnvironmentalRisk(observer, cell, map);
                if (envRisk > danger) danger = envRisk;
            }

            return danger > 100f ? 100f : danger;
        }
    }
}
