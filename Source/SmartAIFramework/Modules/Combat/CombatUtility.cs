using RimWorld;
using SmartAIFramework.Data;
using SmartAIFramework.Decisions;
using SmartAIFramework.Evaluation;
using SmartAIFramework.Grid;
using SmartAIFramework.Work;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Combat
{
    public enum CombatRole { Combatant, NonCombatant }

    public static class CombatUtility
    {
        private const int PredatorAlertCooldownTicks = 600;

        // Cached on the pawn's tactical state for 600 ticks (and until its version changes, like capability).
        public static CombatRole GetRole(Pawn pawn)
        {
            if (pawn == null) return CombatRole.NonCombatant;

            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);
            int now = PawnStateTracker.Now;
            if (state.cachedRole >= 0 && state.roleVersion == state.version && now - state.roleComputedTick < 600)
                return (CombatRole)state.cachedRole;

            CombatRole role = ComputeRole(pawn);
            state.cachedRole = (int)role;
            state.roleVersion = state.version;
            state.roleComputedTick = now;
            return role;
        }

        private static CombatRole ComputeRole(Pawn pawn)
        {
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return CombatRole.NonCombatant;
            if (pawn.DevelopmentalStage.Juvenile())
                return CombatRole.NonCombatant;
            if (pawn.equipment?.Primary == null)
            {
                SkillRecord melee = pawn.skills?.GetSkill(SkillDefOf.Melee);
                if (melee == null || melee.Level < 6)
                    return CombatRole.NonCombatant;
            }
            if (UtilityEvaluator.GetSelfCapability(pawn) < 35f)
                return CombatRole.NonCombatant;
            return CombatRole.Combatant;
        }

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
                    // We are inside TryCastShot: the interrupt is flushed after the cast, see the patch.
                    Arbiter.Submit(new Proposal
                    {
                        pawn = shooter,
                        behaviorId = BehaviorIds.Sidestep,
                        tier = DecisionTier.Tactical,
                        score = 65f,
                        jobDef = JobDefOf.Goto,
                        target = sidestep,
                        jobExpiryTicks = 35,
                        actuation = Actuation.Interrupt,
                        validUntilTick = now + 60,
                        commitTicks = 35,
                        reason = "friendly in line of fire",
                        allowBusyStance = true,
                        deferFlush = true
                    });
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

            Arbiter.Submit(new Proposal
            {
                pawn = shooter,
                behaviorId = isDrafted ? BehaviorIds.KiteAssist : BehaviorIds.Kite,
                tier = DecisionTier.Tactical,
                score = 80f,
                jobDef = JobDefOf.Goto,
                target = fallbackCell,
                jobExpiryTicks = 35,
                actuation = Actuation.Interrupt,
                validUntilTick = now + 60,
                commitTicks = 35,
                reason = "melee threat adjacent: back off",
                allowBusyStance = true,
                assistDrafted = isDrafted
            });
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
            if (JobDirector.IsProtectedFromInterrupt(pawn, pawn.CurJob))
                return;
            if (Arbiter.IsOnCooldown(pawn, BehaviorIds.YieldLane))
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

            // The hold is a deliberate, short (45 tick) wait for the shooter to finish; never a fallback park.
            bool bypass = bestBypass.IsValid;
            int now = PawnStateTracker.Now;
            Arbiter.Submit(new Proposal
            {
                pawn = pawn,
                behaviorId = BehaviorIds.YieldLane,
                tier = DecisionTier.Tactical,
                score = bypass ? 60f : 55f,
                jobDef = bypass ? JobDefOf.Goto : JobDefOf.Wait_MaintainPosture,
                target = bypass ? bestBypass : pawn.Position,
                jobExpiryTicks = 45,
                actuation = Actuation.Interrupt,
                validUntilTick = now + 60,
                commitTicks = 45,
                reason = bypass ? "stepping around a firing lane" : "holding short of a firing lane"
            });
        }

        public static void NotifyDamaged(Pawn victim, DamageInfo dinfo)
        {
            if (victim?.Map == null) return;

            IntVec3 threatPos = dinfo.Instigator?.Position ?? victim.Position;
            PawnTacticalState state = PawnStateTracker.GetOrCreate(victim);
            int now = PawnStateTracker.Now;
            state.RecordDamage(dinfo.Amount, threatPos, now);

            // Fire, falling debris, surgery and self-inflicted damage are not an enemy to take cover from.
            Thing instigator = dinfo.Instigator;
            if (instigator == null || instigator == victim || !instigator.HostileTo(victim))
                return;

            if (victim.Faction == Faction.OfPlayer)
            {
                ColonyAlert.Raise(victim.Map, AlertLevel.Combat);
                // Shot at by this enemy: everything in its reach is dangerous until it is gone or stops.
                if (instigator is Pawn shooter)
                    ThreatMap.Get(victim.Map).Register(shooter, shooter.CurrentEffectiveVerb, now);
            }

            if (!ModuleFlags.Combat || !SmartAIMod.Settings.combatSmartCover || !PawnStateTracker.CanActAutonomously(victim) || victim.IsBurning())
                return;
            if (!UsesTacticalResponse(victim))
                return;

            if (now < state.nextCoverEvalTick)
                return;

            state.nextCoverEvalTick = now + 30;
            // Actual damage is urgent: it skips the budget check but still uses up a slot.
            Arbiter.TryConsumeHeavyBudget(urgent: true);
            ExecuteCombatResponse(victim, threatPos, Actuation.Interrupt);
        }

        public static void NotifyNearMiss(Pawn pawn, BulletImpactData impactData)
        {
            if (pawn?.Map == null || pawn.Dead || pawn.Downed || pawn.Faction != Faction.OfPlayer)
                return;

            Thing launcher = impactData.bullet?.Launcher;
            if (launcher != null)
            {
                if (!launcher.HostileTo(pawn))
                    return;
                ColonyAlert.Raise(pawn.Map, AlertLevel.Combat);
                if (launcher is Pawn shooter)
                    ThreatMap.Get(pawn.Map).Register(shooter, shooter.CurrentEffectiveVerb, PawnStateTracker.Now);
            }

            if (!SmartAIMod.Settings.combatSmartCover || !PawnStateTracker.CanActAutonomously(pawn) || !UsesTacticalResponse(pawn))
                return;

            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);
            int now = PawnStateTracker.Now;
            if (now < state.nextCoverEvalTick)
                return;

            IntVec3 threatPos = launcher?.Position ?? impactData.impactPosition;
            float currentCover = CoverUtility.CalculateOverallBlockChance(pawn.Position, threatPos, pawn.Map);
            if (currentCover < 0.30f)
            {
                state.RecordThreat(threatPos, 200, now);
                if (!Arbiter.TryConsumeHeavyBudget(urgent: false))
                {
                    state.nextCoverEvalTick = now + BudgetRetryTicks;
                    return;
                }

                state.nextCoverEvalTick = now + 60;
                ExecuteCombatResponse(pawn, threatPos, Actuation.Interrupt);
            }
        }

        // Urgent: a hostile is aiming at this pawn. Not urgent: it merely heard gunfire, and breaking off its work
        // for that costs a full vanilla job search, so the reaction waits for the next decision unless it is close
        // to the fight or standing somewhere dangerous.
        public static void NotifyThreatPerceived(Pawn pawn, IntVec3 threatPos, bool urgent)
        {
            if (!SmartAIMod.Settings.combatSmartCover || pawn?.Map == null)
                return;
            if (!PawnStateTracker.CanActAutonomously(pawn) || !UsesTacticalResponse(pawn))
                return;

            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);
            int now = PawnStateTracker.Now;
            if (now < state.nextCoverEvalTick)
                return;

            state.RecordThreat(threatPos, 300, now);

            // Already on its way to cover: re-evaluating every 75 ticks only replaced that move with a
            // near-identical one. Real hits still get through via the damage path.
            if (now < state.committedUntilTick && state.committedBehaviorId == BehaviorIds.Cover)
            {
                state.nextCoverEvalTick = now + 75;
                return;
            }

            // Merely hearing distant gunfire from a safe spot is awareness, not danger: the alert level and the
            // hazard work filter already keep the pawn out of the fight. Moving it here made it trade places with
            // vanilla every time vanilla sent it back to safe work (soak: Repair/Haul/Wander <-> Goto loops).
            if (!urgent &&
                pawn.Position.DistanceToSquared(threatPos) > NearThreatDistSq &&
                UtilityEvaluator.GetDangerAt(pawn.Map, pawn.Position, pawn) < SmartAIMod.Settings.hazardSensitivity)
            {
                state.nextCoverEvalTick = now + 75;
                return;
            }

            // In a long firefight the same pawn hears the same shooter over and over. If the last evaluation
            // left it where it is and neither side has moved, the answer would be identical; skip the search.
            if (now < state.quietUntilTick && pawn.Position == state.quietPawnCell &&
                threatPos.DistanceToSquared(state.quietThreatCell) <= QuietThreatToleranceSq)
            {
                state.nextCoverEvalTick = now + 75;
                return;
            }

            if (!Arbiter.TryConsumeHeavyBudget(urgent: false))
            {
                state.nextCoverEvalTick = now + BudgetRetryTicks;
                return;
            }

            state.nextCoverEvalTick = now + 75;
            // Only real exposure reaches this point (aimed at, threat within 12 cells, or a dangerous cell), so react now.
            if (!ExecuteCombatResponse(pawn, threatPos, Actuation.Interrupt))
            {
                state.quietUntilTick = now + QuietPeriodTicks;
                state.quietPawnCell = pawn.Position;
                state.quietThreatCell = threatPos;
            }
        }

        private const int BudgetRetryTicks = 5;

        private const int QuietPeriodTicks = 250;
        private const int QuietThreatToleranceSq = 9;
        private const int NearThreatDistSq = 144;

        // Only pawns set to Attack get the mod's cover and hold moves. Flee is vanilla's own flee (fed by the
        // ThreatMap through the Combat patches), Ignore is the player's choice of no reaction. Pawns without the
        // setting (guests, animals handled elsewhere) keep the tactical layer.
        public static bool UsesTacticalResponse(Pawn pawn)
        {
            Pawn_PlayerSettings settings = pawn.playerSettings;
            if (settings == null || !settings.UsesConfigurableHostilityResponse)
                return true;
            return settings.hostilityResponse == HostilityResponseMode.Attack;
        }

        // Returns true when the pawn was sent somewhere else; false when it stays put (nothing better, or hold).
        private static bool ExecuteCombatResponse(Pawn pawn, IntVec3 threatPos, Actuation actuation)
        {
            if (!UsesTacticalResponse(pawn))
                return false;

            CombatResponseOption option = CoverFinder.EvaluateBestResponse(pawn, threatPos);
            if (!option.target.IsValid || option.netScore <= 0f)
                return false;

            bool staysPut = option.target.Cell == pawn.Position;
            {
                if (SmartLog.IsEnabled)
                    SmartLog.Decision("CombatResponse", pawn, $"{option.actionType} via {option.jobDef?.defName} -> {option.target.Cell} (score {option.netScore:F0}, threat {threatPos})");

                bool triage = option.actionType == TacticalActionType.SelfTriage;
                // A move to cover must last until the pawn gets there; cut off after 45 ticks it was sent straight
                // back to work and moved again (soak: Haul/Repair/Wander <-> Goto loops).
                bool move = !staysPut && option.actionType == TacticalActionType.SeekCover;
                int holdTicks = move ? MoveTicks(pawn, option.target.Cell) : 45;
                Arbiter.Submit(new Proposal
                {
                    pawn = pawn,
                    behaviorId = ResponseBehaviorId(option),
                    tier = triage ? DecisionTier.Survival : DecisionTier.Tactical,
                    score = option.netScore,
                    jobDef = option.jobDef,
                    target = option.target,
                    jobExpiryTicks = holdTicks,
                    actuation = actuation,
                    validUntilTick = PawnStateTracker.Now + 120,
                    commitTicks = triage ? 90 : holdTicks,
                    reason = "reacting to a threat"
                });
            }
            return !staysPut;
        }

        // Expected walking time to the cell with some slack for detours, bounded so a bad estimate cannot hold
        // the pawn for long.
        public static int MoveTicks(Pawn pawn, IntVec3 cell)
        {
            float ticks = pawn.Position.DistanceTo(cell) * pawn.TicksPerMoveCardinal * 1.4f + 30f;
            return Mathf.Clamp((int)ticks, 45, 900);
        }

        private static string ResponseBehaviorId(CombatResponseOption option)
        {
            switch (option.actionType)
            {
                case TacticalActionType.SelfTriage: return BehaviorIds.SelfTriage;
                case TacticalActionType.SeekCover: return BehaviorIds.Cover;
                default: return option.jobDef == JobDefOf.Wait_Combat ? BehaviorIds.Hold : BehaviorIds.KiteFromCover;
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
            if (JobDirector.IsProtectedFromInterrupt(prey, prey.CurJob))
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
                // The player is only told once the retreat actually starts.
                bool accepted = Arbiter.Submit(new Proposal
                {
                    pawn = prey,
                    behaviorId = BehaviorIds.PredatorRetreat,
                    tier = DecisionTier.Survival,
                    score = 100f,
                    jobDef = JobDefOf.Goto,
                    target = spot.cell,
                    jobExpiryTicks = 600,
                    actuation = Actuation.Interrupt,
                    validUntilTick = now + 60,
                    commitTicks = 120,
                    reason = "predator is hunting this colonist",
                    allowBusyStance = true,
                    executedMessage = $"{prey.LabelShort} spotted hunting {predator.LabelIndefinite()} and is retreating to safety!",
                    executedMessageType = MessageTypeDefOf.ThreatSmall
                });
                if (accepted && SmartLog.IsEnabled)
                    SmartLog.Decision("PredatorAlert", prey, $"retreating from {predator.LabelShort} to {spot.cell}");
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
                bool accepted = Arbiter.Submit(new Proposal
                {
                    pawn = pawn,
                    behaviorId = BehaviorIds.BlastEvac,
                    tier = DecisionTier.Survival,
                    score = 100f,
                    jobDef = JobDefOf.Goto,
                    target = bestCell,
                    jobExpiryTicks = 90,
                    actuation = Actuation.Interrupt,
                    validUntilTick = PawnStateTracker.Now + 60,
                    reason = "explosive about to blow",
                    allowBusyStance = true,
                    assistDrafted = pawn.Drafted
                });
                if (accepted && SmartLog.IsEnabled)
                    SmartLog.Decision("BlastEvac", pawn, $"leaving blast radius of {epicenter} to {bestCell}");
            }
        }
    }
}
