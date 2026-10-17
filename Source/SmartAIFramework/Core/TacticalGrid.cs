using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Grid
{
    public sealed class TacticalGrid : MapComponent
    {
        private static TacticalGrid[] byMapIndex = new TacticalGrid[32];

        private short[] friendlyLanes;
        private short[] hostileLanes;
        private short[] threatGrid;
        private short[] hazardGrid;

        private int activeLaneRegistrations;
        private int activeHazardRegistrations;

        private readonly Dictionary<int, ActorFootprint> footprints = new Dictionary<int, ActorFootprint>(64);
        private readonly Stack<ActorFootprint> footprintPool = new Stack<ActorFootprint>(32);

        private sealed class ActorFootprint
        {
            public readonly List<int> laneCells = new List<int>(32);
            public readonly List<int> threatCells = new List<int>(16);
            public readonly List<int> hazardCells = new List<int>(32);
            public bool isHostile;
            public short hazardWeight = 1;

            public void Reset()
            {
                laneCells.Clear();
                threatCells.Clear();
                hazardCells.Clear();
                isHostile = false;
                hazardWeight = 1;
            }
        }

        public bool HasActiveLanes => activeLaneRegistrations > 0;
        public bool HasActiveHazards => activeHazardRegistrations > 0;

        public TacticalGrid(Map map) : base(map)
        {
            EnsureArrays();
            RegisterMapLookup(map, this);
        }

        private static void RegisterMapLookup(Map map, TacticalGrid grid)
        {
            if (map == null) return;
            int idx = map.Index;
            if (idx < 0) return;
            if (idx >= byMapIndex.Length)
            {
                Array.Resize(ref byMapIndex, Math.Max(idx + 8, byMapIndex.Length * 2));
            }
            byMapIndex[idx] = grid;
        }

        public static TacticalGrid Get(Map map)
        {
            if (map == null) return null;
            int idx = map.Index;
            if ((uint)idx < (uint)byMapIndex.Length)
            {
                TacticalGrid cached = byMapIndex[idx];
                if (cached != null && cached.map == map)
                    return cached;
            }

            TacticalGrid comp = map.GetComponent<TacticalGrid>();
            if (comp == null)
            {
                comp = new TacticalGrid(map);
                map.components.Add(comp);
            }
            else
            {
                RegisterMapLookup(map, comp);
            }
            return comp;
        }

        private void EnsureArrays()
        {
            if (friendlyLanes != null || map?.cellIndices == null) return;
            int n = map.cellIndices.NumGridCells;
            friendlyLanes = new short[n];
            hostileLanes = new short[n];
            threatGrid = new short[n];
            hazardGrid = new short[n];
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureArrays();
            RegisterMapLookup(map, this);
        }

        public int FriendlyFiringLaneAt(IntVec3 c)
        {
            if (activeLaneRegistrations <= 0 || friendlyLanes == null || !c.InBounds(map)) return 0;
            return friendlyLanes[map.cellIndices.CellToIndex(c)];
        }

        public int HostileFiringLaneAt(IntVec3 c)
        {
            if (activeLaneRegistrations <= 0 || hostileLanes == null || !c.InBounds(map)) return 0;
            return hostileLanes[map.cellIndices.CellToIndex(c)];
        }

        public int FiringLaneAt(IntVec3 c)
        {
            if (activeLaneRegistrations <= 0 || friendlyLanes == null || !c.InBounds(map)) return 0;
            int idx = map.cellIndices.CellToIndex(c);
            return friendlyLanes[idx] + hostileLanes[idx];
        }

        public int ThreatAt(IntVec3 c)
        {
            if (activeLaneRegistrations <= 0 || threatGrid == null || !c.InBounds(map)) return 0;
            return threatGrid[map.cellIndices.CellToIndex(c)];
        }

        public int HazardAt(IntVec3 c)
        {
            if (activeHazardRegistrations <= 0 || hazardGrid == null || !c.InBounds(map)) return 0;
            return hazardGrid[map.cellIndices.CellToIndex(c)];
        }

        public float DangerAt(IntVec3 c)
        {
            if ((activeLaneRegistrations | activeHazardRegistrations) == 0) return 0f;
            if (friendlyLanes == null || !c.InBounds(map)) return 0f;

            int idx = map.cellIndices.CellToIndex(c);
            float d = threatGrid[idx] * 25f
                    + hazardGrid[idx] * 30f
                    + hostileLanes[idx] * 20f
                    + friendlyLanes[idx] * 5f;
            return d > 100f ? 100f : d;
        }

        public void RegisterFiringLane(Thing actor, IntVec3 from, IntVec3 to, float aoeRadius, bool hostileToPlayer)
        {
            if (actor == null) return;
            EnsureArrays();
            ClearActor(actor);

            ActorFootprint fp = RentFootprint();
            fp.isHostile = hostileToPlayer;
            footprints[actor.thingIDNumber] = fp;

            short[] laneTarget = hostileToPlayer ? hostileLanes : friendlyLanes;
            LineOfSightUtility.WriteLineCells(map, from, to, laneTarget, fp.laneCells);
            activeLaneRegistrations++;

            if (hostileToPlayer && aoeRadius > 0.5f)
            {
                CellIndices indices = map.cellIndices;
                int count = GenRadial.NumCellsInRadius(aoeRadius);
                IntVec3[] pattern = GenRadial.RadialPattern;
                for (int i = 0; i < count; i++)
                {
                    IntVec3 cell = to + pattern[i];
                    if (!cell.InBounds(map)) continue;
                    int idx = indices.CellToIndex(cell);
                    threatGrid[idx]++;
                    fp.threatCells.Add(idx);
                }
            }
        }

        // weight: how many times each covered cell counts; the footprint remembers it so ClearActor removes exactly what was added.
        public void RegisterHazard(Thing source, IntVec3 center, float radius, short weight = 1)
        {
            if (source == null) return;
            if (weight < 1) weight = 1;
            EnsureArrays();
            ClearActor(source);

            ActorFootprint fp = RentFootprint();
            fp.hazardWeight = weight;
            footprints[source.thingIDNumber] = fp;

            CellIndices indices = map.cellIndices;
            int count = GenRadial.NumCellsInRadius(radius);
            IntVec3[] pattern = GenRadial.RadialPattern;
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = center + pattern[i];
                if (!cell.InBounds(map)) continue;
                int idx = indices.CellToIndex(cell);
                hazardGrid[idx] += weight;
                fp.hazardCells.Add(idx);
            }
            activeHazardRegistrations++;
        }

        public void ClearActor(Thing actor)
        {
            if (actor == null || footprints.Count == 0) return;
            ClearActorById(actor.thingIDNumber);
        }

        public void ClearActorById(int thingId)
        {
            if (footprints.Count == 0 || !footprints.TryGetValue(thingId, out ActorFootprint fp))
                return;

            if (fp.laneCells.Count > 0)
            {
                short[] laneTarget = fp.isHostile ? hostileLanes : friendlyLanes;
                DecrementCells(laneTarget, fp.laneCells);
                if (activeLaneRegistrations > 0) activeLaneRegistrations--;
            }

            if (fp.threatCells.Count > 0)
            {
                DecrementCells(threatGrid, fp.threatCells);
            }

            if (fp.hazardCells.Count > 0)
            {
                DecrementCells(hazardGrid, fp.hazardCells, fp.hazardWeight);
                if (activeHazardRegistrations > 0) activeHazardRegistrations--;
            }

            footprints.Remove(thingId);
            fp.Reset();
            if (footprintPool.Count < 64)
            {
                footprintPool.Push(fp);
            }
        }

        private ActorFootprint RentFootprint()
        {
            return footprintPool.Count > 0 ? footprintPool.Pop() : new ActorFootprint();
        }

        private static void DecrementCells(short[] grid, List<int> indices, short amount = 1)
        {
            if (grid == null) return;
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                int v = grid[idx] - amount;
                grid[idx] = (short)(v > 0 ? v : 0);
            }
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            footprints.Clear();
            activeLaneRegistrations = 0;
            activeHazardRegistrations = 0;
            Data.ColonyAlert.Clear(map);
            int idx = map?.Index ?? -1;
            if ((uint)idx < (uint)byMapIndex.Length && byMapIndex[idx] == this)
            {
                byMapIndex[idx] = null;
            }
        }
    }
}
