using UnityEngine;

namespace ProjectU
{
    public sealed class ProjectileAttack : Attack
    {
        [SerializeField] private ProjectileAttackData data;
        [SerializeField] private ProjectilePool pool;

        public override AttackData Data => data;
        public ProjectilePool Pool { get => pool; set => pool = value; }
        public Projectile LastProjectile { get; private set; }
        protected override bool IsConfigured => data != null && data.ProjectilePrefab != null && pool != null && pool.isActiveAndEnabled;

        protected override bool ExecuteAttack(Vector3 origin, Vector3 direction, in DamageContext context)
        {
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            if (right.sqrMagnitude < 0.0001f) right = transform.right;
            direction = Quaternion.AngleAxis(-data.LaunchAngle, right.normalized) * direction;
            var launch = new ProjectileLaunchData(context, data.PiercesEnemies, data.SizeMultiplier, data.ExplosionRadiusMultiplier);
            LastProjectile = pool.Spawn(data.ProjectilePrefab, Owner.gameObject, origin, direction, launch);
            return LastProjectile != null;
        }
    }
}
