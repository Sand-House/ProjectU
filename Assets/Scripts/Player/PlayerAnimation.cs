using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerCombat))]
    public sealed class PlayerAnimation : MonoBehaviour
    {
        private static readonly int MoveX = Animator.StringToHash("MoveX");
        private static readonly int MoveZ = Animator.StringToHash("MoveZ");
        private static readonly int Grounded = Animator.StringToHash("Grounded");
        private static readonly int VerticalSpeed = Animator.StringToHash("VerticalSpeed");
        private static readonly int Fire = Animator.StringToHash("Fire");
        private static readonly int Reload = Animator.StringToHash("Reload");
        private static readonly int ReloadSpeedMultiplier = Animator.StringToHash("ReloadSpeedMultiplier");
        private static readonly int Skill1 = Animator.StringToHash("Skill1");
        private static readonly int TossGrenade = Animator.StringToHash("Toss Grenade");
        private static readonly int RifleAim = Animator.StringToHash("Upper Body.Rifle Aim");

        [SerializeField] private Animator animator;
        [SerializeField, Min(0f)] private float movementDampTime = 0.08f;
        private PlayerMotor motor;
        private PlayerStat stat;
        private PlayerCombat launcher;
        private bool reloadVisualActive;
        private bool animatorPaused;

        public bool IsReloading => launcher != null && launcher.IsReloading;
        public bool IsThrowingGrenade => animator != null &&
            (animator.GetCurrentAnimatorStateInfo(1).shortNameHash == TossGrenade ||
             (animator.IsInTransition(1) && animator.GetNextAnimatorStateInfo(1).shortNameHash == TossGrenade));

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            stat = GetComponent<PlayerStat>();
            launcher = GetComponent<PlayerCombat>();
            if (animator == null || !animator.isHuman || animator.runtimeAnimatorController == null)
            {
                Debug.LogError("PlayerAnimation requires a configured Humanoid Animator.", this);
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
            {
                launcher.AttackExecuted += OnAttackExecuted;
                launcher.OnReloadStarted += OnReloadStarted;
                launcher.OnReloadCompleted += OnReloadCompleted;
                if (launcher.IsReloading) OnReloadStarted();
            }
            if (motor != null)
            {
                motor.OnDashExecuted += OnDashExecuted;
                // Catch up if this visual component was disabled when the dash began.
                if (motor.IsDashActionLocked)
                    OnDashExecuted();
            }
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
            animator.SetFloat(ReloadSpeedMultiplier, stat.ReloadSpeedMultiplier);
            // Cancellation has no completion event and must never refill the magazine.
            if (reloadVisualActive && !IsReloading) ReturnToRifleAim();
        }

        private void OnReloadStarted()
        {
            reloadVisualActive = true;
            animator.SetFloat(ReloadSpeedMultiplier, stat.ReloadSpeedMultiplier);
            animator.ResetTrigger(Fire);
            animator.ResetTrigger(Skill1);
            animator.SetTrigger(Reload);
        }

        private void OnReloadCompleted() => ReturnToRifleAim();

        private void OnAttackExecuted(AttackSlot slot)
        {
            if (slot == AttackSlot.Skill1)
            {
                // Visual playback only: the attack has already launched its grenade.
                reloadVisualActive = false;
                animator.ResetTrigger(Fire);
                animator.ResetTrigger(Reload);
                animator.SetTrigger(Skill1);
                return;
            }
            if (slot != AttackSlot.PrimaryFire && slot != AttackSlot.SecondaryFire) return;
            reloadVisualActive = false;
            animator.ResetTrigger(Skill1);
            animator.ResetTrigger(Reload);
            animator.SetTrigger(Fire);
        }

        private void OnDashExecuted()
            => ReturnToRifleAim();

        private void ReturnToRifleAim()
        {
            reloadVisualActive = false;
            animator.ResetTrigger(Fire);
            animator.ResetTrigger(Reload);
            animator.ResetTrigger(Skill1);
            animator.CrossFadeInFixedTime(RifleAim, 0.05f, 1);
        }

        private void OnDisable()
        {
            if (launcher != null)
            {
                launcher.AttackExecuted -= OnAttackExecuted;
                launcher.OnReloadStarted -= OnReloadStarted;
                launcher.OnReloadCompleted -= OnReloadCompleted;
            }
            if (motor != null)
                motor.OnDashExecuted -= OnDashExecuted;
            reloadVisualActive = false;
            if (animatorPaused && animator != null)
            {
                animator.enabled = true;
                animatorPaused = false;
            }
        }
    }
}
