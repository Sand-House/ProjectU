using UnityEngine;

namespace ProjectU
{
    [CreateAssetMenu(menuName = "ProjectU/Combat/Hitscan Attack")]
    public sealed class HitscanAttackData : AttackData
    {
        [SerializeField, Range(0f, 89f), Tooltip("Maximum spread angle in degrees. Zero disables spread.")]
        private float spreadAngle = 3f;
        [SerializeField, Min(1)] private int spreadStartShot = 4;
        [SerializeField, Min(0f)] private float spreadResetDelay = 0.5f;
        [SerializeField, Min(0)] private int enemyPenetrationCount;
        [SerializeField, Min(0.01f)] private float maxDistance = 100f;

        public float SpreadAngle => Mathf.Min(89f, FiniteAtLeast(spreadAngle, 0f));
        public int SpreadStartShot => Mathf.Max(1, spreadStartShot);
        public float SpreadResetDelay => FiniteAtLeast(spreadResetDelay, 0f);
        public int EnemyPenetrationCount => Mathf.Max(0, enemyPenetrationCount);
        public float MaxDistance => FiniteAtLeast(maxDistance, 0.01f);

        protected override void OnValidate()
        {
            base.OnValidate();
            spreadAngle = SpreadAngle;
            spreadStartShot = SpreadStartShot;
            spreadResetDelay = SpreadResetDelay;
            enemyPenetrationCount = EnemyPenetrationCount;
            maxDistance = MaxDistance;
        }
    }
}
