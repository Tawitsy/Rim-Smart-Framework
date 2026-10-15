using System;
using Verse;

namespace SmartAIFramework.Core.Time
{
    public static class GameClock
    {
        public static int Now => Find.TickManager?.TicksGame ?? 0;

        public static int ExpiresIn(int ticks) => Now + ticks;

        public static bool IsExpired(int expiresAtTick) => Now >= expiresAtTick;

        public static int TicksSince(int tick) => tick < 0 ? int.MaxValue : Now - tick;
    }

    public struct Expiring<T>
    {
        private T value;
        private int expiresAt;

        public void Set(T newValue, int ttlTicks)
        {
            value = newValue;
            expiresAt = GameClock.ExpiresIn(ttlTicks);
        }

        public void Clear() => expiresAt = 0;

        public bool IsActive => expiresAt > 0 && !GameClock.IsExpired(expiresAt);

        public bool TryGet(out T result)
        {
            if (IsActive) { result = value; return true; }
            result = default;
            return false;
        }

        public T GetOrDefault(T fallback = default) => IsActive ? value : fallback;
    }

    public struct LazyCache<T>
    {
        private T value;
        private int version;
        private int computedAt;
        private bool hasValue;

        public T Get(int currentVersion, int ttlTicks, Func<T> compute)
        {
            if (hasValue && version == currentVersion && GameClock.TicksSince(computedAt) < ttlTicks)
                return value;

            value = compute();
            version = currentVersion;
            computedAt = GameClock.Now;
            hasValue = true;
            return value;
        }

        public void Invalidate() => hasValue = false;
    }
}
