using System;
using UnityEngine;

namespace ProjectU
{
    public enum AttackPhase { Ready, Windup, Recovery }

    // Execute after camera (LateUpdate -100), aim (75), and the player attack controller (100).
    [DefaultExecutionOrder(110)]
    [RequireComponent(typeof(Combatant))]
    public abstract class Attack : MonoBehaviour
    {
        private Combatant owner;
        private bool pending;
        private bool hasAimPoint;
        private Vector3 aimPoint;
        private Transform attackOrigin;
        private Vector3 requestedPosition;
        private bool usesFixedPosition;
        private double executeAt;
        private double recoveryEndsAt;
        private double cooldownEndsAt;
        private int lastExecutionFrame = -1;
        private float damageMultiplier = 1f;
        private float speedMultiplier = 1f;

        public abstract AttackData Data { get; }
        public Combatant Owner => owner != null ? owner : (owner = GetComponent<Combatant>());
        public Vector3 WorldAttackPosition => usesFixedPosition ? requestedPosition :
            attackOrigin != null ? attackOrigin.position : transform.position;
        public Vector3 LastAttackPosition { get; private set; }
        public float DamageMultiplier { get => damageMultiplier; set => damageMultiplier = AttackData.FiniteAtLeast(value, 0f); }
        public float SpeedMultiplier { get => speedMultiplier; set => speedMultiplier = AttackData.FiniteAtLeast(value, 0.01f); }
        public AttackPhase Phase => pending ? AttackPhase.Windup :
            Time.timeAsDouble < recoveryEndsAt ? AttackPhase.Recovery : AttackPhase.Ready;
        public float CooldownRemaining => (float)Math.Max(0d, cooldownEndsAt - Time.timeAsDouble);
        public float RecoveryRemaining => (float)Math.Max(0d, recoveryEndsAt - Time.timeAsDouble);
        // Reserve the execution frame too, including attacks whose two delays are zero.
        public bool BlocksActions => isActiveAndEnabled &&
            (Phase != AttackPhase.Ready || lastExecutionFrame == Time.frameCount);
        public bool CanAttack => isActiveAndEnabled && Time.timeScale > 0f && Owner != null && Owner.IsAlive && Data != null &&
            IsConfigured && !Owner.IsPerformingAttack && CooldownRemaining <= 0f;
        protected virtual bool IsConfigured => true;

        public event Action OnStarted;
        public event Action OnExecuted;
        public event Action OnFailed;
        public event Action OnCancelled;

        public bool TryAttack()
        {
            if (!CanAttack) return false;
            attackOrigin = transform;
            usesFixedPosition = false;
            return ScheduleAttack();
        }

        public bool TryAttack(Vector3 worldAimPoint)
            => TryAttack(transform, worldAimPoint);

        // Keep following an animated muzzle until the windup actually finishes.
        public bool TryAttack(Transform origin, Vector3 worldAimPoint)
        {
            if (!CanAttack || origin == null || !AttackData.IsFinite(origin.position) ||
                !AttackData.IsFinite(worldAimPoint)) return false;
            attackOrigin = origin;
            usesFixedPosition = false;
            SetAimPoint(worldAimPoint);
            return ScheduleAttack();
        }

        // Callers without an animated anchor may supply an explicit world-space snapshot.
        public bool TryAttack(Vector3 worldAttackPosition, Vector3 worldAimPoint)
        {
            if (!CanAttack || !AttackData.IsFinite(worldAttackPosition) || !AttackData.IsFinite(worldAimPoint)) return false;
            attackOrigin = null;
            requestedPosition = worldAttackPosition;
            usesFixedPosition = true;
            SetAimPoint(worldAimPoint);
            return ScheduleAttack();
        }

        private bool ScheduleAttack()
        {
            pending = true;
            executeAt = Time.timeAsDouble + Data.WindupTime / SpeedMultiplier;
            OnStarted?.Invoke();
            return true;
        }

        // May be refreshed during windup by a player aim controller or AI.
        public void SetAimPoint(Vector3 worldAimPoint)
        {
            if (!AttackData.IsFinite(worldAimPoint)) throw new ArgumentException("Aim point must be finite.", nameof(worldAimPoint));
            aimPoint = worldAimPoint;
            hasAimPoint = true;
        }

        public void ClearAimPoint() => hasAimPoint = false;

        // Only pending windup is cancelled. Executed attacks retain their recovery and cooldown.
        public bool CancelAttack()
        {
            if (!pending) return false;
            pending = false;
            OnCancelled?.Invoke();
            return true;
        }

        protected virtual void LateUpdate()
        {
            if (Owner == null || !Owner.IsAlive) { CancelAttack(); return; }
            if (!pending || Time.timeScale <= 0f || Time.timeAsDouble < executeAt) return;
            pending = false;
            lastExecutionFrame = Time.frameCount;
            if (Data == null || !IsConfigured || (!usesFixedPosition && attackOrigin == null) ||
                !AttackData.IsFinite(WorldAttackPosition))
            {
                OnFailed?.Invoke();
                return;
            }
            Vector3 origin = GetExecutionOrigin();
            LastAttackPosition = origin;
            Vector3 direction = hasAimPoint ? aimPoint - origin : transform.forward;
            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
            var context = new DamageContext(Owner, Data, DamageMultiplier);
            if (!ExecuteAttack(origin, direction.normalized, context))
            {
                OnFailed?.Invoke();
                return;
            }
            recoveryEndsAt = Time.timeAsDouble + Data.RecoveryTime / SpeedMultiplier;
            cooldownEndsAt = Time.timeAsDouble + Data.Cooldown / SpeedMultiplier;
            OnExecuted?.Invoke();
        }

        protected abstract bool ExecuteAttack(Vector3 origin, Vector3 direction, in DamageContext context);

        // Check from the character's axis at muzzle height so an animated muzzle cannot fire through a wall.
        private Vector3 GetExecutionOrigin()
        {
            Vector3 origin = WorldAttackPosition;
            Vector3 local = transform.InverseTransformPoint(origin);
            Vector3 start = transform.TransformPoint(new Vector3(0f, local.y, 0f));
            Vector3 offset = origin - start;
            float distance = offset.magnitude;
            if (distance < 0.0001f || Data == null) return origin;
            RaycastHit[] hits = Physics.RaycastAll(start, offset / distance, distance, Data.HitMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Projectile>() != null ||
                    hit.collider.GetComponentInParent<Combatant>() != null) continue;
                return hit.point - offset / distance * Mathf.Min(0.2f, hit.distance);
            }
            return origin;
        }

        protected virtual void OnDisable() => CancelAttack();
    }
}
