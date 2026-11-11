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
    public abstract class PlayerAction : ActionBase, IPlayerStageSubscriber
    {
        [SerializeField] private bool repeatWhileHeld;
        private PlayerCombat combat;
        private PlayerMotor motor;
        private PlayerStageSubscription stageEvents;
        private int resetSequence;
        private PlayerStageSubscription StageEvents => stageEvents ?? (stageEvents = new PlayerStageSubscription(
            this, HandleStageStateChanged, HandleGameplayEnabledChanged, SynchronizeStageReset));
        void IPlayerStageSubscriber.BindStageEvents(GameManager game) => StageEvents.Bind(game);
        void IPlayerStageSubscriber.UnbindStageEvents(GameManager game) => stageEvents?.Unbind(game);
        private void HandleStageStateChanged(StageState state)
        {
            if (!StageEvents.IsCurrent(state)) return;
            if (state == StageState.AfterStage) SynchronizeStageReset(state);
            else if (state == StageState.BeforeStage) CancelAndClear(ActionCancelReason.StageTransition);
        }
        private void SynchronizeStageReset(StageState state)
        {
            if (StageEvents.StageClearSequence == resetSequence || Owner == null || !Owner.IsAlive) return;
            resetSequence = StageEvents.StageClearSequence;
            ResetForStage();
        }
        private void HandleGameplayEnabledChanged(bool enabled)
        {
            if (!enabled) CancelAndClear(ActionCancelReason.StageTransition);
        }
        protected override void OnEnable() { base.OnEnable(); stageEvents?.Enable(); }
        protected override void OnDisable() { stageEvents?.Disable(); base.OnDisable(); }
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
