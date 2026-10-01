using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0f), Tooltip("Horizontal movement speed in meters per second.")]
        private float moveSpeed = 6f;
        [SerializeField, Min(0f), Tooltip("Jump height in meters.")]
        private float jumpHeight = 1.5f;
        [SerializeField, Min(0.1f), Tooltip("Downward acceleration in meters per second squared.")]
        private float gravity = 25f;

        private CharacterController controller;
        private float verticalSpeed;
        private Vector3 moveDirection;
        private float facingYaw;
        private bool hasMoveCommand;
        private bool jumpRequested;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
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
            if (isActiveAndEnabled)
                jumpRequested = true;
        }

        private void Update()
        {
            Vector3 velocity = moveDirection * moveSpeed;
            bool shouldJump = jumpRequested;
            bool shouldRotate = hasMoveCommand;
            ClearCommands();
            if (Time.deltaTime <= 0f)
                return;

            if (shouldRotate)
                transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);

            if (controller.isGrounded && verticalSpeed < 0f)
                verticalSpeed = -2f;
            if (controller.isGrounded && shouldJump)
                verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);

            verticalSpeed -= gravity * Time.deltaTime;
            velocity.y = verticalSpeed;
            CollisionFlags collisions = controller.Move(velocity * Time.deltaTime);
            if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
            if ((collisions & CollisionFlags.Below) != 0 && verticalSpeed < 0f)
                verticalSpeed = -2f;
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
            ClearCommands();
        }

        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0f, moveSpeed);
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = Mathf.Max(0.1f, gravity);
        }
    }
}
