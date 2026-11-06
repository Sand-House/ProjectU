using System;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerStat))]
    public sealed class PlayerMotor : MonoBehaviour, IPlayerStageSubscriber
    {
        private const float GroundStickSpeed = -2f;

        [Header("Movement")]
        [SerializeField, Min(0f), Tooltip("Jump height in meters.")]
        private float jumpHeight = 1.5f;
        [SerializeField, Min(0.1f), Tooltip("Downward acceleration in meters per second squared.")]
        private float gravity = 25f;

        [Header("Dash")]
        [SerializeField, Min(0.01f), Tooltip("Game seconds used to travel the dash distance.")]
        private float dashDuration = 0.2f;

        private CharacterController controller;
        private PlayerStat playerStat;
        private Combatant combatant;
        private float verticalSpeed;
        private Vector3 moveDirection;
        private float facingYaw;
        private bool hasMoveCommand;
        private bool jumpRequested;
        private Vector3 dashDirection;
        private float dashSpeed;
        private float dashRemaining;
        private PlayerAction movementAction;
        private PlayerCombat combat;
        private PlayerStageSubscription stageEvents;
        private PlayerStageSubscription StageEvents => stageEvents ?? (stageEvents = new PlayerStageSubscription(
            this, HandleStageStateChanged, HandleGameplayEnabledChanged));
        void IPlayerStageSubscriber.BindStageEvents(GameManager game) => StageEvents.Bind(game);
        void IPlayerStageSubscriber.UnbindStageEvents(GameManager game) => stageEvents?.Unbind(game);
        private void OnEnable() => stageEvents?.Enable();
        private void HandleStageStateChanged(StageState state)
        {
            if (StageEvents.IsCurrent(state) && state != StageState.OnStage) ResetMovementState();
        }
        private void HandleGameplayEnabledChanged(bool enabled) { if (!enabled) ResetMovementState(); }
        private PlayerAction DashAction
        {
            get
            {
                if (combat == null) combat = GetComponent<PlayerCombat>();
                PlayerAction bound = combat != null ? combat.GetAction(PlayerActionSlot.Dash) : null;
                if (bound != null) return bound;
                foreach (PlayerAction action in GetComponents<PlayerAction>())
                    if (action.Slot == PlayerActionSlot.Dash) return action;
                return null;
            }
        }

        public bool IsGrounded => controller != null && controller.isGrounded;
        public float VerticalSpeed => verticalSpeed;
        public float MoveSpeed => playerStat.MoveSpeed;
        public Vector3 HorizontalVelocity { get; private set; }
        public bool IsDashing => dashRemaining > 0f;
        public bool IsDashPostDelay => DashAction != null && DashAction.Phase == ActionPhase.Recovery;
        // Keep every consumer blocked on the last frame containing dash/recovery time.
        public bool IsDashActionLocked => DashAction != null && DashAction.Blocks(PlayerActionSlot.PrimaryFire);
        public bool CanPerformActions => combatant == null || !combatant.IsActionDelayLocked;
        public bool CanMove => combatant == null ||
            (!combatant.IsGameplayBlocked && !combatant.IsMovementLocked);
        // Jump requests ignore attack/dash recovery locks. Grounding is checked when moving.
        public bool CanJump => isActiveAndEnabled && controller != null && controller.enabled &&
            Time.timeScale > 0f && Time.deltaTime > 0f && !IsDashing && CanMove;
        public bool CanDash => DashAction != null && DashAction.CanStart;
        internal bool CanDashIgnoringActionLock => isActiveAndEnabled && controller != null && controller.enabled &&
            Time.timeScale > 0f && Time.deltaTime > 0f && IsPositiveFinite(playerStat.DashDistance) &&
            IsPositiveFinite(dashDuration) && IsPositiveFinite(playerStat.DashDistance / dashDuration);

        // Raised once after a dash has successfully entered its movement/action-lock state.
        public event Action OnDashExecuted;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            playerStat = GetComponent<PlayerStat>();
            combatant = GetComponent<Combatant>();
        }

        // Commands are world-space requests, valid for one Motor update only.
        public void SetMoveCommand(Vector3 worldDirection, float yaw)
        {
            if (!isActiveAndEnabled)
                return;
            worldDirection.y = 0f;
            moveDirection = Vector3.ClampMagnitude(worldDirection, 1f);
            facingYaw = yaw;
            hasMoveCommand = true;
        }

        public void RequestJump()
        {
            if (CanJump)
                jumpRequested = true;
        }

        // The caller checks action/resource availability before requesting movement.
        // Direction and settings are captured once; looking or steering cannot bend a dash.
        public bool TryDash(Vector3 worldDirection)
            => DashAction != null && DashAction.TryStart(ActionContext.Move(worldDirection));

        internal bool BeginDashMovement(PlayerAction action, Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (action == null || !action.IsExecuting || action.gameObject != gameObject ||
                IsDashing || !CanDashIgnoringActionLock || !IsPositiveFinite(worldDirection.sqrMagnitude))
                return false;

            dashDirection = worldDirection.normalized;
            dashSpeed = playerStat.DashDistance / dashDuration;
            dashRemaining = dashDuration;
            movementAction = action;
            jumpRequested = false;
            return true;
        }

        internal void NotifyDashExecuted() => OnDashExecuted?.Invoke();
        internal void StopDashMovement() => dashRemaining = 0f;

        private void Update()
        {
            DashAction?.Process(PlayerActionStage.BeforeMotor);
            Vector3 velocity = CanMove ? moveDirection * MoveSpeed : Vector3.zero;
            bool shouldJump = jumpRequested && CanJump;
            bool shouldRotate = hasMoveCommand;
            ClearCommands();
            if (Time.deltaTime <= 0f)
            {
                HorizontalVelocity = Vector3.zero;
                return;
            }

            if (shouldRotate)
                transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);

            bool wasDashing = IsDashing;
            float dashTime = Mathf.Min(dashRemaining, Time.deltaTime);
            Vector3 displacement = dashDirection * (dashSpeed * dashTime) + velocity * (Time.deltaTime - dashTime);
            dashRemaining = Mathf.Max(0f, dashRemaining - dashTime);

            if (controller.isGrounded && verticalSpeed < 0f)
                verticalSpeed = GroundStickSpeed;
            if (controller.isGrounded && shouldJump)
                verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);

            verticalSpeed -= gravity * Time.deltaTime;
            displacement.y = verticalSpeed * Time.deltaTime;
            Vector3 previousPosition = transform.position;
            CollisionFlags collisions = controller.Move(displacement);
            Vector3 actualVelocity = (transform.position - previousPosition) / Time.deltaTime;
            HorizontalVelocity = new Vector3(actualVelocity.x, 0f, actualVelocity.z);
            if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
            if ((collisions & CollisionFlags.Below) != 0 && verticalSpeed < 0f)
                verticalSpeed = GroundStickSpeed;
            if (wasDashing && !IsDashing)
                movementAction?.FinishFromMovement(Time.timeAsDouble - (Time.deltaTime - dashTime));
        }

        private void ClearCommands()
        {
            moveDirection = Vector3.zero;
            hasMoveCommand = false;
            jumpRequested = false;
        }

        public void ResetMovementState()
        {
            verticalSpeed = 0f;
            HorizontalVelocity = Vector3.zero;
            dashRemaining = 0f;
            movementAction?.CancelFromDependency();
            PlayerAction dash = DashAction;
            if (dash != movementAction) dash?.CancelFromDependency();
            ClearCommands();
        }

        private void OnDisable() { stageEvents?.Disable(); ResetMovementState(); }

        private void OnValidate()
        {
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = Mathf.Max(0.1f, gravity);
            dashDuration = AttackData.FiniteAtLeast(dashDuration, 0.01f);
        }

        private static bool IsPositiveFinite(float value) => value > 0f && !float.IsInfinity(value);

    }
}
