using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    public sealed class HitscanAttack : Attack
    {
        [SerializeField] private HitscanAttackData data;
        [Header("Debug")]
        [SerializeField] private bool drawDebugLine = true;
        [SerializeField] private Color debugLineColor = Color.yellow;
        [SerializeField, Min(0f)] private float debugLineDuration = 1f;
        private int consecutiveShots;
        private double lastShotAt = double.NegativeInfinity;

        public override AttackData Data => data;
        public int ConsecutiveShots => data == null || Time.timeAsDouble - lastShotAt >= data.SpreadResetDelay ? 0 : consecutiveShots;
        public Vector3 LastShotDirection { get; private set; }
        public Vector3 LastShotEndPoint { get; private set; }

        protected override bool ExecuteAttack(Vector3 origin, Vector3 direction, in DamageContext context)
        {
            consecutiveShots = ConsecutiveShots == int.MaxValue ? int.MaxValue : ConsecutiveShots + 1;
            lastShotAt = Time.timeAsDouble;
            if (data.SpreadAngle > 0f && consecutiveShots >= data.SpreadStartShot)
            {
                // Uniform solid-angle distribution inside the cone.
                float cosine = Mathf.Lerp(1f, Mathf.Cos(data.SpreadAngle * Mathf.Deg2Rad), UnityEngine.Random.value);
                float sine = Mathf.Sqrt(Mathf.Max(0f, 1f - cosine * cosine));
                float azimuth = UnityEngine.Random.value * Mathf.PI * 2f;
                direction = Quaternion.LookRotation(direction) * new Vector3(sine * Mathf.Cos(azimuth), sine * Mathf.Sin(azimuth), cosine);
            }
            LastShotDirection = direction;
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, data.MaxDistance, data.HitMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var visited = new HashSet<Combatant>();
            long remainingTargets = (long)data.EnemyPenetrationCount + 1L;
            Vector3 endPoint = origin + direction * data.MaxDistance;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponentInParent<Projectile>() != null) continue;
                if (Hitbox.TryResolve(hit.collider, out Combatant target, out bool critical))
                {
                    if (!context.CanHit(target) || !visited.Add(target)) continue;
                    DamageResolver.Apply(context, target, critical);
                    if (--remainingTargets == 0)
                    {
                        endPoint = hit.point;
                        break;
                    }
                }
                else if (!hit.collider.isTrigger)
                {
                    endPoint = hit.point;
                    break;
                }
            }
            LastShotEndPoint = endPoint;
            if (drawDebugLine)
                Debug.DrawLine(origin, endPoint, debugLineColor, debugLineDuration, false);
            return true; // A miss still counts as a shot.
        }
    }
}
