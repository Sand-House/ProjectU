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

        public float Yaw { get; private set; }
        private float pitch;

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
            Quaternion rotation = Quaternion.Euler(pitch, Yaw, 0f);
            Vector3 pivot = target.position + Vector3.up * targetHeight;
            Vector3 offset = rotation * new Vector3(shoulderOffset, 0f, -distance);
            float length = offset.magnitude;
            if (Physics.SphereCast(pivot, collisionRadius, offset / length, out RaycastHit hit,
                length, obstructionMask, QueryTriggerInteraction.Ignore))
                offset = offset.normalized * Mathf.Max(0f, hit.distance - 0.05f);

            transform.SetPositionAndRotation(pivot + offset, rotation);
        }
    }
}
