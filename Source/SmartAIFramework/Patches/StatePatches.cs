using HarmonyLib;
using RimWorld;
using SmartAIFramework.Brain;
using SmartAIFramework.Core.Knowledge;
using SmartAIFramework.Core_Engine;
using SmartAIFramework.Modules;
using Verse;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.SetStance))]
    public static class Patch_StanceTracker_SetStance
    {
        public static void Prefix(Pawn_StanceTracker __instance, Stance newStance)
        {
            Pawn pawn = __instance?.pawn;
            if (pawn == null) return;

            if (__instance.curStance is Stance_Warmup oldWarmup && oldWarmup != newStance)
            {
                if (oldWarmup.focusTarg.Thing is Pawn oldTarget)
                {
                    KnowledgeBase.Writer(oldTarget)?.RemoveAimer(pawn);
                }
            }

            if (__instance.curStance != newStance)
            {
                Core.Signals.SignalBus.Publish(new Core.Signals.StanceChangedSignal(pawn, __instance.curStance, newStance));
            }
        }

        public static void Postfix(Pawn_StanceTracker __instance, Stance newStance)
        {
            Pawn pawn = __instance.pawn;
            Map map = pawn?.Map;
            if (map == null) return;

            var grid = TacticalGridMapComponent.For(map);
            if (grid == null) return;

            grid.ClearActor(pawn);

            if (newStance is Stance_Warmup warmup && warmup.verb != null && warmup.focusTarg.IsValid)
            {
                var profile = ModActionAnalyzer.Analyze(warmup.verb);
                if (profile == null || !profile.IsHostileAction) return;

                bool hostileToPlayer = pawn.HostileTo(Faction.OfPlayer);
                grid.RegisterFiringLane(pawn, pawn.Position, warmup.focusTarg.Cell, profile.aoeRadius, hostileToPlayer);

                if (warmup.focusTarg.Thing is Pawn targetPawn && pawn.HostileTo(targetPawn))
                {
                    CognitiveBrain.NotifyTargeted(targetPawn, pawn);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_HealthTracker_MakeDowned
    {
        public static void Prefix(Pawn ___pawn) => ActorCleanup.Clear(___pawn, forget: false);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill
    {
        public static void Prefix(Pawn __instance) => ActorCleanup.Clear(__instance, forget: true);
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    public static class Patch_Thing_DeSpawn
    {
        public static void Prefix(Thing __instance) => ActorCleanup.Clear(__instance, forget: true);
    }

    internal static class ActorCleanup
    {
        public static void Clear(Thing thing, bool forget = false)
        {
            if (thing == null) return;
            Map map = thing.MapHeld ?? thing.Map;
            if (map != null)
            {
                TacticalGridMapComponent.For(map)?.ClearActor(thing);
            }
            if (thing is Pawn p)
            {
                Core.Decisions.IntentArbiter.RemovePawn(p);
                if (forget)
                {
                    KnowledgeBase.Forget(p);
                    Module_CommonSense.ClearPawn(p.thingIDNumber);
                    Module_Delegation.ClearPawn(p.thingIDNumber);
                }
            }
        }
    }
}
