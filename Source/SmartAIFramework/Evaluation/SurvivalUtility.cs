using RimWorld;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Evaluation
{
    public static class SurvivalUtility
    {
        public const float DayTicks = 60000f;
        public const float DownedBloodLossThreshold = 0.58f;

        public static int TicksUntilBleedOut(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed) return 0;
            HediffSet hediffSet = pawn.health?.hediffSet;
            if (hediffSet == null) return int.MaxValue;

            float bleedRate = hediffSet.BleedRateTotal;
            if (bleedRate <= 0.001f) return int.MaxValue;

            Hediff bloodLoss = hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            float currentSeverity = bloodLoss?.Severity ?? 0f;

            float remaining = DownedBloodLossThreshold - currentSeverity;
            if (remaining <= 0f) return 0;

            float ticks = (remaining / bleedRate) * DayTicks;
            return Mathf.Max(0, Mathf.RoundToInt(ticks));
        }

        public static bool IsInBleedOutCrisis(Pawn pawn, int horizonTicks = 3000)
        {
            return TicksUntilBleedOut(pawn) <= horizonTicks;
        }

        public static float GetSpoilageUrgency(Thing thing)
        {
            if (thing == null) return 0f;
            CompRottable rot = thing.TryGetComp<CompRottable>();
            if (rot == null) return 0f;

            int ticksLeft = rot.TicksUntilRotAtCurrentTemp;
            if (ticksLeft <= 0) return 0f;

            float daysLeft = ticksLeft / DayTicks;
            if (daysLeft > 3f) return 10f;

            float score = 100f / (1f + daysLeft * 1.5f);
            return Mathf.Clamp(score, 0f, 100f);
        }

        public static float GetThermalRisk(Pawn pawn, IntVec3 cell, Map map)
        {
            if (pawn == null || map == null || !cell.InBounds(map)) return 0f;

            float ambientTemp = GenTemperature.GetTemperatureForCell(cell, map);
            if (ambientTemp >= 12f && ambientTemp <= 32f)
                return 0f;

            float minComfort = pawn.GetStatValue(StatDefOf.ComfyTemperatureMin);
            float maxComfort = pawn.GetStatValue(StatDefOf.ComfyTemperatureMax);

            if (ambientTemp >= minComfort && ambientTemp <= maxComfort)
                return 0f;

            float deviation = ambientTemp < minComfort ? (minComfort - ambientTemp) : (ambientTemp - maxComfort);
            return Mathf.Clamp01(deviation / 20f) * 100f;
        }

        public static float GetEnvironmentalRisk(Pawn pawn, IntVec3 cell, Map map, JobDef jobDef = null)
        {
            if (pawn == null || map == null || !cell.InBounds(map)) return 0f;

            float hazard = GetThermalRisk(pawn, cell, map);
            if (cell.Roofed(map))
                return hazard;

            GameConditionManager condMgr = map.gameConditionManager;
            if (condMgr != null)
            {
                if (condMgr.ConditionIsActive(GameConditionDefOf.ToxicFallout) ||
                    (ModsConfig.BiotechActive && condMgr.ConditionIsActive(GameConditionDefOf.NoxiousHaze)))
                {
                    if (hazard < 85f) hazard = 85f;
                }
            }

            if (jobDef == JobDefOf.Clean && map.weatherManager?.curWeather != null && map.weatherManager.curWeather.rainRate > 0.1f)
            {
                TerrainDef terrain = cell.GetTerrain(map);
                if (terrain != null && !terrain.IsFloor && hazard < 80f)
                {
                    hazard = 80f;
                }
            }

            return Mathf.Clamp(hazard, 0f, 100f);
        }
    }
}
