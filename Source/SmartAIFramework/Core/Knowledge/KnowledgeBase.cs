using System.Collections.Generic;
using Verse;

namespace SmartAIFramework.Core.Knowledge
{
    public class KnowledgeBase : GameComponent
    {
        private static KnowledgeBase instance;
        private readonly Dictionary<int, PawnMemory> memories = new Dictionary<int, PawnMemory>();

        public KnowledgeBase(Game game)
        {
            instance = this;
            CognitiveBlacklist.Clear();
            Modules.Module_CommonSense.ClearAll();
            Modules.Module_Delegation.ClearAll();
            Decisions.IntentArbiter.Clear();
            Brain.SemanticAffordanceEngine.ClearCache();
            Core_Engine.ModActionAnalyzer.ClearCache();
        }

        private static KnowledgeBase Instance
        {
            get
            {
                if (instance == null || Current.Game == null) return null;
                return instance;
            }
        }

        public static PawnSituation Situation(Pawn pawn)
        {
            var kb = Instance;
            if (kb == null || pawn == null) return PawnSituation.Unknown;
            return kb.memories.TryGetValue(pawn.thingIDNumber, out var m) ? m.Snapshot() : PawnSituation.Unknown;
        }

        internal static PawnMemory PeekMemory(Pawn pawn)
        {
            var kb = Instance;
            if (kb == null || pawn == null) return null;
            kb.memories.TryGetValue(pawn.thingIDNumber, out var m);
            return m;
        }

        internal static IPawnMemoryWriter Writer(Pawn pawn)
        {
            var kb = Instance;
            if (kb == null || pawn == null) return null;
            if (!kb.memories.TryGetValue(pawn.thingIDNumber, out var m))
            {
                m = new PawnMemory(pawn);
                kb.memories[pawn.thingIDNumber] = m;
            }
            return m;
        }

        internal static void Forget(Pawn pawn)
        {
            if (pawn == null) return;
            Instance?.memories.Remove(pawn.thingIDNumber);
            Decisions.IntentArbiter.RemovePawn(pawn);
        }

        internal static IEnumerable<PawnMemory> AllMemories =>
            (IEnumerable<PawnMemory>)Instance?.memories.Values ?? System.Array.Empty<PawnMemory>();
    }
}
