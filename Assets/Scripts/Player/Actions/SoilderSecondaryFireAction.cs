using UnityEngine;

namespace ProjectU
{
    public sealed class SoilderSecondaryFireAction : PlayerAction, IPlayerAimAction
    {
        [Header("Aim Down Sights")]
        [SerializeField, Min(0f), Tooltip("Seconds for each camera transition, including a reversed transition. Zero switches immediately.")]
        private float transitionTime = 0.25f;
        [SerializeField, Range(0f, 1f), Tooltip("Fraction of normal movement speed while aiming. 0.5 means 50% speed.")]
        private float moveSpeedMultiplier = 0.5f;
        [SerializeField, Range(0f, 1f), Tooltip("Fraction of the primary fire spread angle while aiming. 0.5 means half the spread.")]
        private float spreadAngleMultiplier = 0.5f;

        public override PlayerActionSlot Slot => PlayerActionSlot.SecondaryFire;
        public override PlayerActionStage Stage => PlayerActionStage.BeforeMotor;
        public override bool RequiresHeldInput => true;
        protected override bool UsesDelayLock => false;
        public bool IsAiming => isActiveAndEnabled && IsActive && Owner != null && Owner.IsAlive &&
            !Owner.IsGameplayBlocked && Combat != null && Combat.isActiveAndEnabled;
        public float TransitionTime => AttackData.FiniteAtLeast(transitionTime, 0f);
        public float MoveSpeedMultiplier => Mathf.Clamp01(AttackData.FiniteAtLeast(moveSpeedMultiplier, 0f));
        public float SpreadAngleMultiplier => Mathf.Clamp01(AttackData.FiniteAtLeast(spreadAngleMultiplier, 0f));

        // This is a persistent stance, not an attack delay that buffering may bypass.
        protected override bool BlocksCore(PlayerActionSlot slot) => slot != PlayerActionSlot.PrimaryFire;
        protected override bool CheckRequirements(in ActionContext request)
            => Combat != null && Combat.isActiveAndEnabled && Motor != null && Motor.isActiveAndEnabled && !Motor.IsDashing;
        protected override ActionResult ExecuteCore(in ActionContext request) => ActionResult.Running;
        protected override void TickActive(float deltaTime)
        {
            if (!CheckRequirements(default)) Cancel(ActionCancelReason.Disabled);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            transitionTime = TransitionTime;
            moveSpeedMultiplier = MoveSpeedMultiplier;
            spreadAngleMultiplier = SpreadAngleMultiplier;
        }
    }
}

