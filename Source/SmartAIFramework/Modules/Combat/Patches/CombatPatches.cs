using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Combat;
using SmartAIFramework.Data;
using SmartAIFramework.Decisions;
using SmartAIFramework.Grid;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Patches
{
    [HarmonyPatchCategory("Combat")]
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Verb_LaunchProjectile __instance, ref bool __result)
        {
            if (CombatUtility.CanFireSafely(__instance))
                return true;

            __result = false;
            JobDirector.RequestFlushAfterCast();
            return false;
        }
    }

    [HarmonyPatchCategory("Combat")]
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
            else
            {
                if (SmartAIMod.Settings.combatRangedKiting &&
                    ___pawn.CurJob?.targetA.Thing is Pawn targetColonist &&
                    targetColonist.IsColonist &&
                    targetColonist.Spawned &&
                    !targetColonist.Downed &&
                    targetColonist.Position.DistanceToSquared(___pawn.Position) <= 20 &&
                    ___pawn.HostileTo(Faction.OfPlayer))
                {
                    CombatUtility.TryKiteFromMelee(targetColonist, ___pawn);
                }

                if (___pawn.CurJobDef == JobDefOf.PredatorHunt &&
                    SmartAIMod.Settings.predatorSurvivalAlert &&
                    ___pawn.CurJob.targetA.Thing is Pawn prey &&
                    prey.Faction == Faction.OfPlayer &&
                    !prey.Drafted &&
                    prey.Spawned &&
                    !prey.Dead &&
                    !prey.Downed &&
                    prey.Position.DistanceToSquared(___pawn.Position) <= 900)
                {
                    CombatUtility.NotifyPredatorHuntingColonist(___pawn, prey);
                }
            }
        }
    }

    [HarmonyPatchCategory("Combat")]
    [HarmonyPatch(typeof(Region), nameof(Region.DangerFor))]
    public static class Patch_Region_DangerFor
    {
        private static readonly AccessTools.FieldRef<Region, Dictionary<Pawn, Danger>> cachedDangers =
            AccessTools.FieldRefAccess<Region, Dictionary<Pawn, Danger>>("cachedDangers");

        public static void Postfix(Region __instance, Pawn p, ref Danger __result)
        {
            if (__result >= Danger.Deadly || p == null || Current.ProgramState != ProgramState.Playing)
                return;

            Map map = __instance.Map;
            ThreatMap threats = ThreatMap.TryGet(map);
            if (threats == null || !threats.AnyActive)
                return;

            if (p.Map != map || !ThreatMap.IsEligible(p))
                return;

            if (!threats.RegionInReach(__instance))
                return;

            __result = Danger.Deadly;

            cachedDangers(__instance)[p] = Danger.Deadly;
        }
    }

    [HarmonyPatchCategory("Combat")]
    [HarmonyPatch(typeof(SelfDefenseUtility), nameof(SelfDefenseUtility.ShouldStartFleeing))]
    public static class Patch_SelfDefenseUtility_ShouldStartFleeing
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result || pawn == null)
                return;

            Map map = pawn.Map;
            ThreatMap threats = ThreatMap.TryGet(map);
            if (threats == null || !threats.AnyActive || !ThreatMap.IsEligible(pawn))
                return;

            if (threats.IsInReach(pawn.Position, pawn))
                __result = true;
        }
    }

    [HarmonyPatchCategory("Combat")]
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

    [HarmonyPatchCategory("Combat")]
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

    [HarmonyPatchCategory("Combat")]
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
                    CombatUtility.EvacuateBlastRadius(p, parent, radius + 2f, __instance.Props.explosiveDamageType);
                }
            }
        }
    }

    [HarmonyPatchCategory("Combat")]
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

    [HarmonyPatchCategory("Combat")]
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
