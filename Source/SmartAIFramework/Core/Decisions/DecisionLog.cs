using System.Collections.Generic;
using SmartAIFramework.Core.Time;
using Verse;

namespace SmartAIFramework.Core.Decisions
{
    public static class DecisionLog
    {
        public readonly struct Entry
        {
            public readonly int tick;
            public readonly string pawn;
            public readonly string what;
            public readonly bool applied;
            public Entry(int tick, string pawn, string what, bool applied)
            { this.tick = tick; this.pawn = pawn; this.what = what; this.applied = applied; }
            public override string ToString() => $"[{tick}] {(applied ? "APPLIED " : "OBSERVED")} {pawn}: {what}";
        }

        private const int Capacity = 256;
        private static readonly Entry[] ring = new Entry[Capacity];
        private static int head;
        private static int count;

        public static void Record(Pawn pawn, string what, bool applied)
        {
            var e = new Entry(GameClock.Now, pawn?.LabelShort ?? "?", what, applied);
            ring[head] = e;
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
            if (SmartAIMod.Settings.debugLogging) Log.Message("[SmartAI] " + e);
        }

        public static IEnumerable<Entry> Recent()
        {
            for (int i = 0; i < count; i++)
                yield return ring[(head - 1 - i + Capacity) % Capacity];
        }
    }
}
