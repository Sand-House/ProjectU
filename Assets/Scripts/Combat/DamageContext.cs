using UnityEngine;

namespace ProjectU
{
    // A value snapshot. In-flight projectiles never read mutable attack assets or character stats.
    public readonly struct DamageContext
    {
        public bool IsValid { get; }
        public Combatant Source { get; }
        public GameObject Attacker { get; }
        public Faction SourceFaction { get; }
        public AttackTargetRule TargetRule { get; }
        public float Damage { get; }
        public bool CanCritical { get; }
        public float CriticalMultiplier { get; }
        public int HitMask { get; }

        public DamageContext(Combatant source, AttackDataBase data, float damageMultiplier = 1f)
        {
            IsValid = source != null && data != null;
            Source = source;
            Attacker = source != null ? source.gameObject : null;
            SourceFaction = source != null ? source.Faction : default;
            TargetRule = data != null ? data.TargetRule : default;
            double damage = data != null ? (double)data.Damage * AttackDataBase.FiniteAtLeast(damageMultiplier, 0f) : 0;
            Damage = (float)System.Math.Min(int.MaxValue, damage);
            CanCritical = data != null && data.CanCritical;
            CriticalMultiplier = data != null ? data.CriticalMultiplier : 1f;
            HitMask = data != null ? (int)data.HitMask : 0;
        }

        public bool CanHit(Combatant target)
            => IsValid && target != null && target != Source && target.IsAlive &&
               Combatant.Matches(SourceFaction, target.Faction, TargetRule);
    }
}
