using UnityEngine;

namespace ProjectU
{
    public abstract class AttackData : ScriptableObject
    {
        [SerializeField, Min(0f)] private float damage = 20f;
        [SerializeField] private bool canCritical;
        [SerializeField, Min(1f)] private float criticalMultiplier = 2f;
        [SerializeField, Min(0f)] private float windupTime;
        [SerializeField, Min(0f)] private float recoveryTime = 0.1f;
        [SerializeField, Min(0f)] private float cooldown = 0.2f;
        [SerializeField, Tooltip("Allow player input to request repeated attacks while held. Attack timing still applies.")]
        private bool repeatWhileHeld;
        [SerializeField] private AttackTargetRule targetRule = AttackTargetRule.Hostile;
        [SerializeField, Tooltip("Include character hitboxes and blocking world geometry. Includes Ignore Raycast by default.")]
        private LayerMask hitMask = ~0;

        public float Damage => FiniteAtLeast(damage, 0f);
        public bool CanCritical => canCritical;
        public float CriticalMultiplier => FiniteAtLeast(criticalMultiplier, 1f);
        public float WindupTime => FiniteAtLeast(windupTime, 0f);
        public float RecoveryTime => FiniteAtLeast(recoveryTime, 0f);
        public float Cooldown => FiniteAtLeast(cooldown, 0f);
        public bool RepeatWhileHeld => repeatWhileHeld;
        public AttackTargetRule TargetRule => targetRule;
        public LayerMask HitMask => hitMask;

        protected virtual void OnValidate()
        {
            damage = Damage;
            criticalMultiplier = CriticalMultiplier;
            windupTime = WindupTime;
            recoveryTime = RecoveryTime;
            cooldown = Cooldown;
        }

        internal static float FiniteAtLeast(float value, float minimum)
            => float.IsNaN(value) || float.IsInfinity(value) ? minimum : Mathf.Max(minimum, value);

        internal static bool IsFinite(Vector3 value)
            => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
