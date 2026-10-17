using RimWorld;
using SmartAIFramework.Combat;
using Verse;

namespace SmartAIFramework.Work
{
    public static class StructureUtility
    {
        public static bool IsEnclosedHomeRoom(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return false;
            if (!map.areaManager.Home[cell]) return false;
            Room room = cell.GetRoom(map);
            return room != null && !room.PsychologicallyOutdoors && !room.UsesOutdoorTemperature;
        }

        public static bool TryFindNearestSafeRoom(Pawn pawn, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (pawn?.Map == null) return false;

            RetreatSpot spot = SafeCellFinder.FindRetreatSpot(pawn, pawn.Position, 45f);
            if (spot.cell.IsValid && spot.cell != pawn.Position)
            {
                result = spot.cell;
                return true;
            }
            return false;
        }
    }
}
