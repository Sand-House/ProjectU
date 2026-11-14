using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerMotor))]
    [RequireComponent(typeof(PlayerStamina))]
    public sealed class PlayerController : MonoBehaviour, IPlayerStageSubscriber
    {
        [SerializeField] private PlayerCamera playerCamera;
        [SerializeField, Min(0f), Tooltip("Maximum game seconds to keep one eligible pressed action. Zero disables buffering.")]
        private float maxInputBufferTime = 1f;
        private PlayerInputReader input;
        private PlayerMotor motor;
        private PlayerCombat combat;
        private Combatant combatant;
        private GameManager game;
        private PlayerActionSlot bufferedAction = PlayerActionSlot.None;
        private double bufferExpiresAt;
        private PlayerStageSubscription stageEvents;
        private PlayerStageSubscription StageEvents => stageEvents ?? (stageEvents = new PlayerStageSubscription(
            this, HandleStageStateChanged, HandleGameplayEnabledChanged));
        void IPlayerStageSubscriber.BindStageEvents(GameManager manager)
        {
            game = manager;
            StageEvents.Bind(manager);
        }
        void IPlayerStageSubscriber.UnbindStageEvents(GameManager manager)
        {
            stageEvents?.Unbind(manager);
            if (game == manager) game = null;
        }
        private void OnEnable() => stageEvents?.Enable();
        private void HandleStageStateChanged(StageState state) { if (StageEvents.IsCurrent(state)) ClearBuffer(); }
        private void HandleGameplayEnabledChanged(bool enabled) => ClearBuffer();

        public PlayerActionSlot BufferedAction => bufferedAction;
        public float BufferRemaining => bufferedAction == PlayerActionSlot.None ? 0f :
            (float)System.Math.Max(0d, bufferExpiresAt - Time.timeAsDouble);

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            motor = GetComponent<PlayerMotor>();
            combat = GetComponent<PlayerCombat>();
            combatant = GetComponent<Combatant>();
            if (playerCamera == null)
            {
                Debug.LogError("PlayerController requires a Player Camera reference.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (!input.isActiveAndEnabled || !input.IsGameplayInputEnabled || Time.timeScale <= 0f ||
                (combatant != null && (!combatant.IsAlive || combatant.IsGameplayBlocked)))
            {
                ClearActions();
                return;
            }
            Quaternion heading = Quaternion.Euler(0f, playerCamera.Yaw, 0f);
            Vector3 moveDirection = heading * new Vector3(input.Move.x, 0f, input.Move.y);
            motor.SetMoveCommand(moveDirection, playerCamera.Yaw);
            if (combat != null && combat.isActiveAndEnabled)
            {
                combat.SetMovementContext(moveDirection, heading * Vector3.forward);
                combat.ProcessBeforeMotor();
                foreach (PlayerActionBinding binding in combat.ActionBindings)
                    combat.DeliverInput(binding.Slot, ReadInput(binding.Slot));
                ProcessHeldActions();
            }
            SelectAction();
            if (input.Jump.Pressed && motor.CanJump) motor.RequestJump();
            HandleInteraction();
        }

        private void ProcessHeldActions()
        {
            foreach (PlayerActionBinding binding in combat.ActionBindings)
                if (binding.Action.RequiresHeldInput &&
                    ReadInput(binding.Slot).Held && !binding.Action.IsActive)
                    combat.TryStartAction(binding.Slot);
        }

        private void HandleInteraction()
        {
            if (game == null || !game.isActiveAndEnabled || !input.Interact.Pressed ||
                (combatant != null && combatant.IsInteractionBlocked)) return;
            if (game.IsSpawnFailed) game.TryRetrySpawn();
            else if (game.State == StageState.BeforeStage) game.TryStartStage();
            else if (game.State == StageState.AfterStage) game.TryOpenReward();
        }

        private void SelectAction()
        {
            if (combatant != null && !combatant.IsAlive) { ClearActions(); return; }
            if (bufferedAction != PlayerActionSlot.None &&
                (Time.timeAsDouble >= bufferExpiresAt || !CanKeepBufferedAction()))
                ClearBuffer();
            PlayerActionSlot pressed = ReadPressedAction();
            if (pressed != PlayerActionSlot.None &&
                (bufferedAction != PlayerActionSlot.Dash || pressed == PlayerActionSlot.Dash))
            {
                bufferedAction = pressed;
                bufferExpiresAt = Time.timeAsDouble + maxInputBufferTime;
            }
            if (bufferedAction != PlayerActionSlot.None && combat != null && combat.IsDelayBlocking(bufferedAction))
            {
                if (maxInputBufferTime <= 0f) ClearBuffer();
                return;
            }
            if (bufferedAction != PlayerActionSlot.None)
            {
                PlayerActionSlot action = bufferedAction;
                ClearBuffer();
                combat?.RequestAction(action);
                return;
            }
            if (combat == null || !combat.isActiveAndEnabled) return;
            foreach (PlayerActionBinding binding in combat.ActionBindings)
            {
                if (binding.Slot == PlayerActionSlot.PrimaryFire ||
                    binding.Slot == PlayerActionSlot.Dash || binding.Slot == PlayerActionSlot.Reload) continue;
                if (WantsRepeat(binding))
                {
                    combat.RequestAction(binding.Slot);
                    return;
                }
            }
            if (combat.CurrentAmmo == 0)
            {
                if (combat.GetAction(PlayerActionSlot.Reload)?.RequiresHeldInput == false && combat.CanReload)
                    combat.RequestReload();
            }
            else if (WantsRepeat(combat.GetBinding(PlayerActionSlot.PrimaryFire)))
                combat.RequestAction(PlayerActionSlot.PrimaryFire);
        }

        private PlayerActionSlot ReadPressedAction()
        {
            if (input.Dash.Pressed && CanAcceptAction(PlayerActionSlot.Dash)) return PlayerActionSlot.Dash;
            if (combat != null && combat.isActiveAndEnabled)
                foreach (PlayerActionBinding binding in combat.ActionBindings)
                    if (binding.Slot != PlayerActionSlot.PrimaryFire && binding.Slot != PlayerActionSlot.Dash &&
                        binding.Slot != PlayerActionSlot.Reload &&
                        ReadInput(binding.Slot).Pressed && CanAcceptAction(binding.Slot))
                        return binding.Slot;
            if (input.Reload.Pressed && CanAcceptAction(PlayerActionSlot.Reload)) return PlayerActionSlot.Reload;
            if (input.PrimaryFire.Pressed && CanAcceptAction(PlayerActionSlot.PrimaryFire)) return PlayerActionSlot.PrimaryFire;
            return PlayerActionSlot.None;
        }

        private bool CanKeepBufferedAction()
        {
            PlayerActionBase action = combat != null && combat.isActiveAndEnabled ? combat.GetAction(bufferedAction) : null;
            return action != null && !action.RequiresHeldInput && !combat.IsBufferBlocked(bufferedAction);
        }

        private bool CanAcceptAction(PlayerActionSlot slot)
        {
            PlayerActionBase action = combat != null ? combat.GetAction(slot) : null;
            return action != null && !action.RequiresHeldInput && combat.CanBuffer(slot);
        }
        private bool WantsRepeat(PlayerActionBinding binding) => binding != null && binding.Action != null &&
            !binding.Action.RequiresHeldInput && binding.Action.RepeatWhileHeld &&
            ReadInput(binding.Slot).Held && combat.CanStart(binding.Slot);

        private ButtonInputState ReadInput(PlayerActionSlot slot)
        {
            switch (slot)
            {
                case PlayerActionSlot.PrimaryFire: return input.PrimaryFire;
                case PlayerActionSlot.SecondaryFire: return input.SecondaryFire;
                case PlayerActionSlot.Skill1: return input.Skill1;
                case PlayerActionSlot.Skill2: return input.Skill2;
                case PlayerActionSlot.Ultimate: return input.Ultimate;
                case PlayerActionSlot.Dash: return input.Dash;
                case PlayerActionSlot.Reload: return input.Reload;
                default: return default;
            }
        }

        private void ClearBuffer() { bufferedAction = PlayerActionSlot.None; bufferExpiresAt = 0d; }
        private void ClearActions() { ClearBuffer(); if (combat != null) combat.InterruptInput(); }
        public void ClearBufferedInput() => ClearActions();
        private void OnDisable() { stageEvents?.Disable(); ClearActions(); }
        private void OnApplicationFocus(bool focused) { if (!focused) ClearActions(); }

        private void OnValidate() => maxInputBufferTime = AttackDataBase.FiniteAtLeast(maxInputBufferTime, 0f);
    }
}
