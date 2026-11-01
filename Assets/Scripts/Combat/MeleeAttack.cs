using UnityEngine;

namespace ProjectU
{
    public sealed class MeleeAttack : Attack
    {
        [SerializeField] private MeleeAttackData data;
        public override AttackData Data => data;

        protected override bool ExecuteAttack(Vector3 origin, Vector3 direction, in DamageContext context)
        {
            if (data.Shape != MeleeShape.Circle && data.Shape != MeleeShape.Sector) return false;
            GetVolume(origin, direction, out Vector3 center, out Vector3 forward, out Quaternion rotation);
            Collider[] candidates = Physics.OverlapBox(center, new Vector3(data.Range, data.Height * 0.5f, data.Range),
                rotation, data.HitMask, QueryTriggerInteraction.Collide);
            DamageResolver.ApplyArea(context, candidates, collider => IsInsideAndVisible(collider, center, forward));
            return true;
        }

        // AI uses the same volume as the actual strike, including target collider size and vertical reach.
        public bool CanReach(Combatant target, Vector3 origin, Vector3 direction)
        {
            if (!IsConfiguredForExecution || target == null || !Owner.CanTarget(target, data.TargetRule)) return false;
            GetVolume(origin, direction, out Vector3 center, out Vector3 forward, out Quaternion rotation);
            foreach (Collider collider in Physics.OverlapBox(center, new Vector3(data.Range, data.Height * 0.5f, data.Range),
                rotation, data.HitMask, QueryTriggerInteraction.Collide))
                if (Hitbox.TryResolve(collider, out Combatant resolved, out _) && resolved == target &&
                    IsInsideAndVisible(collider, center, forward)) return true;
            return false;
        }

        private void GetVolume(Vector3 origin, Vector3 direction, out Vector3 center, out Vector3 forward, out Quaternion rotation)
        {
            forward = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            rotation = Quaternion.LookRotation(forward.normalized);
            center = origin + rotation * data.OriginOffset;
        }

        private bool IsInsideAndVisible(Collider collider, Vector3 center, Vector3 forward)
        {
            Vector3 point = collider.ClosestPoint(center);
            Vector3 offset = Vector3.ProjectOnPlane(point - center, Vector3.up);
            if (offset.sqrMagnitude > data.Range * data.Range) return false;
            if (data.Shape != MeleeShape.Circle && offset.sqrMagnitude > 0.0001f &&
                Vector3.Angle(forward, offset) > data.SectorAngle * 0.5f) return false;
            return !WorldObstruction.IsBlocked(center, point, data.ObstructionMask);
        }
    }
}
