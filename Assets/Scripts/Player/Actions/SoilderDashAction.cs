using UnityEngine;

namespace ProjectU
{
    public sealed class SoilderDashAction : PlayerAction
    {
        [SerializeField, Min(0f)] private float staminaCost = 25f;
        private PlayerStamina stamina;
        private PlayerStamina Stamina => stamina != null ? stamina : (stamina = GetComponent<PlayerStamina>());

        public override PlayerActionSlot Slot => PlayerActionSlot.Dash;
        public override PlayerActionStage Stage => PlayerActionStage.BeforeMotor;
        protected override bool WaitForRecoveryTick => true;
        public float StaminaCost => AttackData.FiniteAtLeast(staminaCost, 0f);

        protected override bool CheckRequirements(in PlayerActionContext request)
            => Motor != null && Motor.CanDashIgnoringActionLock &&
                (!request.HasDirection || (AttackData.IsFinite(request.Direction) &&
                    new Vector2(request.Direction.x, request.Direction.z).sqrMagnitude > 0.0001f)) &&
                (StaminaCost == 0f || (Stamina != null && Stamina.isActiveAndEnabled && Stamina.CanConsume(StaminaCost)));

        protected override PlayerActionResult ExecuteCore(in PlayerActionContext request)
        {
            Vector3 direction = request.HasDirection ? request.Direction :
                Combat != null ? Combat.DashDirection : transform.forward;
            // No callbacks occur until both movement and resource changes are committed.
            if (!Motor.BeginDashMovement(this, direction)) return PlayerActionResult.Failed;
            if (StaminaCost > 0f && !Stamina.TryConsumeWithoutNotification(StaminaCost))
            {
                Motor.StopDashMovement();
                return PlayerActionResult.Failed;
            }
            return PlayerActionResult.Running;
        }

        protected override void OnExecutionSucceeded()
        {
            Combat?.CancelReload();
            Motor.NotifyDashExecuted();
            if (StaminaCost > 0f) Stamina.PublishChanged();
        }

        protected override void Cleanup(PlayerActionCancelReason reason) => Motor?.StopDashMovement();
        protected override void OnDisable()
        {
            base.OnDisable();
            ClearActionLock();
        }
        protected override void OnValidate()
        {
            base.OnValidate();
            staminaCost = StaminaCost;
        }
    }
}
