using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Brain;
using SmartAIFramework.Core_Engine;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Modules
{
    public enum SafetyTier
    {
        None = 0,
        TopologicalShadow = 1,
        AllyBastion = 2,
        FortifiedRoom = 3
    }

    public struct SafetyAssessment
    {
        public IntVec3 cell;
        public SafetyTier tier;
        public float safetyScore;
        public string description;

        public static readonly SafetyAssessment None = new SafetyAssessment
        {
            cell = IntVec3.Invalid,
            tier = SafetyTier.None,
            safetyScore = 0f,
            description = "No safe zone found"
        };
    }

    public static class DynamicSanctuaryFinder
    {
        public static SafetyAssessment FindBestSafety(Pawn pawn, IntVec3 threatPos, float maxSearchDist = 45f)
        {
            if (pawn?.Map == null) return SafetyAssessment.None;
            Map map = pawn.Map;

            SafetyAssessment best = SafetyAssessment.None;

            foreach (IntVec3 c in GenRadial.RadialCellsAround(pawn.Position, maxSearchDist, false))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.GetDoor(map) != null) continue;
                if (!Module_Structure.IsSanctuary(map, c)) continue;
                if (CognitiveBrain.DangerAt(map, c, pawn) > 10f) continue;

                if (pawn.CanReach(c, Verse.AI.PathEndMode.OnCell, Danger.Deadly))
                {
                    float distScore = Mathf.Clamp01(1f - pawn.Position.DistanceTo(c) / maxSearchDist) * 30f;
                    float score = 70f + distScore;
                    best = new SafetyAssessment
                    {
                        cell = c,
                        tier = SafetyTier.FortifiedRoom,
                        safetyScore = score,
                        description = "Fortified home sanctuary"
                    };
                    break;
                }
            }

            if (best.tier == SafetyTier.FortifiedRoom) return best;

            IntVec3 allyClusterCenter = FindAllyBastionCenter(pawn, threatPos, maxSearchDist);
            if (allyClusterCenter.IsValid && pawn.CanReach(allyClusterCenter, Verse.AI.PathEndMode.OnCell, Danger.Deadly))
            {
                float dist = pawn.Position.DistanceTo(allyClusterCenter);
                float score = 55f + Mathf.Clamp01(1f - dist / maxSearchDist) * 20f;
                if (score > best.safetyScore)
                {
                    best = new SafetyAssessment
                    {
                        cell = allyClusterCenter,
                        tier = SafetyTier.AllyBastion,
                        safetyScore = score,
                        description = "Allied defensive perimeter"
                    };
                }
            }

            if (best.tier == SafetyTier.AllyBastion) return best;

            if (threatPos.IsValid)
            {
                IntVec3 losShadowCell = FindTopologicalShadow(pawn, threatPos, 25f);
                if (losShadowCell.IsValid && pawn.CanReach(losShadowCell, Verse.AI.PathEndMode.OnCell, Danger.Deadly))
                {
                    float dist = pawn.Position.DistanceTo(losShadowCell);
                    float score = 40f + Mathf.Clamp01(1f - dist / 25f) * 20f;
                    if (score > best.safetyScore)
                    {
                        best = new SafetyAssessment
                        {
                            cell = losShadowCell,
                            tier = SafetyTier.TopologicalShadow,
                            safetyScore = score,
                            description = "Natural terrain LoS shadow"
                        };
                    }
                }
            }

            return best;
        }

        private static IntVec3 FindAllyBastionCenter(Pawn pawn, IntVec3 threatPos, float maxDist)
        {
            Map map = pawn.Map;
            var pawns = map.mapPawns.FreeColonistsSpawned;
            float totalWeight = 0f;
            Vector3 center = Vector3.zero;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn ally = pawns[i];
                if (ally == pawn || ally.Dead || ally.Downed) continue;
                if (ally.Position.DistanceTo(pawn.Position) > maxDist) continue;

                float combatPower = CognitiveBrain.SelfCapability(ally);
                if (combatPower < 30f) continue;

                center += ally.Position.ToVector3() * combatPower;
                totalWeight += combatPower;
            }

            if (totalWeight <= 0f) return IntVec3.Invalid;

            IntVec3 avgCell = (center / totalWeight).ToIntVec3();

            foreach (IntVec3 c in GenRadial.RadialCellsAround(avgCell, 5f, true))
            {
                if (!c.InBounds(map) || !c.Standable(map)) continue;
                if (CognitiveBrain.DangerAt(map, c, pawn) > 20f) continue;
                return c;
            }

            return avgCell.InBounds(map) && avgCell.Standable(map) ? avgCell : IntVec3.Invalid;
        }

        private static IntVec3 FindTopologicalShadow(Pawn pawn, IntVec3 threatPos, float radius)
        {
            Map map = pawn.Map;
            IntVec3 bestCell = IntVec3.Invalid;
            float maxDistFromThreat = threatPos.DistanceTo(pawn.Position);

            foreach (IntVec3 c in GenRadial.RadialCellsAround(pawn.Position, radius, false))
            {
                if (!c.InBounds(map) || !c.Standable(map)) continue;
                if (CognitiveBrain.DangerAt(map, c, pawn) > 15f) continue;

                if (GenSight.LineOfSight(threatPos, c, map, true)) continue;

                float distToThreat = c.DistanceTo(threatPos);
                if (distToThreat >= maxDistFromThreat - 2f)
                {
                    bestCell = c;
                    break;
                }
            }

            return bestCell;
        }
    }
}
