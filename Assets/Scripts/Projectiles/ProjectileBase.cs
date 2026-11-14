using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public abstract class ProjectileBase : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float speed = 40f;
        [SerializeField, Min(0.01f)] private float maxLifeTime = 5f;
        [SerializeField] private bool useGravity;

        public GameObject Owner { get; private set; }
        public bool IsActive { get; private set; }
        public float Speed => speed;
        public float MaxLifeTime => maxLifeTime;
        public bool UseGravity => useGravity;
        public ProjectileLaunchData AttackInfo { get; private set; }
        public bool HasAttackData => AttackInfo.IsValid;
        protected Rigidbody Body { get; private set; }
        protected SphereCollider HitCollider { get; private set; }
        private ProjectilePool pool;
        private float elapsed;
        private bool expired;
        private bool cachedScale;
        private Vector3 baseScale;
        private readonly List<Collider> ignoredColliders = new List<Collider>();
        private readonly HashSet<Combatant> damagedTargets = new HashSet<Combatant>();

        protected virtual void Awake() => CacheComponents();

        private void CacheComponents()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            if (HitCollider == null) HitCollider = GetComponent<SphereCollider>();
            if (!cachedScale)
            {
                baseScale = transform.localScale;
                cachedScale = true;
            }
        }

        internal void AttachPool(ProjectilePool value)
        {
            CacheComponents();
            pool = value;
        }

        public void Launch(GameObject owner, Vector3 position, Vector3 direction)
            => Launch(owner, position, direction, default);

        public void Launch(GameObject owner, Vector3 position, Vector3 direction, in ProjectileLaunchData attackInfo)
        {
            CacheComponents();
            RestoreCollisions();
            damagedTargets.Clear();
            AttackInfo = attackInfo;
            transform.localScale = baseScale * (HasAttackData ? attackInfo.SizeMultiplier : 1f);
            Owner = owner;
            elapsed = 0f;
            expired = false;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            Body.position = position;
            Body.rotation = transform.rotation;
            Body.isKinematic = false;
            Body.useGravity = useGravity;
            HitCollider.enabled = true;
            IsActive = true;
            gameObject.SetActive(true);
            Body.linearVelocity = direction * speed;
            Body.angularVelocity = Vector3.zero;
            if (owner != null)
                foreach (Collider target in owner.GetComponentsInChildren<Collider>())
                {
                    if (target == HitCollider) continue;
                    Physics.IgnoreCollision(HitCollider, target, true);
                    ignoredColliders.Add(target);
                }
            OnLaunched();
        }

        protected virtual void OnLaunched() { }

        // Sweep before physics advances. This also finds trigger hitboxes and lets piercing
        // shots ignore character collision pairs before the Rigidbody would be stopped.
        protected virtual void FixedUpdate()
        {
            if (!HasAttackData || !IsActive || expired || Time.timeScale <= 0f) return;
            Vector3 velocity = Body.linearVelocity;
            float distance = velocity.magnitude * Time.fixedDeltaTime;
            Vector3 scale = transform.lossyScale;
            float radius = HitCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 center = transform.TransformPoint(HitCollider.center);
            foreach (Collider overlap in Physics.OverlapSphere(center, radius, AttackInfo.Damage.HitMask, QueryTriggerInteraction.Collide))
                if (ProcessSweptContact(overlap)) return;
            if (distance <= 0.0001f) return;
            RaycastHit[] hits = Physics.SphereCastAll(center, radius, velocity.normalized, distance,
                AttackInfo.Damage.HitMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
                if (ProcessSweptContact(hit.collider)) break;
        }

        // True stops this sweep at the first blocking object.
        private bool ProcessSweptContact(Collider collider)
        {
            if (collider == null || collider == HitCollider || BelongsToOwner(collider) ||
                collider.GetComponentInParent<ProjectileBase>() != null) return false;
            if (Hitbox.TryResolve(collider, out Combatant target, out _))
            {
                bool allowed = AttackInfo.Damage.CanHit(target);
                if (!allowed || AttackInfo.PiercesEnemies)
                {
                    foreach (Collider bodyCollider in target.GetComponentsInChildren<Collider>())
                        IgnoreCollider(bodyCollider);
                    if (allowed) OnSweptImpact(collider);
                    return !IsActive;
                }
                OnSweptImpact(collider);
                return true;
            }
            if (collider.isTrigger) return false;
            OnSweptImpact(collider);
            return true;
        }

        protected virtual void OnSweptImpact(Collider collider) { }

        protected bool TryDamage(Collider collider)
        {
            if (!HasAttackData || !Hitbox.TryResolve(collider, out Combatant target, out bool critical) ||
                !AttackInfo.Damage.CanHit(target) || !damagedTargets.Add(target)) return false;
            DamageResolver.Apply(AttackInfo.Damage, target, critical);
            return true;
        }

        private void IgnoreCollider(Collider collider)
        {
            if (collider == null || collider == HitCollider || ignoredColliders.Contains(collider)) return;
            Physics.IgnoreCollision(HitCollider, collider, true);
            ignoredColliders.Add(collider);
        }

        protected virtual void Update()
        {
            if (!IsActive || expired) return;
            elapsed += Time.deltaTime;
            if (elapsed < maxLifeTime) return;
            expired = true;
            OnLifeTimeExpired();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsActive || expired || BelongsToOwner(collision.collider)) return;
            if (HasAttackData)
            {
                Collider collider = collision.collider;
                if ((AttackInfo.Damage.HitMask & (1 << collider.gameObject.layer)) == 0) return;
                if (Hitbox.TryResolve(collider, out Combatant target, out _))
                {
                    if (!AttackInfo.Damage.CanHit(target)) { IgnoreCollider(collider); return; }
                    if (AttackInfo.PiercesEnemies)
                    {
                        IgnoreCollider(collider);
                        OnSweptImpact(collider);
                        return;
                    }
                }
            }
            OnImpact(collision);
        }

        protected bool BelongsToOwner(Collider target) => Owner != null &&
            (target.gameObject == Owner || target.transform.IsChildOf(Owner.transform));

        protected abstract void OnImpact(Collision collision);
        protected virtual void OnLifeTimeExpired() => Despawn();

        public void Despawn()
        {
            if (!IsActive) return;
            IsActive = false;
            if (pool != null) pool.Return(this);
            else gameObject.SetActive(false);
        }

        protected virtual void OnDisable()
        {
            StopAllCoroutines();
            RestoreCollisions();
            if (Body != null && !Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            Owner = null;
            IsActive = false;
            AttackInfo = default;
            damagedTargets.Clear();
            if (cachedScale) transform.localScale = baseScale;
        }

        private void RestoreCollisions()
        {
            foreach (Collider target in ignoredColliders)
                if (target != null && HitCollider != null)
                    Physics.IgnoreCollision(HitCollider, target, false);
            ignoredColliders.Clear();
        }
    }
}
