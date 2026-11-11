using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private PlayerInputReader input;
        [Header("Look")]
        [SerializeField, Min(0f), Tooltip("Degrees per mouse pixel.")]
        private float mouseSensitivity = 0.12f;
        [SerializeField] private bool invertY;
        [SerializeField, Range(-85f, 0f)] private float minimumPitch = -40f;
        [SerializeField, Range(0f, 85f)] private float maximumPitch = 70f;
        [SerializeField, Range(-85f, 85f)] private float initialPitch = 15f;
        [Header("Follow")]
        [SerializeField] private float targetHeight = 0.65f;
        [SerializeField, Min(0.5f)] private float distance = 4.5f;
        [SerializeField] private float shoulderOffset = 0.65f;
        [SerializeField, Min(0.1f)] private float collisionRadius = 0.25f;
        [SerializeField] private LayerMask obstructionMask = 1;
        [Header("Aim Down Sights")]
        [SerializeField, Tooltip("Camera offset from the follow pivot in view space at full ADS. Field of view is unchanged.")]
        private Vector3 aimOffset = new Vector3(0f, 0f, 0.25f);

        public float Yaw { get; private set; }
        public float AimBlend { get; private set; }
        public Vector3 AimCameraPosition { get; private set; }
        public bool IsAimTransitioning
        {
            get
            {
                if (!isActiveAndEnabled) return false;
                bool requested = combat != null && combat.IsAiming;
                // Controller can change the stance before this camera's LateUpdate.
                return AimBlend != (requested ? 1f : 0f) &&
                    (requested != aiming ? AimTransitionTime > 0f : transitionElapsed < transitionDuration);
            }
        }
        private float pitch;
        private PlayerCombat combat;
        private bool aiming;
        private float transitionStart, transitionElapsed, transitionDuration;
        private float AimTransitionTime => combat != null && combat.AimAction != null ? combat.AimAction.TransitionTime : 0f;

        private void Awake()
        {
            if (target == null || input == null)
            {
                Debug.LogError("PlayerCamera requires Target and Input references.", this);
                enabled = false;
                return;
            }
            Yaw = target.eulerAngles.y;
            pitch = Mathf.Clamp(initialPitch, minimumPitch, maximumPitch);
            combat = target.GetComponent<PlayerCombat>();
        }

        private void Update()
        {
            // Mouse delta is already accumulated per frame; do not multiply by deltaTime.
            Yaw = Mathf.Repeat(Yaw + input.Look.x * mouseSensitivity, 360f);
            pitch = Mathf.Clamp(pitch + input.Look.y * mouseSensitivity * (invertY ? 1f : -1f),
                minimumPitch, maximumPitch);
        }

        private void LateUpdate()
        {
            UpdateAimBlend();
            Quaternion rotation = Quaternion.Euler(pitch, Yaw, 0f);
            Vector3 pivot = target.position + Vector3.up * targetHeight;
            AimCameraPosition = ResolvePosition(pivot, rotation * aimOffset);
            Vector3 localOffset = Vector3.Lerp(new Vector3(shoulderOffset, 0f, -distance), aimOffset, AimBlend);
            transform.SetPositionAndRotation(ResolvePosition(pivot, rotation * localOffset), rotation);
        }

        private Vector3 ResolvePosition(Vector3 pivot, Vector3 offset)
        {
            float length = offset.magnitude;
            if (length > 0.0001f && Physics.SphereCast(pivot, collisionRadius, offset / length, out RaycastHit hit,
                length, obstructionMask, QueryTriggerInteraction.Ignore))
                offset = offset.normalized * Mathf.Max(0f, hit.distance - 0.05f);

            return pivot + offset;
        }

        private void UpdateAimBlend()
        {
            bool requested = combat != null && combat.IsAiming;
            if (requested != aiming)
            {
                aiming = requested;
                transitionStart = AimBlend;
                transitionElapsed = 0f;
                transitionDuration = AimTransitionTime;
            }
            // A death/input cancellation must also leave ADS when gameplay time stops.
            transitionElapsed += Time.deltaTime > 0f ? Time.deltaTime : !aiming ? Time.unscaledDeltaTime : 0f;
            float progress = transitionDuration > 0f ? Mathf.Clamp01(transitionElapsed / transitionDuration) : 1f;
            AimBlend = Mathf.Lerp(transitionStart, aiming ? 1f : 0f, Mathf.SmoothStep(0f, 1f, progress));
        }

        private void OnDisable()
        {
            AimBlend = transitionStart = transitionElapsed = transitionDuration = 0f;
            aiming = false;
        }

        private void OnValidate()
        {
            if (!AttackData.IsFinite(aimOffset)) aimOffset = new Vector3(0f, 0f, 0.25f);
        }
    }
}
