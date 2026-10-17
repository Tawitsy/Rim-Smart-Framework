using System;
using System.Reflection;
using HarmonyLib;
using SmartAIFramework.Data;
using Verse;

namespace SmartAIFramework.Compat
{
    // Common Sense already sweeps the work area before a bill; doing it twice would only waste the colonist's time.
    public static class CommonSenseCompat
    {
        private static bool resolved;
        private static FieldInfo cleanBeforeWork;

        public static bool CleansBeforeWork
        {
            get
            {
                if (!resolved)
                    Resolve();
                if (cleanBeforeWork == null)
                    return false;

                try
                {
                    return (bool)cleanBeforeWork.GetValue(null);
                }
                catch (Exception ex)
                {
                    cleanBeforeWork = null;
                    Log.Warning($"[Rim Smart Framework] Common Sense adapter disabled: {ex.Message}");
                    return false;
                }
            }
        }

        private static void Resolve()
        {
            resolved = true;
            try
            {
                Type settings = AccessTools.TypeByName("CommonSense.Settings");
                FieldInfo field = settings != null ? AccessTools.Field(settings, "clean_before_work") : null;
                if (field != null && field.IsStatic && field.FieldType == typeof(bool))
                {
                    cleanBeforeWork = field;
                    if (SmartLog.IsEnabled)
                        SmartLog.Message("Compat", "Common Sense detected: its clean-before-work setting replaces our sanitation toil when enabled.");
                }
            }
            catch (Exception ex)
            {
                cleanBeforeWork = null;
                Log.Warning($"[Rim Smart Framework] Common Sense adapter disabled: {ex.Message}");
            }
        }
    }
}
