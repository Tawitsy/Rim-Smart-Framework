using RimWorld;
using SmartAIFramework.Data;
using SmartAIFramework.Evaluation;
using SmartAIFramework.Grid;
using SmartAIFramework.Work;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Combat
{
    public static class CombatUtility
    {
        private const int PredatorAlertCooldownTicks = 600;

        public static bool CanFireSafely(Verb verb)
        {
            if (!SmartAIMod.Settings.combatFiringDiscipline || verb == null)
                return true;

            Pawn shooter = verb.CasterPawn;
            Map map = shooter?.Map;
            if (map == null || !shooter.Spawned)
                return true;

            LocalTargetInfo target = verb.CurrentTarget;
            if (!target.IsValid)
                return true;

            VerbProfile profile = VerbProfileCache.Get(verb);
            if (profile == null || !profile.IsHostile)
                return true;

            float blastRadius = profile.forcedMissRadius + profile.aoeRadius;
            if (!LineOfSightUtility.HasFriendlyInLineOfFire(map, shooter, shooter.Position, target.Cell, blastRadius))
                return true;

            if (shooter.Drafted && shooter.CurJob != null && shooter.CurJob.playerForced && SmartAIMod.Settings.allowDraftedForceFire)
                return true;

            if (!PawnStateTracker.CanAssist(shooter))
                return true;

            if (SmartLog.IsEnabled)
                SmartLog.Decision("FiringDiscipline", shooter, $"held fire at {target.Cell} (friendly in line/blast)");

            if (PawnStateTracker.CanActAutonomously(shooter))
            {
                PawnTacticalState state = PawnStateTracker.GetOrCreate(shooter);
                int now = PawnStateTracker.Now;
                if (now >= state.nextSidestepTick && TryFindSidestepCell(shooter, target.Cell, out IntVec3 sidestep))
                {
                    state.nextSidestepTick = now + 75;
                    PawnStateTracker.QueueTacticalJob(
                        shooter,
                        TacticalActionType.Sidestep,
                        JobDefOf.Goto,
                        sidestep,
                        priorityScore: 65f,
                        validForTicks: 60,
                        jobExpiryTicks: 35,
                        cancelWarmup: true);
                }
            }

            return false;
        }

        public static bool TryFindSidestepCell(Pawn shooter, IntVec3 targetCell, out IntVec3 bestCell)
        {
            bestCell = IntVec3.Invalid;
            Map map = shooter?.Map;
            if (map == null) return false;

            IntVec3 origin = shooter.Position;
            float currentDanger = UtilityEvaluator.GetDangerAt(map, origin, shooter);

            for (int i = 0; i < 8; i++)
            {
                IntVec3 c = origin + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.ContainsTrap(map) || c.GetFirstPawn(map) != null)
                    continue;
                if (UtilityEvaluator.GetDangerAt(map, c, shooter) > currentDanger)
                    continue;
                if (!GenSight.LineOfSight(c, targetCell, map, skipFirstCell: true))
                    continue;
                if (!LineOfSightUtility.HasFriendlyAlongSegment(map, shooter, c, targetCell))
                {
                    bestCell = c;
                    return true;
                }
            }

            return false;
        }

        public static bool TryFindKiteCell(Pawn shooter, Pawn threat, out IntVec3 bestCell)
        {
            bestCell = IntVec3.Invalid;
            Map map = shooter?.Map;
            if (map == null || threat == null) return false;

            IntVec3 shooterPos = shooter.Position;
            IntVec3 threatPos = threat.Position;
            float currentDist = shooterPos.DistanceTo(threatPos);

            Vector3 awayDir = (shooterPos - threatPos).ToVector3().normalized;
            TacticalGrid grid = TacticalGrid.Get(map);
            District myDistrict = shooter.GetDistrict();

            int cellCount = GenRadial.NumCellsInRadius(4f);
            IntVec3[] pattern = GenRadial.RadialPattern;
            float bestScore = -999f;

            for (int i = 1; i < cellCount; i++)
            {
                IntVec3 c = shooterPos + pattern[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null)
                    continue;
                if (myDistrict != null && c.GetDistrict(map) != myDistrict)
                    continue;
                if (c.ContainsStaticFire(map) || c.ContainsTrap(map))
                    continue;
                if (grid != null && (grid.DangerAt(c) > 25f || grid.FriendlyFiringLaneAt(c) > 0))
                    continue;

                float distToThreat = c.DistanceTo(threatPos);
                if (distToThreat <= currentDist && distToThreat < 3.5f)
                    continue;
                if (!GenSight.LineOfSight(c, threatPos, map, skipFirstCell: true))
                    continue;

                float distGain = distToThreat - currentDist;
                float score = distGain * 15f;

                Vector3 moveDir = (c - shooterPos).ToVector3().normalized;
                score += Vector3.Dot(awayDir, moveDir) * 20f;
                score += CoverUtility.CalculateOverallBlockChance(c, threatPos, map) * 40f;

                if (StructureUtility.IsEnclosedHomeRoom(map, c))
                {
                    score += 25f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestCell = c;
                }
            }

            if (bestCell.IsValid && bestScore > 0f && shooter.CanReach(bestCell, PathEndMode.OnCell, Danger.Some))
            {
                return true;
            }

            bestCell = IntVec3.Invalid;
            return false;
        }

        public static void TryKiteFromMelee(Pawn shooter, Pawn threat)
        {
            if (!SmartAIMod.Settings.combatRangedKiting) return;
            if (shooter == null || threat == null || !shooter.Spawned || shooter.Dead || shooter.Downed) return;
            if (shooter.equipment?.Primary?.def.IsRangedWeapon != true) return;

            bool isDrafted = shooter.Drafted;
            if (isDrafted)
            {
                if (!SmartAIMod.Settings.allowDraftedKitingAssist || !PawnStateTracker.CanAssist(shooter))
                    return;
                if (shooter.CurJob != null && shooter.CurJob.playerForced && shooter.CurJob.def == JobDefOf.Goto)
                    return;
            }
            else if (!PawnStateTracker.CanActAutonomously(shooter))
            {
                return;
            }

            PawnTacticalState state = PawnStateTracker.GetOrCreate(shooter);
            int now = PawnStateTracker.Now;
            if (now < state.nextKiteEvalTick)
                return;

            state.nextKiteEvalTick = now + 35;

            if (!TryFindKiteCell(shooter, threat, out IntVec3 fallbackCell))
                return;

            if (isDrafted)
            {
                Job job = JobMaker.MakeJob(JobDefOf.Goto, fallbackCell);
                job.playerForced = false;
                job.expiryInterval = 35;
                job.checkOverrideOnExpire = true;
                shooter.jobs?.StartJob(job, JobCondition.InterruptForced);
            }
            else
            {
                PawnStateTracker.QueueTacticalJob(
                    shooter,
                    TacticalActionType.Sidestep,
                    JobDefOf.Goto,
                    fallbackCell,
                    priorityScore: 80f,
                    validForTicks: 60,
                    jobExpiryTicks: 35,
                    cancelWarmup: true);
                PawnStateTracker.FlushInterrupts();
            }
        }

        public static bool IsEnteringFiringLane(Pawn pawn, IntVec3 nextCell)
        {
            if (!SmartAIMod.Settings.combatMuzzleDiscipline || pawn?.Map == null)
                return false;

            TacticalGrid grid = TacticalGrid.Get(pawn.Map);
            if (grid == null || !grid.HasActiveLanes)
                return false;

            return grid.FiringLaneAt(nextCell) > 0 && grid.FiringLaneAt(pawn.Position) == 0;
        }

        public static void YieldFiringLane(Pawn pawn, IntVec3 finalDest)
        {
            Map map = pawn?.Map;
            if (map == null || !PawnStateTracker.CanActAutonomously(pawn))
                return;
            if (PawnStateTracker.IsProtectedFromInterrupt(pawn, pawn.CurJob))
                return;

            TacticalGrid grid = TacticalGrid.Get(map);
            if (grid == null) return;

            IntVec3 bestBypass = IntVec3.Invalid;
            float bestDist = finalDest.IsValid ? pawn.Position.DistanceTo(finalDest) + 1.5f : 999f;

            for (int i = 0; i < 8; i++)
            {
                IntVec3 c = pawn.Position + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.GetDoor(map) != null || c.ContainsTrap(map))
                    continue;
                if (grid.FiringLaneAt(c) > 0 || grid.DangerAt(c) > 20f)
                    continue;

                float d = finalDest.IsValid ? c.DistanceTo(finalDest) : 0f;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestBypass = c;
                }
            }

            if (bestBypass.IsValid)
            {
                PawnStateTracker.QueueTacticalJob(
                    pawn,
                    TacticalActionType.Sidestep,
                    JobDefOf.Goto,
                    bestBypass,
                    priorityScore: 60f,
                    validForTicks: 60,
                    jobExpiryTicks: 45);
            }
            else
            {
                PawnStateTracker.QueueTacticalJob(
                    pawn,
                    TacticalActionType.Sidestep,
                    JobDefOf.Wait_MaintainPosture,
                    pawn.Position,
                    priorityScore: 55f,
                    validForTicks: 60,
                    jobExpiryTicks: 45);
            }

            PawnStateTracker.FlushInterrupts();
        }

        public static void NotifyDamaged(Pawn victim, DamageInfo dinfo)
        {
            if (victim?.Map == null) return;

            IntVec3 threatPos = dinfo.Instigator?.Position ?? victim.Position;
            PawnTacticalState state = PawnStateTracker.GetOrCreate(victim);
            int now = PawnStateTracker.Now;
            state.RecordDamage(dinfo.Amount, threatPos, now);

            if (!SmartAIMod.Settings.combatSmartCover || !PawnStateTracker.CanActAutonomously(victim))
                return;

            // Fire, falling debris, surgery and self-inflicted damage are not an enemy to take cover from.
            Thing instigator = dinfo.Instigator;
            if (instigator == null || instigator == victim || !instigator.HostileTo(victim) || victim.IsBurning())
                return;

            if (now < state.nextCoverEvalTick)
                return;

            state.nextCoverEvalTick = now + 30;
            ExecuteCombatResponse(victim, threatPos);
        }

        public static void NotifyNearMiss(Pawn pawn, BulletImpactData impactData)
        {
            if (!SmartAIMod.Settings.combatSmartCover || pawn?.Map == null || pawn.Dead || pawn.Downed)
                return;
            if (!PawnStateTracker.CanActAutonomously(pawn))
                return;

            Thing launcher = impactData.bullet?.Launcher;
            if (launcher != null && !launcher.HostileTo(pawn))
                return;

            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);
            int now = PawnStateTracker.Now;
            if (now < state.nextCoverEvalTick)
                return;

            IntVec3 threatPos = launcher?.Position ?? impactData.impactPosition;
            float currentCover = CoverUtility.CalculateOverallBlockChance(pawn.Position, threatPos, pawn.Map);
            if (currentCover < 0.30f)
            {
                state.nextCoverEvalTick = now + 60;
                state.RecordThreat(threatPos, 200, now);
                ExecuteCombatResponse(pawn, threatPos);
            }
        }

        public static void NotifyThreatPerceived(Pawn pawn, IntVec3 threatPos)
        {
            if (!SmartAIMod.Settings.combatSmartCover || pawn?.Map == null)
                return;
            if (!PawnStateTracker.CanActAutonomously(pawn))
                return;

            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);
            int now = PawnStateTracker.Now;
            if (now < state.nextCoverEvalTick)
                return;

            state.nextCoverEvalTick = now + 75;
            state.RecordThreat(threatPos, 300, now);
            ExecuteCombatResponse(pawn, threatPos);
        }

        private static void ExecuteCombatResponse(Pawn pawn, IntVec3 threatPos)
        {
            CombatResponseOption option = CoverFinder.EvaluateBestResponse(pawn, threatPos);
            if (option.target.IsValid && option.netScore > 0f)
            {
                if (SmartLog.IsEnabled)
                    SmartLog.Decision("CombatResponse", pawn, $"{option.actionType} via {option.jobDef?.defName} -> {option.target.Cell} (score {option.netScore:F0}, threat {threatPos})");

                PawnStateTracker.QueueTacticalJob(
                    pawn,
                    option.actionType,
                    option.jobDef,
                    option.target,
                    option.netScore);
                PawnStateTracker.FlushInterrupts();
            }
        }

        public static void NotifyPredatorHuntingColonist(Pawn predator, Pawn prey)
        {
            if (!SmartAIMod.Settings.predatorSurvivalAlert) return;
            if (prey == null || predator == null || !PawnStateTracker.CanActAutonomously(prey))
                return;

            if (prey.CurJob != null && (prey.CurJob.def == JobDefOf.Flee || prey.CurJob.def == JobDefOf.Goto))
            {
                if (prey.CurJob.targetA.Cell.IsValid && StructureUtility.IsEnclosedHomeRoom(prey.Map, prey.CurJob.targetA.Cell))
                    return;
            }

            if (StructureUtility.IsEnclosedHomeRoom(prey.Map, prey.Position))
                return;
            if (PawnStateTracker.IsProtectedFromInterrupt(prey, prey.CurJob))
                return;

            PawnTacticalState state = PawnStateTracker.GetOrCreate(prey);
            int now = PawnStateTracker.Now;
            if (now < state.nextPredatorAlertTick)
                return;
            state.nextPredatorAlertTick = now + PredatorAlertCooldownTicks;
            state.RecordThreat(predator.Position, 400, now);

            GenClamor.DoClamor(prey, 35f, ClamorDefOf.Movement);

            RetreatSpot spot = SafeCellFinder.FindRetreatSpot(prey, predator.Position, 50f);
            if (spot.cell.IsValid && spot.cell != prey.Position)
            {
                Job retreatJob = JobMaker.MakeJob(JobDefOf.Goto, spot.cell);
                retreatJob.expiryInterval = 600;
                retreatJob.checkOverrideOnExpire = true;
                prey.jobs?.StartJob(retreatJob, JobCondition.InterruptForced);
                if (SmartLog.IsEnabled)
                    SmartLog.Decision("PredatorAlert", prey, $"retreating from {predator.LabelShort} to {spot.cell}");

                Messages.Message(
                    $"{prey.LabelShort} spotted hunting {predator.LabelIndefinite()} and is retreating to safety!",
                    prey,
                    MessageTypeDefOf.ThreatSmall);
            }
        }

        public static void EvacuateBlastRadius(Pawn pawn, IntVec3 epicenter, float safeRadius)
        {
            if (pawn == null || !PawnStateTracker.CanAssist(pawn)) return;
            if (pawn.Drafted && !SmartAIMod.Settings.allowDraftedKitingAssist) return;
            if (!pawn.Position.InHorDistOf(epicenter, safeRadius)) return;

            Map map = pawn.Map;
            if (map == null) return;

            IntVec3 bestCell = IntVec3.Invalid;
            float maxDistSq = -1f;
            float safeRadiusSq = safeRadius * safeRadius;
            District myDistrict = pawn.GetDistrict();
            IntVec3[] pattern = GenRadial.RadialPattern;

            for (int i = 1; i < 37; i++)
            {
                IntVec3 c = pawn.Position + pattern[i];
                if (!c.InBounds(map) || !c.Walkable(map) || c.Impassable(map) || c.ContainsTrap(map))
                    continue;
                if (myDistrict != null && c.GetDistrict(map) != myDistrict)
                    continue;
                if (c.GetDangerFor(pawn, map) == Danger.Deadly)
                    continue;

                float distSq = c.DistanceToSquared(epicenter);
                if (distSq >= safeRadiusSq && distSq > maxDistSq)
                {
                    maxDistSq = distSq;
                    bestCell = c;
                }
            }

            if (bestCell.IsValid && bestCell != pawn.Position && pawn.CanReach(bestCell, PathEndMode.OnCell, Danger.Deadly))
            {
                Job dodgeJob = JobMaker.MakeJob(JobDefOf.Goto, bestCell);
                dodgeJob.playerForced = false;
                dodgeJob.expiryInterval = 90;
                dodgeJob.checkOverrideOnExpire = true;
                pawn.jobs?.StartJob(dodgeJob, JobCondition.InterruptForced);
                if (SmartLog.IsEnabled)
                    SmartLog.Decision("BlastEvac", pawn, $"leaving blast radius of {epicenter} to {bestCell}");
            }
        }
    }
}
