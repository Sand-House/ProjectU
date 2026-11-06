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
        private PlayerActionSlot bufferedAction = PlayerActionSlot.None;
        private double bufferExpiresAt;
        private PlayerStageSubscription stageEvents;
        private PlayerStageSubscription StageEvents => stageEvents ?? (stageEvents = new PlayerStageSubscription(
            this, HandleStageStateChanged, HandleGameplayEnabledChanged));
        void IPlayerStageSubscriber.BindStageEvents(GameManager game) => StageEvents.Bind(game);
        void IPlayerStageSubscriber.UnbindStageEvents(GameManager game) => stageEvents?.Unbind(game);
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
            }
            SelectAction();
            if (input.Jump.Pressed && motor.CanJump) motor.RequestJump();
        }

        private void SelectAction()
        {
            if (combatant != null && !combatant.IsAlive) { ClearActions(); return; }
            if (bufferedAction != PlayerActionSlot.None &&
                (Time.timeAsDouble >= bufferExpiresAt ||
                 (combat != null && combat.GetAction(bufferedAction) != null &&
                  combat.GetAction(bufferedAction).RequiresHeldInput && !ReadInput(bufferedAction).Held)))
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
            if (combat.CurrentAmmo == 0) combat.RequestReload();
            else if (WantsRepeat(combat.GetBinding(PlayerActionSlot.PrimaryFire)))
                combat.RequestAction(PlayerActionSlot.PrimaryFire);
        }

        private PlayerActionSlot ReadPressedAction()
        {
            if (input.Dash.Pressed && CanAcceptAction(PlayerActionSlot.Dash)) return PlayerActionSlot.Dash;
            if (combat != null && combat.isActiveAndEnabled)
                foreach (PlayerActionBinding binding in combat.ActionBindings)
                    if (binding.Slot != PlayerActionSlot.PrimaryFire && binding.Slot != PlayerActionSlot.Dash &&
                        binding.Slot != PlayerActionSlot.Reload && ReadInput(binding.Slot).Pressed && CanAcceptAction(binding.Slot))
                        return binding.Slot;
            if (input.Reload.Pressed && CanAcceptAction(PlayerActionSlot.Reload)) return PlayerActionSlot.Reload;
            if (input.PrimaryFire.Pressed && CanAcceptAction(PlayerActionSlot.PrimaryFire)) return PlayerActionSlot.PrimaryFire;
            return PlayerActionSlot.None;
        }

        private bool CanAcceptAction(PlayerActionSlot slot) => combat != null && combat.CanBuffer(slot) &&
            (!combat.GetAction(slot).RequiresHeldInput || ReadInput(slot).Held);
        private bool WantsRepeat(PlayerActionBinding binding) => binding != null && binding.Action != null &&
            binding.Action.RepeatWhileHeld && ReadInput(binding.Slot).Held && combat.CanStart(binding.Slot);

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

        private void OnValidate() => maxInputBufferTime = AttackData.FiniteAtLeast(maxInputBufferTime, 0f);
    }
}
