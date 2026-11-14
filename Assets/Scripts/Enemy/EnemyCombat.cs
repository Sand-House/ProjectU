using System;
using UnityEngine;

namespace ProjectU
{
    [DisallowMultipleComponent, RequireComponent(typeof(Combatant))]
    public sealed class EnemyCombat : MonoBehaviour
    {
        [SerializeField, Tooltip("Selection priority, first available action wins. Each action owns its timers.")]
        private EnemyActionBase[] actions = Array.Empty<EnemyActionBase>();
        public System.Collections.Generic.IReadOnlyList<EnemyActionBase> Actions => actions;
        public bool IsBusy
        {
            get
            {
                foreach (EnemyActionBase action in actions)
                    if (action != null && (action.IsActive || action.IsDelayLocked)) return true;
                return false;
            }
        }
        public bool IsInPosition(Combatant target)
        {
            foreach (EnemyActionBase action in actions)
                if (action != null && action.isActiveAndEnabled && action.IsInPosition(target)) return true;
            return false;
        }
        public bool TryStartAvailable(Combatant target)
        {
            if (!isActiveAndEnabled || IsBusy) return false;
            foreach (EnemyActionBase action in actions)
                if (action != null && action.TryStartFor(target)) return true;
            return false;
        }
        public void CancelAll(ActionCancelReason reason)
        {
            // Include unbound actions so death/teardown cannot leave a direct request alive.
            foreach (EnemyActionBase action in GetComponents<EnemyActionBase>()) action.CancelAndClear(reason);
        }
        private void OnEnable()
        {
            var seen = new System.Collections.Generic.HashSet<EnemyActionBase>();
            var executors = new System.Collections.Generic.HashSet<AttackBase>();
            foreach (EnemyActionBase action in actions)
                if (action == null || action.gameObject != gameObject || !seen.Add(action) ||
                    (action is EnemyAttackAction attack && (attack.Attack == null || !executors.Add(attack.Attack))))
                {
                    Debug.LogError("EnemyCombat requires unique local actions and attack executors.", this);
                    enabled = false;
                    return;
                }
        }
        private void OnDisable() => CancelAll(ActionCancelReason.Disabled);
    }
}
