using System;
using HarmonyLib;
using SmartAIFramework.Core;
using SmartAIFramework.Core.Decisions;
using SmartAIFramework.Modules;
using Verse;

namespace SmartAIFramework.Patches
{
    public static class CombatExtendedCompatPatch
    {
        public static void TryApply(Harmony harmony)
        {
            try
            {
                var ceVerbType = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
                if (ceVerbType != null)
                {
                    var targetMethod = AccessTools.Method(ceVerbType, "TryCastShot");
                    if (targetMethod != null)
                    {
                        var prefix = AccessTools.Method(typeof(CombatExtendedCompatPatch), nameof(TryCastShotCE_Prefix));
                        harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefix));
                        SmartAILog.Log("SmartAI-Compat", "Patched CombatExtended.Verb_LaunchProjectileCE.TryCastShot for friendly fire & explosive safety.");
                    }
                }
            }
            catch (Exception ex)
            {
                SmartAILog.Log("SmartAI-Compat", $"CombatExtended integration skipped: {ex.Message}");
            }
        }

        private static bool TryCastShotCE_Prefix(Verb __instance, ref bool __result)
        {
            if (Module_Combat.ShouldAllowShot(__instance)) return true;
            __result = false;
            IntentArbiter.FlushInterrupts();
            return false;
        }
    }
}
