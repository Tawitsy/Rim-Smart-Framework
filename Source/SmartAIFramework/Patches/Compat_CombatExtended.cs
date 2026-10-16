using System;
using HarmonyLib;
using SmartAIFramework.Combat;
using SmartAIFramework.Data;
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
            PawnStateTracker.RequestFlushAfterCast();
            return false;
        }
    }
}
