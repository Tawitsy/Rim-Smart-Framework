using System;
using System.Reflection;
using HarmonyLib;
using SmartAIFramework.Data;
using Verse;

namespace SmartAIFramework.Compat
{
    // The Perspective Shift avatar is the player's own body: the mod must never steer it.
    public static class PerspectiveShiftCompat
    {
        private static bool resolved;
        private static Func<Pawn, bool> isAvatar;

        public static bool IsDirectlyControlled(Pawn pawn)
        {
            if (!resolved)
                Resolve();
            return isAvatar != null && pawn != null && isAvatar(pawn);
        }

        private static void Resolve()
        {
            resolved = true;
            try
            {
                Type state = AccessTools.TypeByName("PerspectiveShift.State");
                MethodInfo method = state != null ? AccessTools.Method(state, "IsAvatar", new[] { typeof(Pawn) }) : null;
                if (method != null && method.IsStatic && method.ReturnType == typeof(bool))
                {
                    isAvatar = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), method);
                    if (SmartLog.IsEnabled)
                        SmartLog.Message("Compat", "Perspective Shift detected: its avatar pawn is left to the player.");
                }
            }
            catch (Exception ex)
            {
                isAvatar = null;
                Log.Warning($"[Rim Smart Framework] Perspective Shift adapter disabled: {ex.Message}");
            }
        }
    }
}
