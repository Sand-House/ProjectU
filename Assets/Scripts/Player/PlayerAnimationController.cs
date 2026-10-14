using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader), typeof(PlayerProjectileLauncher))]
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
        [SerializeField] private bool logAnimationBlending = true;
        [SerializeField, Min(0.1f)] private float animationBlendLogInterval = 0.2f;

        private PlayerMotor motor;
        private PlayerInputReader input;
        private PlayerProjectileLauncher launcher;
        private float reloadRemaining;
        private bool animatorPaused;
        private float nextAnimationBlendLog;
        private readonly List<AnimatorClipInfo> clipInfos = new List<AnimatorClipInfo>(8);
        private readonly StringBuilder animationBlendLog = new StringBuilder(512);

        public bool IsReloading => reloadRemaining > 0f;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            launcher = GetComponent<PlayerProjectileLauncher>();
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
            nextAnimationBlendLog = 0f;
            if (launcher != null)
                launcher.PrimaryFired += OnPrimaryFired;
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
            if (input.Reload.Pressed && !IsReloading && !input.PrimaryFire.Pressed)
            {
                reloadRemaining = reloadPreviewDuration;
                animator.ResetTrigger(Fire);
                animator.SetTrigger(Reload);
            }
        }

        private void LateUpdate()
        {
            // Read clip weights after this frame's Animator evaluation.
            if (!logAnimationBlending || Time.deltaTime <= 0f || !animator.isActiveAndEnabled ||
                Time.unscaledTime < nextAnimationBlendLog)
                return;

            nextAnimationBlendLog = Time.unscaledTime + animationBlendLogInterval;
            animationBlendLog.Clear();
            animationBlendLog.Append("[Player Animation Blend]");
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                // The base layer always supplies the base pose at full weight.
                float layerWeight = layer == 0 ? 1f : animator.GetLayerWeight(layer);
                animationBlendLog.AppendFormat("\n{0} | Layer Weight: {1:F2}% | Current: ",
                    animator.GetLayerName(layer), layerWeight * 100f);
                clipInfos.Clear();
                animator.GetCurrentAnimatorClipInfo(layer, clipInfos);
                AppendClipWeights();
                if (animator.IsInTransition(layer))
                {
                    animationBlendLog.Append(" | Next: ");
                    clipInfos.Clear();
                    animator.GetNextAnimatorClipInfo(layer, clipInfos);
                    AppendClipWeights();
                }
            }
            Debug.Log(animationBlendLog.ToString(), this);
        }

        private void AppendClipWeights()
        {
            if (clipInfos.Count == 0)
            {
                animationBlendLog.Append("None");
                return;
            }
            for (int i = 0; i < clipInfos.Count; i++)
            {
                if (i > 0) animationBlendLog.Append(", ");
                AnimatorClipInfo clipInfo = clipInfos[i];
                animationBlendLog.AppendFormat("{0}={1:F2}%",
                    clipInfo.clip != null ? clipInfo.clip.name : "None", clipInfo.weight * 100f);
            }
        }

        private void OnPrimaryFired()
        {
            reloadRemaining = 0f;
            animator.ResetTrigger(Reload);
            animator.SetTrigger(Fire);
        }

        private void OnDisable()
        {
            if (launcher != null)
                launcher.PrimaryFired -= OnPrimaryFired;
            reloadRemaining = 0f;
            if (animatorPaused && animator != null)
            {
                animator.enabled = true;
                animatorPaused = false;
            }
        }
    }
}
