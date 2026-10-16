using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Core.Knowledge;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Core_Engine
{
    public class TacticalGridMapComponent : MapComponent, IReadOnlyTacticalGrid, ITacticalGridWriter
    {
        private short[] friendlyFiringLaneGrid;
        private short[] hostileFiringLaneGrid;
        private short[] threatGrid;
        private short[] hazardGrid;

        private readonly Dictionary<Thing, ActorFootprint> footprints = new Dictionary<Thing, ActorFootprint>();

        private sealed class ActorFootprint
        {
            public readonly List<int> laneCells = new List<int>();
            public readonly List<int> threatCells = new List<int>();
            public readonly List<int> hazardCells = new List<int>();
            public bool isHostile;
            public Faction faction;
        }

        public TacticalGridMapComponent(Map map) : base(map) { }

        public static TacticalGridMapComponent For(Map map)
        {
            if (map == null) return null;
            var comp = map.GetComponent<TacticalGridMapComponent>();
            if (comp == null)
            {
                comp = new TacticalGridMapComponent(map);
                comp.FinalizeInit();
                map.components.Add(comp);
            }
            return comp;
        }

        private void EnsureInit()
        {
            if (friendlyFiringLaneGrid != null) return;
            int n = map.cellIndices.NumGridCells;
            friendlyFiringLaneGrid = new short[n];
            hostileFiringLaneGrid = new short[n];
            threatGrid = new short[n];
            hazardGrid = new short[n];
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureInit();
            int colonistCount = map.mapPawns?.FreeColonists?.Count ?? 0;
            SmartAIFramework.Core.SmartAILog.Log("SmartAI-Map", $"Map {map.Index} TacticalGridMapComponent initialized. Total cells: {map.cellIndices.NumGridCells}, Colonists: {colonistCount}");

            if (map.mapPawns != null)
            {
                foreach (var colonist in map.mapPawns.FreeColonists)
                {
                    string curJob = colonist.CurJob?.def?.defName ?? "Idle";
                    SmartAIFramework.Core.SmartAILog.Log("Colonist-State", $"{colonist.LabelShort} at {colonist.Position} | Job: {curJob}");
                }
            }
        }


        public int FriendlyFiringLaneAt(IntVec3 c) => Read(friendlyFiringLaneGrid, c);
        public int HostileFiringLaneAt(IntVec3 c) => Read(hostileFiringLaneGrid, c);
        public int FiringLaneAt(IntVec3 c) => FriendlyFiringLaneAt(c) + HostileFiringLaneAt(c);
        public int ThreatAt(IntVec3 c) => Read(threatGrid, c);
        public int HazardAt(IntVec3 c) => Read(hazardGrid, c);

        public float DangerAt(IntVec3 c)
        {
            float d = ThreatAt(c) * 25f + HazardAt(c) * 30f + HostileFiringLaneAt(c) * 20f + FriendlyFiringLaneAt(c) * 5f;
            return Mathf.Min(d, 100f);
        }

        private int Read(short[] grid, IntVec3 c)
        {
            var g = grid;
            if (g == null || !c.InBounds(map)) return 0;
            return g[map.cellIndices.CellToIndex(c)];
        }

        public void RegisterFiringLane(Thing actor, IntVec3 from, IntVec3 to, float aoeRadius, bool hostileToPlayer)
        {
            EnsureInit();
            ClearActor(actor);

            var fp = new ActorFootprint { faction = actor.Faction, isHostile = hostileToPlayer };
            footprints[actor] = fp;
            short[] targetLaneGrid = hostileToPlayer ? hostileFiringLaneGrid : friendlyFiringLaneGrid;

            GenSight.PointsOnLineOfSight(from, to, cell =>
            {
                if (!cell.InBounds(map)) return;
                int idx = map.cellIndices.CellToIndex(cell);
                targetLaneGrid[idx]++;
                fp.laneCells.Add(idx);
            });

            if (hostileToPlayer && aoeRadius > 0f)
            {
                foreach (var cell in GenRadial.RadialCellsAround(to, aoeRadius, true))
                {
                    if (!cell.InBounds(map)) continue;
                    int idx = map.cellIndices.CellToIndex(cell);
                    threatGrid[idx]++;
                    fp.threatCells.Add(idx);
                }
            }
        }

        public void RegisterHazard(Thing source, IntVec3 center, float radius)
        {
            if (source == null) return;
            EnsureInit();
            ClearActor(source);

            var fp = new ActorFootprint { faction = source.Faction };
            footprints[source] = fp;

            foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map)) continue;
                int idx = map.cellIndices.CellToIndex(cell);
                hazardGrid[idx]++;
                fp.hazardCells.Add(idx);
            }
        }

        public void ClearActor(Thing actor)
        {
            if (actor == null || friendlyFiringLaneGrid == null) return;
            if (!footprints.TryGetValue(actor, out var fp)) return;

            short[] targetLaneGrid = fp.isHostile ? hostileFiringLaneGrid : friendlyFiringLaneGrid;
            Decrement(targetLaneGrid, fp.laneCells);
            Decrement(threatGrid, fp.threatCells);
            Decrement(hazardGrid, fp.hazardCells);
            footprints.Remove(actor);
        }

        private static void Decrement(short[] grid, List<int> cells)
        {
            if (grid == null) return;
            for (int i = 0; i < cells.Count; i++)
            {
                int idx = cells[i];
                if (grid[idx] > 0) grid[idx]--;
            }
        }

        public override void ExposeData() => base.ExposeData();

        public override void MapRemoved()
        {
            base.MapRemoved();
            footprints.Clear();
        }
    }
}
