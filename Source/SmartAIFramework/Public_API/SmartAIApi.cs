using System;
using System.Collections.Generic;
using SmartAIFramework.Brain;
using SmartAIFramework.Core_Engine;
using Verse;

namespace SmartAIFramework.Public_API
{
    public class AIThreatExtension : DefModExtension
    {
        public float hazardRadius = 0f;
        public float dangerScore = 50f;
        public bool avoidWhenDrafted = true;
    }

    public static class SmartAIApi
    {
        public delegate float ScoreOverride(float utility, UtilityInput input);

        private static readonly List<ScoreOverride> overrides = new List<ScoreOverride>();

        public static void RegisterScoreOverride(ScoreOverride fn)
        {
            if (fn != null && !overrides.Contains(fn)) overrides.Add(fn);
        }

        public static void UnregisterScoreOverride(ScoreOverride fn) => overrides.Remove(fn);

        internal static float ApplyScoreOverrides(float utility, UtilityInput input)
        {
            for (int i = 0; i < overrides.Count; i++)
            {
                try { utility = overrides[i](utility, input); }
                catch (Exception e) { Log.ErrorOnce($"[SmartAI] Score override threw: {e}", overrides[i].GetHashCode()); }
            }
            return utility;
        }

        public static void RegisterHazard(Thing source, float radius)
        {
            if (source?.Map == null) return;
            TacticalGridMapComponent.For(source.Map)?.RegisterHazard(source, source.Position, radius);
        }

        public static void ClearHazard(Thing source)
        {
            if (source?.Map == null) return;
            TacticalGridMapComponent.For(source.Map)?.ClearActor(source);
        }

        public static float GetDanger(Map map, IntVec3 cell, Pawn observer = null) =>
            CognitiveBrain.DangerAt(map, cell, observer);

        public static ActionProfile AnalyzeVerb(Verb verb) => ModActionAnalyzer.Analyze(verb);

        public static ThingAffordanceProfile AnalyzeAffordance(ThingDef def) =>
            SemanticAffordanceEngine.Analyze(def);

        public static int PredictTicksUntilDowned(Pawn pawn) =>
            DeterministicPredictor.PredictTicksUntilDowned(pawn);

        public static void SuppressTarget(Thing target, int durationTicks) =>
            Core.Knowledge.CognitiveBlacklist.Suppress(target, durationTicks);

        public static Core.Knowledge.PawnSituation GetSituation(Pawn pawn) =>
            Core.Knowledge.KnowledgeBase.Situation(pawn);
    }
}
