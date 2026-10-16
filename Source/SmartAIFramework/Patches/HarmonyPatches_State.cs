using HarmonyLib;
using RimWorld;
using SmartAIFramework.Combat;
using SmartAIFramework.Data;
using SmartAIFramework.Grid;
using Verse;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.SetStance))]
    public static class Patch_StanceTracker_SetStance
    {
        public static void Prefix(Pawn_StanceTracker __instance, Stance newStance)
        {
            if (!(__instance?.curStance is Stance_Warmup oldWarmup) || oldWarmup == newStance)
                return;

            Pawn pawn = __instance.pawn;
            if (pawn?.Map != null)
            {
                TacticalGrid.Get(pawn.Map)?.ClearActor(pawn);
            }

            if (oldWarmup.focusTarg.Thing is Pawn oldTarget)
            {
                PawnTacticalState state = PawnStateTracker.TryGet(oldTarget);
                if (state != null && state.activeAimerCount > 0)
                {
                    state.activeAimerCount--;
                    state.version++;
                }
            }
        }

        public static void Postfix(Pawn_StanceTracker __instance, Stance newStance)
        {
            if (!(newStance is Stance_Warmup warmup) || warmup.verb == null || !warmup.focusTarg.IsValid)
                return;

            Pawn pawn = __instance.pawn;
            Map map = pawn?.Map;
            if (map == null) return;

            VerbProfile profile = VerbProfileCache.Get(warmup.verb);
            if (profile == null || !profile.IsHostile)
                return;

            bool hostileToPlayer = pawn.HostileTo(Faction.OfPlayer);
            TacticalGrid.Get(map)?.RegisterFiringLane(pawn, pawn.Position, warmup.focusTarg.Cell, profile.aoeRadius, hostileToPlayer);

            if (warmup.focusTarg.Thing is Pawn targetPawn && targetPawn.IsColonist && pawn.HostileTo(targetPawn))
            {
                PawnTacticalState state = PawnStateTracker.GetOrCreate(targetPawn);
                state.activeAimerCount++;
                state.version++;
                CombatUtility.NotifyThreatPerceived(targetPawn, pawn.Position);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_HealthTracker_MakeDowned
    {
        public static void Prefix(Pawn ___pawn) => StateCleanupUtility.Cleanup(___pawn, forgetState: false);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill
    {
        public static void Prefix(Pawn __instance) => StateCleanupUtility.Cleanup(__instance, forgetState: true);
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    public static class Patch_Thing_DeSpawn
    {
        public static void Prefix(Thing __instance) => StateCleanupUtility.Cleanup(__instance, forgetState: true);
    }

    internal static class StateCleanupUtility
    {
        public static void Cleanup(Thing thing, bool forgetState)
        {
            if (thing == null) return;

            Map map = thing.MapHeld ?? thing.Map;
            if (map != null)
            {
                TacticalGrid grid = TacticalGrid.Get(map);
                if (grid != null && (grid.HasActiveLanes || grid.HasActiveHazards))
                {
                    grid.ClearActor(thing);
                }
            }

            if (thing is Pawn p)
            {
                PawnStateTracker.RemovePawn(p, forgetState);
            }
        }
    }
}
