using System.Collections.Generic;
using SmartAIFramework.Core.Time;
using Verse;

namespace SmartAIFramework.Core.Knowledge
{
    public static class CognitiveBlacklist
    {
        private static readonly Dictionary<int, int> suppressedUntilTick = new Dictionary<int, int>(256);

        public static void Suppress(Thing target, int durationTicks = 1200)
        {
            if (target == null) return;
            suppressedUntilTick[target.thingIDNumber] = GameClock.ExpiresIn(durationTicks);
        }

        public static bool IsSuppressed(Thing target)
        {
            if (target == null) return false;
            if (!suppressedUntilTick.TryGetValue(target.thingIDNumber, out int expiresAt))
                return false;

            if (GameClock.IsExpired(expiresAt))
            {
                suppressedUntilTick.Remove(target.thingIDNumber);
                return false;
            }

            return true;
        }

        public static void Clear()
        {
            suppressedUntilTick.Clear();
        }
    }
}
