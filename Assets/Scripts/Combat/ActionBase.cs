using System;
using UnityEngine;

namespace ProjectU
{
    // Only actions own request timing. Attack is a synchronous effect executor.
    [DefaultExecutionOrder(110)]
    [RequireComponent(typeof(Combatant))]
    public abstract class ActionBase : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float windupTime;
        [SerializeField, Min(0f)] private float recoveryTime;
        [SerializeField, Min(0f)] private float cooldown;
        [SerializeField, Tooltip("Require ground contact when accepting and executing this action.")]
        private bool requiresGrounded;
        [SerializeField, Tooltip("Allow normal movement and jumping during windup, active execution and recovery.")]
        private bool canMove = true;

        private Combatant owner;
        private bool pending, active, recoveryPending, completionPending;
        private bool executing;
        private double executeAt, recoveryEndsAt, cooldownEndsAt;
        private float executionRecovery;
        private float speedMultiplier = 1f;
        private int lastBlockingFrame = -1, lastTickFrame = -1;
        private ActionContext context;

        public virtual bool IsAttackAction => false;
        protected virtual bool UsesDelayLock => true;
        protected virtual bool WaitForRecoveryTick => false;
        public Combatant Owner => owner != null ? owner : (owner = GetComponent<Combatant>());
        public float WindupTime => AttackData.FiniteAtLeast(windupTime, 0f);
        public float RecoveryTime => AttackData.FiniteAtLeast(recoveryTime, 0f);
        public float Cooldown => AttackData.FiniteAtLeast(cooldown, 0f);
        public bool RequiresGrounded => requiresGrounded;
        public bool CanMove => canMove;
        public bool BlocksMovement => !CanMove && HasActionLock;
        public float SpeedMultiplier
        {
            get => speedMultiplier;
            set => speedMultiplier = AttackData.FiniteAtLeast(value, 0.01f);
        }
        public ActionPhase Phase => pending ? ActionPhase.Windup :
            active ? ActionPhase.Active :
            recoveryPending && (WaitForRecoveryTick || Time.timeAsDouble < recoveryEndsAt)
                ? ActionPhase.Recovery : ActionPhase.Ready;
        public bool IsActive => Phase != ActionPhase.Ready;
        public float CooldownRemaining => (float)Math.Max(0d, cooldownEndsAt - Time.timeAsDouble);
        public float RecoveryRemaining => (float)Math.Max(0d, recoveryEndsAt - Time.timeAsDouble);
        public bool CanStart => CanStartWithContext(default);
        internal bool IsExecuting => executing;
        internal bool IsDelayLocked => UsesDelayLock && HasActionLock;
        protected bool HasActionLock => isActiveAndEnabled && Owner != null && Owner.IsAlive &&
            (IsActive || lastBlockingFrame == Time.frameCount);

        public event Action OnStarted;
        public event Action OnExecuted;
        public event Action OnCompleted;
        public event Action OnFailed;
        public event Action<ActionCancelReason> OnCancelled;

        public bool Blocks(ActionBase requested) => HasActionLock && BlocksCore(requested);
        protected virtual bool BlocksCore(ActionBase requested) => true;
        public bool CanStartWithContext(in ActionContext request) => Evaluate(request, false);

        protected bool Evaluate(in ActionContext request, bool ignoreDelay)
            => isActiveAndEnabled && CanRequest &&
                Time.timeScale > 0f && Time.deltaTime > 0f &&
                Owner != null && Owner.IsAlive && !Owner.IsGameplayBlocked && CooldownRemaining <= 0f &&
                (ignoreDelay ? (UsesDelayLock || (!pending && !active)) :
                    (!IsActive && lastBlockingFrame != Time.frameCount)) &&
                MeetsGroundRequirement && CheckRequirements(request) && !Owner.BlocksAction(this, ignoreDelay);

        protected virtual bool CanRequest => true;
        protected virtual bool IsGrounded => false;
        protected virtual bool ExecuteImmediately => false;
        private bool MeetsGroundRequirement => !RequiresGrounded || IsGrounded;

        public bool TryStart() => TryStart(default);

        public bool TryStart(in ActionContext request)
        {
            if (!CanStartWithContext(request)) return false;
            context = request;
            pending = true;
            completionPending = true;
            executeAt = Time.timeAsDouble + WindupTime / SpeedMultiplier;
            OnAccepted(request);
            OnStarted?.Invoke();
            // Dash must enter before Motor.Update; public reload requests start immediately.
            if (ExecuteImmediately && WindupTime <= 0f) ExecutePending();
            return true;
        }

        protected virtual void OnAccepted(in ActionContext request) { }
        protected abstract bool CheckRequirements(in ActionContext request);
        protected virtual bool CheckExecutionRequirements(in ActionContext request) => CheckRequirements(request);
        protected abstract ActionResult ExecuteCore(in ActionContext request);
        protected virtual void TickActive(float deltaTime) { }
        protected virtual void OnExecutionSucceeded() { }
        protected virtual void Cleanup(ActionCancelReason reason) { }

        protected void ProcessAction()
        {
            if (!isActiveAndEnabled) return;
            if (Owner == null || !Owner.IsAlive) { Cancel(ActionCancelReason.Death); return; }
            if (Owner.IsGameplayBlocked) { CancelAndClear(ActionCancelReason.StageTransition); return; }
            if (Time.timeScale <= 0f || Time.deltaTime <= 0f) return;
            if (lastTickFrame == Time.frameCount) return;
            lastTickFrame = Time.frameCount;
            if (WaitForRecoveryTick && recoveryPending) lastBlockingFrame = Time.frameCount;
            ExecutePending();
            if (active && lastBlockingFrame != Time.frameCount) TickActive(Time.deltaTime);
            if (recoveryPending && Time.timeAsDouble >= recoveryEndsAt) recoveryPending = false;
            if (completionPending && !pending && !active && !recoveryPending)
            {
                completionPending = false;
                OnCompleted?.Invoke();
            }
        }

        private void ExecutePending()
        {
            if (!pending || !isActiveAndEnabled || Time.timeScale <= 0f ||
                Time.timeAsDouble < executeAt) return;
            pending = false;
            lastBlockingFrame = Time.frameCount;
            if (Owner == null || !Owner.IsAlive || Owner.IsGameplayBlocked || !MeetsGroundRequirement || !CheckExecutionRequirements(context) ||
                Owner.BlocksAction(this, false, this))
            {
                completionPending = false;
                OnFailed?.Invoke();
                return;
            }
            executing = true;
            ActionResult result;
            try { result = ExecuteCore(context); }
            finally { executing = false; }
            if (result == ActionResult.Failed || !isActiveAndEnabled || Owner == null || !Owner.IsAlive)
            {
                completionPending = false;
                OnFailed?.Invoke();
                return;
            }
            executionRecovery = RecoveryTime / SpeedMultiplier;
            cooldownEndsAt = Time.timeAsDouble + Cooldown / SpeedMultiplier;
            active = result == ActionResult.Running;
            if (!active) BeginRecovery(Time.timeAsDouble);
            OnExecutionSucceeded();
            OnExecuted?.Invoke();
        }

        protected void FinishActive() => FinishActive(Time.timeAsDouble);
        protected void FinishActive(double completedAt)
        {
            if (!active) return;
            active = false;
            lastBlockingFrame = Time.frameCount;
            BeginRecovery(completedAt);
        }

        private void BeginRecovery(double completedAt)
        {
            recoveryEndsAt = completedAt + executionRecovery;
            recoveryPending = executionRecovery > 0f;
        }

        // Dependency teardown may release a lock without resetting an earned cooldown.
        protected void ClearActionLock()
        {
            recoveryPending = completionPending = false;
            recoveryEndsAt = 0d;
            lastBlockingFrame = -1;
        }

        internal void FinishFromMovement(double completedAt) => FinishActive(completedAt);
        internal void CancelFromDependency()
        {
            Cancel(ActionCancelReason.Disabled);
            ClearActionLock();
        }

        public bool Cancel(ActionCancelReason reason = ActionCancelReason.Explicit)
        {
            if (!pending && !active) return false;
            pending = active = completionPending = false;
            Cleanup(reason);
            OnCancelled?.Invoke(reason);
            return true;
        }

        protected virtual void OnEnable()
        {
            if (Owner != null) Owner.Mortal.OnDead += HandleDeath;
        }
        private void HandleDeath(GameObject attacker) => CancelAndClear(ActionCancelReason.Death);
        internal void CancelAndClear(ActionCancelReason reason)
        {
            Cancel(reason);
            ClearActionLock();
        }

        public void ResetForStage()
        {
            CancelAndClear(ActionCancelReason.StageTransition);
            executeAt = cooldownEndsAt = 0d;
            executionRecovery = 0f;
            context = default;
            lastTickFrame = -1;
        }
        protected virtual void OnDisable()
        {
            if (Owner != null) Owner.Mortal.OnDead -= HandleDeath;
            CancelAndClear(ActionCancelReason.Disabled);
        }

        protected virtual void OnValidate()
        {
            windupTime = WindupTime; recoveryTime = RecoveryTime; cooldown = Cooldown;
        }
    }
}
