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

        private CharacterController controller;
        private PlayerStat playerStat;
        private float verticalSpeed;
        private Vector3 moveDirection;
        private float facingYaw;
        private bool hasMoveCommand;
        private bool jumpRequested;

        public bool IsGrounded => controller != null && controller.isGrounded;
        public float VerticalSpeed => verticalSpeed;
        public float MoveSpeed => playerStat.MoveSpeed;
        public Vector3 HorizontalVelocity { get; private set; }

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
            if (isActiveAndEnabled)
                jumpRequested = true;
        }

        private void Update()
        {
            Vector3 velocity = moveDirection * MoveSpeed;
            bool shouldJump = jumpRequested;
            bool shouldRotate = hasMoveCommand;
            ClearCommands();
            if (Time.deltaTime <= 0f)
            {
                HorizontalVelocity = Vector3.zero;
                return;
            }

            if (shouldRotate)
                transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);

            if (controller.isGrounded && verticalSpeed < 0f)
                verticalSpeed = GroundStickSpeed;
            if (controller.isGrounded && shouldJump)
                verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);

            verticalSpeed -= gravity * Time.deltaTime;
            velocity.y = verticalSpeed;
            Vector3 previousPosition = transform.position;
            CollisionFlags collisions = controller.Move(velocity * Time.deltaTime);
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
            ClearCommands();
        }

        private void OnValidate()
        {
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = Mathf.Max(0.1f, gravity);
        }
    }
}
