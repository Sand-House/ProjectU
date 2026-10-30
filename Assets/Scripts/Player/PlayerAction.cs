using System;
using UnityEngine;

namespace ProjectU
{
    public enum PlayerActionSlot
    {
        None = -1, PrimaryFire = 0, SecondaryFire = 1, Skill1 = 2,
        Skill2 = 3, Ultimate = 4, Dash = 5, Reload = 6
    }

    public enum PlayerActionPhase { Ready, Windup, Active, Recovery }
    public enum PlayerActionStage { BeforeMotor, CombatLateUpdate, AfterAim }
    public enum PlayerActionResult { Failed, Completed, Running }
    public enum PlayerActionCancelReason { Explicit, Released, InputInterrupted, Disabled, Death }

    public interface IPlayerReloadAction
    {
        bool IsReloading { get; }
        float ReloadRemaining { get; }
    }

    // Optional execution anchors, never stored in the controller's input buffer.
    public readonly struct PlayerActionContext
    {
        public readonly Transform Origin;
        public readonly Vector3 Position;
        public readonly Vector3 AimPoint;
        public readonly Vector3 Direction;
        public readonly bool HasOrigin, HasPosition, HasAimPoint, HasDirection;

        private PlayerActionContext(Transform origin, Vector3 position, Vector3 aimPoint,
            Vector3 direction, bool hasOrigin, bool hasPosition, bool hasAimPoint, bool hasDirection)
        {
            Origin = origin; Position = position; AimPoint = aimPoint; Direction = direction;
            HasOrigin = hasOrigin; HasPosition = hasPosition;
            HasAimPoint = hasAimPoint; HasDirection = hasDirection;
        }

        public static PlayerActionContext Aim(Transform origin, Vector3 point)
            => new PlayerActionContext(origin, default, point, default, true, false, true, false);
        public static PlayerActionContext Aim(Vector3 position, Vector3 point)
            => new PlayerActionContext(null, position, point, default, false, true, true, false);
        public static PlayerActionContext Move(Vector3 direction)
            => new PlayerActionContext(null, default, default, direction, false, false, false, true);
    }

    // Only actions own request timing. Attack is a synchronous effect executor.
    [DefaultExecutionOrder(110)]
    [RequireComponent(typeof(Combatant))]
    public abstract class PlayerAction : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float windupTime;
        [SerializeField, Min(0f)] private float recoveryTime;
        [SerializeField, Min(0f)] private float cooldown;
        [SerializeField] private bool repeatWhileHeld;

        private Combatant owner;
        private PlayerCombat combat;
        private bool pending, active, recoveryPending, completionPending;
        private bool executing;
        private double executeAt, recoveryEndsAt, cooldownEndsAt;
        private float executionRecovery;
        private float speedMultiplier = 1f;
        private int lastBlockingFrame = -1, lastTickFrame = -1;
        private PlayerActionContext context;

        public abstract PlayerActionSlot Slot { get; }
        public virtual PlayerActionStage Stage => PlayerActionStage.AfterAim;
        public virtual bool RequiresHeldInput => false;
        public virtual bool IsAttackAction => false;
        protected virtual bool UsesDelayLock => true;
        protected virtual bool WaitForRecoveryTick => false;
        public Combatant Owner => owner != null ? owner : (owner = GetComponent<Combatant>());
        protected PlayerCombat Combat => combat != null ? combat : (combat = GetComponent<PlayerCombat>());
        public float WindupTime => AttackData.FiniteAtLeast(windupTime, 0f);
        public float RecoveryTime => AttackData.FiniteAtLeast(recoveryTime, 0f);
        public float Cooldown => AttackData.FiniteAtLeast(cooldown, 0f);
        public bool RepeatWhileHeld => repeatWhileHeld;
        public float SpeedMultiplier
        {
            get => speedMultiplier;
            set => speedMultiplier = AttackData.FiniteAtLeast(value, 0.01f);
        }
        public PlayerActionPhase Phase => pending ? PlayerActionPhase.Windup :
            active ? PlayerActionPhase.Active :
            recoveryPending && (WaitForRecoveryTick || Time.timeAsDouble < recoveryEndsAt)
                ? PlayerActionPhase.Recovery : PlayerActionPhase.Ready;
        public bool IsActive => Phase != PlayerActionPhase.Ready;
        public float CooldownRemaining => (float)Math.Max(0d, cooldownEndsAt - Time.timeAsDouble);
        public float RecoveryRemaining => (float)Math.Max(0d, recoveryEndsAt - Time.timeAsDouble);
        public bool CanStart => CanStartWithContext(default);
        public bool CanBuffer => Evaluate(default, true);
        internal bool IsExecuting => executing;
        internal bool IsDelayLocked => UsesDelayLock && HasActionLock;
        private bool HasActionLock => isActiveAndEnabled && Owner != null && Owner.IsAlive &&
            (IsActive || lastBlockingFrame == Time.frameCount);

        public event Action OnStarted;
        public event Action OnExecuted;
        public event Action OnCompleted;
        public event Action OnFailed;
        public event Action<PlayerActionCancelReason> OnCancelled;

        public bool Blocks(PlayerActionSlot slot) => HasActionLock && BlocksCore(slot);
        protected virtual bool BlocksCore(PlayerActionSlot slot) => true;
        public bool CanStartWithContext(in PlayerActionContext request) => Evaluate(request, false);

        private bool Evaluate(in PlayerActionContext request, bool ignoreDelay)
            => isActiveAndEnabled && (Combat == null || Combat.isActiveAndEnabled) &&
                Time.timeScale > 0f && Time.deltaTime > 0f &&
                Owner != null && Owner.IsAlive && CooldownRemaining <= 0f &&
                (ignoreDelay ? (UsesDelayLock || (!pending && !active)) :
                    (!IsActive && lastBlockingFrame != Time.frameCount)) &&
                CheckRequirements(request) && !Owner.BlocksAction(Slot, ignoreDelay);

        public bool TryStart() => TryStart(default);

        public bool TryStart(in PlayerActionContext request)
        {
            if (!CanStartWithContext(request)) return false;
            context = request;
            pending = true;
            completionPending = true;
            executeAt = Time.timeAsDouble + WindupTime / SpeedMultiplier;
            OnAccepted(request);
            OnStarted?.Invoke();
            // Dash must enter before Motor.Update; public reload requests start immediately.
            if (Stage != PlayerActionStage.AfterAim && WindupTime <= 0f) ExecutePending();
            return true;
        }

        protected virtual void OnAccepted(in PlayerActionContext request) { }
        protected abstract bool CheckRequirements(in PlayerActionContext request);
        protected abstract PlayerActionResult ExecuteCore(in PlayerActionContext request);
        protected virtual void TickActive(float deltaTime) { }
        protected virtual void OnExecutionSucceeded() { }
        protected virtual void Cleanup(PlayerActionCancelReason reason) { }

        internal void Process(PlayerActionStage stage)
        {
            if (stage != Stage || !isActiveAndEnabled) return;
            if (Owner == null || !Owner.IsAlive) { Cancel(PlayerActionCancelReason.Death); return; }
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
            if (Owner == null || !Owner.IsAlive || !CheckRequirements(context) ||
                Owner.BlocksAction(Slot, false, this))
            {
                completionPending = false;
                OnFailed?.Invoke();
                return;
            }
            executing = true;
            PlayerActionResult result;
            try { result = ExecuteCore(context); }
            finally { executing = false; }
            if (result == PlayerActionResult.Failed)
            {
                completionPending = false;
                OnFailed?.Invoke();
                return;
            }
            executionRecovery = RecoveryTime / SpeedMultiplier;
            cooldownEndsAt = Time.timeAsDouble + Cooldown / SpeedMultiplier;
            active = result == PlayerActionResult.Running;
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
            Cancel(PlayerActionCancelReason.Disabled);
            ClearActionLock();
        }

        public void Release()
        {
            if (RequiresHeldInput) Cancel(PlayerActionCancelReason.Released);
        }

        public bool Cancel(PlayerActionCancelReason reason = PlayerActionCancelReason.Explicit)
        {
            if (!pending && !active) return false;
            pending = active = completionPending = false;
            Cleanup(reason);
            OnCancelled?.Invoke(reason);
            return true;
        }

        internal void InterruptInput()
        {
            if (RequiresHeldInput) Cancel(PlayerActionCancelReason.InputInterrupted);
        }

        protected virtual void LateUpdate() => Process(PlayerActionStage.AfterAim);
        protected virtual void OnDisable() => Cancel(PlayerActionCancelReason.Disabled);
        protected virtual void OnValidate()
        {
            windupTime = WindupTime; recoveryTime = RecoveryTime; cooldown = Cooldown;
        }
    }
}
