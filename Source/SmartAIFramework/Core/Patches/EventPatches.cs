using HarmonyLib;
using RimWorld;
using SmartAIFramework.Combat;
using SmartAIFramework.Data;
using SmartAIFramework.Decisions;
using SmartAIFramework.Work;
using Verse;

namespace SmartAIFramework.Patches
{
    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_Verb_TryCastNextBurstShot
    {
        public static void Postfix()
        {
            JobDirector.FlushIfRequestedAfterCast();
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PostApplyDamage))]
    public static class Patch_HealthTracker_PostApplyDamage
    {
        public static void Postfix(Pawn ___pawn, DamageInfo dinfo, float totalDamageDealt)
        {
            if (___pawn == null || ___pawn.Dead || !___pawn.Spawned || totalDamageDealt <= 0f)
                return;

            // Records the damage and raises the colony alert; NotifyDamaged itself skips the cover reaction when Combat is off.
            CombatUtility.NotifyDamaged(___pawn, dinfo);

            if (ModuleFlags.Combat &&
                SmartAIMod.Settings.combatRangedKiting &&
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

            if (ModuleFlags.Survival &&
                SmartAIMod.Settings.selfPreservationMedicalRest &&
                SmartAIMod.Settings.emergencyBleedOutInterruption &&
                PawnStateTracker.CanActAutonomously(___pawn) &&
                !WorkUtility.IsMedicalOrRestJob(___pawn.CurJob?.def))
            {
                // Interrupts only when a triage job exists; the decision point then swaps it in.
                SelfTriageBehavior.SubmitInterrupt(___pawn);
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
}
