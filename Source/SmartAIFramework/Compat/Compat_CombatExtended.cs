using System;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Combat;
using SmartAIFramework.Data;
using SmartAIFramework.Decisions;
using SmartAIFramework.Grid;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Patches
{
    public static class Compat_CombatExtended
    {
        public static void TryApply(Harmony harmony)
        {
            try
            {
                Type ceVerbType = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
                if (ceVerbType == null) return;

                var targetMethod = AccessTools.Method(ceVerbType, "TryCastShot");
                if (targetMethod == null) return;

                var prefix = new HarmonyMethod(typeof(Compat_CombatExtended), nameof(TryCastShotCE_Prefix))
                {
                    priority = Priority.High
                };
                harmony.Patch(targetMethod, prefix: prefix);

                var lineMethod = AccessTools.Method(ceVerbType, "TryFindCEShootLineFromTo",
                    new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(ShootLine).MakeByRefType(), typeof(Vector3).MakeByRefType() });
                if (lineMethod != null)
                {
                    harmony.Patch(lineMethod, postfix: new HarmonyMethod(typeof(Compat_CombatExtended), nameof(TryFindCEShootLineFromTo_Postfix)));
                }
            }
            catch (Exception ex)
            {
                SmartLog.Message("Compat", $"CombatExtended hook skipped: {ex.Message}");
            }
        }

        private static bool TryCastShotCE_Prefix(Verb __instance, ref bool __result)
        {
            if (CombatUtility.CanFireSafely(__instance))
                return true;

            __result = false;
            JobDirector.RequestFlushAfterCast();
            return false;
        }

        private static void TryFindCEShootLineFromTo_Postfix(Verb __instance, bool __result, IntVec3 root, LocalTargetInfo targ)
        {
            if (__result) return;
            TrySidestepAfterRejectedLine(__instance, root, targ);
        }

        private static void TrySidestepAfterRejectedLine(Verb verb, IntVec3 root, LocalTargetInfo targ)
        {
            Pawn pawn = verb.CasterPawn;
            if (pawn == null || root != pawn.Position || pawn.Faction != Faction.OfPlayer)
                return;
            if (!SmartAIMod.Settings.combatFiringDiscipline || !PawnStateTracker.CanActAutonomously(pawn))
                return;
            if (Arbiter.IsOnCooldown(pawn, BehaviorIds.Sidestep))
                return;

            PawnTacticalState state = PawnStateTracker.GetOrCreate(pawn);
            int now = PawnStateTracker.Now;
            if (now < state.nextSidestepTick)
                return;

            Map map = pawn.Map;
            IntVec3 targetCell = targ.Cell;
            if (map == null || !targetCell.IsValid || !targetCell.InBounds(map))
                return;

            float range = verb.EffectiveRange;
            if (range > 0f && root.DistanceToSquared(targetCell) > range * range)
                return;
            if (!LineOfSightUtility.HasFriendlyAlongSegment(map, pawn, root, targetCell))
                return;
            if (!CombatUtility.TryFindSidestepCell(pawn, targetCell, range, out IntVec3 sidestep))
                return;

            state.nextSidestepTick = now + 75;
            Arbiter.Submit(new Proposal
            {
                pawn = pawn,
                behaviorId = BehaviorIds.Sidestep,
                tier = DecisionTier.Tactical,
                score = 65f,
                jobDef = JobDefOf.Goto,
                target = sidestep,
                jobExpiryTicks = CombatUtility.SidestepTicks(pawn.Position, sidestep),
                actuation = Actuation.Interrupt,
                validUntilTick = now + 60,
                commitTicks = 35,
                reason = "friendly in line of fire (CE)",
                allowBusyStance = true
            });
        }
    }
}
