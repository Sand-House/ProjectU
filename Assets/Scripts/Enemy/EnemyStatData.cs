using UnityEngine;

namespace ProjectU
{
    [CreateAssetMenu(fileName = "EnemyStatData", menuName = "ProjectU/Enemy/Stat Data")]
    public sealed class EnemyStatData : ScriptableObject
    {
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField, Min(0f), Tooltip("Horizontal movement speed in meters per second.")]
        private float moveSpeed = 3.5f;
        [SerializeField, Min(0f), Tooltip("Multiplies AttackData damage and the action's damage multiplier.")]
        private float attackMultiplier = 1f;
        [SerializeField, Min(0.01f), Tooltip("Divides attack windup, recovery and cooldown. 1 uses the action's base timings.")]
        private float attackSpeed = 1f;

        public int MaxHealth => Mathf.Max(1, maxHealth);
        public float MoveSpeed => AttackData.FiniteAtLeast(moveSpeed, 0f);
        public float AttackMultiplier => AttackData.FiniteAtLeast(attackMultiplier, 0f);
        public float AttackSpeed => AttackData.FiniteAtLeast(attackSpeed, 0.01f);

        private void OnValidate()
        {
            maxHealth = MaxHealth;
            moveSpeed = MoveSpeed;
            attackMultiplier = AttackMultiplier;
            attackSpeed = AttackSpeed;
        }
    }
}
