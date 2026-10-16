using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Evaluation;
using SmartAIFramework.Work;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Combat
{
    public enum RetreatKind
    {
        None = 0,
        LineOfSightCover = 1,
        AlliedGroup = 2,
        EnclosedRoom = 3
    }

    public struct RetreatSpot
    {
        public IntVec3 cell;
        public RetreatKind kind;
        public float score;

        public static readonly RetreatSpot Invalid = new RetreatSpot
        {
            cell = IntVec3.Invalid,
            kind = RetreatKind.None,
            score = 0f
        };
    }

    public static class SafeCellFinder
    {
        public static RetreatSpot FindRetreatSpot(Pawn pawn, IntVec3 threatPos, float maxDist = 45f)
        {
            if (pawn?.Map == null || !pawn.Spawned)
                return RetreatSpot.Invalid;

            Map map = pawn.Map;

            if (TryFindEnclosedRoomViaRegions(pawn, map, maxDist, out IntVec3 roomCell, out float roomDist))
            {
                float distBonus = Mathf.Clamp01(1f - roomDist / maxDist) * 30f;
                return new RetreatSpot
                {
                    cell = roomCell,
                    kind = RetreatKind.EnclosedRoom,
                    score = 70f + distBonus
                };
            }

            IntVec3 allyCluster = FindAlliedGroupCenter(pawn, map, maxDist);
            if (allyCluster.IsValid)
            {
                float dist = pawn.Position.DistanceTo(allyCluster);
                return new RetreatSpot
                {
                    cell = allyCluster,
                    kind = RetreatKind.AlliedGroup,
                    score = 55f + Mathf.Clamp01(1f - dist / maxDist) * 20f
                };
            }

            if (threatPos.IsValid && threatPos != pawn.Position)
            {
                IntVec3 shadowCell = FindNearbyLoSBreaker(pawn, map, threatPos);
                if (shadowCell.IsValid)
                {
                    float dist = pawn.Position.DistanceTo(shadowCell);
                    return new RetreatSpot
                    {
                        cell = shadowCell,
                        kind = RetreatKind.LineOfSightCover,
                        score = 45f + Mathf.Clamp01(1f - dist / 12f) * 20f
                    };
                }
            }

            return RetreatSpot.Invalid;
        }

        private static bool TryFindEnclosedRoomViaRegions(Pawn pawn, Map map, float maxDist, out IntVec3 foundCell, out float foundDist)
        {
            foundCell = IntVec3.Invalid;
            foundDist = 0f;

            Region startReg = pawn.GetRegion(RegionType.Set_Passable);
            if (startReg == null) return false;

            Area_Home home = map.areaManager?.Home;
            if (home == null) return false;

            IntVec3 pawnPos = pawn.Position;
            float maxDistSq = maxDist * maxDist;
            TraverseParms traverseParms = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn);

            IntVec3 bestCell = IntVec3.Invalid;
            float bestDistSq = float.MaxValue;

            RegionTraverser.BreadthFirstTraverse(
                startReg,
                (Region from, Region r) => r.Allows(traverseParms, isDestination: false),
                (Region r) =>
                {
                    if (r.type != RegionType.Normal || r.IsDoorway)
                        return false;

                    Room room = r.Room;
                    if (room == null || room.PsychologicallyOutdoors || room.UsesOutdoorTemperature)
                        return false;

                    if (room.Temperature > 55f)
                        return false;

                    IntVec3 sample = r.AnyCell;
                    if (!home[sample])
                        return false;

                    float approxDistSq = pawnPos.DistanceToSquared(sample);
                    if (approxDistSq > maxDistSq)
                        return false;

                    foreach (IntVec3 c in r.Cells)
                    {
                        if (!c.Standable(map) || c.ContainsStaticFire(map) || c.ContainsTrap(map))
                            continue;
                        if (!home[c] || c == pawnPos)
                            continue;
                        if (UtilityEvaluator.GetDangerAt(map, c, pawn) > 10f)
                            continue;

                        float dSq = pawnPos.DistanceToSquared(c);
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestCell = c;
                            break;
                        }
                    }

                    return bestCell.IsValid;
                },
                maxRegions: 28,
                traversableRegionTypes: RegionType.Set_Passable);

            if (bestCell.IsValid)
            {
                foundCell = bestCell;
                foundDist = Mathf.Sqrt(bestDistSq);
                return true;
            }

            return false;
        }

        private static IntVec3 FindAlliedGroupCenter(Pawn pawn, Map map, float maxDist)
        {
            List<Pawn> colonists = map.mapPawns?.FreeColonistsSpawned;
            if (colonists == null || colonists.Count <= 1)
                return IntVec3.Invalid;

            IntVec3 pawnPos = pawn.Position;
            float maxDistSq = maxDist * maxDist;
            District myDistrict = pawn.GetDistrict();

            float totalWeight = 0f;
            Vector3 weightedSum = Vector3.zero;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn ally = colonists[i];
                if (ally == null || ally == pawn || ally.Dead || ally.Downed)
                    continue;
                if (ally.Position.DistanceToSquared(pawnPos) > maxDistSq)
                    continue;
                if (myDistrict != null && ally.GetDistrict() != myDistrict)
                    continue;

                float cap = UtilityEvaluator.GetSelfCapability(ally);
                if (cap < 30f) continue;

                weightedSum += ally.Position.ToVector3() * cap;
                totalWeight += cap;
            }

            if (totalWeight <= 0f)
                return IntVec3.Invalid;

            IntVec3 center = (weightedSum / totalWeight).ToIntVec3();
            IntVec3[] pattern = GenRadial.RadialPattern;

            for (int i = 0; i < 25; i++)
            {
                IntVec3 c = center + pattern[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.ContainsTrap(map))
                    continue;
                if (myDistrict != null && c.GetDistrict(map) != myDistrict)
                    continue;
                if (UtilityEvaluator.GetDangerAt(map, c, pawn) <= 20f)
                    return c;
            }

            return IntVec3.Invalid;
        }

        private static IntVec3 FindNearbyLoSBreaker(Pawn pawn, Map map, IntVec3 threatPos)
        {
            IntVec3 pawnPos = pawn.Position;
            int baseDistSq = pawnPos.DistanceToSquared(threatPos);
            District myDistrict = pawn.GetDistrict();
            IntVec3[] pattern = GenRadial.RadialPattern;

            int searchCount = GenRadial.NumCellsInRadius(7.5f);
            int raycastsRemaining = 4;

            for (int i = 1; i < searchCount; i++)
            {
                IntVec3 c = pawnPos + pattern[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.ContainsTrap(map) || c.ContainsStaticFire(map))
                    continue;
                if (myDistrict != null && c.GetDistrict(map) != myDistrict)
                    continue;

                if (c.DistanceToSquared(threatPos) < baseDistSq - 4)
                    continue;

                if (!HasAdjacentSolidBlockerToward(map, c, threatPos))
                    continue;

                if (UtilityEvaluator.GetDangerAt(map, c, pawn) > 15f)
                    continue;

                raycastsRemaining--;
                if (!GenSight.LineOfSight(threatPos, c, map, skipFirstCell: true))
                {
                    return c;
                }

                if (raycastsRemaining <= 0)
                    break;
            }

            return IntVec3.Invalid;
        }

        private static bool HasAdjacentSolidBlockerToward(Map map, IntVec3 cell, IntVec3 threatPos)
        {
            int dx = threatPos.x - cell.x;
            int dz = threatPos.z - cell.z;

            for (int i = 0; i < 4; i++)
            {
                IntVec3 dir = GenAdj.CardinalDirections[i];
                if (dir.x * dx + dir.z * dz <= 0)
                    continue;

                IntVec3 adj = cell + dir;
                if (!adj.InBounds(map)) continue;

                Building edifice = adj.GetEdifice(map);
                if (edifice?.def != null &&
                    (edifice.def.passability == Traversability.Impassable || edifice.def.fillPercent >= 0.65f))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
