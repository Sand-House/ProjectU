using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    public static class DamageResolver
    {
        public static int CalculateDamage(in DamageContext context, bool critical)
        {
            if (!context.IsValid) return 0;
            double value = context.Damage * (critical && context.CanCritical ? (double)context.CriticalMultiplier : 1d);
            return (int)Math.Min(int.MaxValue, Math.Round(value, MidpointRounding.AwayFromZero));
        }

        // Returns actual HP lost. Mortal remains responsible for immunity, HP and death events.
        public static int Apply(in DamageContext context, Combatant target, bool critical = false)
        {
            if (!context.CanHit(target)) return 0;
            Mortal mortal = target.Mortal;
            int previous = mortal.HP;
            mortal.TakeDamage(CalculateDamage(context, critical), context.Attacker);
            return Mathf.Max(0, previous - mortal.HP);
        }

        // Area attacks select one hit per Combatant; any qualifying critical hitbox wins.
        public static int ApplyArea(in DamageContext context, Collider[] colliders, Predicate<Collider> filter = null)
        {
            var targets = new Dictionary<Combatant, bool>();
            foreach (Collider collider in colliders)
            {
                if (filter != null && !filter(collider)) continue;
                if (!Hitbox.TryResolve(collider, out Combatant target, out bool critical) || !context.CanHit(target)) continue;
                targets.TryGetValue(target, out bool existingCritical);
                targets[target] = existingCritical || critical;
            }
            int damagedCount = 0;
            foreach (var pair in targets)
                if (Apply(context, pair.Key, pair.Value) > 0) damagedCount++;
            return damagedCount;
        }
    }
}
