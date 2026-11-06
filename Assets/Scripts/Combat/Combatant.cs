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
        private readonly List<ActionBase> actions = new List<ActionBase>();

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
        public bool IsGameplayBlocked { get; private set; }
        public void SetGameplayBlocked(bool blocked) => IsGameplayBlocked = blocked;
        public bool IsPerformingAttack
        {
            get
            {
                GetComponents(actions);
                foreach (ActionBase action in actions)
                    if (action.IsAttackAction && action.IsDelayLocked) return true;
                return false;
            }
        }

        public bool IsActionDelayLocked
        {
            get
            {
                GetComponents(actions);
                foreach (ActionBase action in actions)
                    if (action.IsDelayLocked) return true;
                return false;
            }
        }

        public bool IsMovementLocked
        {
            get
            {
                GetComponents(actions);
                foreach (ActionBase action in actions)
                    if (action.BlocksMovement) return true;
                return false;
            }
        }

        internal bool BlocksAction(PlayerActionSlot slot, bool ignoreDelay, PlayerAction except = null)
        {
            GetComponents(actions);
            foreach (ActionBase action in actions)
                if (action != except && !(ignoreDelay && action.IsDelayLocked) &&
                    (action is PlayerAction player ? player.Blocks(slot) : action.IsDelayLocked)) return true;
            return false;
        }

        internal bool BlocksAction(ActionBase requested, bool ignoreDelay, ActionBase except = null)
        {
            GetComponents(actions);
            foreach (ActionBase action in actions)
                if (action != except && !(ignoreDelay && action.IsDelayLocked) && action.Blocks(requested)) return true;
            return false;
        }

        internal bool IsDelayBlocking(PlayerActionSlot slot)
        {
            GetComponents(actions);
            foreach (ActionBase action in actions)
                if (action.IsDelayLocked && (action is PlayerAction player ? player.Blocks(slot) : true)) return true;
            return false;
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
