using UnityEngine;

namespace ProjectU
{
    public sealed class SoilderPrimaryFireAction : PlayerAttackAction
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.PrimaryFire;
        private PlayerCamera aimView;

        protected override bool CheckRequirements(in ActionContext request)
        {
            Camera camera = Combat != null ? Combat.AimCamera : null;
            if (camera != null && (aimView == null || aimView.gameObject != camera.gameObject))
                aimView = camera.GetComponent<PlayerCamera>();
            return (camera == null || aimView == null || !aimView.IsAimTransitioning) && base.CheckRequirements(request);
        }

        protected override ActionResult ExecuteCore(in ActionContext request)
        {
            if (!(Attack is HitscanAttack hitscan)) return base.ExecuteCore(request);
            float previous = hitscan.SpreadAngleMultiplier;
            hitscan.SpreadAngleMultiplier = previous * (Combat != null ? Combat.AimSpreadAngleMultiplier : 1f);
            try { return base.ExecuteCore(request); }
            finally { hitscan.SpreadAngleMultiplier = previous; }
        }
    }
}

