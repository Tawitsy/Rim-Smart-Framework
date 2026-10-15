using RimWorld;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Modules
{
    public static class Module_Structure
    {
        public static bool IsSanctuary(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return false;
            if (!map.areaManager.Home[cell]) return false;
            Room room = cell.GetRoom(map);
            return room != null && !room.PsychologicallyOutdoors && !room.UsesOutdoorTemperature;
        }

        public static bool IsBadDropCell(Map map, IntVec3 cell)
        {
            if (!SmartAIMod.Settings.structureDoorDiscipline) return false;
            return cell.GetDoor(map) != null;
        }

        public static bool TryFindNearestSanctuary(Pawn pawn, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (pawn?.Map == null) return false;

            var safety = DynamicSanctuaryFinder.FindBestSafety(pawn, pawn.Position, 50f);
            if (safety.cell.IsValid && safety.cell != pawn.Position)
            {
                result = safety.cell;
                return true;
            }
            return false;
        }
    }
}
