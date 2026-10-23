using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader), typeof(PlayerAttackController))]
    public sealed class PlayerAnimationController : MonoBehaviour
    {
        private static readonly int MoveX = Animator.StringToHash("MoveX");
        private static readonly int MoveZ = Animator.StringToHash("MoveZ");
        private static readonly int Grounded = Animator.StringToHash("Grounded");
        private static readonly int VerticalSpeed = Animator.StringToHash("VerticalSpeed");
        private static readonly int Fire = Animator.StringToHash("Fire");
        private static readonly int Reload = Animator.StringToHash("Reload");

        [SerializeField] private Animator animator;
        [SerializeField, Min(0f)] private float movementDampTime = 0.08f;
        [SerializeField, Min(0.01f), Tooltip("Visual reload duration; ammunition is not implemented yet.")]
        private float reloadPreviewDuration = 3.3f;

        private PlayerMotor motor;
        private PlayerInputReader input;
        private PlayerAttackController launcher;
        private float reloadRemaining;
        private bool animatorPaused;

        public bool IsReloading => reloadRemaining > 0f;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            launcher = GetComponent<PlayerAttackController>();
            if (animator == null || !animator.isHuman || animator.runtimeAnimatorController == null)
            {
                Debug.LogError("PlayerAnimationController requires a configured Humanoid Animator.", this);
                enabled = false;
                return;
            }
            animator.applyRootMotion = false;
            animator.keepAnimatorStateOnDisable = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void OnEnable()
        {
            if (launcher != null)
                launcher.AttackExecuted += OnAttackExecuted;
        }

        private void Update()
        {
            if (Time.deltaTime <= 0f)
            {
                // An enabled Animator can write its un-aimed pose even at zero delta.
                // Freeze its evaluation as well as the late procedural corrections.
                if (animator.enabled)
                {
                    animator.enabled = false;
                    animatorPaused = true;
                }
                return;
            }
            if (animatorPaused)
            {
                animator.enabled = true;
                animatorPaused = false;
            }
            Vector3 localVelocity = transform.InverseTransformDirection(motor.HorizontalVelocity);
            float speed = Mathf.Max(0.001f, motor.MoveSpeed);
            animator.SetFloat(MoveX, localVelocity.x / speed, movementDampTime, Time.deltaTime);
            animator.SetFloat(MoveZ, localVelocity.z / speed, movementDampTime, Time.deltaTime);
            animator.SetBool(Grounded, motor.IsGrounded);
            animator.SetFloat(VerticalSpeed, motor.VerticalSpeed);
            reloadRemaining = Mathf.Max(0f, reloadRemaining - Time.deltaTime);
            if (input.Reload.Pressed && !IsReloading &&
                !input.PrimaryFire.Pressed && !input.PrimaryFire.Held &&
                !input.SecondaryFire.Pressed && !input.SecondaryFire.Held)
            {
                reloadRemaining = reloadPreviewDuration;
                animator.ResetTrigger(Fire);
                animator.SetTrigger(Reload);
            }
        }

        private void OnAttackExecuted(AttackSlot slot)
        {
            if (slot != AttackSlot.PrimaryFire && slot != AttackSlot.SecondaryFire) return;
            reloadRemaining = 0f;
            animator.ResetTrigger(Reload);
            animator.SetTrigger(Fire);
        }

        private void OnDisable()
        {
            if (launcher != null)
                launcher.AttackExecuted -= OnAttackExecuted;
            reloadRemaining = 0f;
            if (animatorPaused && animator != null)
            {
                animator.enabled = true;
                animatorPaused = false;
            }
        }
    }
}
