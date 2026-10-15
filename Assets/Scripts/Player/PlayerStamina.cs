using System;
using UnityEngine;

namespace ProjectU
{
    // Recover before Controller handles this frame's consumption request.
    [DefaultExecutionOrder(-75)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerStat))]
    public sealed class PlayerStamina : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("Starting stamina; also editable while playing. Clamped to PlayerStat.MaxStamina.")]
        private float current = 100f;
        [SerializeField, Min(0f), Tooltip("Game seconds to wait after the last successful consumption.")]
        private float recoveryDelay = 1f;
        [SerializeField, Min(0f), Tooltip("Stamina restored per game second after the recovery delay.")]
        private float recoveryRate = 20f;

        private PlayerStat playerStat;
        private float recoveryDelayRemaining;
        private float reportedCurrent;
        private float reportedMax;

        public float Current => Mathf.Clamp(current, 0f, Max);
        public float Max
        {
            get
            {
                if (playerStat == null)
                    playerStat = GetComponent<PlayerStat>();
                return playerStat.MaxStamina;
            }
        }

        public float RecoveryDelay
        {
            get => recoveryDelay;
            set => recoveryDelay = NonNegativeFinite(value);
        }

        public float RecoveryRate
        {
            get => recoveryRate;
            set => recoveryRate = NonNegativeFinite(value);
        }

        public float RecoveryDelayRemaining => recoveryDelayRemaining;

        // Arguments are the current amount and the current maximum, for a future HUD.
        public event Action<float, float> OnChanged;

        private void Awake()
        {
            playerStat = GetComponent<PlayerStat>();
            current = Mathf.Clamp(NonNegativeFinite(current), 0f, Max);
            reportedCurrent = current;
            reportedMax = Max;
        }

        private void Update()
        {
            Synchronize();
            float recoveryTime = Time.deltaTime;
            if (recoveryTime <= 0f)
                return;

            // Only the portion of this frame after the wait expires may recover.
            float waitingTime = Mathf.Min(recoveryDelayRemaining, recoveryTime);
            recoveryDelayRemaining -= waitingTime;
            recoveryTime -= waitingTime;
            if (recoveryTime > 0f)
                Recover(RecoveryRate * recoveryTime);
        }

        // Validation does not consume stamina or restart recovery.
        public bool CanConsume(float amount)
        {
            return IsPositiveFinite(amount) && Current >= amount;
        }

        public bool TryConsume(float amount)
        {
            if (!isActiveAndEnabled || !CanConsume(amount))
                return false;

            current = Current - amount;
            recoveryDelayRemaining = RecoveryDelay;
            NotifyChanged();
            return true;
        }

        // Explicit recovery (e.g. a pickup) bypasses the wait without resetting it.
        // Returns the amount actually restored; automatic recovery calls this too.
        public float Recover(float amount)
        {
            if (!IsPositiveFinite(amount))
                return 0f;

            float previous = Current;
            current = Mathf.Min(Max, previous + amount);
            NotifyChanged();
            return current - previous;
        }

        private void Synchronize()
        {
            // A larger maximum does not refill stamina; a smaller one clamps it.
            current = Mathf.Clamp(NonNegativeFinite(current), 0f, Max);
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            float maximum = Max;
            if (reportedCurrent == current && reportedMax == maximum)
                return;

            reportedCurrent = current;
            reportedMax = maximum;
            OnChanged?.Invoke(current, maximum);
        }

        private void OnValidate()
        {
            current = NonNegativeFinite(current);
            RecoveryDelay = recoveryDelay;
            RecoveryRate = recoveryRate;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsInfinity(value);
        }

        private static float NonNegativeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }
    }
}
