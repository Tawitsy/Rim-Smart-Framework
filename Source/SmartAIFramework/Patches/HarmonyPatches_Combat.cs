using HarmonyLib;
using RimWorld;
using SmartAIFramework.Combat;
using SmartAIFramework.Data;
using SmartAIFramework.Evaluation;
using SmartAIFramework.Grid;
using SmartAIFramework.Work;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Verb_LaunchProjectile __instance, ref bool __result)
        {
            if (CombatUtility.CanFireSafely(__instance))
                return true;

            __result = false;
            PawnStateTracker.FlushInterrupts();
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    public static class Patch_PathFollower_TryEnterNextPathCell
    {
        public static void Postfix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            if (___pawn == null || !___pawn.Spawned)
                return;

            if (!___pawn.Drafted && ___pawn.Faction == Faction.OfPlayer)
            {
                if (CombatUtility.IsEnteringFiringLane(___pawn, __instance.nextCell))
                {
                    CombatUtility.YieldFiringLane(___pawn, __instance.Destination.Cell);
                }
            }
            else if (SmartAIMod.Settings.combatRangedKiting && ___pawn.HostileTo(Faction.OfPlayer))
            {
                if (___pawn.CurJob?.targetA.Thing is Pawn targetColonist &&
                    targetColonist.IsColonist &&
                    targetColonist.Spawned &&
                    !targetColonist.Downed &&
                    targetColonist.Position.DistanceToSquared(___pawn.Position) <= 20)
                {
                    CombatUtility.TryKiteFromMelee(targetColonist, ___pawn);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PostApplyDamage))]
    public static class Patch_HealthTracker_PostApplyDamage
    {
        public static void Postfix(Pawn ___pawn, DamageInfo dinfo, float totalDamageDealt)
        {
            if (___pawn == null || ___pawn.Dead || !___pawn.Spawned || totalDamageDealt <= 0f)
                return;

            CombatUtility.NotifyDamaged(___pawn, dinfo);

            if (SmartAIMod.Settings.combatRangedKiting &&
                ___pawn.IsColonist &&
                dinfo.Instigator is Pawn attacker &&
                attacker.Spawned &&
                attacker.HostileTo(___pawn))
            {
                if (dinfo.Weapon?.IsMeleeWeapon == true || ___pawn.Position.DistanceToSquared(attacker.Position) <= 6)
                {
                    CombatUtility.TryKiteFromMelee(___pawn, attacker);
                }
            }

            if (SmartAIMod.Settings.selfPreservationMedicalRest &&
                SmartAIMod.Settings.emergencyBleedOutInterruption &&
                PawnStateTracker.CanActAutonomously(___pawn) &&
                !WorkUtility.IsMedicalOrRestJob(___pawn.CurJob?.def))
            {
                if (SurvivalUtility.IsInBleedOutCrisis(___pawn, SmartAIMod.Settings.crisisHorizonTicks))
                {
                    ___pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GenClamor), nameof(GenClamor.DoClamor),
        new[] { typeof(Thing), typeof(IntVec3), typeof(float), typeof(ClamorDef) })]
    public static class Patch_GenClamor_DoClamor
    {
        public static void Postfix(Thing source, IntVec3 position, float radius, ClamorDef type)
        {
            SoundAwarenessUtility.OnClamor(source, position, radius, type);
        }
    }

    [HarmonyPatch(typeof(CastPositionFinder), "CastPositionPreference")]
    public static class Patch_CastPositionFinder_CastPositionPreference
    {
        public static void Postfix(IntVec3 c, CastPositionRequest ___req, ref float __result)
        {
            if (!SmartAIMod.Settings.smartCastPositionPenalties || PawnStateTracker.ObserveOnly || __result <= 0.001f)
                return;

            Pawn caster = ___req.caster;
            Map map = caster?.Map;
            if (map == null) return;

            TacticalGrid grid = TacticalGrid.Get(map);
            if (grid == null || (!grid.HasActiveLanes && !grid.HasActiveHazards))
                return;

            if (grid.ThreatAt(c) > 0 || grid.HazardAt(c) > 0)
            {
                __result *= 0.05f;
            }

            bool isPlayer = caster.Faction == Faction.OfPlayer;
            int hostileLanes = isPlayer ? grid.HostileFiringLaneAt(c) : grid.FriendlyFiringLaneAt(c);
            if (hostileLanes > 0)
            {
                __result *= 0.1f;
            }

            int friendlyLanes = isPlayer ? grid.FriendlyFiringLaneAt(c) : grid.HostileFiringLaneAt(c);
            if (friendlyLanes > 0)
            {
                __result *= 0.25f;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Notify_BulletImpactNearby))]
    public static class Patch_Pawn_Notify_BulletImpactNearby
    {
        public static void Postfix(Pawn __instance, BulletImpactData impactData)
        {
            if (!SmartAIMod.Settings.combatNearMissReaction || __instance == null)
                return;
            CombatUtility.NotifyNearMiss(__instance, impactData);
        }
    }

    [HarmonyPatch(typeof(JobDriver_PredatorHunt), "CheckWarnPlayerInterval", new[] { typeof(int) })]
    public static class Patch_JobDriver_PredatorHunt_CheckWarnPlayerInterval
    {
        public static void Postfix(JobDriver_PredatorHunt __instance)
        {
            if (__instance == null) return;
            Pawn predator = __instance.pawn;
            Pawn prey = __instance.Prey;
            if (predator == null || prey == null || !prey.Spawned || prey.Dead || prey.Downed)
                return;
            if (prey.Faction != Faction.OfPlayer || prey.Drafted)
                return;

            CombatUtility.NotifyPredatorHuntingColonist(predator, prey);
        }
    }

    [HarmonyPatch(typeof(CompExplosive), nameof(CompExplosive.StartWick))]
    public static class Patch_CompExplosive_StartWick
    {
        public static void Postfix(CompExplosive __instance)
        {
            Thing parent = __instance?.parent;
            Map map = parent?.Map;
            if (map == null) return;

            float radius = __instance.ExplosiveRadius();
            if (radius <= 0.5f) return;

            TacticalGrid.Get(map)?.RegisterHazard(parent, parent.Position, radius + 1.5f);

            int count = GenRadial.NumCellsInRadius(radius + 1.5f);
            IntVec3[] pattern = GenRadial.RadialPattern;
            IntVec3 origin = parent.Position;

            for (int i = 0; i < count; i++)
            {
                IntVec3 c = origin + pattern[i];
                if (!c.InBounds(map)) continue;
                Pawn p = c.GetFirstPawn(map);
                if (p != null && p.IsColonist && !p.Dead && !p.Downed)
                {
                    CombatUtility.EvacuateBlastRadius(p, origin, radius + 2f);
                }
            }
        }
    }

    [HarmonyPatch(typeof(CompExplosive), nameof(CompExplosive.StopWick))]
    public static class Patch_CompExplosive_StopWick
    {
        public static void Postfix(CompExplosive __instance)
        {
            Thing parent = __instance?.parent;
            Map map = parent?.MapHeld ?? parent?.Map;
            if (map == null) return;
            TacticalGrid.Get(map)?.ClearActor(parent);
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder), "GetShootingTargetScore")]
    public static class Patch_AttackTargetFinder_GetShootingTargetScore
    {
        public static void Postfix(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb, ref float __result)
        {
            if (PawnStateTracker.ObserveOnly || searcher?.Thing?.Faction != Faction.OfPlayer)
                return;
            if (!(target?.Thing is Pawn enemy) || enemy.Dead || enemy.Downed)
                return;

            Verb enemyVerb = enemy.TryGetAttackVerb(null, false, false);
            if (enemyVerb != null)
            {
                VerbProfile profile = VerbProfileCache.Get(enemyVerb);
                if (profile != null && (profile.HasTrait(VerbTraits.Explosive) || profile.aoeRadius >= 1.5f))
                {
                    __result += 65f;
                }
            }

            if (enemy.health?.summaryHealth != null)
            {
                float hp = enemy.health.summaryHealth.SummaryHealthPercent;
                if (hp < 0.55f)
                {
                    __result += (1f - hp) * 35f;
                }
            }

            if (enemy.stances?.curStance is Stance_Warmup warmup &&
                warmup.focusTarg.Thing is Pawn aimedAlly &&
                aimedAlly.Faction == Faction.OfPlayer)
            {
                __result += 30f;
            }

            if (enemy.CurJob?.def == JobDefOf.AttackMelee &&
                enemy.CurJob.targetA.Thing is Pawn meleeVictim &&
                meleeVictim.Faction == Faction.OfPlayer &&
                enemy.Position.DistanceToSquared(meleeVictim.Position) <= 25)
            {
                __result += 25f;
            }
        }
    }
}
