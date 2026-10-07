using UnityEngine;

namespace ProjectU
{
    public sealed class ProjectileBullet : Projectile
    {
        // TODO: 피해 시스템을 구현할 때 연결한다.
        // [SerializeField, Min(0f)] private float damage = 20f;

        protected override void OnImpact(Collision collision)
        {
            // var target = collision.collider.GetComponentInParent<IDamageable>();
            // target?.TakeDamage(damage, Owner);
            Despawn();
        }
    }
}
