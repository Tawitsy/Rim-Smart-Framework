using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using SmartAIFramework.Data;
using Unity.Collections;
using UnityEngine;
using Verse;

namespace SmartAIFramework.Grid
{
    public sealed class ThreatPathCustomizer : PathRequest.IPathGridCustomizer, IDisposable
    {
        public const int MinRebuildGapTicks = 15;
        public const int RefreshTicks = 600;
        public const int MaxCellCost = 9999;
        private const int MaxRegionsPerEntry = 400;

        private readonly Map map;
        private readonly ThreatMap threats;
        private readonly Action forceCompleteJobs;
        private readonly Action recycleGridJobs;
        private readonly RegionEntryPredicate entryPredicate;
        private readonly RegionProcessor regionProcessor;
        private readonly HashSet<int> processedRegions = new HashSet<int>();

        private NativeArray<ushort> front;
        private NativeArray<ushort> back;
        private ushort[] zeros;
        private bool disposed;
        private int lastVersion = -1;
        private int lastCost = -1;
        private int lastRebuildTick = -99999;
        private int rebuilds;

        private IntVec3 traverseOrigin;
        private float traverseReachSq;
        private int traverseCost;

        public int Rebuilds => rebuilds;
        public bool IsCreated => front.IsCreated && back.IsCreated;

        public ThreatPathCustomizer(Map map, ThreatMap threats)
        {
            this.map = map;
            this.threats = threats;
            entryPredicate = EntersRegion;
            regionProcessor = ProcessRegion;
            if (map.pathFinder == null)
                return;
            System.Reflection.MethodInfo force = AccessTools.Method(typeof(PathFinder), "ForceCompleteScheduledJobs");
            System.Reflection.MethodInfo recycle = AccessTools.Method(typeof(PathFinder), "RecycleGridJobData");
            if (force != null)
                forceCompleteJobs = (Action)Delegate.CreateDelegate(typeof(Action), map.pathFinder, force);
            if (recycle != null)
                recycleGridJobs = (Action)Delegate.CreateDelegate(typeof(Action), map.pathFinder, recycle);
        }

        public bool CanRebuild => forceCompleteJobs != null && recycleGridJobs != null;

        private void EnsureBuffers()
        {
            if (IsCreated) return;
            int size = map.cellIndices.NumGridCells;
            front = new NativeArray<ushort>(size, Allocator.Persistent);
            back = new NativeArray<ushort>(size, Allocator.Persistent);
            zeros = new ushort[size];
        }

        public NativeArray<ushort> GetOffsetGrid()
        {
            if (disposed)
                return default(NativeArray<ushort>);
            EnsureBuffers();
            return front;
        }

        public void EnsureFresh()
        {
            if (disposed || !CanRebuild)
                return;
            EnsureBuffers();
            int now = GenTicks.TicksGame;
            int cost = Mathf.Clamp(SmartAIMod.Settings?.advPathAvoidCost ?? 0, 0, MaxCellCost);
            bool changed = lastVersion != threats.Version || lastCost != cost;
            if (!changed && now - lastRebuildTick < RefreshTicks)
                return;
            int gap = SmartAIMod.Settings != null ? Mathf.Max(MinRebuildGapTicks, SmartAIMod.Settings.advRegionCacheTicks) : MinRebuildGapTicks;
            if (now - lastRebuildTick < gap)
                return;
            Rebuild(now, cost);
        }

        private void Rebuild(int now, int cost)
        {
            long started = Stopwatch.GetTimestamp();
            forceCompleteJobs();
            recycleGridJobs();

            back.CopyFrom(zeros);
            processedRegions.Clear();
            traverseCost = cost;
            threats.PruneExpired();
            if (cost > 0)
            {
                for (int i = 0; i < threats.EntryCount; i++)
                {
                    if (!threats.TryGetLiveEntry(i, out IntVec3 origin, out float reach))
                        continue;
                    Region root = origin.GetRegion(map);
                    if (root == null)
                        continue;
                    traverseOrigin = origin;
                    traverseReachSq = reach * reach;
                    RegionTraverser.BreadthFirstTraverse(root, entryPredicate, regionProcessor, MaxRegionsPerEntry);
                }
            }

            NativeArray<ushort> swap = front;
            front = back;
            back = swap;

            lastVersion = threats.Version;
            lastCost = cost;
            lastRebuildTick = now;
            rebuilds++;

            if (SmartLog.IsEnabled)
            {
                double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                SmartLog.Message("PathCost", $"rebuilt at tick {now}: {processedRegions.Count} regions, cost {cost}, {ms:F3} ms");
            }
        }

        private bool EntersRegion(Region from, Region to)
        {
            return to.extentsClose.ClosestCellTo(traverseOrigin).DistanceToSquared(traverseOrigin) <= traverseReachSq;
        }

        private bool ProcessRegion(Region region)
        {
            if (!processedRegions.Add(region.id))
                return false;
            if (!threats.RegionInReach(region))
                return false;

            CellRect extents = region.extentsClose;
            RegionGrid grid = map.regionGrid;
            CellIndices indices = map.cellIndices;
            for (int z = extents.minZ; z <= extents.maxZ; z++)
            {
                for (int x = extents.minX; x <= extents.maxX; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (grid.GetRegionAt_NoRebuild_InvalidAllowed(cell) != region)
                        continue;
                    int index = indices.CellToIndex(cell);
                    int sum = back[index] + traverseCost;
                    back[index] = (ushort)(sum > MaxCellCost ? MaxCellCost : sum);
                }
            }
            return false;
        }

        public ushort CostAt(IntVec3 cell)
        {
            if (!IsCreated || !cell.InBounds(map))
                return 0;
            return front[map.cellIndices.CellToIndex(cell)];
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            try
            {
                forceCompleteJobs?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warning("[Rim Smart Framework] could not complete scheduled path jobs before freeing the path cost buffers: " + ex.Message);
            }
            if (front.IsCreated) front.Dispose();
            if (back.IsCreated) back.Dispose();
            zeros = null;
        }
    }
}
