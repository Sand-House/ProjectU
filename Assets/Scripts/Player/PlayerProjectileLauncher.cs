using System;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(PlayerInputReader))]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerProjectileLauncher : MonoBehaviour
    {
        [SerializeField] private ProjectilePool pool;
        [SerializeField] private ProjectileBullet bulletPrefab;
        [SerializeField] private ProjectileGrenade grenadePrefab;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform obstructionOrigin;
        [SerializeField] private Vector3 muzzleOffset = new Vector3(0.35f, 0.55f, 0.65f);
        [SerializeField, Min(1f)] private float aimDistance = 100f;
        [SerializeField, Range(0f, 45f)] private float grenadeLiftAngle = 12f;
        private PlayerInputReader input;
        private readonly RaycastHit[] aimHits = new RaycastHit[32];

        public event Action PrimaryFired;
        public Transform Muzzle => muzzle;

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            if (pool != null && bulletPrefab != null && grenadePrefab != null && aimCamera != null) return;
            Debug.LogError("PlayerProjectileLauncher requires pool, prefabs and aim camera.", this);
            enabled = false;
        }

        private void LateUpdate()
        {
            if (input.PrimaryFire.Pressed) Fire(bulletPrefab, false);
            if (input.SecondaryFire.Pressed) Fire(grenadePrefab, true);
        }

        // Both the pose controller and projectiles converge on this camera-center target.
        public Vector3 GetAimPoint()
        {
            Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            Vector3 aimPoint = ray.GetPoint(aimDistance);
            float closest = aimDistance;
            int count = Physics.RaycastNonAlloc(ray, aimHits, aimDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            // A full buffer may omit the closest hit, so use the complete set in this rare case.
            RaycastHit[] hits = count == aimHits.Length
                ? Physics.RaycastAll(ray, aimDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : aimHits;
            if (hits != aimHits)
                count = hits.Length;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Projectile>() != null) continue;
                if (hit.distance >= closest) continue;
                closest = hit.distance;
                aimPoint = hit.point;
            }
            return aimPoint;
        }

        private void Fire(Projectile prefab, bool lob)
        {
            Vector3 origin = muzzle != null ? muzzle.position : transform.TransformPoint(muzzleOffset);
            Vector3 aimPoint = GetAimPoint();
            // Start at the player side of any close obstruction instead of beyond a wall.
            Vector3 shoulder = obstructionOrigin != null
                ? obstructionOrigin.position : transform.position + Vector3.up * muzzleOffset.y;
            if (Physics.Linecast(shoulder, origin, out RaycastHit obstruction, 1, QueryTriggerInteraction.Ignore))
                origin = obstruction.point - (origin - shoulder).normalized * 0.2f;
            Vector3 direction = aimPoint - origin;
            if (direction.sqrMagnitude < 0.0001f)
                direction = aimCamera.transform.forward;
            direction.Normalize();
            if (lob)
                direction = Quaternion.AngleAxis(-grenadeLiftAngle, aimCamera.transform.right) * direction;
            if (pool.Spawn(prefab, gameObject, origin, direction) != null && !lob)
                PrimaryFired?.Invoke();
        }
    }
}
