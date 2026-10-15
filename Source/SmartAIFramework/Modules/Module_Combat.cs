using RimWorld;
using SmartAIFramework.Brain;
using SmartAIFramework.Core.Decisions;
using SmartAIFramework.Core.Knowledge;
using SmartAIFramework.Core_Engine;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Modules
{
    public static class Module_Combat
    {
        public static bool ShouldAllowShot(Verb verb)
        {
            if (!SmartAIMod.Settings.combatFiringDiscipline) return true;
            Pawn shooter = verb.CasterPawn;
            if (shooter == null || shooter.Map == null) return true;

            LocalTargetInfo target = verb.CurrentTarget;
            if (!target.IsValid) return true;

            var profile = ModActionAnalyzer.Analyze(verb);
            if (profile == null || !profile.IsHostileAction) return true;

            if (AllyOnLineOrCone(shooter, target.Cell, profile.forcedMissRadius + profile.aoeRadius))
            {
                if (shooter.Drafted && shooter.CurJob != null && shooter.CurJob.playerForced && SmartAIMod.Settings.allowDraftedForceFire)
                {
                    DecisionLog.Record(shooter, "Player manually forced dangerous shot - permitting fire", applied: false);
                    return true;
                }

                if (!ActionGate.ApplyVeto(shooter, assistLevel: true, "Hold fire (ally in line of sight)"))
                    return true;

                if (ActionGate.MayActAutonomously(shooter) && TryFindSidestepCell(shooter, target.Cell, out IntVec3 sidestep))
                {
                    IntentArbiter.Propose(new Intent(
                        shooter, IntentKind.Reposition, utility: 65f, urgent: true,
                        JobDefOf.Goto, sidestep, "Module_Combat", "Sidestep for clear shot",
                        validForTicks: 60, jobExpiryTicks: 35));
                }
                return false;
            }
            return true;
        }

        private static bool AllyOnLineOrCone(Pawn shooter, IntVec3 targetCell, float dangerRadius)
        {
            Map map = shooter.Map;
            bool blocked = false;
            IntVec3 origin = shooter.Position;

            GenSight.PointsOnLineOfSight(origin, targetCell, cell =>
            {
                if (blocked || cell == origin || cell == targetCell) return;
                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn p && p != shooter && !p.Downed && !p.HostileTo(shooter))
                    {
                        blocked = true;
                        return;
                    }
                }
            });

            if (!blocked && dangerRadius > 0.5f)
            {
                if (shooter.Position.DistanceTo(targetCell) <= dangerRadius + 0.5f)
                {
                    return true;
                }

                foreach (IntVec3 c in GenRadial.RadialCellsAround(targetCell, dangerRadius, true))
                {
                    if (!c.InBounds(map)) continue;
                    var things = c.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing t = things[i];
                        if (t is Pawn p && !p.Downed && !p.HostileTo(shooter))
                        {
                            return true;
                        }

                        if (SmartAIMod.Settings.combatExplosiveSafety && map.areaManager.Home[c])
                        {
                            if (t.def == ThingDefOf.Chemfuel || t.TryGetComp<CompExplosive>() != null)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return blocked;
        }

        private static bool TryFindSidestepCell(Pawn shooter, IntVec3 targetCell, out IntVec3 bestCell)
        {
            bestCell = IntVec3.Invalid;
            Map map = shooter.Map;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 c = shooter.Position + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null) continue;
                if (CognitiveBrain.DangerAt(map, c) > CognitiveBrain.DangerAt(map, shooter.Position)) continue;
                if (!GenSight.LineOfSight(c, targetCell, map, true)) continue;

                bool clear = true;
                GenSight.PointsOnLineOfSight(c, targetCell, check =>
                {
                    if (!clear || check == c || check == targetCell) return;
                    Pawn p = check.GetFirstPawn(map);
                    if (p != null && p != shooter && !p.Downed && !p.HostileTo(shooter)) clear = false;
                });

                if (clear)
                {
                    bestCell = c;
                    return true;
                }
            }
            return false;
        }

        public static bool TryFindKitingFallbackCell(Pawn shooter, Pawn threat, out IntVec3 bestCell)
        {
            bestCell = IntVec3.Invalid;
            if (shooter?.Map == null || threat == null) return false;

            Map map = shooter.Map;
            IntVec3 shooterPos = shooter.Position;
            IntVec3 threatPos = threat.Position;
            float currentDist = shooterPos.DistanceTo(threatPos);

            Vector3 awayDir = (shooterPos - threatPos).ToVector3().normalized;
            var grid = TacticalGridMapComponent.For(map);
            float bestScore = -999f;

            foreach (IntVec3 c in GenRadial.RadialCellsAround(shooterPos, 4f, false))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c == shooterPos) continue;
                if (c.GetFirstPawn(map) != null) continue;
                if (c.ContainsStaticFire(map) || (grid != null && grid.DangerAt(c) > 25f)) continue;

                float distToThreat = c.DistanceTo(threatPos);

                if (distToThreat <= currentDist && distToThreat < 3.5f) continue;

                if (!GenSight.LineOfSight(c, threatPos, map, true)) continue;

                if (grid != null && grid.FriendlyFiringLaneAt(c) > 0) continue;

                if (!shooter.CanReach(c, PathEndMode.OnCell, Danger.Some)) continue;

                float distGain = distToThreat - currentDist;
                float score = distGain * 15f;

                Vector3 moveDir = (c - shooterPos).ToVector3().normalized;
                float dot = Vector3.Dot(awayDir, moveDir);
                score += dot * 20f;

                float cover = CoverUtility.CalculateOverallBlockChance(c, threatPos, map);
                score += cover * 40f;

                if (Module_Structure.IsSanctuary(map, c))
                {
                    score += 25f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestCell = c;
                }
            }

            return bestCell.IsValid && bestScore > 0f;
        }

        public static void CheckTacticalFallback(Pawn shooter, Pawn threat)
        {
            if (!SmartAIMod.Settings.combatRangedKiting) return;
            if (shooter == null || threat == null || !shooter.Spawned || shooter.Dead || shooter.Downed) return;
            if (shooter.equipment?.Primary?.def.IsRangedWeapon != true) return;

            if (shooter.Drafted)
            {
                if (!SmartAIMod.Settings.allowDraftedKitingAssist) return;
                if (shooter.CurJob != null && shooter.CurJob.playerForced && shooter.CurJob.def == JobDefOf.Goto)
                    return;
            }

            if (!ActionGate.MayActAutonomously(shooter)) return;

            if (TryFindKitingFallbackCell(shooter, threat, out IntVec3 fallbackCell))
            {
                IntentArbiter.Propose(new Intent(
                    shooter, IntentKind.Reposition, utility: 80f, urgent: true,
                    JobDefOf.Goto, fallbackCell, "Module_Combat",
                    $"Tactical fallback from {threat.LabelShort}",
                    validForTicks: 60, jobExpiryTicks: 35));
                IntentArbiter.FlushInterrupts();
            }
        }

        public static bool IsSteppingIntoFiringLane(Pawn pawn, IntVec3 nextCell)
        {
            if (!SmartAIMod.Settings.combatMuzzleDiscipline || pawn?.Map == null) return false;
            var grid = TacticalGridMapComponent.For(pawn.Map);
            if (grid == null) return false;

            return grid.FiringLaneAt(nextCell) > 0 && grid.FiringLaneAt(pawn.Position) == 0;
        }

        public static void YieldToFiringLane(Pawn pawn, IntVec3 finalDest)
        {
            Map map = pawn?.Map;
            if (map == null || !ActionGate.MayActAutonomously(pawn)) return;

            var grid = TacticalGridMapComponent.For(map);
            if (grid == null) return;

            IntVec3 bestBypass = IntVec3.Invalid;
            float bestDist = finalDest.IsValid ? pawn.Position.DistanceTo(finalDest) + 1.5f : 999f;

            for (int i = 0; i < 8; i++)
            {
                IntVec3 c = pawn.Position + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.GetDoor(map) != null) continue;
                if (grid.FiringLaneAt(c) > 0 || grid.DangerAt(c) > 20f) continue;

                float d = finalDest.IsValid ? c.DistanceTo(finalDest) : 0f;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestBypass = c;
                }
            }

            if (bestBypass.IsValid)
            {
                IntentArbiter.Propose(new Intent(
                    pawn, IntentKind.Reposition, utility: 60f, urgent: true,
                    JobDefOf.Goto, bestBypass, "Module_Combat", "Bypass firing lane",
                    validForTicks: 60, jobExpiryTicks: 45));
            }
            else
            {
                IntentArbiter.Propose(new Intent(
                    pawn, IntentKind.Reposition, utility: 55f, urgent: true,
                    JobDefOf.Wait_MaintainPosture, pawn.Position, "Module_Combat", "Hold position in chokepoint until shot clears",
                    validForTicks: 60, jobExpiryTicks: 45));
            }
            IntentArbiter.FlushInterrupts();
        }

        public static void OnDamaged(Pawn victim, DamageInfo dinfo)
        {
            if (!SmartAIMod.Settings.combatSmartCover || victim?.Map == null) return;

            KnowledgeBase.Writer(victim)?.RecordDamage(dinfo.Amount);

            IntVec3 threatPos = dinfo.Instigator?.Position ?? victim.Position;
            EvaluateSurvivalOrCover(victim, threatPos, isDamaged: true);
        }

        public static void OnNearMiss(Pawn pawn, BulletImpactData impactData)
        {
            if (!SmartAIMod.Settings.combatSmartCover || pawn?.Map == null) return;
            if (pawn.Dead || pawn.Downed) return;

            Thing launcher = impactData.bullet?.Launcher;
            if (launcher != null && !launcher.HostileTo(pawn)) return;

            IntVec3 threatPos = launcher?.Position ?? impactData.impactPosition;
            float currentCover = CoverUtility.CalculateOverallBlockChance(pawn.Position, threatPos, pawn.Map);

            if (currentCover < 0.30f)
            {
                KnowledgeBase.Writer(pawn)?.RecordThreat(threatPos, 200);
                EvaluateSurvivalOrCover(pawn, threatPos, isDamaged: false);
            }
        }

        public static void OnClamorHeard(Pawn listener, Thing source, ClamorDef type)
        {
            if (listener == null || listener.Map == null || !listener.IsColonist || listener.Drafted) return;
            if (source == null || !source.HostileTo(listener)) return;

            if (type == ClamorDefOf.Harm || type == ClamorDefOf.Construction || type == ClamorDefOf.Ability)
            {
                KnowledgeBase.Writer(listener)?.RecordThreat(source.Position, 250);
                if (listener.CurJob != null && listener.CurJob.def.joyKind != null)
                {
                    listener.jobs.CheckForJobOverride();
                }
            }
        }

        public static void OnThreatPerceived(Pawn pawn, IntVec3 threatPos)
        {
            if (!SmartAIMod.Settings.combatSmartCover || pawn?.Map == null) return;

            KnowledgeBase.Writer(pawn)?.RecordThreat(threatPos, 300);
            EvaluateSurvivalOrCover(pawn, threatPos, isDamaged: false);
        }

        private static void EvaluateSurvivalOrCover(Pawn pawn, IntVec3 threatPos, bool isDamaged)
        {
            if (!ActionGate.MayActAutonomously(pawn)) return;

            TacticalCandidate best = TacticalDeliberator.Deliberate(pawn, threatPos, isDamaged);
            if (best.target.IsValid && best.netScore > 0f)
            {
                IntentArbiter.Propose(new Intent(
                    pawn, best.kind, best.netScore, urgent: true,
                    best.jobDef, best.target, "TacticalDeliberator", best.reasoning));
                IntentArbiter.FlushInterrupts();
            }
        }

        private static bool TryFindNearbyCover(Pawn pawn, IntVec3 threatPos, out IntVec3 bestCover)
        {
            bestCover = IntVec3.Invalid;
            Map map = pawn.Map;
            float currentCover = CoverUtility.CalculateOverallBlockChance(pawn.Position, threatPos, map);
            if (currentCover >= 0.4f) return false;

            float bestScore = currentCover;
            float radius = SmartAIMod.Settings.coverSearchRadius;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(pawn.Position, radius, false))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null) continue;
                if (c.ContainsStaticFire(map) || CognitiveBrain.DangerAt(map, c, pawn) > 20f) continue;

                float block = CoverUtility.CalculateOverallBlockChance(c, threatPos, map);
                if (block > bestScore + 0.15f && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
                {
                    bestScore = block;
                    bestCover = c;
                }
            }
            return bestCover.IsValid;
        }

        public static void Notify_PredatorHuntingColonist(Pawn predator, Pawn prey)
        {
            if (!SmartAIMod.Settings.predatorSurvivalAlert) return;
            if (prey == null || !ActionGate.MayAssist(prey) || predator == null || prey.Dead || prey.Downed) return;
            if (prey.Drafted) return;

            if (prey.CurJob != null && (prey.CurJob.def == JobDefOf.Flee || prey.CurJob.def == JobDefOf.Goto))
            {
                if (prey.CurJob.targetA.Cell.IsValid && Module_Structure.IsSanctuary(prey.Map, prey.CurJob.targetA.Cell))
                    return;
            }

            KnowledgeBase.Writer(prey)?.RecordThreat(predator.Position, 400);

            GenClamor.DoClamor(prey, 35f, ClamorDefOf.Movement);

            var safety = DynamicSanctuaryFinder.FindBestSafety(prey, predator.Position, 60f);
            if (safety.cell.IsValid && safety.cell != prey.Position)
            {
                Job retreatJob = JobMaker.MakeJob(JobDefOf.Goto, safety.cell);
                retreatJob.expiryInterval = 600;
                retreatJob.checkOverrideOnExpire = true;
                prey.jobs.StartJob(retreatJob, JobCondition.InterruptForced);

                Messages.Message(
                    $"{prey.LabelShort} spotted hunting {predator.LabelIndefinite()} and is retreating to safety!",
                    prey,
                    MessageTypeDefOf.ThreatSmall
                );
            }
        }

        public static void EvacuateExplosionRadius(Pawn pawn, IntVec3 epicenter, float safeRadius)
        {
            if (pawn == null || !ActionGate.MayAssist(pawn) || pawn.Dead || pawn.Downed) return;
            if (pawn.Drafted && !SmartAIMod.Settings.allowDraftedKitingAssist) return;
            if (pawn.Position.DistanceTo(epicenter) >= safeRadius) return;

            Map map = pawn.Map;
            if (map == null) return;

            IntVec3 bestCell = IntVec3.Invalid;
            float maxDistFromEpicenter = -1f;

            for (int i = 0; i < 37; i++)
            {
                IntVec3 c = pawn.Position + GenRadial.RadialPattern[i];
                if (!c.InBounds(map) || !c.Walkable(map) || c.Impassable(map)) continue;
                if (c.GetDangerFor(pawn, map) == Danger.Deadly) continue;

                float distToExplosion = c.DistanceTo(epicenter);
                if (distToExplosion >= safeRadius && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
                {
                    if (distToExplosion > maxDistFromEpicenter)
                    {
                        maxDistFromEpicenter = distToExplosion;
                        bestCell = c;
                    }
                }
            }

            if (bestCell.IsValid && bestCell != pawn.Position)
            {
                Job dodgeJob = JobMaker.MakeJob(JobDefOf.Goto, bestCell);
                dodgeJob.playerForced = false;
                dodgeJob.expiryInterval = 90;
                dodgeJob.checkOverrideOnExpire = true;
                pawn.jobs.StartJob(dodgeJob, JobCondition.InterruptForced);

                DecisionLog.Record(pawn, $"Evacuated explosion radius from {epicenter} to {bestCell}", applied: true);
                SmartAIFramework.Core.SmartAILog.Log("Combat-Dodge", $"{pawn.LabelShort} dodging explosion radius from {epicenter} to {bestCell}");
            }
        }
    }
}
