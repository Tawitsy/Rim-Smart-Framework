using RimWorld;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Brain
{
    public static class DeterministicPredictor
    {
        public const float DayTicks = 60000f;
        public const float DownedBloodLossThreshold = 0.58f;
        public const float CriticalConsciousnessThreshold = 0.30f;

        public static int PredictTicksUntilDowned(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed) return 0;
            var hediffSet = pawn.health?.hediffSet;
            if (hediffSet == null) return int.MaxValue;

            float bleedRate = hediffSet.BleedRateTotal;
            if (bleedRate <= 0.001f) return int.MaxValue;

            Hediff bloodLoss = hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            float currentSeverity = bloodLoss?.Severity ?? 0f;

            float remainingSeverity = DownedBloodLossThreshold - currentSeverity;
            if (remainingSeverity <= 0f) return 0;

            float ticksToDowned = (remainingSeverity / bleedRate) * DayTicks;
            return Mathf.Max(0, Mathf.RoundToInt(ticksToDowned));
        }

        public static bool IsInImmediatePhysiologicalCrisis(Pawn pawn, int horizonTicks = 3000)
        {
            return PredictTicksUntilDowned(pawn) <= horizonTicks;
        }

        public static int PredictTimeToImpact(Verb verb, IntVec3 targetCell)
        {
            if (verb == null) return int.MaxValue;

            int warmupLeft = 0;
            if (verb.CasterPawn?.stances?.curStance is Stance_Warmup warmup)
            {
                warmupLeft = Mathf.Max(0, warmup.ticksLeft);
            }

            var projProps = verb.verbProps?.defaultProjectile?.projectile;
            if (projProps == null) return warmupLeft;

            float speedTilesPerTick = projProps.SpeedTilesPerTick;
            if (speedTilesPerTick <= 0.001f) return warmupLeft;

            float distance = verb.caster.Position.DistanceTo(targetCell);
            int flightTicks = Mathf.RoundToInt(distance / speedTilesPerTick);

            return warmupLeft + flightTicks;
        }

        public static bool CanEvacuateCell(Pawn pawn, float distanceTiles, int availableTicks)
        {
            if (pawn == null || availableTicks <= 0) return false;
            float tilesPerSecond = pawn.GetStatValue(StatDefOf.MoveSpeed);
            if (tilesPerSecond <= 0.1f) return false;

            float ticksPerTile = 60f / tilesPerSecond;
            float requiredTicks = distanceTiles * ticksPerTile;

            return requiredTicks <= availableTicks;
        }

        public static float PredictSpoilageUrgency(Thing thing)
        {
            if (thing == null) return 0f;
            var comp = thing.TryGetComp<CompRottable>();
            if (comp == null) return 0f;

            int ticksLeft = comp.TicksUntilRotAtCurrentTemp;
            if (ticksLeft <= 0) return 0f;

            float daysLeft = ticksLeft / DayTicks;
            if (daysLeft > 3f) return 10f;

            float score = 100f / (1f + daysLeft * 1.5f);
            return Mathf.Clamp(score, 0f, 100f);
        }

        public static float PredictThermalRisk(Pawn pawn, IntVec3 cell, Map map)
        {
            if (pawn == null || map == null || !cell.InBounds(map)) return 0f;

            float ambientTemp = GenTemperature.GetTemperatureForCell(cell, map);
            float minComfort = pawn.GetStatValue(StatDefOf.ComfyTemperatureMin);
            float maxComfort = pawn.GetStatValue(StatDefOf.ComfyTemperatureMax);

            if (ambientTemp >= minComfort && ambientTemp <= maxComfort) return 0f;

            float deviation = ambientTemp < minComfort ? (minComfort - ambientTemp) : (ambientTemp - maxComfort);

            return Mathf.Clamp01(deviation / 20f) * 100f;
        }

        public static float PredictEnvironmentalHazard(Pawn pawn, IntVec3 cell, Map map, JobDef jobDef = null)
        {
            if (pawn == null || map == null || !cell.InBounds(map)) return 0f;

            float hazard = 0f;
            bool isRoofed = cell.Roofed(map);

            float thermalRisk = PredictThermalRisk(pawn, cell, map);
            if (thermalRisk > hazard) hazard = thermalRisk;

            if (isRoofed) return hazard;

            if (map.gameConditionManager != null && map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout))
            {
                hazard = Mathf.Max(hazard, 85f);
            }

            if (jobDef == JobDefOf.Clean && map.weatherManager?.curWeather != null && map.weatherManager.curWeather.rainRate > 0.1f)
            {
                var terrain = cell.GetTerrain(map);
                if (terrain != null && !terrain.IsFloor)
                {
                    hazard = Mathf.Max(hazard, 80f);
                }
            }

            return Mathf.Clamp(hazard, 0f, 100f);
        }
    }
}
