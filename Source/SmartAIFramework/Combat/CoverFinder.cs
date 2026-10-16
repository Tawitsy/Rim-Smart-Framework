using RimWorld;
using SmartAIFramework.Data;
using SmartAIFramework.Evaluation;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Combat
{
    public struct CombatResponseOption
    {
        public TacticalActionType actionType;
        public JobDef jobDef;
        public LocalTargetInfo target;
        public float netScore;
    }

    public static class CoverFinder
    {
        public static CombatResponseOption EvaluateBestResponse(Pawn pawn, IntVec3 threatPos)
        {
            if (pawn?.Map == null || !pawn.Spawned)
                return default;

            Map map = pawn.Map;
            CombatResponseOption best = default;
            best.netScore = -1f;

            float selfCap = UtilityEvaluator.GetSelfCapability(pawn);
            int bleedTicks = SurvivalUtility.TicksUntilBleedOut(pawn);

            if (bleedTicks < SmartAIMod.Settings.crisisHorizonTicks)
            {
                Building_Bed bed = RestUtility.FindBedFor(pawn);
                LocalTargetInfo bedTarget = bed != null ? (LocalTargetInfo)bed : (LocalTargetInfo)pawn.Position;
                float score = 90f - (bleedTicks / 60f);
                if (score > best.netScore)
                {
                    best = new CombatResponseOption
                    {
                        actionType = TacticalActionType.SelfTriage,
                        jobDef = JobDefOf.LayDown,
                        target = bedTarget,
                        netScore = score
                    };
                }
            }

            RetreatSpot retreat = SafeCellFinder.FindRetreatSpot(pawn, threatPos, 45f);
            if (retreat.cell.IsValid)
            {
                float dist = pawn.Position.DistanceTo(retreat.cell);
                float travelRisk = (dist / 45f) * 20f + UtilityEvaluator.GetDangerAt(map, retreat.cell, pawn);
                float net = (retreat.score * 0.65f) + 15f - travelRisk;
                if (selfCap < 40f) net += 25f;

                if (net > best.netScore)
                {
                    best = new CombatResponseOption
                    {
                        actionType = TacticalActionType.Retreat,
                        jobDef = JobDefOf.Goto,
                        target = retreat.cell,
                        netScore = net
                    };
                }
            }

            if (TryFindDirectionalCover(pawn, threatPos, out IntVec3 coverCell, out float blockChance))
            {
                float dist = pawn.Position.DistanceTo(coverCell);
                float travelRisk = dist * 4f + UtilityEvaluator.GetDangerAt(map, coverCell, pawn);
                float survival = blockChance * 100f;
                float tactical = selfCap >= 40f ? 70f : 30f;
                float net = (survival * 0.55f) + (tactical * 0.45f) - travelRisk;

                if (net > best.netScore)
                {
                    best = new CombatResponseOption
                    {
                        actionType = TacticalActionType.SeekCover,
                        jobDef = JobDefOf.Goto,
                        target = coverCell,
                        netScore = net
                    };
                }
            }

            float currentCover = CoverUtility.CalculateOverallBlockChance(pawn.Position, threatPos, map);
            int distSqToThreat = pawn.Position.DistanceToSquared(threatPos);

            if (currentCover >= 0.50f && selfCap >= 40f && distSqToThreat > 16)
            {
                float holdScore = (currentCover * 50f) + 52.5f;
                if (holdScore > best.netScore)
                {
                    best = new CombatResponseOption
                    {
                        actionType = TacticalActionType.Sidestep,
                        jobDef = JobDefOf.Wait_Combat,
                        target = pawn.Position,
                        netScore = holdScore
                    };
                }
            }

            if (SmartAIMod.Settings.combatRangedKiting &&
                pawn.equipment?.Primary?.def.IsRangedWeapon == true &&
                distSqToThreat <= 20)
            {
                Pawn threatPawn = FindHostileAtOrAdjacent(map, pawn, threatPos);
                if (threatPawn != null && CombatUtility.TryFindKiteCell(pawn, threatPawn, out IntVec3 kiteCell))
                {
                    float distGain = kiteCell.DistanceTo(threatPos) - Mathf.Sqrt(distSqToThreat);
                    float cover = CoverUtility.CalculateOverallBlockChance(kiteCell, threatPos, map);
                    float kiteScore = 80f + (distGain * 5f) + (cover * 25f);

                    if (kiteScore > best.netScore)
                    {
                        best = new CombatResponseOption
                        {
                            actionType = TacticalActionType.Sidestep,
                            jobDef = JobDefOf.Goto,
                            target = kiteCell,
                            netScore = kiteScore
                        };
                    }
                }
            }

            return best;
        }

        private static Pawn FindHostileAtOrAdjacent(Map map, Pawn self, IntVec3 pos)
        {
            if (pos.InBounds(map))
            {
                Pawn p = pos.GetFirstPawn(map);
                if (p != null && p.HostileTo(self)) return p;
            }

            for (int i = 0; i < 4; i++)
            {
                IntVec3 adj = pos + GenAdj.CardinalDirections[i];
                if (!adj.InBounds(map)) continue;
                Pawn p = adj.GetFirstPawn(map);
                if (p != null && p.HostileTo(self)) return p;
            }
            return null;
        }

        public static bool TryFindDirectionalCover(Pawn pawn, IntVec3 threatPos, out IntVec3 bestCover, out float bestBlockChance)
        {
            bestCover = IntVec3.Invalid;
            bestBlockChance = 0f;
            Map map = pawn.Map;

            float currentCover = CoverUtility.CalculateOverallBlockChance(pawn.Position, threatPos, map);
            if (currentCover >= 0.55f) return false;

            float minRequiredBlock = currentCover + 0.15f;
            float bestScore = currentCover;

            float radius = Mathf.Clamp(SmartAIMod.Settings.coverSearchRadius, 3f, 10f);
            int cellCount = GenRadial.NumCellsInRadius(radius);
            IntVec3[] pattern = GenRadial.RadialPattern;
            District myDistrict = pawn.GetDistrict();
            IntVec3 pawnPos = pawn.Position;

            for (int i = 1; i < cellCount; i++)
            {
                IntVec3 c = pawnPos + pattern[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null)
                    continue;
                if (myDistrict != null && c.GetDistrict(map) != myDistrict)
                    continue;
                if (c.ContainsStaticFire(map) || c.ContainsTrap(map))
                    continue;
                if (UtilityEvaluator.GetDangerAt(map, c, pawn) > 20f)
                    continue;
                if (!HasSafeAdjacentCoverBuilding(map, c))
                    continue;

                float block = CoverUtility.CalculateOverallBlockChance(c, threatPos, map);
                if (block > minRequiredBlock && block > bestScore)
                {
                    bestScore = block;
                    bestCover = c;
                    bestBlockChance = block;
                }
            }

            if (bestCover.IsValid && pawn.CanReach(bestCover, PathEndMode.OnCell, Danger.Deadly))
            {
                return true;
            }

            bestCover = IntVec3.Invalid;
            return false;
        }

        private static bool HasSafeAdjacentCoverBuilding(Map map, IntVec3 cell)
        {
            bool hasCoverPiece = false;

            for (int i = 0; i < 4; i++)
            {
                IntVec3 adj = cell + GenAdj.CardinalDirections[i];
                if (!adj.InBounds(map)) continue;

                Building edifice = adj.GetEdifice(map);
                if (edifice?.def == null) continue;

                ThingProfile profile = ThingProfileCache.Get(edifice.def);
                if (edifice.IsBurning() || (profile != null && profile.isExplosive))
                    return false;

                if (edifice.def.fillPercent >= 0.25f || edifice.def.passability == Traversability.Impassable)
                    hasCoverPiece = true;
            }

            return hasCoverPiece;
        }
    }
}
