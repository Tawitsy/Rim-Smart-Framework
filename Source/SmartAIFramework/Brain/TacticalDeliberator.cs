using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Core.Decisions;
using SmartAIFramework.Modules;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Brain
{
    public struct TacticalCandidate
    {
        public IntentKind kind;
        public JobDef jobDef;
        public LocalTargetInfo target;
        public float survivalProbability;
        public float tacticalValue;
        public float travelRisk;
        public float netScore;
        public string reasoning;
    }

    public static class TacticalDeliberator
    {
        public static TacticalCandidate Deliberate(Pawn pawn, IntVec3 threatPos, bool isDamaged)
        {
            if (pawn?.Map == null) return default;
            Map map = pawn.Map;

            var candidates = new List<TacticalCandidate>(4);
            float selfCap = CognitiveBrain.SelfCapability(pawn);
            int ticksUntilDowned = DeterministicPredictor.PredictTicksUntilDowned(pawn);

            if (ticksUntilDowned < SmartAIMod.Settings.crisisHorizonTicks)
            {
                Building_Bed bed = RestUtility.FindBedFor(pawn);
                LocalTargetInfo target = bed != null ? (LocalTargetInfo)bed : (LocalTargetInfo)pawn.Position;

                candidates.Add(new TacticalCandidate
                {
                    kind = IntentKind.TendSelf,
                    jobDef = JobDefOf.LayDown,
                    target = target,
                    survivalProbability = 95f,
                    tacticalValue = 10f,
                    travelRisk = CognitiveBrain.DangerAt(map, target.Cell, pawn) * 0.5f,
                    netScore = 90f - (ticksUntilDowned / 60f),
                    reasoning = $"Critical physiological crisis! {ticksUntilDowned} ticks until downed."
                });
            }

            SafetyAssessment safety = DynamicSanctuaryFinder.FindBestSafety(pawn, threatPos, 50f);
            if (safety.cell.IsValid)
            {
                float dist = pawn.Position.DistanceTo(safety.cell);
                float travelRisk = (dist / 50f) * 20f + CognitiveBrain.DangerAt(map, safety.cell, pawn);
                float survival = safety.safetyScore;

                float net = (survival * 0.65f) + (15f) - travelRisk;

                if (selfCap < 40f) net += 25f;

                candidates.Add(new TacticalCandidate
                {
                    kind = IntentKind.Retreat,
                    jobDef = JobDefOf.Goto,
                    target = safety.cell,
                    survivalProbability = survival,
                    tacticalValue = 20f,
                    travelRisk = travelRisk,
                    netScore = net,
                    reasoning = $"Retreat to {safety.description} (Tier: {safety.tier})"
                });
            }

            if (TryFindTacticalCover(pawn, threatPos, out IntVec3 coverCell, out float blockChance))
            {
                float dist = pawn.Position.DistanceTo(coverCell);
                float travelRisk = dist * 4f + CognitiveBrain.DangerAt(map, coverCell, pawn);
                float survival = blockChance * 100f;
                float tactical = selfCap >= 40f ? 70f : 30f;

                candidates.Add(new TacticalCandidate
                {
                    kind = IntentKind.SeekCover,
                    jobDef = JobDefOf.Goto,
                    target = coverCell,
                    survivalProbability = survival,
                    tacticalValue = tactical,
                    travelRisk = travelRisk,
                    netScore = (survival * 0.55f) + (tactical * 0.45f) - travelRisk,
                    reasoning = $"Take directional cover ({blockChance:P0} protection)"
                });
            }

            float currentCover = CoverUtility.CalculateOverallBlockChance(pawn.Position, threatPos, map);
            float distToThreat = pawn.Position.DistanceTo(threatPos);

            if (currentCover >= 0.50f && selfCap >= 40f && distToThreat > 4f)
            {
                candidates.Add(new TacticalCandidate
                {
                    kind = IntentKind.Reposition,
                    jobDef = JobDefOf.Wait_Combat,
                    target = pawn.Position,
                    survivalProbability = currentCover * 100f,
                    tacticalValue = 85f,
                    travelRisk = 0f,
                    netScore = (currentCover * 100f * 0.5f) + (85f * 0.5f) + 10f,
                    reasoning = $"Hold ground in solid cover ({currentCover:P0}) and return fire"
                });
            }

            if (SmartAIMod.Settings.combatRangedKiting && pawn.equipment?.Primary?.def.IsRangedWeapon == true && distToThreat <= 4.5f)
            {
                Pawn threatPawn = threatPos.GetFirstPawn(map);
                if (threatPawn != null && Module_Combat.TryFindKitingFallbackCell(pawn, threatPawn, out IntVec3 fallbackCell))
                {
                    float distGain = fallbackCell.DistanceTo(threatPos) - distToThreat;
                    float cover = CoverUtility.CalculateOverallBlockChance(fallbackCell, threatPos, map);
                    float net = 80f + (distGain * 5f) + (cover * 25f);

                    candidates.Add(new TacticalCandidate
                    {
                        kind = IntentKind.Reposition,
                        jobDef = JobDefOf.Goto,
                        target = fallbackCell,
                        survivalProbability = 85f + (cover * 15f),
                        tacticalValue = 85f,
                        travelRisk = 5f,
                        netScore = net,
                        reasoning = $"Tactical fallback from close melee threat ({distToThreat:F1} tiles away)"
                    });
                }
            }

            TacticalCandidate best = default;
            float maxScore = float.MinValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.netScore > maxScore)
                {
                    maxScore = c.netScore;
                    best = c;
                }
            }

            return best;
        }

        private static bool TryFindTacticalCover(Pawn pawn, IntVec3 threatPos, out IntVec3 bestCover, out float bestBlockChance)
        {
            bestCover = IntVec3.Invalid;
            bestBlockChance = 0f;
            Map map = pawn.Map;

            float currentCover = CoverUtility.CalculateOverallBlockChance(pawn.Position, threatPos, map);
            if (currentCover >= 0.55f) return false;

            float bestScore = currentCover;
            float searchRadius = SmartAIMod.Settings.coverSearchRadius;

            foreach (IntVec3 c in GenRadial.RadialCellsAround(pawn.Position, searchRadius, false))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null) continue;
                if (c.ContainsStaticFire(map) || CognitiveBrain.DangerAt(map, c, pawn) > 20f) continue;

                float block = CoverUtility.CalculateOverallBlockChance(c, threatPos, map);
                if (block > bestScore + 0.15f && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
                {
                    bestScore = block;
                    bestCover = c;
                    bestBlockChance = block;
                }
            }

            return bestCover.IsValid;
        }
    }
}
