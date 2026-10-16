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
            Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(forward.normalized);
            Vector3 center = origin + rotation * data.OriginOffset;
            Collider[] candidates = Physics.OverlapBox(center, new Vector3(data.Range, data.Height * 0.5f, data.Range),
                rotation, data.HitMask, QueryTriggerInteraction.Collide);
            DamageResolver.ApplyArea(context, candidates, collider =>
            {
                Vector3 offset = Vector3.ProjectOnPlane(collider.ClosestPoint(center) - center, Vector3.up);
                if (offset.sqrMagnitude > data.Range * data.Range) return false;
                return data.Shape == MeleeShape.Circle || offset.sqrMagnitude < 0.0001f ||
                    Vector3.Angle(forward, offset) <= data.SectorAngle * 0.5f;
            });
            return true;
        }
    }
}
