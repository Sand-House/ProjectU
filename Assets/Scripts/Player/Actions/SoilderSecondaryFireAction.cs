using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(PlayerADS))]
    public sealed class SoilderSecondaryFireAction : PlayerActionBase
    {
        private PlayerADS ads;
        private PlayerADS ADS => ads != null ? ads : (ads = GetComponent<PlayerADS>());

        public override PlayerActionSlot Slot => PlayerActionSlot.SecondaryFire;
        public override PlayerActionStage Stage => PlayerActionStage.BeforeMotor;
        public override bool RequiresHeldInput => true;
        protected override bool UsesDelayLock => false;

        // This is a persistent stance, not an attack delay that buffering may bypass.
        protected override bool BlocksCore(PlayerActionSlot slot) => slot != PlayerActionSlot.PrimaryFire;
        protected override bool CheckRequirements(in ActionContext request)
            => Combat != null && Combat.isActiveAndEnabled && Motor != null && Motor.isActiveAndEnabled && !Motor.IsDashing &&
                ADS != null && ADS.CanBegin(this);
        // Preserve the existing stance timing: effects begin at acceptance, including a configured windup.
        protected override void OnAccepted(in ActionContext request) => ADS.TryBegin(this);
        protected override ActionResult ExecuteCore(in ActionContext request) => ActionResult.Running;
        protected override void TickActive(float deltaTime)
        {
            if (!CheckRequirements(default)) Cancel(ActionCancelReason.Disabled);
        }

        protected override void Cleanup(ActionCancelReason reason) => ADS?.End(this);
    }
}

