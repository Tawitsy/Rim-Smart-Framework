using HarmonyLib;
using RimWorld;
using SmartAIFramework.Brain;
using SmartAIFramework.Core.Decisions;
using SmartAIFramework.Core_Engine;
using SmartAIFramework.Modules;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot
    {
        public static bool Prefix(Verb_LaunchProjectile __instance, ref bool __result)
        {
            if (Module_Combat.ShouldAllowShot(__instance)) return true;
            __result = false;
            IntentArbiter.FlushInterrupts();
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    public static class Patch_PathFollower_TryEnterNextPathCell
    {
        public static void Postfix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            if (___pawn == null) return;

            if (!___pawn.Drafted && ___pawn.Faction == Faction.OfPlayer)
            {
                if (Module_Combat.IsSteppingIntoFiringLane(___pawn, __instance.nextCell))
                {
                    if (!ActionGate.ApplyVeto(___pawn, assistLevel: false, "Yielded to active firing lane"))
                        return;

                    Module_Combat.YieldToFiringLane(___pawn, __instance.Destination.Cell);
                }
            }

            else if (SmartAIMod.Settings.combatRangedKiting && ___pawn.HostileTo(Faction.OfPlayer))
            {
                Pawn targetColonist = ___pawn.CurJob?.targetA.Thing as Pawn;
                if (targetColonist != null && targetColonist.IsColonist && targetColonist.Spawned && !targetColonist.Downed)
                {
                    if (targetColonist.Position.DistanceTo(___pawn.Position) <= 4.5f)
                    {
                        Module_Combat.CheckTacticalFallback(targetColonist, ___pawn);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PostApplyDamage))]
    public static class Patch_HealthTracker_PostApplyDamage
    {
        public static void Postfix(Pawn ___pawn, DamageInfo dinfo, float totalDamageDealt)
        {
            if (___pawn == null || ___pawn.Dead || totalDamageDealt <= 0f) return;
            CognitiveBrain.NotifyDamaged(___pawn, dinfo);

            if (SmartAIMod.Settings.combatRangedKiting && ___pawn.IsColonist && dinfo.Instigator is Pawn attacker && attacker.HostileTo(___pawn))
            {
                if (dinfo.Weapon?.IsMeleeWeapon == true || ___pawn.Position.DistanceTo(attacker.Position) <= 2.5f)
                {
                    Module_Combat.CheckTacticalFallback(___pawn, attacker);
                }
            }

            if (SmartAIMod.Settings.selfPreservationMedicalRest && SmartAIMod.Settings.emergencyBleedOutInterruption &&
                !ActionGate.ObserveOnly && ___pawn.IsColonist && !___pawn.Drafted && !Module_CommonSense.IsMedicalOrRestJob(___pawn.CurJob?.def))
            {
                if (DeterministicPredictor.IsInImmediatePhysiologicalCrisis(___pawn, SmartAIMod.Settings.crisisHorizonTicks))
                {
                    ___pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
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
            AcousticEventBus.Broadcast(source, position, radius, type);
        }
    }

    [HarmonyPatch(typeof(CastPositionFinder), "CastPositionPreference")]
    public static class Patch_CastPositionFinder_CastPositionPreference
    {
        public static void Postfix(IntVec3 c, CastPositionRequest ___req, ref float __result)
        {
            if (!SmartAIMod.Settings.smartCastPositionPenalties || ActionGate.ObserveOnly || __result <= 0.001f) return;

            Pawn caster = ___req.caster;
            if (caster == null || caster.Map == null) return;

            var grid = TacticalGridMapComponent.For(caster.Map);
            if (grid == null) return;

            bool isPlayer = caster.Faction == Faction.OfPlayer;

            if (grid.ThreatAt(c) > 0 || grid.HazardAt(c) > 0)
            {
                __result *= 0.05f;
            }

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
            if (!SmartAIMod.Settings.combatNearMissReaction || __instance == null) return;
            Module_Combat.OnNearMiss(__instance, impactData);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.HearClamor))]
    public static class Patch_Pawn_HearClamor
    {
        public static void Postfix(Pawn __instance, Thing source, ClamorDef type)
        {
            if (__instance == null) return;
            Module_Combat.OnClamorHeard(__instance, source, type);
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
            if (predator == null || prey == null || !prey.Spawned || prey.Dead || prey.Downed) return;
            if (prey.Faction != Faction.OfPlayer || prey.Drafted) return;

            Module_Combat.Notify_PredatorHuntingColonist(predator, prey);
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

            TacticalGridMapComponent.For(map)?.RegisterHazard(parent, parent.Position, radius + 1.5f);

            foreach (IntVec3 c in GenRadial.RadialCellsAround(parent.Position, radius + 1.5f, true))
            {
                if (!c.InBounds(map)) continue;
                Pawn p = c.GetFirstPawn(map);
                if (p != null && p.IsColonist && !p.Dead && !p.Downed)
                {
                    Module_Combat.EvacuateExplosionRadius(p, parent.Position, radius + 2f);
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
            TacticalGridMapComponent.For(map)?.ClearActor(parent);
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder), "GetShootingTargetScore")]
    public static class Patch_AttackTargetFinder_GetShootingTargetScore
    {
        public static void Postfix(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb, ref float __result)
        {
            if (ActionGate.ObserveOnly || searcher?.Thing?.Faction != Faction.OfPlayer) return;
            if (!(target?.Thing is Pawn enemy) || enemy.Dead || enemy.Downed) return;

            Verb enemyVerb = enemy.TryGetAttackVerb(null, false, false);
            if (enemyVerb != null)
            {
                var profile = ModActionAnalyzer.Analyze(enemyVerb);
                if (profile != null && (profile.Is(ActionTraits.Explosive) || profile.aoeRadius >= 1.5f))
                {
                    __result += 65f;
                }
            }

            if (enemy.health?.summaryHealth != null)
            {
                float enemyHealth = enemy.health.summaryHealth.SummaryHealthPercent;
                if (enemyHealth < 0.55f)
                {
                    __result += (1f - enemyHealth) * 35f;
                }
            }

            if (enemy.stances?.curStance is Stance_Warmup warmup && warmup.focusTarg.Thing is Pawn aimedAlly && aimedAlly.Faction == Faction.OfPlayer)
            {
                __result += 30f;
            }

            if (enemy.CurJob?.def == JobDefOf.AttackMelee && enemy.CurJob.targetA.Thing is Pawn meleeVictim && meleeVictim.Faction == Faction.OfPlayer)
            {
                if (enemy.Position.DistanceTo(meleeVictim.Position) <= 5f)
                {
                    __result += 25f;
                }
            }
        }
    }
}
