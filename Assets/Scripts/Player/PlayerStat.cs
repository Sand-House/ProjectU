using System;
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
        private float attackSpeedMultiplier = 1f;
        [SerializeField, Min(0f), Tooltip("Horizontal movement speed in meters per second.")]
        private float moveSpeed = 6f;
        [SerializeField, Min(1)] private int maxMagazine = 30;
        [SerializeField, Min(0.01f), Tooltip("Character's base reload duration in game seconds.")]
        private float reloadDuration = 3.3f;
        [SerializeField, Min(0.01f), Tooltip("Reload speed multiplier. Reload time is duration divided by this value.")]
        private float reloadSpeedMultiplier = 1f;
        [SerializeField, Min(0.01f), Tooltip("Dash distance in meters, before collision limits.")]
        private float dashDistance = 5f;

        public float AttackMultiplierBonus { get; set; }
        public int MaxHealthBonus { get; set; }
        public float MaxStaminaBonus { get; set; }
        public float AttackSpeedBonus { get; set; }
        public float MoveSpeedBonus { get; set; }
        public int MaxMagazineBonus { get; set; }
        public float ReloadDurationBonus { get; set; }
        public float ReloadSpeedMultiplierBonus { get; set; }
        public float DashDistanceBonus { get; set; }

        // Existing setters edit base values. Each getter includes its runtime bonus.
        public float AttackMultiplier
        {
            get => AddBonus(attackMultiplier, AttackMultiplierBonus, 0f);
            set => attackMultiplier = ClampFinite(value, 0f);
        }

        public int MaxHealth
        {
            get => AddBonus(maxHealth, MaxHealthBonus);
            set => maxHealth = Mathf.Max(1, value);
        }

        public float MaxStamina
        {
            get => AddBonus(maxStamina, MaxStaminaBonus, 0f);
            set => maxStamina = ClampFinite(value, 0f);
        }

        public float AttackSpeed
        {
            get => AddBonus(attackSpeedMultiplier, AttackSpeedBonus, 0.01f);
            set => attackSpeedMultiplier = ClampFinite(value, 0.01f);
        }

        public float MoveSpeed
        {
            get => AddBonus(moveSpeed, MoveSpeedBonus, 0f);
            set => moveSpeed = ClampFinite(value, 0f);
        }

        public int MaxMagazine
        {
            get => AddBonus(maxMagazine, MaxMagazineBonus);
            set => maxMagazine = Mathf.Max(1, value);
        }

        public float ReloadDuration => AddBonus(reloadDuration, ReloadDurationBonus, 0.01f);

        public float ReloadSpeedMultiplier
        {
            get => AddBonus(reloadSpeedMultiplier, ReloadSpeedMultiplierBonus, 0.01f);
            set => reloadSpeedMultiplier = ClampFinite(value, 0.01f);
        }

        public float DashDistance
        {
            get => AddBonus(dashDistance, DashDistanceBonus, 0.01f);
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
            AttackSpeed = attackSpeedMultiplier;
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

        private static float AddBonus(float baseValue, float bonus, float minimum)
        {
            if (float.IsNaN(bonus) || float.IsInfinity(bonus))
                return baseValue;

            return (float)Math.Clamp((double)baseValue + bonus, minimum, float.MaxValue);
        }

        private static int AddBonus(int baseValue, int bonus)
        {
            return (int)Math.Clamp((long)baseValue + bonus, 1L, int.MaxValue);
        }
    }
}
