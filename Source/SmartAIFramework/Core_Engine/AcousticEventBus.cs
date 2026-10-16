using System;
using RimWorld;
using Verse;

namespace SmartAIFramework.Core_Engine
{
    public enum AcousticKind
    {
        Unknown,
        Movement,
        Gunfire,
        Impact,
        Construction,
        CryForHelp
    }

    public struct AcousticEvent
    {
        public Thing source;
        public Thing instigator;
        public IntVec3 position;
        public float radius;
        public AcousticKind kind;

        public bool IsHostileTo(Pawn listener)
        {
            if (listener == null) return false;
            Thing actor = instigator ?? source;
            if (actor != null && actor.HostileTo(listener))
                return true;

            if (kind == AcousticKind.CryForHelp)
                return true;

            return false;
        }
    }

    public static class AcousticEventBus
    {
        public static event Action<Pawn, AcousticEvent> OnPawnHeard;

        public static AcousticKind Classify(ClamorDef def, Thing source, Thing instigator)
        {
            if (def == null) return AcousticKind.Unknown;
            if (def == ClamorDefOf.Harm)
            {
                if (source is Pawn victim && victim.Faction == Faction.OfPlayer &&
                    victim.Map?.dangerWatcher?.DangerRating == StoryDanger.High)
                {
                    return AcousticKind.CryForHelp;
                }
                return AcousticKind.Gunfire;
            }
            if (def == ClamorDefOf.Impact) return AcousticKind.Impact;
            if (def == ClamorDefOf.Construction) return AcousticKind.Construction;
            if (def == ClamorDefOf.Movement) return AcousticKind.Movement;
            return AcousticKind.Unknown;
        }

        public static void Broadcast(Thing source, IntVec3 position, float radius, ClamorDef def)
        {
            Map map = source?.Map;
            if (map == null) return;

            Thing instigator = (source is Projectile proj) ? (proj.Launcher ?? source) : source;
            var kind = Classify(def, source, instigator);
            if (kind == AcousticKind.Movement || kind == AcousticKind.Unknown) return;

            float baseRadius = radius * SmartAIMod.Settings.acousticRadiusMultiplier;
            var evt = new AcousticEvent
            {
                source = source,
                instigator = instigator,
                position = position,
                radius = baseRadius,
                kind = kind
            };

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn listener = pawns[i];
                if (listener == source || listener == instigator || listener.Dead || listener.Downed) continue;
                if (listener.health?.capacities == null) continue;

                float hearing = listener.health.capacities.GetLevel(PawnCapacityDefOf.Hearing);
                float effective = baseRadius * hearing;
                if (effective <= 0f) continue;
                if (!listener.Position.InHorDistOf(position, effective)) continue;

                OnPawnHeard?.Invoke(listener, evt);
                Brain.CognitiveBrain.NotifyHeard(listener, evt);
            }
        }
    }
}
