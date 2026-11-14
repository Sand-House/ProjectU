using System;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(Combatant))]
    public abstract class AttackBase : MonoBehaviour
    {
        [SerializeField] private Transform origin;
        private Combatant owner;
        public abstract AttackDataBase Data { get; }
        public Transform Origin => origin;
        public Combatant Owner => owner != null ? owner : (owner = GetComponent<Combatant>());
        public Vector3 WorldAttackPosition { get; private set; }
        public Vector3 LastAttackPosition { get; private set; }
        public bool IsConfiguredForExecution => isActiveAndEnabled && Owner != null && Owner.IsAlive &&
            Data != null && IsConfigured;
        protected virtual bool IsConfigured => true;
        public event Action OnExecuted;

        // Action timing and authorization precede this synchronous effect call.
        internal bool TryExecute(ActionBase action, Vector3 position, Vector3 worldAimPoint, float damageMultiplier)
        {
            if (action == null || !action.IsExecuting || action.Owner != Owner ||
                !IsConfiguredForExecution || Time.timeScale <= 0f ||
                !AttackDataBase.IsFinite(position) || !AttackDataBase.IsFinite(worldAimPoint)) return false;
            WorldAttackPosition = position;
            Vector3 origin = GetExecutionOrigin(position);
            LastAttackPosition = origin;
            Vector3 direction = worldAimPoint - origin;
            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
            var context = new DamageContext(Owner, Data, AttackDataBase.FiniteAtLeast(damageMultiplier, 0f));
            if (!ExecuteAttack(origin, direction.normalized, context)) return false;
            OnExecuted?.Invoke();
            return true;
        }

        protected abstract bool ExecuteAttack(Vector3 origin, Vector3 direction, in DamageContext context);

        private Vector3 GetExecutionOrigin(Vector3 origin)
        {
            Vector3 local = transform.InverseTransformPoint(origin);
            Vector3 start = transform.TransformPoint(new Vector3(0f, local.y, 0f));
            Vector3 offset = origin - start;
            float distance = offset.magnitude;
            if (distance < 0.0001f || Data == null) return origin;
            RaycastHit[] hits = Physics.RaycastAll(start, offset / distance, distance, Data.HitMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<ProjectileBase>() != null ||
                    hit.collider.GetComponentInParent<Combatant>() != null) continue;
                return hit.point - offset / distance * Mathf.Min(0.2f, hit.distance);
            }
            return origin;
        }
    }
}
