using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(PlayerStat))]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerCombat : MonoBehaviour, IPlayerStageSubscriber
    {
        [SerializeField] private PlayerActionBinding[] actionBindings = Array.Empty<PlayerActionBinding>();
        [SerializeField] private Camera aimCamera;
        [SerializeField, Min(1f)] private float aimDistance = 100f;
        private PlayerStat stat;
        private Combatant owner;
        private int currentAmmo;
        private PlayerActionSlot requestedAction = PlayerActionSlot.None;
        private int requestFrame = -1;
        private int reloadCompletedFrame = -1;
        private PlayerStageSubscription stageEvents;
        private PlayerStageSubscription StageEvents => stageEvents ?? (stageEvents = new PlayerStageSubscription(
            this, HandleStageStateChanged, HandleGameplayEnabledChanged));
        void IPlayerStageSubscriber.BindStageEvents(GameManager game) => StageEvents.Bind(game);
        void IPlayerStageSubscriber.UnbindStageEvents(GameManager game) => stageEvents?.Unbind(game);
        private void HandleStageStateChanged(StageState state)
        {
            if (!StageEvents.IsCurrent(state)) return;
            ClearRequests();
            if (state != StageState.OnStage) InterruptInput();
        }
        private void HandleGameplayEnabledChanged(bool enabled)
        {
            ClearRequests();
            if (!enabled) InterruptInput();
        }
        private Vector3 dashDirection;
        private readonly RaycastHit[] aimHits = new RaycastHit[32];
        private readonly Dictionary<PlayerActionBase, Action> executionHandlers = new Dictionary<PlayerActionBase, Action>();

        public event Action<PlayerActionSlot> AttackExecuted;
        public event Action<PlayerActionSlot> ActionExecuted;
        public event Action OnReloadStarted;
        public event Action OnReloadCompleted;
        public IReadOnlyList<PlayerActionBinding> ActionBindings => actionBindings;
        public Camera AimCamera => aimCamera;
        public int CurrentAmmo => Mathf.Min(currentAmmo, MaxAmmo);
        public int MaxAmmo => Stat.MaxMagazine;
        private PlayerStat Stat => stat != null ? stat : (stat = GetComponent<PlayerStat>());
        private IPlayerReloadAction Reload => GetAction(PlayerActionSlot.Reload) as IPlayerReloadAction;
        public bool IsReloading => Reload != null && Reload.IsReloading;
        public float ReloadRemaining => Reload != null ? Reload.ReloadRemaining : 0f;
        public bool CanReload => CanStart(PlayerActionSlot.Reload);
        internal bool CanReloadIgnoringActionLock => CanBuffer(PlayerActionSlot.Reload);
        internal Vector3 DashDirection => dashDirection.sqrMagnitude > 0.0001f ? dashDirection : transform.forward;

        public PlayerActionBinding GetBinding(PlayerActionSlot slot)
        {
            foreach (PlayerActionBinding binding in actionBindings)
                if (binding != null && binding.Slot == slot) return binding;
            return null;
        }

        public PlayerActionBase GetAction(PlayerActionSlot slot) => GetBinding(slot)?.Action;
        public bool CanStart(PlayerActionSlot slot) => isActiveAndEnabled && GetAction(slot) != null && GetAction(slot).CanStart;
        public bool CanBuffer(PlayerActionSlot slot) => isActiveAndEnabled && GetAction(slot) != null && GetAction(slot).CanBuffer;
        internal bool IsDelayBlocking(PlayerActionSlot slot) => owner != null && owner.IsDelayBlocking(slot);
        // Ordinary action delays may retain a reservation; persistent restrictions may not.
        internal bool IsBufferBlocked(PlayerActionSlot slot) => owner != null && owner.BlocksAction(slot, true);

        private void Awake()
        {
            stat = GetComponent<PlayerStat>();
            owner = GetComponent<Combatant>();
            currentAmmo = MaxAmmo;
        }

        private void OnEnable()
        {
            if (!ValidateBindings()) { enabled = false; return; }
            foreach (PlayerActionBinding binding in actionBindings)
            {
                PlayerActionBase action = binding.Action;
                Action handler = () => ActionExecuted?.Invoke(binding.Slot);
                executionHandlers.Add(action, handler);
                action.OnExecuted += handler;
            }
            stageEvents?.Enable();
        }

        private bool ValidateBindings()
        {
            var slots = new HashSet<PlayerActionSlot>();
            var actions = new HashSet<PlayerActionBase>();
            var attacks = new HashSet<AttackBase>();
            foreach (PlayerActionBinding binding in actionBindings)
            {
                if (binding == null || binding.Slot == PlayerActionSlot.None ||
                    !Enum.IsDefined(typeof(PlayerActionSlot), binding.Slot) || binding.Action == null ||
                    binding.Action.gameObject != gameObject || binding.Action.Slot != binding.Slot ||
                    !slots.Add(binding.Slot) || !actions.Add(binding.Action) ||
                    (binding.Attack != null && !attacks.Add(binding.Attack)))
                {
                    Debug.LogError("PlayerCombat requires unique slots and actions on this player; attack executors cannot be shared by actions.", this);
                    return false;
                }
            }
            return true;
        }

        internal void SetMovementContext(Vector3 movement, Vector3 forward)
            => dashDirection = movement.sqrMagnitude > 0.0001f ? movement : forward;

        internal void ProcessBeforeMotor()
        {
            if (!isActiveAndEnabled) return;
            foreach (PlayerActionBinding binding in actionBindings)
                binding?.Action?.Process(PlayerActionStage.BeforeMotor);
        }

        private void Prepare(PlayerActionBase action)
        {
            if (action is PlayerAttackActionBase attackAction)
            {
                attackAction.DamageMultiplier = Stat.AttackMultiplier;
                attackAction.SpeedMultiplier = Stat.AttackSpeed;
            }
        }

        public bool TryStartAction(PlayerActionSlot slot)
        {
            if (!isActiveAndEnabled) return false;
            PlayerActionBase action = GetAction(slot);
            if (action == null) return false;
            Prepare(action);
            return action.TryStart();
        }

        // Commands last for one LateUpdate. Controller alone owns input history and priority.
        public void RequestAction(PlayerActionSlot slot)
        {
            if (slot == PlayerActionSlot.Dash) { TryStartAction(slot); return; }
            requestedAction = slot;
            requestFrame = Time.frameCount;
        }

        public void RequestReload() => RequestAction(PlayerActionSlot.Reload);
        public void ClearRequests() { requestedAction = PlayerActionSlot.None; requestFrame = -1; }

        internal void InterruptInput()
        {
            ClearRequests();
            foreach (PlayerActionBinding binding in actionBindings) binding?.Action?.InterruptInput();
        }

        internal void DeliverInput(PlayerActionSlot slot, ButtonInputState input)
        {
            PlayerActionBase action = GetAction(slot);
            if (action != null && (input.Released || (action.RequiresHeldInput && !input.Held))) action.Release();
        }

        private void LateUpdate()
        {
            PlayerActionSlot command = requestFrame == Time.frameCount ? requestedAction : PlayerActionSlot.None;
            ClearRequests();
            currentAmmo = CurrentAmmo;
            if (owner != null && !owner.IsAlive)
            {
                foreach (PlayerActionBinding binding in actionBindings) binding.Action.Cancel(ActionCancelReason.Death);
                return;
            }
            if (Time.timeScale <= 0f || Time.deltaTime <= 0f) return;
            bool wasReloading = IsReloading;
            foreach (PlayerActionBinding binding in actionBindings) Prepare(binding.Action);
            // Accepted skills cancel reload before this frame's reload work or completion.
            if (command != PlayerActionSlot.None && command != PlayerActionSlot.PrimaryFire &&
                command != PlayerActionSlot.Reload) TryStartAction(command);
            foreach (PlayerActionBinding binding in actionBindings)
                binding.Action.Process(PlayerActionStage.CombatLateUpdate);
            if (command == PlayerActionSlot.Reload) TryReload();
            if (wasReloading || IsReloading || reloadCompletedFrame == Time.frameCount || CurrentAmmo == 0) return;
            if (command == PlayerActionSlot.PrimaryFire) TryStartAction(command);
        }

        public bool TryReload() => TryStartAction(PlayerActionSlot.Reload);
        public bool CancelReload() => GetAction(PlayerActionSlot.Reload)?.Cancel() == true;
        internal void ConsumePrimaryAmmo() => currentAmmo = Mathf.Max(0, CurrentAmmo - 1);
        internal void NotifyAttackExecuted(PlayerActionSlot slot) => AttackExecuted?.Invoke(slot);
        internal void NotifyReloadStarted() => OnReloadStarted?.Invoke();
        internal void CompleteReload()
        {
            currentAmmo = MaxAmmo;
            reloadCompletedFrame = Time.frameCount;
            OnReloadCompleted?.Invoke();
        }

        public Vector3 GetAimPoint()
        {
            if (aimCamera == null) return transform.position + transform.forward * aimDistance;
            Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            Vector3 aimPoint = ray.GetPoint(aimDistance);
            float closest = aimDistance;
            int mask = 0;
            foreach (PlayerActionBinding binding in actionBindings)
                if (binding != null && binding.Attack != null && binding.Attack.Data != null)
                    mask |= binding.Attack.Data.HitMask;
            int count = Physics.RaycastNonAlloc(ray, aimHits, aimDistance, mask, QueryTriggerInteraction.Collide);
            RaycastHit[] hits = count == aimHits.Length
                ? Physics.RaycastAll(ray, aimDistance, mask, QueryTriggerInteraction.Collide) : aimHits;
            if (hits != aimHits) count = hits.Length;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<ProjectileBase>() != null) continue;
                if (hit.collider.isTrigger && !Hitbox.TryResolve(hit.collider, out _, out _)) continue;
                if (hit.distance >= closest) continue;
                closest = hit.distance;
                aimPoint = hit.point;
            }
            return aimPoint;
        }

        private void OnDisable()
        {
            stageEvents?.Disable();
            ClearRequests();
            foreach (PlayerActionBinding binding in actionBindings)
                if (binding?.Action != null) binding.Action.Cancel(ActionCancelReason.Disabled);
            foreach (KeyValuePair<PlayerActionBase, Action> handler in executionHandlers)
                if (handler.Key != null) handler.Key.OnExecuted -= handler.Value;
            executionHandlers.Clear();
        }
    }
}
