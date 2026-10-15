using System;
using HarmonyLib;
using RimWorld;
using SmartAIFramework.Core;
using Verse;

namespace SmartAIFramework.Patches
{
    public static class VehicleFrameworkCompatPatch
    {
        public static void TryApply(Harmony harmony)
        {
            try
            {
                var targetType = AccessTools.TypeByName("Vehicles.Patch_VehiclePathing");
                if (targetType != null)
                {
                    var m1 = AccessTools.Method(targetType, "RecalculatePerceivedPathCostForVehicle");
                    if (m1 != null)
                    {
                        harmony.Patch(m1, finalizer: new HarmonyMethod(typeof(VehicleFrameworkCompatPatch), nameof(NullRefFinalizer)));
                    }
                    var m2 = AccessTools.Method(targetType, "SpawnAndNotifyVehicleRegions");
                    if (m2 != null)
                    {
                        harmony.Patch(m2, finalizer: new HarmonyMethod(typeof(VehicleFrameworkCompatPatch), nameof(NullRefFinalizer)));
                    }
                    SmartAILog.Log("SmartAI-Compat", "Registered VehicleFramework loading safeguards.");
                }
            }
            catch (Exception ex)
            {
                SmartAILog.Log("SmartAI-Compat", $"VehicleFramework safeguards skipped: {ex.Message}");
            }

            try
            {
                var wholeMapChanged = AccessTools.Method(typeof(MapDrawer), nameof(MapDrawer.WholeMapChanged));
                if (wholeMapChanged != null)
                {
                    harmony.Patch(wholeMapChanged, prefix: new HarmonyMethod(typeof(VehicleFrameworkCompatPatch), nameof(MapDrawer_Prefix)));
                }
            }
            catch (Exception ex)
            {
                SmartAILog.Log("SmartAI-Compat", $"MapDrawer safeguard skipped: {ex.Message}");
            }

            try
            {
                var compTick = AccessTools.Method(typeof(CompPowerPlantSteam), nameof(CompPowerPlantSteam.CompTick));
                if (compTick != null)
                {
                    harmony.Patch(compTick, finalizer: new HarmonyMethod(typeof(VehicleFrameworkCompatPatch), nameof(NullRefFinalizer)));
                }
            }
            catch (Exception ex)
            {
                SmartAILog.Log("SmartAI-Compat", $"CompPowerPlantSteam safeguard skipped: {ex.Message}");
            }
        }

        private static bool MapDrawer_Prefix(MapDrawer __instance)
        {
            var field = AccessTools.Field(typeof(MapDrawer), "sections");
            if (field == null) return true;
            return field.GetValue(__instance) != null;
        }

        private static Exception NullRefFinalizer(Exception __exception)
        {
            if (__exception is NullReferenceException)
            {
                return null;
            }
            return __exception;
        }
    }
}
