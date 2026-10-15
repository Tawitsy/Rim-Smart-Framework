using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace SmartAIFramework.Core_Engine
{
    [Flags]
    public enum ActionTraits
    {
        None          = 0,
        Damaging      = 1 << 0,
        AreaOfEffect  = 1 << 1,
        Incapacitates = 1 << 2,
        Debuff        = 1 << 3,
        Beneficial    = 1 << 4,
        Explosive     = 1 << 5,
    }

    public sealed class ActionProfile
    {
        public ActionTraits traits;
        public float damage;
        public float aoeRadius;
        public float range;
        public float forcedMissRadius;

        public bool Is(ActionTraits t) => (traits & t) != 0;
        public bool IsHostileAction => Is(ActionTraits.Damaging) || Is(ActionTraits.Incapacitates) || Is(ActionTraits.Debuff);

        public override string ToString() => $"[{traits}] dmg={damage} aoe={aoeRadius} range={range} miss={forcedMissRadius}";
    }

    public static class ModActionAnalyzer
    {
        private static readonly Dictionary<VerbProperties, ActionProfile> cache = new Dictionary<VerbProperties, ActionProfile>();

        private static readonly string[] CustomRadiusFieldNames = { "radius", "effectRadius", "aoeRadius", "areaRadius", "explosionRadius" };
        private static readonly Dictionary<Type, FieldInfo> customRadiusFieldCache = new Dictionary<Type, FieldInfo>();

        public static ActionProfile Analyze(Verb verb) => verb?.verbProps == null ? null : Analyze(verb.verbProps);

        public static ActionProfile Analyze(VerbProperties props)
        {
            if (props == null) return null;
            if (cache.TryGetValue(props, out var cached)) return cached;

            var p = new ActionProfile
            {
                range = props.range,
                forcedMissRadius = props.ForcedMissRadius,
            };

            ProjectileProperties proj = props.defaultProjectile?.projectile;
            if (proj != null)
            {
                p.damage = SafeDamage(proj);
                p.aoeRadius = proj.explosionRadius;
                InspectDamageDef(proj.damageDef, p);
            }

            if (p.aoeRadius <= 0f)
            {
                float r = ReadCustomRadius(props);
                if (r > 0f) p.aoeRadius = r;
            }

            if (p.aoeRadius > 0f) p.traits |= ActionTraits.AreaOfEffect;
            if (p.damage > 0f) p.traits |= ActionTraits.Damaging;

            cache[props] = p;
            if (SmartAIMod.Settings.debugLogging) Log.Message($"[SmartAI] Analyzed verb {props.label ?? props.verbClass?.Name}: {p}");
            return p;
        }

        private static float SafeDamage(ProjectileProperties proj)
        {
            try { return proj.GetDamageAmount((Thing)null, null); }
            catch { return 0f; }
        }

        private static void InspectDamageDef(DamageDef def, ActionProfile p)
        {
            if (def == null) return;
            if (def.isExplosive) p.traits |= ActionTraits.Explosive;

            if (def.harmsHealth)
            {
                p.traits |= ActionTraits.Damaging;
            }

            HediffDef h = def.hediff;
            if (h != null)
            {
                if (h.isBad) p.traits |= ActionTraits.Debuff;
                else if (!def.harmsHealth) p.traits |= ActionTraits.Beneficial;
            }

            if (def == DamageDefOf.Stun || def == DamageDefOf.EMP)
                p.traits |= ActionTraits.Incapacitates;
        }

        private static float ReadCustomRadius(object obj)
        {
            Type t = obj.GetType();
            if (!customRadiusFieldCache.TryGetValue(t, out var field))
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var name in CustomRadiusFieldNames)
                {
                    var f = t.GetField(name, bf);
                    if (f != null && f.FieldType == typeof(float)) { field = f; break; }
                }
                customRadiusFieldCache[t] = field;
            }
            return field != null ? (float)field.GetValue(obj) : 0f;
        }

        public static void ClearCache()
        {
            cache.Clear();
            customRadiusFieldCache.Clear();
        }
    }
}
