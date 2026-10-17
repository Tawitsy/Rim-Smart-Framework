using System;
using System.Collections.Generic;
using SmartAIFramework.Data;
using SmartAIFramework.Evaluation;
using SmartAIFramework.Grid;
using Verse;

namespace SmartAIFramework.API
{
    public class AIThreatExtension : DefModExtension
    {
        public float hazardRadius = 0f;

        public float dangerScore = 50f;

        public bool avoidWhenDrafted = true;
    }

    public static class SmartAIApi
    {
        public delegate float ScoreOverride(float utility, ActionEvalInput input);

        private static readonly List<ScoreOverride> overrides = new List<ScoreOverride>();

        public static void RegisterScoreOverride(ScoreOverride fn)
        {
            if (fn != null && !overrides.Contains(fn))
                overrides.Add(fn);
        }

        public static void UnregisterScoreOverride(ScoreOverride fn)
        {
            overrides.Remove(fn);
        }

        internal static float ApplyScoreOverrides(float utility, ActionEvalInput input)
        {
            if (overrides.Count == 0)
                return utility;

            for (int i = 0; i < overrides.Count; i++)
            {
                try
                {
                    utility = overrides[i](utility, input);
                }
                catch (Exception e)
                {
                    Log.ErrorOnce($"[RimSmartFramework] Score override error: {e}", overrides[i].GetHashCode());
                }
            }
            return utility;
        }

        public static void RegisterHazard(Thing source, float radius)
        {
            if (source?.Map == null) return;
            TacticalGrid.Get(source.Map)?.RegisterHazard(source, source.Position, radius);
        }

        public static void ClearHazard(Thing source)
        {
            if (source?.Map == null) return;
            TacticalGrid.Get(source.Map)?.ClearActor(source);
        }

        public static float GetDanger(Map map, IntVec3 cell, Pawn observer = null)
        {
            return UtilityEvaluator.GetDangerAt(map, cell, observer);
        }

        public static VerbProfile GetVerbProfile(Verb verb)
        {
            return VerbProfileCache.Get(verb);
        }

        public static ThingProfile GetThingProfile(ThingDef def)
        {
            return ThingProfileCache.Get(def);
        }

        public static int TicksUntilBleedOut(Pawn pawn)
        {
            return SurvivalUtility.TicksUntilBleedOut(pawn);
        }

        public static void SuppressTarget(Thing target, int durationTicks)
        {
            PawnStateTracker.SuppressTarget(target, durationTicks);
        }
    }
}
