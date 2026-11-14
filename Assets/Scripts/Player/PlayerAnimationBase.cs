using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerCombat))]
    public abstract class PlayerAnimationBase : MonoBehaviour, IPlayerStageSubscriber
    {
        private static readonly int MoveX = Animator.StringToHash("MoveX");
        private static readonly int MoveZ = Animator.StringToHash("MoveZ");
        private static readonly int Grounded = Animator.StringToHash("Grounded");
        private static readonly int VerticalSpeed = Animator.StringToHash("VerticalSpeed");

        [SerializeField] private Animator animator;
        [SerializeField, Min(0f)] private float movementDampTime = 0.08f;
        private bool initialized;
        private bool animatorPaused;
        private PlayerStageSubscription stageEvents;
        private int resetSequence;

        protected Animator AnimatorComponent => animator;
        protected PlayerMotor Motor { get; private set; }
        protected PlayerCombat Combat { get; private set; }
        public virtual bool CanApplyAimPose => true;

        private PlayerStageSubscription StageEvents => stageEvents ?? (stageEvents = new PlayerStageSubscription(
            this, HandleStageStateChanged, synchronize: SynchronizeStageReset));
        void IPlayerStageSubscriber.BindStageEvents(GameManager game) => StageEvents.Bind(game);
        void IPlayerStageSubscriber.UnbindStageEvents(GameManager game) => stageEvents?.Unbind(game);

        private void HandleStageStateChanged(StageState state)
        {
            if (StageEvents.IsCurrent(state) && state == StageState.AfterStage) SynchronizeStageReset(state);
        }

        private void SynchronizeStageReset(StageState state)
        {
            if (StageEvents.StageClearSequence == resetSequence) return;
            resetSequence = StageEvents.StageClearSequence;
            ResetForStage();
        }

        // Keep Unity messages here; subclasses extend the character hooks below.
        private void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            Combat = GetComponent<PlayerCombat>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError(GetType().Name + " requires a configured Animator.", this);
                enabled = false;
                return;
            }
            animator.applyRootMotion = false;
            animator.keepAnimatorStateOnDisable = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            initialized = InitializeCharacterAnimation();
            if (!initialized) enabled = false;
        }

        private void OnEnable()
        {
            if (!initialized) return;
            // Apply a missed clear before catching up newer character-specific visuals.
            stageEvents?.Enable();
            Combat.AttackExecuted += HandleAttackExecuted;
            EnableCharacterAnimation();
        }

        private void Update()
        {
            if (!initialized) return;
            if (Time.deltaTime <= 0f)
            {
                // Freeze Animator evaluation as well as late procedural corrections.
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
            Vector3 localVelocity = transform.InverseTransformDirection(Motor.HorizontalVelocity);
            float speed = Mathf.Max(0.001f, Motor.MoveSpeed);
            animator.SetFloat(MoveX, localVelocity.x / speed, movementDampTime, Time.deltaTime);
            animator.SetFloat(MoveZ, localVelocity.z / speed, movementDampTime, Time.deltaTime);
            animator.SetBool(Grounded, Motor.IsGrounded);
            animator.SetFloat(VerticalSpeed, Motor.VerticalSpeed);
            UpdateCharacterAnimation();
        }

        public void ResetForStage()
        {
            if (initialized && animator != null) ResetCharacterAnimation();
        }

        private void OnDisable()
        {
            stageEvents?.Disable();
            if (Combat != null) Combat.AttackExecuted -= HandleAttackExecuted;
            if (initialized) DisableCharacterAnimation();
            if (animatorPaused && animator != null)
            {
                animator.enabled = true;
                animatorPaused = false;
            }
        }

        protected virtual bool InitializeCharacterAnimation() => true;
        protected virtual void EnableCharacterAnimation() { }
        protected virtual void DisableCharacterAnimation() { }
        protected virtual void UpdateCharacterAnimation() { }
        protected virtual void HandleAttackExecuted(PlayerActionSlot slot) { }
        protected abstract void ResetCharacterAnimation();
    }
}
