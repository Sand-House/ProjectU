using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(50)]
    public sealed class SoilderAnimation : PlayerAnimationBase
    {
        private const int UpperBodyLayer = 1;
        private static readonly int Fire = Animator.StringToHash("Fire");
        private static readonly int Reload = Animator.StringToHash("Reload");
        private static readonly int ReloadSpeedMultiplier = Animator.StringToHash("ReloadSpeedMultiplier");
        private static readonly int Skill1 = Animator.StringToHash("Skill1");
        private static readonly int TossGrenade = Animator.StringToHash("Toss Grenade");
        private static readonly int RifleAim = Animator.StringToHash("Upper Body.Rifle Aim");

        private PlayerStat stat;
        private bool reloadVisualActive;

        public bool IsReloading => Combat != null && Combat.IsReloading;
        public bool IsThrowingGrenade => AnimatorComponent != null &&
            (AnimatorComponent.GetCurrentAnimatorStateInfo(UpperBodyLayer).shortNameHash == TossGrenade ||
             (AnimatorComponent.IsInTransition(UpperBodyLayer) &&
              AnimatorComponent.GetNextAnimatorStateInfo(UpperBodyLayer).shortNameHash == TossGrenade));
        public override bool CanApplyAimPose => !IsReloading && !IsThrowingGrenade;

        protected override bool InitializeCharacterAnimation()
        {
            stat = GetComponent<PlayerStat>();
            if (stat != null && AnimatorComponent.isHuman && AnimatorComponent.layerCount > UpperBodyLayer) return true;
            Debug.LogError("SoilderAnimation requires PlayerStat and a Humanoid Animator with an upper-body layer.", this);
            return false;
        }

        protected override void EnableCharacterAnimation()
        {
            Combat.OnReloadStarted += OnReloadStarted;
            Combat.OnReloadCompleted += OnReloadCompleted;
            if (Combat.IsReloading) OnReloadStarted();
            Motor.OnDashExecuted += OnDashExecuted;
            // Catch up if this visual component was disabled when the dash began.
            if (Motor.IsDashActionLocked) OnDashExecuted();
        }

        protected override void DisableCharacterAnimation()
        {
            Combat.OnReloadStarted -= OnReloadStarted;
            Combat.OnReloadCompleted -= OnReloadCompleted;
            Motor.OnDashExecuted -= OnDashExecuted;
            reloadVisualActive = false;
        }

        protected override void UpdateCharacterAnimation()
        {
            AnimatorComponent.SetFloat(ReloadSpeedMultiplier, stat.ReloadSpeedMultiplier);
            // Cancellation has no completion event and must never refill the magazine.
            if (reloadVisualActive && !IsReloading) ReturnToRifleAim();
        }

        protected override void HandleAttackExecuted(PlayerActionSlot slot)
        {
            if (slot == PlayerActionSlot.Skill1)
            {
                // Visual playback only: the attack has already launched its grenade.
                reloadVisualActive = false;
                AnimatorComponent.ResetTrigger(Fire);
                AnimatorComponent.ResetTrigger(Reload);
                AnimatorComponent.SetTrigger(Skill1);
                return;
            }
            if (slot != PlayerActionSlot.PrimaryFire) return;
            reloadVisualActive = false;
            AnimatorComponent.ResetTrigger(Skill1);
            AnimatorComponent.ResetTrigger(Reload);
            AnimatorComponent.SetTrigger(Fire);
        }

        protected override void ResetCharacterAnimation() => ReturnToRifleAim();

        private void OnReloadStarted()
        {
            reloadVisualActive = true;
            AnimatorComponent.SetFloat(ReloadSpeedMultiplier, stat.ReloadSpeedMultiplier);
            AnimatorComponent.ResetTrigger(Fire);
            AnimatorComponent.ResetTrigger(Skill1);
            AnimatorComponent.SetTrigger(Reload);
        }

        private void OnReloadCompleted() => ReturnToRifleAim();
        private void OnDashExecuted() => ReturnToRifleAim();

        private void ReturnToRifleAim()
        {
            reloadVisualActive = false;
            AnimatorComponent.ResetTrigger(Fire);
            AnimatorComponent.ResetTrigger(Reload);
            AnimatorComponent.ResetTrigger(Skill1);
            AnimatorComponent.CrossFadeInFixedTime(RifleAim, 0.05f, UpperBodyLayer);
        }
    }
}
