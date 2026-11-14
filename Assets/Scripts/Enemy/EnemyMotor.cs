using UnityEngine;
using UnityEngine.AI;

namespace ProjectU
{
    [DefaultExecutionOrder(90), DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(Combatant))]
    public sealed class EnemyMotor : MonoBehaviour
    {
        [SerializeField, Min(0.02f)] private float repathInterval = 0.2f;
        [SerializeField, Min(0.1f)] private float sampleDistance = 2f;
        [SerializeField, Min(0f)] private float turnSpeed = 540f;
        private NavMeshAgent agent;
        private Combatant owner;
        private Vector3 destination, facing;
        private bool wantsMove, wantsFace;
        private double nextRepath;
        public NavMeshAgent Agent => agent != null ? agent : (agent = GetComponent<NavMeshAgent>());
        public bool IsGrounded => isActiveAndEnabled && Agent.enabled && Agent.isOnNavMesh && !Agent.isOnOffMeshLink;
        public bool HasCompletePath => IsGrounded && !Agent.pathPending && Agent.hasPath && Agent.pathStatus == NavMeshPathStatus.PathComplete;

        private void Awake() { owner = GetComponent<Combatant>(); Agent.updateRotation = false; }
        public void Chase(Vector3 point)
        {
            if (!wantsMove) nextRepath = 0d;
            destination = point; wantsMove = true;
            Face(point);
        }
        public void Face(Vector3 point) { facing = point; wantsFace = true; }
        public void Stop()
        {
            wantsMove = wantsFace = false;
            if (Agent.enabled && Agent.isOnNavMesh) { Agent.isStopped = true; Agent.ResetPath(); }
        }

        private void Update()
        {
            if (!Agent.enabled || !Agent.isOnNavMesh) return;
            bool locked = owner == null || !owner.IsAlive || owner.IsMovementLocked || Time.timeScale <= 0f;
            Agent.isStopped = locked || !wantsMove;
            if (locked) return;
            if (wantsFace)
            {
                Vector3 direction = Vector3.ProjectOnPlane(facing - transform.position, Vector3.up);
                if (direction.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
            }
            if (!wantsMove || Time.timeAsDouble < nextRepath) return;
            nextRepath = Time.timeAsDouble + repathInterval;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, sampleDistance, Agent.areaMask))
            {
                if (!Agent.SetDestination(hit.position)) Agent.isStopped = true;
            }
            else { Agent.ResetPath(); Agent.isStopped = true; }
        }

        private void OnDisable() => Stop();
        private void OnValidate()
        {
            repathInterval = AttackDataBase.FiniteAtLeast(repathInterval, 0.02f);
            sampleDistance = AttackDataBase.FiniteAtLeast(sampleDistance, 0.1f);
            turnSpeed = AttackDataBase.FiniteAtLeast(turnSpeed, 0f);
        }
    }
}
