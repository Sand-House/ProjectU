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
        [SerializeField] private Vector3 muzzleOffset = new Vector3(0.35f, 0.55f, 0.65f);
        [SerializeField, Min(1f)] private float aimDistance = 100f;
        [SerializeField, Range(0f, 45f)] private float grenadeLiftAngle = 12f;
        private PlayerInputReader input;

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

        private void Fire(Projectile prefab, bool lob)
        {
            Vector3 origin = transform.TransformPoint(muzzleOffset);
            Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            Vector3 aimPoint = ray.GetPoint(aimDistance);
            float closest = aimDistance;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, aimDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Projectile>() != null) continue;
                if (hit.distance >= closest) continue;
                closest = hit.distance;
                aimPoint = hit.point;
            }
            Vector3 direction = (aimPoint - origin).normalized;
            if (lob) direction = Quaternion.AngleAxis(-grenadeLiftAngle, aimCamera.transform.right) * direction;
            // Start at the player side of any close obstruction instead of beyond a wall.
            Vector3 shoulder = transform.position + Vector3.up * muzzleOffset.y;
            if (Physics.Linecast(shoulder, origin, out RaycastHit obstruction, 1, QueryTriggerInteraction.Ignore))
                origin = obstruction.point - (origin - shoulder).normalized * 0.2f;
            pool.Spawn(prefab, gameObject, origin, direction);
        }
    }
}
