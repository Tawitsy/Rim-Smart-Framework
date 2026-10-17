using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Compat;
using SmartAIFramework.Data;
using Verse;

namespace SmartAIFramework.Grid
{
    public sealed class ThreatMap : MapComponent
    {
        public const int MaxEntries = 128;
        private const int MovedCellsSq = 9;
        private const int MaxCachedRegions = 2048;

        public static int ExpiryTicks => SmartAIMod.Settings?.advThreatExpiryTicks ?? SmartAISettings.DefaultThreatExpiryTicks;
        public static float MinReach => SmartAIMod.Settings?.advThreatMinReach ?? SmartAISettings.DefaultThreatMinReach;
        public static float MaxReach => SmartAIMod.Settings?.advThreatMaxReach ?? SmartAISettings.DefaultThreatMaxReach;
        private static int CacheTicks => SmartAIMod.Settings?.advRegionCacheTicks ?? SmartAISettings.DefaultRegionCacheTicks;

        private static ThreatMap[] byMapIndex = new ThreatMap[32];

        private struct Entry
        {
            public int thingId;
            public Pawn actor;
            public IntVec3 origin;
            public float reach;
            public int expiresTick;
        }

        private readonly Entry[] entries = new Entry[MaxEntries];
        private int count;
        private int nextExpiry = int.MaxValue;
        private int version;

        private readonly Dictionary<int, bool> regionCache = new Dictionary<int, bool>(MaxCachedRegions + 8);
        private int regionCacheVersion = -1;
        private int cacheBuiltTick;

        public bool AnyActive => count > 0;
        public int Count => count;
        public int Version => version;

        public ThreatMap(Map map) : base(map)
        {
            RegisterMapLookup(map, this);
        }

        private static void RegisterMapLookup(Map map, ThreatMap threats)
        {
            if (map == null) return;
            int idx = map.Index;
            if (idx < 0) return;
            if (idx >= byMapIndex.Length)
            {
                System.Array.Resize(ref byMapIndex, System.Math.Max(idx + 8, byMapIndex.Length * 2));
            }
            byMapIndex[idx] = threats;
        }

        public static ThreatMap Get(Map map)
        {
            if (map == null) return null;
            int idx = map.Index;
            if ((uint)idx < (uint)byMapIndex.Length)
            {
                ThreatMap cached = byMapIndex[idx];
                if (cached != null && cached.map == map)
                    return cached;
            }

            ThreatMap comp = map.GetComponent<ThreatMap>();
            if (comp == null)
            {
                comp = new ThreatMap(map);
                map.components.Add(comp);
            }
            else
            {
                RegisterMapLookup(map, comp);
            }
            return comp;
        }

        public static ThreatMap TryGet(Map map)
        {
            if (map == null) return null;
            int idx = map.Index;
            if ((uint)idx < (uint)byMapIndex.Length)
            {
                ThreatMap cached = byMapIndex[idx];
                if (cached != null && cached.map == map)
                    return cached;
            }
            return null;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RegisterMapLookup(map, this);
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            count = 0;
            version++;
            regionCache.Clear();
            int idx = map?.Index ?? -1;
            if ((uint)idx < (uint)byMapIndex.Length && byMapIndex[idx] == this)
                byMapIndex[idx] = null;
        }

        public static bool IsEligible(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Drafted || pawn.Faction != Faction.OfPlayer || PawnStateTracker.ObserveOnly)
                return false;
            Pawn_PlayerSettings settings = pawn.playerSettings;
            if (settings == null || !settings.UsesConfigurableHostilityResponse || settings.hostilityResponse != HostilityResponseMode.Flee)
                return false;
            if (pawn.InMentalState || PerspectiveShiftCompat.IsDirectlyControlled(pawn))
                return false;
            return !IsExemptByNeeds(pawn);
        }

        public static bool IsExemptByNeeds(Pawn pawn)
        {
            Pawn_NeedsTracker needs = pawn?.needs;
            if (needs == null) return false;
            if (needs.food != null && needs.food.CurCategory >= HungerCategory.UrgentlyHungry)
                return true;
            if (needs.rest != null && needs.rest.CurCategory == RestCategory.Exhausted)
                return true;
            return CompatRegistry.DubsBadHygieneActive && IsDesperateForHygieneNeed(needs);
        }

        private static bool hygieneNeedsResolved;
        private static NeedDef bladderNeed;
        private static NeedDef thirstNeed;

        private static bool IsDesperateForHygieneNeed(Pawn_NeedsTracker needs)
        {
            if (!hygieneNeedsResolved)
            {
                hygieneNeedsResolved = true;
                bladderNeed = DefDatabase<NeedDef>.GetNamedSilentFail("Bladder");
                thirstNeed = DefDatabase<NeedDef>.GetNamedSilentFail("DBHThirst");
            }
            return IsAtOrBelow(needs, bladderNeed) || IsAtOrBelow(needs, thirstNeed);
        }

        private static bool IsAtOrBelow(Pawn_NeedsTracker needs, NeedDef def)
        {
            return def != null && needs.TryGetNeed(def, out Need need) && need.CurLevelPercentage <= 0.1f;
        }

        public void Register(Pawn actor, Verb verb, int now)
        {
            Register(actor, verb != null ? verb.EffectiveRange : 0f, now);
        }

        public void Register(Pawn actor, float rawReach, int now)
        {
            if (actor == null) return;
            float reach = rawReach < MinReach ? MinReach : (rawReach > MaxReach ? MaxReach : rawReach);
            int id = actor.thingIDNumber;
            IntVec3 pos = actor.Position;

            for (int i = 0; i < count; i++)
            {
                if (entries[i].thingId != id) continue;

                if (reach != entries[i].reach || pos.DistanceToSquared(entries[i].origin) >= MovedCellsSq)
                    version++;
                entries[i].origin = pos;
                entries[i].reach = reach;
                entries[i].actor = actor;
                entries[i].expiresTick = now + ExpiryTicks;
                return;
            }

            int slot = count;
            if (count == MaxEntries)
            {
                slot = 0;
                for (int i = 1; i < count; i++)
                {
                    if (entries[i].expiresTick < entries[slot].expiresTick)
                        slot = i;
                }
            }
            else
            {
                count++;
            }

            entries[slot].thingId = id;
            entries[slot].actor = actor;
            entries[slot].origin = pos;
            entries[slot].reach = reach;
            entries[slot].expiresTick = now + ExpiryTicks;
            if (entries[slot].expiresTick < nextExpiry)
                nextExpiry = entries[slot].expiresTick;
            version++;
        }

        public void Remove(int thingId)
        {
            for (int i = 0; i < count; i++)
            {
                if (entries[i].thingId != thingId) continue;
                RemoveAt(i);
                return;
            }
        }

        public void Clear()
        {
            if (count == 0) return;
            for (int i = 0; i < count; i++)
                entries[i] = default(Entry);
            count = 0;
            nextExpiry = int.MaxValue;
            version++;
        }

        public bool Contains(int thingId)
        {
            for (int i = 0; i < count; i++)
            {
                if (entries[i].thingId == thingId) return true;
            }
            return false;
        }

        private void RemoveAt(int i)
        {
            count--;
            entries[i] = entries[count];
            entries[count] = default(Entry);
            version++;
        }

        private void PruneIfNeeded(int now)
        {
            if (count == 0 || now < nextExpiry) return;

            int soonest = int.MaxValue;
            for (int i = count - 1; i >= 0; i--)
            {
                if (entries[i].expiresTick <= now)
                {
                    RemoveAt(i);
                    continue;
                }
                if (entries[i].expiresTick < soonest)
                    soonest = entries[i].expiresTick;
            }
            nextExpiry = soonest;
        }

        private bool IsLive(Pawn actor, Pawn forPawn)
        {
            if (actor == null || !actor.Spawned || actor.Dead || actor.Downed || actor.Map != map)
                return false;
            if (forPawn != null ? !actor.HostileTo(forPawn) : !actor.HostileTo(Faction.OfPlayer))
                return false;
            return !actor.ThreatDisabled(forPawn);
        }

        public bool IsInReach(IntVec3 cell, Pawn forPawn = null)
        {
            if (count == 0) return false;
            PruneIfNeeded(GenTicks.TicksGame);

            for (int i = 0; i < count; i++)
            {
                IntVec3 origin = entries[i].origin;
                float reach = entries[i].reach;
                if (cell.DistanceToSquared(origin) > reach * reach)
                    continue;
                if (!IsLive(entries[i].actor, forPawn))
                    continue;
                if (GenSight.LineOfSight(origin, cell, map, skipFirstCell: true))
                    return true;
            }
            return false;
        }

        public bool RegionInReach(Region region)
        {
            if (count == 0 || region == null) return false;
            int now = GenTicks.TicksGame;
            PruneIfNeeded(now);
            if (count == 0) return false;

            if (regionCacheVersion != version || regionCache.Count > MaxCachedRegions || now - cacheBuiltTick >= CacheTicks)
            {
                regionCache.Clear();
                regionCacheVersion = version;
                cacheBuiltTick = now;
            }
            else if (regionCache.TryGetValue(region.id, out bool cachedResult))
            {
                return cachedResult;
            }

            bool result = false;
            for (int i = 0; i < count && !result; i++)
            {
                IntVec3 origin = entries[i].origin;
                float reach = entries[i].reach;
                if (!IsLive(entries[i].actor, null))
                    continue;

                CellRect r = region.extentsClose;
                float reachSq = reach * reach;
                result = SampleSees(r.ClosestCellTo(origin), region, origin, reachSq)
                    || SampleSees(r.CenterCell, region, origin, reachSq)
                    || SampleSees(new IntVec3(r.minX, 0, r.minZ), region, origin, reachSq)
                    || SampleSees(new IntVec3(r.maxX, 0, r.minZ), region, origin, reachSq)
                    || SampleSees(new IntVec3(r.minX, 0, r.maxZ), region, origin, reachSq)
                    || SampleSees(new IntVec3(r.maxX, 0, r.maxZ), region, origin, reachSq);
            }
            regionCache[region.id] = result;
            return result;
        }

        private bool SampleSees(IntVec3 cell, Region region, IntVec3 origin, float reachSq)
        {
            if (!cell.InBounds(map) || cell.DistanceToSquared(origin) > reachSq)
                return false;
            if (map.regionGrid.GetValidRegionAt_NoRebuild(cell) != region)
                return false;
            return GenSight.LineOfSight(origin, cell, map, skipFirstCell: true);
        }

        private static readonly UnityEngine.Color OverlayLive = new UnityEngine.Color(1f, 0.25f, 0.2f);
        private static readonly UnityEngine.Color OverlayStale = new UnityEngine.Color(0.6f, 0.6f, 0.6f);

        public override void MapComponentUpdate()
        {
            if (count == 0 || !Prefs.DevMode || map != Find.CurrentMap)
                return;
            SmartAISettings settings = SmartAIMod.Settings;
            if (settings == null || !settings.devShowThreatOverlay)
                return;

            int now = GenTicks.TicksGame;
            float maxRing = GenRadial.MaxRadialPatternRadius - 1f;
            for (int i = 0; i < count; i++)
            {
                bool live = entries[i].expiresTick > now && IsLive(entries[i].actor, null);
                float radius = entries[i].reach < maxRing ? entries[i].reach : maxRing;
                GenDraw.DrawRadiusRing(entries[i].origin, radius, live ? OverlayLive : OverlayStale);
            }
        }
    }
}
