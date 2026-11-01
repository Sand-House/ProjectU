using UnityEngine;

namespace ProjectU
{
    // Only solid world geometry occludes sight and melee. Units and projectiles do not become walls.
    public static class WorldObstruction
    {
        public static bool IsBlocked(Vector3 start, Vector3 end, int mask)
        {
            foreach (Collider collider in Physics.OverlapSphere(start, 0.01f, mask, QueryTriggerInteraction.Ignore))
                if (IsWorld(collider)) return true;
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.000001f) return false;
            foreach (RaycastHit hit in Physics.RaycastAll(start, delta.normalized, delta.magnitude, mask, QueryTriggerInteraction.Ignore))
                if (IsWorld(hit.collider)) return true;
            return false;
        }

        private static bool IsWorld(Collider collider)
            => collider != null && !Hitbox.TryResolve(collider, out _, out _) &&
                collider.GetComponentInParent<Projectile>() == null;
    }
}
