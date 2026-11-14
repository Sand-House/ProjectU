using UnityEngine;

namespace ProjectU
{
    [DisallowMultipleComponent, RequireComponent(typeof(Combatant))]
    public sealed class EnemySensor : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional scene player. If empty, resolves the Player tag when scanning.")]
        private Combatant player;
        [SerializeField, Min(0f)] private float viewDistance = 12f;
        [SerializeField, Range(0f, 360f)] private float viewAngle = 120f;
        [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 1.5f, 0f);
        [SerializeField, Min(0.02f)] private float scanInterval = 0.15f;
        [SerializeField] private LayerMask obstructionMask = ~0;
        private Combatant owner;
        private double nextScan;
        public float ViewDistance => viewDistance;
        public float ViewAngle => viewAngle;
        public Vector3 EyePosition => transform.TransformPoint(eyeOffset);
        public void SetPlayer(Combatant value) { player = value; nextScan = 0d; }

        public bool TryDetect(out Combatant detected)
        {
            detected = null;
            if (!isActiveAndEnabled || Time.timeScale <= 0f || Time.timeAsDouble < nextScan) return false;
            nextScan = Time.timeAsDouble + scanInterval;
            if (player == null)
            {
                GameObject candidate = GameObject.FindGameObjectWithTag("Player");
                if (candidate != null) player = candidate.GetComponent<Combatant>();
            }
            if (!CanSee(player)) return false;
            detected = player;
            return true;
        }

        public bool CanSee(Combatant candidate)
        {
            if (owner == null) owner = GetComponent<Combatant>();
            if (!isActiveAndEnabled || !owner.IsAlive || candidate == null || candidate.Faction != Faction.Player ||
                !owner.CanTarget(candidate)) return false;
            Vector3 delta = TargetPoint(candidate) - EyePosition;
            if (delta.sqrMagnitude > viewDistance * viewDistance) return false;
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            if (horizontal.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, horizontal) > viewAngle * 0.5f) return false;
            return !WorldObstruction.IsBlocked(EyePosition, TargetPoint(candidate), obstructionMask);
        }

        public static Vector3 TargetPoint(Combatant target)
        {
            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
                if (collider.enabled && collider.gameObject.activeInHierarchy) return collider.bounds.center;
            return target.transform.position;
        }

        private void OnEnable() => nextScan = 0d;
        private void OnValidate()
        {
            viewDistance = AttackDataBase.FiniteAtLeast(viewDistance, 0f);
            viewAngle = Mathf.Clamp(AttackDataBase.FiniteAtLeast(viewAngle, 0f), 0f, 360f);
            scanInterval = AttackDataBase.FiniteAtLeast(scanInterval, 0.02f);
            if (!AttackDataBase.IsFinite(eyeOffset)) eyeOffset = Vector3.up * 1.5f;
        }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(EyePosition, viewDistance);
            Gizmos.DrawRay(EyePosition, Quaternion.AngleAxis(-viewAngle * 0.5f, Vector3.up) * transform.forward * viewDistance);
            Gizmos.DrawRay(EyePosition, Quaternion.AngleAxis(viewAngle * 0.5f, Vector3.up) * transform.forward * viewDistance);
        }
    }
}
