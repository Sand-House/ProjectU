using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectU
{
    [DefaultExecutionOrder(-150), DisallowMultipleComponent]
    [RequireComponent(typeof(Mortal))]
    public sealed class PlayerStat : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("Attack power multiplier. 1 means the weapon's base damage.")]
        [FormerlySerializedAs("attackPowerMultiplier")]
        private float attackMultiplier = 1f;
        [SerializeField, Min(1), Tooltip("Starting HP applied to Mortal once in Awake. Changing this during play does not refill HP.")]
        private int maxHealth = 100;
        [SerializeField, Min(0f)] private float maxStamina = 100f;
        [SerializeField, Min(0.01f), Tooltip("Attack speed multiplier. 1 means the weapon's base fire rate.")]
        private float attackSpeed = 1f;
        [SerializeField, Min(0f), Tooltip("Horizontal movement speed in meters per second.")]
        private float moveSpeed = 6f;
        [SerializeField, Min(1)] private int maxMagazine = 30;
        [SerializeField, Min(0.01f), Tooltip("Character's base reload duration in game seconds.")]
        private float reloadDuration = 3.3f;
        [SerializeField, Min(0.01f), Tooltip("Reload speed multiplier. Reload time is duration divided by this value.")]
        private float reloadSpeedMultiplier = 1f;
        [SerializeField, Min(0.01f), Tooltip("Dash distance in meters, before collision limits.")]
        private float dashDistance = 5f;

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

        public float ReloadDuration => reloadDuration;

        public float ReloadSpeedMultiplier
        {
            get => reloadSpeedMultiplier;
            set => reloadSpeedMultiplier = ClampFinite(value, 0.01f);
        }

        public float DashDistance
        {
            get => dashDistance;
            set => dashDistance = ClampFinite(value, 0.01f);
        }

        private void Awake()
        {
            // A repeated or late initialization must preserve existing damage and death.
            GetComponent<Mortal>().TryInitializeHealth(MaxHealth);
        }

        private void OnValidate()
        {
            AttackMultiplier = attackMultiplier;
            MaxHealth = maxHealth;
            MaxStamina = maxStamina;
            AttackSpeed = attackSpeed;
            MoveSpeed = moveSpeed;
            MaxMagazine = maxMagazine;
            reloadDuration = ClampFinite(reloadDuration, 0.01f);
            ReloadSpeedMultiplier = reloadSpeedMultiplier;
            DashDistance = dashDistance;
        }

        private static float ClampFinite(float value, float minimum)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? minimum : Mathf.Max(minimum, value);
        }
    }
}
