using System;
using RimWorld;
using Verse;

namespace SmartAIFramework.Data
{
    public enum AlertLevel
    {
        Peace = 0,
        Alert,
        Combat
    }

    public static class ColonyAlert
    {
        public const int CombatWindowTicks = 900;
        public const int AlertWindowTicks = 2500;

        private sealed class Slot
        {
            public Map map;
            public int lastCombatTick = NeverTick;
            public int lastAlertTick = NeverTick;
            public int envTick = NeverTick;
            public bool envHazard;
        }

        private const int NeverTick = -1000000;

        private static Slot[] byMapIndex = new Slot[8];

        public static void Reset()
        {
            Array.Clear(byMapIndex, 0, byMapIndex.Length);
        }

        public static void Clear(Map map)
        {
            if (map == null) return;
            int idx = map.Index;
            if ((uint)idx < (uint)byMapIndex.Length && byMapIndex[idx] != null && byMapIndex[idx].map == map)
                byMapIndex[idx] = null;
        }

        public static void Raise(Map map, AlertLevel level)
        {
            if (level == AlertLevel.Peace) return;
            Slot slot = GetOrCreate(map);
            if (slot == null) return;

            int now = PawnStateTracker.Now;
            slot.lastAlertTick = now;
            if (level == AlertLevel.Combat)
                slot.lastCombatTick = now;
        }

        public static AlertLevel Get(Map map)
        {
            if (map == null) return AlertLevel.Peace;
            int idx = map.Index;
            if ((uint)idx >= (uint)byMapIndex.Length) return AlertLevel.Peace;

            Slot slot = byMapIndex[idx];
            if (slot == null || slot.map != map) return AlertLevel.Peace;

            int now = PawnStateTracker.Now;
            if (now - slot.lastCombatTick < CombatWindowTicks) return AlertLevel.Combat;
            if (now - slot.lastAlertTick < AlertWindowTicks) return AlertLevel.Alert;
            return AlertLevel.Peace;
        }

        public static bool HasEnvironmentalHazard(Map map)
        {
            Slot slot = GetOrCreate(map);
            if (slot == null) return false;

            int now = PawnStateTracker.Now;
            if (slot.envTick != now)
            {
                slot.envTick = now;
                GameConditionManager mgr = map.gameConditionManager;
                slot.envHazard = mgr != null &&
                    (mgr.ConditionIsActive(GameConditionDefOf.ToxicFallout) ||
                     (ModsConfig.BiotechActive && mgr.ConditionIsActive(GameConditionDefOf.NoxiousHaze)));
            }
            return slot.envHazard;
        }

        private static Slot GetOrCreate(Map map)
        {
            if (map == null) return null;
            int idx = map.Index;
            if (idx < 0) return null;

            if (idx >= byMapIndex.Length)
                Array.Resize(ref byMapIndex, Math.Max(idx + 4, byMapIndex.Length * 2));

            Slot slot = byMapIndex[idx];
            if (slot == null || slot.map != map)
            {
                slot = new Slot { map = map };
                byMapIndex[idx] = slot;
            }
            return slot;
        }
    }
}
