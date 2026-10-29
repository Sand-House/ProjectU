using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    public enum Faction { Player, Enemy }
    public enum AttackTargetRule { Hostile, Friendly, Any }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Mortal))]
    public sealed class Combatant : MonoBehaviour
    {
        [SerializeField] private Faction faction = Faction.Enemy;
        private Mortal mortal;
        private readonly List<Attack> attacks = new List<Attack>();

        public Faction Faction { get => faction; set => faction = value; }
        public Mortal Mortal
        {
            get
            {
                if (mortal == null) mortal = GetComponent<Mortal>();
                return mortal;
            }
        }
        public bool IsAlive => isActiveAndEnabled && Mortal != null && !Mortal.isDead;
        public bool IsPerformingAttack
        {
            get
            {
                // Reuse the list and include attacks added at runtime, even without player bindings.
                GetComponents(attacks);
                foreach (Attack attack in attacks)
                    if (attack.BlocksActions) return true;
                return false;
            }
        }

        public bool CanTarget(Combatant target, AttackTargetRule rule = AttackTargetRule.Hostile)
        {
            return target != null && target != this && target.IsAlive && Matches(Faction, target.Faction, rule);
        }

        internal static bool Matches(Faction source, Faction target, AttackTargetRule rule)
        {
            switch (rule)
            {
                case AttackTargetRule.Hostile: return source != target;
                case AttackTargetRule.Friendly: return source == target;
                case AttackTargetRule.Any: return true;
                default: return false;
            }
        }
    }
}
