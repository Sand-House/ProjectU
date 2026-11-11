using UnityEngine;

namespace ProjectU
{
    // Optional stance feature. The requesting action owns its lifetime, independently of its input slot.
    [DisallowMultipleComponent]
    public sealed class PlayerADS : MonoBehaviour, IMovementSpeedModifier, IInteractionBlocker
    {
        [SerializeField, Min(0f), Tooltip("Seconds for each camera transition, including a reversed transition. Zero switches immediately.")]
        private float transitionTime = 0.25f;
        [SerializeField, Range(0f, 1f), Tooltip("Fraction of normal movement speed while aiming. 0.5 means 50% speed.")]
        private float moveSpeedMultiplier = 0.5f;
        [SerializeField, Range(0f, 1f), Tooltip("Fraction of the primary fire spread angle while aiming. 0.5 means half the spread.")]
        private float spreadAngleMultiplier = 0.5f;
        private PlayerAction source;

        public bool IsAiming => isActiveAndEnabled && source != null && source.isActiveAndEnabled && source.IsActive &&
            source.Owner != null && source.Owner.IsAlive && !source.Owner.IsGameplayBlocked;
        public float TransitionTime => AttackData.FiniteAtLeast(transitionTime, 0f);
        public float MoveSpeedMultiplier => Mathf.Clamp01(AttackData.FiniteAtLeast(moveSpeedMultiplier, 0f));
        public float SpreadAngleMultiplier => Mathf.Clamp01(AttackData.FiniteAtLeast(spreadAngleMultiplier, 0f));
        public float MovementSpeedMultiplier => IsAiming ? MoveSpeedMultiplier : 1f;
        public bool BlocksInteraction => IsAiming;

        public bool CanBegin(PlayerAction action) => isActiveAndEnabled && action != null &&
            action.gameObject == gameObject && (source == null || !source.IsActive || source == action);

        public bool TryBegin(PlayerAction action)
        {
            if (!CanBegin(action) || !action.isActiveAndEnabled || !action.IsActive) return false;
            source = action;
            return true;
        }

        public void End(PlayerAction action)
        {
            if (source == action) source = null;
        }

        private void OnDisable()
        {
            PlayerAction previous = source;
            source = null;
            if (previous != null) previous.CancelFromDependency();
        }

        private void OnValidate()
        {
            transitionTime = TransitionTime;
            moveSpeedMultiplier = MoveSpeedMultiplier;
            spreadAngleMultiplier = SpreadAngleMultiplier;
        }
    }
}
