using System.Collections;
using UnityEngine;

namespace ProjectU
{
    public sealed class ProjectileGrenade : Projectile
    {
        // TODO: 피해 시스템을 구현할 때 아래 피해 처리 코드를 연결한다.
        // [SerializeField, Min(0f)] private float damage = 60f;
        [SerializeField, Min(0.1f)] private float explosionRadius = 4f;
        // [SerializeField] private LayerMask damageMask = ~0;
        [SerializeField] private Transform explosionVisual;
        [SerializeField] private Renderer bodyVisual;
        // private readonly System.Collections.Generic.HashSet<IDamageable> damagedTargets = new();

        protected override void OnLaunched()
        {
            if (explosionVisual != null) explosionVisual.gameObject.SetActive(false);
            if (bodyVisual != null) bodyVisual.enabled = true;
            Body.angularVelocity = transform.right * 8f;
            // damagedTargets.Clear();
        }

        protected override void OnImpact(Collision collision) { }

        protected override void OnLifeTimeExpired()
        {
            // foreach (Collider targetCollider in Physics.OverlapSphere(transform.position,
            //     explosionRadius, damageMask, QueryTriggerInteraction.Ignore))
            // {
            //     if (BelongsToOwner(targetCollider)) continue;
            //     var target = targetCollider.GetComponentInParent<IDamageable>();
            //     if (target != null && damagedTargets.Add(target))
            //         target.TakeDamage(damage, Owner);
            // }
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
            HitCollider.enabled = false;
            if (bodyVisual != null) bodyVisual.enabled = false;
            if (explosionVisual == null) Despawn();
            else StartCoroutine(ShowExplosion());
        }

        private IEnumerator ShowExplosion()
        {
            explosionVisual.gameObject.SetActive(true);
            float time = 0f;
            while (time < 0.2f)
            {
                // Account for the prefab root scale: the displayed radius is world-space.
                float diameter = explosionRadius * 2f * Mathf.Lerp(0.1f, 1f, time / 0.2f);
                explosionVisual.localScale = Vector3.one * diameter / transform.lossyScale.x;
                time += Time.deltaTime;
                yield return null;
            }
            explosionVisual.gameObject.SetActive(false);
            Despawn();
        }
    }
}
