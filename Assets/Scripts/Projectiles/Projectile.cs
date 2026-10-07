using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public abstract class Projectile : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float speed = 40f;
        [SerializeField, Min(0.01f)] private float maxLifeTime = 5f;
        [SerializeField] private bool useGravity;

        public GameObject Owner { get; private set; }
        public bool IsActive { get; private set; }
        public float Speed => speed;
        public float MaxLifeTime => maxLifeTime;
        public bool UseGravity => useGravity;
        protected Rigidbody Body { get; private set; }
        protected SphereCollider HitCollider { get; private set; }
        private ProjectilePool pool;
        private float elapsed;
        private bool expired;
        private readonly List<Collider> ignoredColliders = new List<Collider>();

        protected virtual void Awake() => CacheComponents();

        private void CacheComponents()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            if (HitCollider == null) HitCollider = GetComponent<SphereCollider>();
        }

        internal void AttachPool(ProjectilePool value) => pool = value;

        public void Launch(GameObject owner, Vector3 position, Vector3 direction)
        {
            CacheComponents();
            RestoreCollisions();
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
