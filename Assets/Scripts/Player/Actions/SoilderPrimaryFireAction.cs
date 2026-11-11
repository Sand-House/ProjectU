using UnityEngine;

namespace ProjectU
{
    public sealed class SoilderPrimaryFireAction : PlayerAttackAction
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.PrimaryFire;
        private PlayerCamera aimView;
        private PlayerADS ads;
        private PlayerADS ADS => ads != null ? ads : (ads = GetComponent<PlayerADS>());

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
            hitscan.SpreadAngleMultiplier = previous * (ADS != null && ADS.IsAiming ? ADS.SpreadAngleMultiplier : 1f);
            try { return base.ExecuteCore(request); }
            finally { hitscan.SpreadAngleMultiplier = previous; }
        }
    }
}

