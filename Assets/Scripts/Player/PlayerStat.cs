using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectU
{
    [DisallowMultipleComponent]
    public sealed class PlayerStat : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("Attack power multiplier. 1 means the weapon's base damage.")]
        [FormerlySerializedAs("attackPowerMultiplier")]
        private float attackMultiplier = 1f;
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField, Min(0f)] private float maxStamina = 100f;
        [SerializeField, Min(0.01f), Tooltip("Attack speed multiplier. 1 means the weapon's base fire rate.")]
        private float attackSpeed = 1f;
        [SerializeField, Min(0f), Tooltip("Horizontal movement speed in meters per second.")]
        private float moveSpeed = 6f;
        [SerializeField, Min(1)] private int maxMagazine = 30;

        public float AttackMultiplier
        {
            get => attackMultiplier;
            set => attackMultiplier = ClampFinite(value, 0f);
        }

        public int MaxHealth
        {
            get => maxHealth;
            set => maxHealth = Mathf.Max(1, value);
        }

        public float MaxStamina
        {
            get => maxStamina;
            set => maxStamina = ClampFinite(value, 0f);
        }

        public float AttackSpeed
        {
            get => attackSpeed;
            set => attackSpeed = ClampFinite(value, 0.01f);
        }

        public float MoveSpeed
        {
            get => moveSpeed;
            set => moveSpeed = ClampFinite(value, 0f);
        }

        public int MaxMagazine
        {
            get => maxMagazine;
            set => maxMagazine = Mathf.Max(1, value);
        }

        private void OnValidate()
        {
            AttackMultiplier = attackMultiplier;
            MaxHealth = maxHealth;
            MaxStamina = maxStamina;
            AttackSpeed = attackSpeed;
            MoveSpeed = moveSpeed;
            MaxMagazine = maxMagazine;
        }

        private static float ClampFinite(float value, float minimum)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? minimum : Mathf.Max(minimum, value);
        }
    }
}
