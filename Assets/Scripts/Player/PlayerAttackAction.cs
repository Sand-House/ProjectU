using UnityEngine;

namespace ProjectU
{
    // Shared adapter for actions whose current effect is an Attack.
    public abstract class PlayerAttackAction : PlayerAction
    {
        [SerializeField] private Attack attack;
        private Transform pendingOrigin;
        private bool fixedPosition;
        private Vector3 pendingPosition;
        private Vector3 requestedAimPoint;
        private bool hasRequestedAim;
        private float damageMultiplier = 1f;

        public Attack Attack => attack;
        public Transform Origin => attack != null ? attack.Origin : null;
        public Vector3 WorldAttackPosition => fixedPosition ? pendingPosition :
            pendingOrigin != null ? pendingOrigin.position : Origin != null ? Origin.position : transform.position;
        public override bool IsAttackAction => true;
        public float DamageMultiplier
        {
            get => damageMultiplier;
            set => damageMultiplier = AttackData.FiniteAtLeast(value, 0f);
        }

        public bool TryStart(Transform anchor, Vector3 point) => TryStart(PlayerActionContext.Aim(anchor, point));
        public bool TryStart(Vector3 position, Vector3 point) => TryStart(PlayerActionContext.Aim(position, point));
        public bool TryStart(Vector3 point) => TryStart(transform, point);

        protected override bool CheckRequirements(in PlayerActionContext request)
        {
            if (attack == null || attack.gameObject != gameObject || !attack.IsConfiguredForExecution) return false;
            if (request.HasAimPoint && !AttackData.IsFinite(request.AimPoint)) return false;
            if (request.HasPosition)
            {
                if (!AttackData.IsFinite(request.Position)) return false;
            }
            else
            {
                Transform anchor = request.HasOrigin ? request.Origin : Origin;
                if (anchor == null || !AttackData.IsFinite(anchor.position)) return false;
            }
            if (Combat == null) return true;
            if ((Slot == PlayerActionSlot.PrimaryFire || Slot == PlayerActionSlot.SecondaryFire) && Combat.IsReloading)
                return false;
            return Slot != PlayerActionSlot.PrimaryFire || Combat.CurrentAmmo > 0;
        }

        protected override void OnAccepted(in PlayerActionContext request)
        {
            pendingOrigin = request.HasOrigin ? request.Origin : Origin;
            fixedPosition = request.HasPosition;
            pendingPosition = request.Position;
            hasRequestedAim = request.HasAimPoint;
            requestedAimPoint = request.AimPoint;
            if (Slot == PlayerActionSlot.Skill1 || Slot == PlayerActionSlot.Skill2 || Slot == PlayerActionSlot.Ultimate)
                Combat?.CancelReload();
        }

        protected override PlayerActionResult ExecuteCore(in PlayerActionContext request)
        {
            if (!fixedPosition && pendingOrigin == null) return PlayerActionResult.Failed;
            Vector3 position = fixedPosition ? pendingPosition : pendingOrigin.position;
            Vector3 point = Combat != null ? Combat.GetAimPoint() :
                hasRequestedAim ? requestedAimPoint : position + transform.forward;
            return attack.TryExecute(this, position, point, DamageMultiplier)
                ? PlayerActionResult.Completed : PlayerActionResult.Failed;
        }

        protected override void OnExecutionSucceeded()
        {
            if (Slot == PlayerActionSlot.PrimaryFire) Combat?.ConsumePrimaryAmmo();
            Combat?.NotifyAttackExecuted(Slot);
        }
    }
}
