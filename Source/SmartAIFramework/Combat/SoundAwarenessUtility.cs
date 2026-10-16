using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Data;
using Verse;

namespace SmartAIFramework.Combat
{
    public enum ClamorCategory
    {
        None = 0,
        Gunfire,
        Impact,
        Construction,
        DistressCall
    }

    public static class SoundAwarenessUtility
    {
        public static ClamorCategory Classify(ClamorDef def, Thing source)
        {
            if (def == null) return ClamorCategory.None;
            if (def == ClamorDefOf.Harm)
            {
                if (source is Pawn victim &&
                    victim.Faction == Faction.OfPlayer &&
                    victim.Map?.dangerWatcher?.DangerRating == StoryDanger.High)
                {
                    return ClamorCategory.DistressCall;
                }
                return ClamorCategory.Gunfire;
            }
            if (def == ClamorDefOf.Impact) return ClamorCategory.Impact;
            if (def == ClamorDefOf.Construction) return ClamorCategory.Construction;
            return ClamorCategory.None;
        }

        public static void OnClamor(Thing source, IntVec3 position, float radius, ClamorDef def)
        {
            Map map = source?.Map;
            if (map == null) return;

            ClamorCategory category = Classify(def, source);
            if (category == ClamorCategory.None) return;

            Thing instigator = (source is Projectile proj) ? (proj.Launcher ?? source) : source;
            if (instigator == null) return;

            bool isDistress = category == ClamorCategory.DistressCall;
            if (!isDistress && instigator.Faction == Faction.OfPlayer)
                return;

            List<Pawn> colonists = map.mapPawns?.FreeColonistsSpawned;
            if (colonists == null || colonists.Count == 0) return;

            float baseRadius = radius * SmartAIMod.Settings.acousticRadiusMultiplier;
            if (baseRadius <= 0.5f) return;

            int now = PawnStateTracker.Now;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn listener = colonists[i];
                if (listener == null || listener == source || listener == instigator || listener.Dead || listener.Downed)
                    continue;
                if (listener.Drafted)
                    continue;

                if (!isDistress && !instigator.HostileTo(listener))
                    continue;

                PawnTacticalState state = PawnStateTracker.GetOrCreate(listener);
                if (now < state.nextClamorEvalTick)
                    continue;

                float hearing = listener.health?.capacities?.GetLevel(PawnCapacityDefOf.Hearing) ?? 1f;
                if (hearing <= 0.05f) continue;

                float maxDist = baseRadius * hearing;
                if (!listener.Position.InHorDistOf(position, maxDist))
                    continue;

                state.nextClamorEvalTick = now + 90;
                state.RecordThreat(position, 250, now);

                if (category == ClamorCategory.Gunfire || category == ClamorCategory.Impact || isDistress)
                {
                    CombatUtility.NotifyThreatPerceived(listener, position);
                }
                else if (category == ClamorCategory.Construction && listener.CurJob?.def?.joyKind != null)
                {
                    listener.jobs?.CheckForJobOverride();
                }
            }
        }
    }
}
