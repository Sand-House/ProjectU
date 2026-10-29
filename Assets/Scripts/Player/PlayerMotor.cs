using System;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerStat))]
    public sealed class PlayerMotor : MonoBehaviour
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
        [SerializeField, Min(0f), Tooltip("Game seconds after the dash during which only movement is allowed.")]
        private float dashPostDelay = 0.3f;

        private CharacterController controller;
        private PlayerStat playerStat;
        private float verticalSpeed;
        private Vector3 moveDirection;
        private float facingYaw;
        private bool hasMoveCommand;
        private bool jumpRequested;
        private Vector3 dashDirection;
        private float dashSpeed;
        private float dashRemaining;
        private float dashPostDelayRemaining;
        private int dashBlockedFrame = -1;

        public bool IsGrounded => controller != null && controller.isGrounded;
        public float VerticalSpeed => verticalSpeed;
        public float MoveSpeed => playerStat.MoveSpeed;
        public Vector3 HorizontalVelocity { get; private set; }
        public bool IsDashing => dashRemaining > 0f;
        public bool IsDashPostDelay => !IsDashing && dashPostDelayRemaining > 0f;
        // Keep every consumer blocked on the last frame containing dash/recovery time.
        public bool CanPerformActions => !IsDashing && !IsDashPostDelay && dashBlockedFrame != Time.frameCount;
        public bool CanDash => isActiveAndEnabled && controller != null && controller.enabled &&
            Time.deltaTime > 0f && CanPerformActions && IsPositiveFinite(playerStat.DashDistance) &&
            IsPositiveFinite(dashDuration) && IsPositiveFinite(playerStat.DashDistance / dashDuration);

        // Raised once after a dash has successfully entered its movement/action-lock state.
        public event Action OnDashExecuted;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            playerStat = GetComponent<PlayerStat>();
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
            if (isActiveAndEnabled && CanPerformActions)
                jumpRequested = true;
        }

        // The caller checks action/resource availability before requesting movement.
        // Direction and settings are captured once; looking or steering cannot bend a dash.
        public bool TryDash(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (!CanDash || !IsPositiveFinite(worldDirection.sqrMagnitude))
                return false;

            dashDirection = worldDirection.normalized;
            dashSpeed = playerStat.DashDistance / dashDuration;
            dashRemaining = dashDuration;
            dashPostDelayRemaining = NonNegativeFinite(dashPostDelay);
            dashBlockedFrame = Time.frameCount;
            jumpRequested = false;
            OnDashExecuted?.Invoke();
            return true;
        }

        private void Update()
        {
            Vector3 velocity = moveDirection * MoveSpeed;
            bool shouldJump = jumpRequested && CanPerformActions;
            bool shouldRotate = hasMoveCommand;
            ClearCommands();
            if (Time.deltaTime <= 0f)
            {
                HorizontalVelocity = Vector3.zero;
                return;
            }

            if (shouldRotate)
                transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);

            if (!CanPerformActions)
                dashBlockedFrame = Time.frameCount;
            float dashTime = Mathf.Min(dashRemaining, Time.deltaTime);
            Vector3 displacement = dashDirection * (dashSpeed * dashTime) + velocity * (Time.deltaTime - dashTime);
            dashRemaining = Mathf.Max(0f, dashRemaining - dashTime);
            dashPostDelayRemaining = Mathf.Max(0f, dashPostDelayRemaining - (Time.deltaTime - dashTime));

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
        }

        private void ClearCommands()
        {
            moveDirection = Vector3.zero;
            hasMoveCommand = false;
            jumpRequested = false;
        }

        private void OnDisable()
        {
            verticalSpeed = 0f;
            HorizontalVelocity = Vector3.zero;
            dashRemaining = 0f;
            dashPostDelayRemaining = 0f;
            dashBlockedFrame = -1;
            ClearCommands();
        }

        private void OnValidate()
        {
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = Mathf.Max(0.1f, gravity);
            dashDuration = Mathf.Max(0.01f, NonNegativeFinite(dashDuration));
            dashPostDelay = NonNegativeFinite(dashPostDelay);
        }

        private static bool IsPositiveFinite(float value) => value > 0f && !float.IsInfinity(value);

        private static float NonNegativeFinite(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
    }
}
