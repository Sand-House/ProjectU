using UnityEngine;

namespace ProjectU
{
    public sealed class SoilderReloadAction : PlayerAction, IPlayerReloadAction
    {
        private PlayerStat stat;
        private float workRemaining;
        private PlayerStat Stat => stat != null ? stat : (stat = GetComponent<PlayerStat>());
        public override PlayerActionSlot Slot => PlayerActionSlot.Reload;
        public override PlayerActionStage Stage => PlayerActionStage.CombatLateUpdate;
        protected override bool UsesDelayLock => false;
        public bool IsReloading => Phase == ActionPhase.Active;
        public float ReloadRemaining => IsReloading ? workRemaining / Stat.ReloadSpeedMultiplier : 0f;

        protected override bool BlocksCore(PlayerActionSlot slot)
            => slot == PlayerActionSlot.PrimaryFire || slot == PlayerActionSlot.SecondaryFire || slot == PlayerActionSlot.Reload;

        protected override bool CheckRequirements(in ActionContext request)
            => Combat != null && Combat.isActiveAndEnabled && Stat != null && Combat.CurrentAmmo < Combat.MaxAmmo;

        protected override ActionResult ExecuteCore(in ActionContext request)
        {
            workRemaining = Stat.ReloadDuration;
            return ActionResult.Running;
        }

        protected override void OnExecutionSucceeded() => Combat.NotifyReloadStarted();

        protected override void TickActive(float deltaTime)
        {
            if (Combat == null || !Combat.isActiveAndEnabled || Owner.BlocksAction(Slot, false, this))
            {
                Cancel();
                return;
            }
            workRemaining = Mathf.Max(0f, workRemaining - deltaTime * Stat.ReloadSpeedMultiplier);
            if (workRemaining > 0f) return;
            FinishActive();
            Combat.CompleteReload();
        }

        protected override void Cleanup(ActionCancelReason reason) => workRemaining = 0f;
    }
}
