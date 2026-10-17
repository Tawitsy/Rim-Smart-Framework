using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace SmartAIFramework.Data
{
    [Flags]
    public enum VerbTraits
    {
        None          = 0,
        Damaging      = 1 << 0,
        AreaOfEffect  = 1 << 1,
        Incapacitates = 1 << 2,
        Debuff        = 1 << 3,
        Beneficial    = 1 << 4,
        Explosive     = 1 << 5
    }

    public sealed class VerbProfile
    {
        public VerbTraits traits;
        public float damage;
        public float aoeRadius;
        public float range;
        public float forcedMissRadius;

        public bool HasTrait(VerbTraits flag) => (traits & flag) != 0;
        public bool IsHostile => HasTrait(VerbTraits.Damaging) || HasTrait(VerbTraits.Incapacitates) || HasTrait(VerbTraits.Debuff);
    }

    public static class VerbProfileCache
    {
        private static readonly Dictionary<VerbProperties, VerbProfile> cache = new Dictionary<VerbProperties, VerbProfile>(256);
        private static readonly Dictionary<Type, FieldInfo> radiusFields = new Dictionary<Type, FieldInfo>(32);
        private static readonly string[] CustomRadiusNames = { "radius", "effectRadius", "aoeRadius", "areaRadius", "explosionRadius" };

        public static VerbProfile Get(Verb verb) => verb?.verbProps == null ? null : Get(verb.verbProps);

        public static VerbProfile Get(VerbProperties props)
        {
            if (props == null) return null;
            if (cache.TryGetValue(props, out VerbProfile cached))
                return cached;

            var profile = new VerbProfile
            {
                range = props.range,
                forcedMissRadius = props.ForcedMissRadius
            };

            ProjectileProperties proj = props.defaultProjectile?.projectile;
            if (proj != null)
            {
                profile.damage = GetBaseProjectileDamage(proj);
                profile.aoeRadius = proj.explosionRadius;
                EvaluateDamageDef(proj.damageDef, profile);
            }

            if (profile.aoeRadius <= 0f)
            {
                float customRadius = GetCustomRadius(props);
                if (customRadius > 0f)
                    profile.aoeRadius = customRadius;
            }

            if (profile.aoeRadius > 0f) profile.traits |= VerbTraits.AreaOfEffect;
            if (profile.damage > 0f) profile.traits |= VerbTraits.Damaging;

            cache[props] = profile;
            return profile;
        }

        private static float GetBaseProjectileDamage(ProjectileProperties proj)
        {
            try
            {
                return proj.GetDamageAmount((Thing)null, null);
            }
            catch
            {
                return 0f;
            }
        }

        private static void EvaluateDamageDef(DamageDef def, VerbProfile profile)
        {
            if (def == null) return;
            if (def.isExplosive) profile.traits |= VerbTraits.Explosive;
            if (def.harmsHealth) profile.traits |= VerbTraits.Damaging;

            HediffDef hediff = def.hediff;
            if (hediff != null)
            {
                if (hediff.isBad) profile.traits |= VerbTraits.Debuff;
                else if (!def.harmsHealth) profile.traits |= VerbTraits.Beneficial;
            }

            if (def == DamageDefOf.Stun || def == DamageDefOf.EMP)
            {
                profile.traits |= VerbTraits.Incapacitates;
            }
        }

        private static float GetCustomRadius(VerbProperties props)
        {
            Type type = props.GetType();
            if (type == typeof(VerbProperties)) return 0f;

            if (!radiusFields.TryGetValue(type, out FieldInfo field))
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                for (int i = 0; i < CustomRadiusNames.Length; i++)
                {
                    FieldInfo candidate = type.GetField(CustomRadiusNames[i], flags);
                    if (candidate != null && candidate.FieldType == typeof(float))
                    {
                        field = candidate;
                        break;
                    }
                }
                radiusFields[type] = field;
            }

            return field != null ? (float)field.GetValue(props) : 0f;
        }

        public static void Clear()
        {
            cache.Clear();
            radiusFields.Clear();
        }
    }
}
