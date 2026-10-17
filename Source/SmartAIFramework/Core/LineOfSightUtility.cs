using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SmartAIFramework.Grid
{
    public static class LineOfSightUtility
    {
        public static bool HasFriendlyInLineOfFire(Map map, Pawn shooter, IntVec3 from, IntVec3 to, float blastRadius)
        {
            if (map == null || shooter == null) return false;

            if (HasFriendlyAlongSegment(map, shooter, from, to))
                return true;

            if (blastRadius > 0.5f)
            {
                if (from.InHorDistOf(to, blastRadius + 0.5f))
                    return true;

                if (HasFriendlyOrVolatileInRadius(map, shooter, to, blastRadius))
                    return true;
            }

            return false;
        }

        public static bool IsProtectedBystander(Pawn p, Pawn shooter, bool includeDowned = false)
        {
            if ((p.Downed && !includeDowned) || p.HostileTo(shooter))
                return false;
            return p.Faction != null || p.RaceProps.Humanlike;
        }

        public static bool HasFriendlyAlongSegment(Map map, Pawn shooter, IntVec3 from, IntVec3 to)
        {
            int x0 = from.x;
            int z0 = from.z;
            int x1 = to.x;
            int z1 = to.z;

            int dx = Math.Abs(x1 - x0);
            int dz = Math.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;

            CellIndices indices = map.cellIndices;
            ThingGrid thingGrid = map.thingGrid;
            int mapSizeX = map.Size.x;
            int mapSizeZ = map.Size.z;

            while (true)
            {
                if ((x0 != from.x || z0 != from.z) && (x0 != x1 || z0 != z1))
                {
                    if ((uint)x0 < (uint)mapSizeX && (uint)z0 < (uint)mapSizeZ)
                    {
                        List<Thing> list = thingGrid.ThingsListAtFast(indices.CellToIndex(x0, z0));
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] is Pawn p && p != shooter && IsProtectedBystander(p, shooter))
                                return true;
                        }
                    }
                }

                if (x0 == x1 && z0 == z1) break;

                int e2 = err * 2;
                if (e2 > -dz)
                {
                    err -= dz;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    z0 += sz;
                }
            }

            return false;
        }

        public static bool HasFriendlyOrVolatileInRadius(Map map, Pawn shooter, IntVec3 center, float radius)
        {
            CellIndices indices = map.cellIndices;
            ThingGrid thingGrid = map.thingGrid;
            Area_Home home = SmartAIMod.Settings.combatExplosiveSafety ? map.areaManager?.Home : null;

            int count = GenRadial.NumCellsInRadius(radius);
            IntVec3[] pattern = GenRadial.RadialPattern;

            for (int i = 0; i < count; i++)
            {
                IntVec3 c = center + pattern[i];
                if (!c.InBounds(map)) continue;

                int cellIdx = indices.CellToIndex(c);
                List<Thing> things = thingGrid.ThingsListAtFast(cellIdx);
                bool inHome = home != null && home[cellIdx];

                for (int j = 0; j < things.Count; j++)
                {
                    Thing t = things[j];
                    if (t is Pawn p && p != shooter && IsProtectedBystander(p, shooter, includeDowned: true))
                        return true;

                    if (inHome)
                    {
                        var profile = Data.ThingProfileCache.Get(t.def);
                        if (profile != null && profile.isExplosive)
                            return true;
                    }
                }
            }

            return false;
        }

        public static void WriteLineCells(Map map, IntVec3 from, IntVec3 to, short[] grid, List<int> recordedIndices)
        {
            int x0 = from.x;
            int z0 = from.z;
            int x1 = to.x;
            int z1 = to.z;

            int dx = Math.Abs(x1 - x0);
            int dz = Math.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;

            CellIndices indices = map.cellIndices;
            int mapSizeX = map.Size.x;
            int mapSizeZ = map.Size.z;

            while (true)
            {
                if ((uint)x0 < (uint)mapSizeX && (uint)z0 < (uint)mapSizeZ)
                {
                    int idx = indices.CellToIndex(x0, z0);
                    grid[idx]++;
                    recordedIndices.Add(idx);
                }

                if (x0 == x1 && z0 == z1) break;

                int e2 = err * 2;
                if (e2 > -dz)
                {
                    err -= dz;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    z0 += sz;
                }
            }
        }
    }
}
