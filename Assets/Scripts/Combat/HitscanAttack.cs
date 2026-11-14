using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    public sealed class HitscanAttack : AttackBase
    {
        [SerializeField] private HitscanAttackData data;
        [Header("Debug")]
        [SerializeField] private bool drawDebugLine = true;
        [SerializeField] private Color debugLineColor = Color.yellow;
        [SerializeField, Min(0f)] private float debugLineDuration = 1f;
        [SerializeField, Min(0.001f)] private float debugLineWidth = 0.02f;
        private readonly List<DebugShot> debugShots = new List<DebugShot>();
        private Material debugLineMaterial;
        private int consecutiveShots;
        private double lastShotAt = double.NegativeInfinity;
        private float spreadAngleMultiplier = 1f;

        public override AttackDataBase Data => data;
        public int ConsecutiveShots => data == null || Time.timeAsDouble - lastShotAt >= data.SpreadResetDelay ? 0 : consecutiveShots;
        public Vector3 LastShotDirection { get; private set; }
        public Vector3 LastShotEndPoint { get; private set; }
        public float LastSpreadAngle { get; private set; }
        // Per-execution runtime modifier; shared AttackData assets are never edited.
        public float SpreadAngleMultiplier
        {
            get => spreadAngleMultiplier;
            set => spreadAngleMultiplier = Mathf.Clamp01(AttackDataBase.FiniteAtLeast(value, 0f));
        }

        protected override bool ExecuteAttack(Vector3 origin, Vector3 direction, in DamageContext context)
        {
            consecutiveShots = ConsecutiveShots == int.MaxValue ? int.MaxValue : ConsecutiveShots + 1;
            lastShotAt = Time.timeAsDouble;
            LastSpreadAngle = consecutiveShots >= data.SpreadStartShot ? data.SpreadAngle * SpreadAngleMultiplier : 0f;
            if (LastSpreadAngle > 0f)
            {
                // Uniform solid-angle distribution inside the cone.
                float cosine = Mathf.Lerp(1f, Mathf.Cos(LastSpreadAngle * Mathf.Deg2Rad), UnityEngine.Random.value);
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
                if (hit.collider.GetComponentInParent<ProjectileBase>() != null) continue;
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
            if (drawDebugLine) DrawShotLine(origin, endPoint);
            return true; // A miss still counts as a shot.
        }

        private void DrawShotLine(Vector3 start, Vector3 end)
        {
            if (debugLineMaterial == null)
            {
                // Resources keeps this shader available in player builds as well as the Editor.
                Shader shader = Resources.Load<Shader>("HitscanDebugLine");
                if (shader == null) return;
                debugLineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            DebugShot shot = debugShots.Find(item => !item.Renderer.enabled);
            if (shot == null)
            {
                var lineObject = new GameObject("Hitscan Debug Line")
                {
                    layer = gameObject.layer,
                    hideFlags = HideFlags.DontSave
                };
                lineObject.transform.SetParent(transform, false);
                var line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.sharedMaterial = debugLineMaterial;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                line.allowOcclusionWhenDynamic = false;
                shot = new DebugShot { Renderer = line };
                debugShots.Add(shot);
            }

            shot.Renderer.SetPosition(0, start);
            shot.Renderer.SetPosition(1, end);
            shot.Renderer.startColor = shot.Renderer.endColor = debugLineColor;
            shot.Renderer.widthMultiplier = AttackDataBase.FiniteAtLeast(debugLineWidth, 0.001f);
            shot.Renderer.enabled = true;
            shot.ExpiresAt = Time.timeAsDouble + AttackDataBase.FiniteAtLeast(debugLineDuration, 0f);
            shot.Frame = Time.frameCount;
        }

        private void Update()
        {
            foreach (DebugShot shot in debugShots)
            {
                // A zero duration still displays the firing frame, like Debug.DrawLine.
                if (!drawDebugLine || (shot.Frame != Time.frameCount && Time.timeAsDouble >= shot.ExpiresAt))
                    shot.Renderer.enabled = false;
            }
        }

        private void OnDisable()
        {
            foreach (DebugShot shot in debugShots) shot.Renderer.enabled = false;
        }

        private void OnDestroy()
        {
            foreach (DebugShot shot in debugShots)
                if (shot.Renderer != null) Destroy(shot.Renderer.gameObject);
            if (debugLineMaterial != null) Destroy(debugLineMaterial);
        }

        private sealed class DebugShot
        {
            public LineRenderer Renderer;
            public double ExpiresAt;
            public int Frame;
        }
    }
}
