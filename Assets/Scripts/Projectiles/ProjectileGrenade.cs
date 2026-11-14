using System.Collections;
using UnityEngine;

namespace ProjectU
{
    public sealed class ProjectileGrenade : ProjectileBase
    {
        [SerializeField, Min(0.1f)] private float explosionRadius = 4f;
        [SerializeField] private Transform explosionVisual;
        [SerializeField] private Renderer bodyVisual;
        public float ExplosionRadius => explosionRadius * (HasAttackData ? AttackInfo.ExplosionRadiusMultiplier : 1f);

        protected override void OnLaunched()
        {
            if (explosionVisual != null) explosionVisual.gameObject.SetActive(false);
            if (bodyVisual != null) bodyVisual.enabled = true;
            Body.angularVelocity = transform.right * 8f;
        }

        protected override void OnImpact(Collision collision) { }

        protected override void OnLifeTimeExpired()
        {
            if (HasAttackData)
                DamageResolver.ApplyArea(AttackInfo.Damage, Physics.OverlapSphere(transform.position, ExplosionRadius,
                    AttackInfo.Damage.HitMask, QueryTriggerInteraction.Collide));
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
                float diameter = ExplosionRadius * 2f * Mathf.Lerp(0.1f, 1f, time / 0.2f);
                explosionVisual.localScale = Vector3.one * diameter / transform.lossyScale.x;
                time += Time.deltaTime;
                yield return null;
            }
            explosionVisual.gameObject.SetActive(false);
            Despawn();
        }
    }
}
