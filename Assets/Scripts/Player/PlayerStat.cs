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
        [SerializeField, Min(1), Tooltip("Base maximum HP used to initialize Mortal when the player spawns.")]
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

        private BaseStats? initializedBaseStats;
        private BaseStats Base => initializedBaseStats ?? new BaseStats(this);

        public float AttackMultiplierBonus { get; set; }
        public int MaxHealthBonus { get; set; }
        public float MaxStaminaBonus { get; set; }
        public float AttackSpeedBonus { get; set; }
        public float MoveSpeedBonus { get; set; }
        public int MaxMagazineBonus { get; set; }
        public float ReloadDurationBonus { get; set; }
        public float ReloadSpeedMultiplierBonus { get; set; }
        public float DashDistanceBonus { get; set; }

        public float AttackMultiplier => AddBonus(Base.AttackMultiplier, AttackMultiplierBonus, 0f);
        public int MaxHealth => AddBonus(Base.MaxHealth, MaxHealthBonus);
        public float MaxStamina => AddBonus(Base.MaxStamina, MaxStaminaBonus, 0f);
        public float AttackSpeed => AddBonus(Base.AttackSpeed, AttackSpeedBonus, 0.01f);
        public float MoveSpeed => AddBonus(Base.MoveSpeed, MoveSpeedBonus, 0f);
        public int MaxMagazine => AddBonus(Base.MaxMagazine, MaxMagazineBonus);
        public float ReloadDuration => AddBonus(Base.ReloadDuration, ReloadDurationBonus, 0.01f);
        public float ReloadSpeedMultiplier => AddBonus(Base.ReloadSpeedMultiplier, ReloadSpeedMultiplierBonus, 0.01f);
        public float DashDistance => AddBonus(Base.DashDistance, DashDistanceBonus, 0.01f);

        private void Awake()
        {
            initializedBaseStats ??= new BaseStats(this);
            // A repeated or late initialization must preserve existing damage and death.
            GetComponent<Mortal>().TryInitializeHealth(MaxHealth);
        }

        private void OnValidate()
        {
            var settings = new BaseStats(this);
            attackMultiplier = settings.AttackMultiplier;
            maxHealth = settings.MaxHealth;
            maxStamina = settings.MaxStamina;
            attackSpeedMultiplier = settings.AttackSpeed;
            moveSpeed = settings.MoveSpeed;
            maxMagazine = settings.MaxMagazine;
            reloadDuration = settings.ReloadDuration;
            reloadSpeedMultiplier = settings.ReloadSpeedMultiplier;
            dashDistance = settings.DashDistance;
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

        private readonly struct BaseStats
        {
            public readonly float AttackMultiplier;
            public readonly int MaxHealth;
            public readonly float MaxStamina;
            public readonly float AttackSpeed;
            public readonly float MoveSpeed;
            public readonly int MaxMagazine;
            public readonly float ReloadDuration;
            public readonly float ReloadSpeedMultiplier;
            public readonly float DashDistance;

            public BaseStats(PlayerStat settings)
            {
                AttackMultiplier = ClampFinite(settings.attackMultiplier, 0f);
                MaxHealth = Mathf.Max(1, settings.maxHealth);
                MaxStamina = ClampFinite(settings.maxStamina, 0f);
                AttackSpeed = ClampFinite(settings.attackSpeedMultiplier, 0.01f);
                MoveSpeed = ClampFinite(settings.moveSpeed, 0f);
                MaxMagazine = Mathf.Max(1, settings.maxMagazine);
                ReloadDuration = ClampFinite(settings.reloadDuration, 0.01f);
                ReloadSpeedMultiplier = ClampFinite(settings.reloadSpeedMultiplier, 0.01f);
                DashDistance = ClampFinite(settings.dashDistance, 0.01f);
            }
        }
    }
}
