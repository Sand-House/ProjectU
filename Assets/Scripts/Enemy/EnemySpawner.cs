using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectU
{
    [DisallowMultipleComponent]
    public sealed class EnemySpawner : MonoBehaviour
    {
        internal readonly struct PrefabRequest
        {
            public readonly EnemyAI Prefab;
            public readonly int Count;
            public PrefabRequest(EnemyAI prefab, int count) { Prefab = prefab; Count = count; }
        }

        [SerializeField, Min(0f), Tooltip("World-space half width of the square spawn area.")]
        private float halfExtent = 4f;
        [SerializeField, Min(0f)] private float spacing = 0.25f;
        [SerializeField, Min(0.1f)] private float searchStep = 0.5f;
        [SerializeField, Min(0.01f)] private float navMeshSampleDistance = 0.5f;
        [SerializeField, Min(1)] private int maxBatchSize = 128;

        public float HalfExtent => halfExtent;
        public int MaxBatchSize => maxBatchSize;

        internal struct Shape
        {
            public EnemyAI Prefab;
            public NavMeshQueryFilter Filter;
            public float Radius, Bottom, Top;
        }

        internal struct Placement
        {
            public Shape Shape;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Spacing;
        }

        // Plans are private to one synchronous manager transaction: no stale plans or public commit API.
        internal bool TryPlan(IReadOnlyList<PrefabRequest> requests, List<Placement> plan)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || requests == null || requests.Count == 0 || requests.Count > maxBatchSize ||
                !ValidSettings()) return false;
            int total = 0;
            var shapes = new Shape[requests.Count];
            for (int index = 0; index < requests.Count; index++)
            {
                PrefabRequest request = requests[index];
                if (request.Count <= 0 || request.Count > maxBatchSize - total ||
                    !TryGetShape(request.Prefab, out shapes[index])) return false;
                total += request.Count;
            }
            int start = plan.Count;
            for (int index = 0; index < requests.Count; index++)
                for (int count = 0; count < requests[index].Count; count++)
                {
                    if (!TryFindPosition(shapes[index], plan, out Vector3 position))
                    {
                        plan.RemoveRange(start, plan.Count - start);
                        return false;
                    }
                    plan.Add(new Placement { Shape = shapes[index], Position = position,
                        Rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Spacing = spacing });
                }
            return true;
        }

        internal static EnemyAI Create(in Placement placement)
            => Instantiate(placement.Shape.Prefab, placement.Position, placement.Rotation);

        private bool TryFindPosition(Shape shape, List<Placement> plan, out Vector3 position)
        {
            position = default;
            int rings = Mathf.CeilToInt(halfExtent / searchStep);
            for (int ring = 0; ring <= rings; ring++)
                for (int z = -ring; z <= ring; z++)
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != ring) continue;
                        Vector3 candidate = transform.position + new Vector3(x * searchStep, 0f, z * searchStep);
                        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, shape.Filter)) continue;
                        Vector3 delta = hit.position - transform.position;
                        if (Mathf.Abs(delta.x) + shape.Radius > halfExtent || Mathf.Abs(delta.z) + shape.Radius > halfExtent ||
                            Mathf.Abs(delta.y) > navMeshSampleDistance) continue;

                        // Conservative upright bounds cover the capsule and navigation footprint.
                        // A small bottom inset allows contact with the supporting ground.
                        float bottom = hit.position.y + shape.Bottom + 0.02f;
                        float top = hit.position.y + shape.Top;
                        Vector3 center = new Vector3(hit.position.x, (bottom + top) * 0.5f, hit.position.z);
                        if (Physics.CheckBox(center, new Vector3(shape.Radius + spacing, (top - bottom) * 0.5f, shape.Radius + spacing),
                            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                        bool occupied = false;
                        foreach (Placement other in plan)
                        {
                            float distance = shape.Radius + other.Shape.Radius + Mathf.Max(spacing, other.Spacing);
                            if (Mathf.Abs(hit.position.x - other.Position.x) < distance &&
                                Mathf.Abs(hit.position.z - other.Position.z) < distance &&
                                hit.position.y + shape.Bottom < other.Position.y + other.Shape.Top &&
                                hit.position.y + shape.Top > other.Position.y + other.Shape.Bottom)
                            { occupied = true; break; }
                        }
                        if (occupied) continue;
                        position = hit.position;
                        return true;
                    }
            return false;
        }

        internal static bool TryGetShape(EnemyAI prefab, out Shape shape)
        {
            shape = default;
            if (prefab == null || !prefab.enabled || !prefab.gameObject.activeSelf) return false;
            CapsuleCollider capsule = prefab.GetComponent<CapsuleCollider>();
            NavMeshAgent agent = prefab.GetComponent<NavMeshAgent>();
            EnemyStat stat = prefab.GetComponent<EnemyStat>();
            // Current enemy prefab contract: one upright root capsule; child trigger hitboxes are allowed.
            if (capsule == null || !capsule.enabled || capsule.isTrigger || capsule.direction != 1 ||
                agent == null || !agent.enabled || stat == null || !stat.enabled || stat.Data == null) return false;
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger && collider != capsule) return false;
            Vector3 scale = prefab.transform.localScale;
            if (!AttackData.IsFinite(scale) || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f) return false;
            Vector3 center = Vector3.Scale(capsule.center, scale);
            float radialScale = Mathf.Max(scale.x, scale.z);
            float radius = capsule.radius * radialScale;
            float height = Mathf.Max(capsule.height * scale.y, radius * 2f);
            shape = new Shape
            {
                Prefab = prefab,
                Filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask },
                Radius = Mathf.Max(agent.radius * radialScale, radius + new Vector2(center.x, center.z).magnitude),
                Bottom = Mathf.Min(center.y - height * 0.5f, agent.baseOffset * scale.y),
                Top = Mathf.Max(center.y + height * 0.5f, (agent.baseOffset + agent.height) * scale.y)
            };
            return shape.Radius > 0f && shape.Top - shape.Bottom > 0.02f &&
                AttackData.IsFinite(new Vector3(shape.Radius, shape.Bottom, shape.Top));
        }

        private bool ValidSettings()
        {
            return AttackData.IsFinite(transform.position) && AttackData.IsFinite(new Vector3(halfExtent, spacing, searchStep)) &&
                halfExtent >= 0f && spacing >= 0f && searchStep >= 0.1f && halfExtent / searchStep <= 64f &&
                !float.IsNaN(navMeshSampleDistance) && !float.IsInfinity(navMeshSampleDistance) && navMeshSampleDistance > 0f &&
                maxBatchSize > 0;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.05f, new Vector3(halfExtent * 2f, 0.1f, halfExtent * 2f));
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.25f, 0.25f);
        }
    }
}
