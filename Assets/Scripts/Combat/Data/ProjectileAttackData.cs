using UnityEngine;

namespace ProjectU
{
    [CreateAssetMenu(menuName = "ProjectU/Combat/Projectile Attack")]
    public sealed class ProjectileAttackData : AttackDataBase
    {
        [SerializeField] private ProjectileBase projectilePrefab;
        [SerializeField] private bool piercesEnemies;
        [SerializeField, Min(0.01f)] private float sizeMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float explosionRadiusMultiplier = 1f;
        [SerializeField, Range(0f, 45f), Tooltip("Upward angle in degrees relative to the aim direction. Zero follows the aim direction.")]
        private float launchAngle;

        public ProjectileBase ProjectilePrefab => projectilePrefab;
        public bool PiercesEnemies => piercesEnemies;
        public float SizeMultiplier => FiniteAtLeast(sizeMultiplier, 0.01f);
        public float ExplosionRadiusMultiplier => FiniteAtLeast(explosionRadiusMultiplier, 0.01f);
        public float LaunchAngle => Mathf.Min(45f, FiniteAtLeast(launchAngle, 0f));

        protected override void OnValidate()
        {
            base.OnValidate();
            sizeMultiplier = SizeMultiplier;
            explosionRadiusMultiplier = ExplosionRadiusMultiplier;
            launchAngle = LaunchAngle;
        }
    }
}
