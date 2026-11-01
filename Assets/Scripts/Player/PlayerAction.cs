using UnityEngine;

namespace ProjectU
{
    public enum PlayerActionSlot
    {
        None = -1, PrimaryFire = 0, SecondaryFire = 1, Skill1 = 2,
        Skill2 = 3, Ultimate = 4, Dash = 5, Reload = 6
    }
    public enum PlayerActionStage { BeforeMotor, CombatLateUpdate, AfterAim }
    public interface IPlayerReloadAction
    {
        bool IsReloading { get; }
        float ReloadRemaining { get; }
    }

    [DefaultExecutionOrder(110)]
    public abstract class PlayerAction : ActionBase
    {
        [SerializeField] private bool repeatWhileHeld;
        private PlayerCombat combat;
        private PlayerMotor motor;
        public abstract PlayerActionSlot Slot { get; }
        public virtual PlayerActionStage Stage => PlayerActionStage.AfterAim;
        public virtual bool RequiresHeldInput => false;
        public bool RepeatWhileHeld => repeatWhileHeld;
        public bool CanBuffer => Evaluate(default, true);
        protected PlayerCombat Combat => combat != null ? combat : (combat = GetComponent<PlayerCombat>());
        protected PlayerMotor Motor => motor != null ? motor : (motor = GetComponent<PlayerMotor>());
        protected override bool CanRequest => Combat == null || Combat.isActiveAndEnabled;
        protected override bool IsGrounded => Motor != null && Motor.IsGrounded;
        protected override bool ExecuteImmediately => Stage != PlayerActionStage.AfterAim;

        public bool Blocks(PlayerActionSlot slot) => HasActionLock && BlocksCore(slot);
        protected virtual bool BlocksCore(PlayerActionSlot slot) => true;
        protected override bool BlocksCore(ActionBase requested)
            => requested is PlayerAction player ? BlocksCore(player.Slot) : base.BlocksCore(requested);
        internal void Process(PlayerActionStage stage)
        {
            if (stage == Stage) ProcessAction();
        }
        public void Release()
        {
            if (RequiresHeldInput) Cancel(ActionCancelReason.Released);
        }
        internal void InterruptInput()
        {
            if (RequiresHeldInput) Cancel(ActionCancelReason.InputInterrupted);
        }
        protected virtual void LateUpdate() => Process(PlayerActionStage.AfterAim);
    }
}
