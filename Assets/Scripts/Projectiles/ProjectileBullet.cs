using UnityEngine;

namespace ProjectU
{
    public sealed class ProjectileBullet : ProjectileBase
    {
        protected override void OnImpact(Collision collision)
        {
            TryDamage(collision.collider);
            Despawn();
        }

        protected override void OnSweptImpact(Collider collider)
        {
            bool isCharacter = Hitbox.TryResolve(collider, out _, out _);
            TryDamage(collider);
            if (!isCharacter || !AttackInfo.PiercesEnemies) Despawn();
        }
    }
}
