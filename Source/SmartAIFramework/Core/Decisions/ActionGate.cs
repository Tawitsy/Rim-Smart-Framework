using RimWorld;
using Verse;

namespace SmartAIFramework.Core.Decisions
{
    public static class ActionGate
    {
        public static bool ObserveOnly => SmartAIMod.Settings.observeOnly;

        public static bool MayActAutonomously(Pawn pawn)
        {
            if (ObserveOnly || pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed) return false;
            if (pawn.Drafted) return false;
            if (pawn.Faction != Faction.OfPlayer) return false;
            if (pawn.InMentalState) return false;
            return true;
        }

        public static bool MayAssist(Pawn pawn)
        {
            if (ObserveOnly || pawn == null || !pawn.Spawned) return false;
            return pawn.Faction == Faction.OfPlayer;
        }

        public static bool ApplyVeto(Pawn pawn, bool assistLevel, string what)
        {
            bool allowed = assistLevel ? MayAssist(pawn) : MayActAutonomously(pawn);
            DecisionLog.Record(pawn, what, allowed);
            return allowed;
        }
    }
}
