using System.Collections.Generic;
using RimWorld;
using SmartAIFramework.Compat;
using SmartAIFramework.Data;
using Verse;

namespace SmartAIFramework.Grid
{
    // Hostile shooters that can currently hit the colony: where they stood, how far they reach, until when.
    // Written by sensors (aiming, damage, near misses), read by vanilla's own danger and flee decisions through
    // the Combat patches. Time is compared lazily when a query arrives; nothing here ticks.
    public sealed class ThreatMap : MapComponent
    {
        public const int MaxEntries = 64;
        public const int ExpiryTicks = 600;
        public const float MinReach = 8f;
        public const float MaxReach = 40f;
        private const int MovedCellsSq = 9;
        private const int MaxCachedRegions = 2048;

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

        // Region id -> in reach, valid for one version of the entry set.
        private readonly Dictionary<int, bool> regionCache = new Dictionary<int, bool>(64);
        private int regionCacheVersion = -1;

        // For fast paths. Expired entries are only dropped inside a query, so this may stay true until the next one.
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

        // Read-only lookup for fast paths: a map nobody ever registered a threat on has no component to find.
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

        // The pawns vanilla's flee, danger and work searches may steer for the colony's safety. Drafted pawns, the
        // player-controlled avatar, mental states and pawns set to Attack or Ignore keep their own rules.
        public static bool IsEligible(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Drafted || pawn.Faction != Faction.OfPlayer || PawnStateTracker.ObserveOnly)
                return false;
            Pawn_PlayerSettings settings = pawn.playerSettings;
            if (settings == null || !settings.UsesConfigurableHostilityResponse || settings.hostilityResponse != HostilityResponseMode.Flee)
                return false;
            return !pawn.InMentalState && !PerspectiveShiftCompat.IsDirectlyControlled(pawn);
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
                // A shooter that barely moved or changed range does not change any answer: keep the caches.
                if (reach != entries[i].reach || pos.DistanceToSquared(entries[i].origin) >= MovedCellsSq)
                {
                    entries[i].origin = pos;
                    entries[i].reach = reach;
                    version++;
                }
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

        // Mirrors what vanilla's flee giver needs to see: a spawned, hostile, still-threatening attacker.
        private bool IsLive(Pawn actor, Pawn forPawn)
        {
            if (actor == null || !actor.Spawned || actor.Dead || actor.Downed || actor.Map != map)
                return false;
            if (forPawn != null ? !actor.HostileTo(forPawn) : !actor.HostileTo(Faction.OfPlayer))
                return false;
            return !actor.ThreatDisabled(forPawn);
        }

        // forPawn: the pawn asking, so hostility is judged against it (vanilla's flee giver does the same).
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
            PruneIfNeeded(GenTicks.TicksGame);
            if (count == 0) return false;

            if (regionCacheVersion != version || regionCache.Count > MaxCachedRegions)
            {
                regionCache.Clear();
                regionCacheVersion = version;
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
                IntVec3 nearest = region.extentsClose.ClosestCellTo(origin);
                if (nearest.DistanceToSquared(origin) > reach * reach)
                    continue;
                if (!IsLive(entries[i].actor, null))
                    continue;
                result = GenSight.LineOfSight(origin, nearest, map, skipFirstCell: true);
            }
            regionCache[region.id] = result;
            return result;
        }
    }
}
