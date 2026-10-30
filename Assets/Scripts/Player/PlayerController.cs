using UnityEngine;

namespace ProjectU
{
    public enum PlayerAction { None, Dash, Reload, PrimaryFire, SecondaryFire, Skill1, Skill2, Ultimate }

    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerMotor))]
    [RequireComponent(typeof(PlayerStamina))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private PlayerCamera playerCamera;
        [SerializeField] private bool logInput = true;
        [SerializeField, Min(0.1f), Tooltip("Seconds between held-input logs for each action.")]
        private float heldLogInterval = 0.5f;
        [SerializeField, Min(0f), Tooltip("Stamina consumed per successful dash. Zero allows a free dash.")]
        private float dashStaminaCost = 25f;
        [SerializeField, Min(0f), Tooltip("Maximum game seconds to keep one pressed action during attack or dash delays. Zero disables buffering.")]
        private float maxInputBufferTime = 1f;

        private PlayerInputReader input;
        private PlayerMotor motor;
        private PlayerStamina stamina;
        private PlayerCombat attacks;
        private Combatant combatant;
        private PlayerAction bufferedAction;
        private double bufferExpiresAt;
        private readonly float[] nextHeldLog = new float[8];

        public PlayerAction BufferedAction => bufferedAction;
        public float BufferRemaining => bufferedAction == PlayerAction.None ? 0f :
            (float)System.Math.Max(0d, bufferExpiresAt - Time.timeAsDouble);

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            motor = GetComponent<PlayerMotor>();
            stamina = GetComponent<PlayerStamina>();
            attacks = GetComponent<PlayerCombat>();
            combatant = GetComponent<Combatant>();
            if (playerCamera == null)
            {
                Debug.LogError("PlayerController requires a Player Camera reference.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (!input.isActiveAndEnabled || !input.IsGameplayInputEnabled || Time.timeScale <= 0f)
            {
                ClearActions();
                return;
            }
            Quaternion heading = Quaternion.Euler(0f, playerCamera.Yaw, 0f);
            Vector3 moveDirection = heading * new Vector3(input.Move.x, 0f, input.Move.y);
            motor.SetMoveCommand(moveDirection, playerCamera.Yaw);
            SelectAction(moveDirection, heading);
            if (input.Jump.Pressed && motor.CanJump)
                motor.RequestJump();

            if (logInput && motor.CanJump)
                Handle(input.Jump, "Jump (Space)", 0);
            if (!logInput || !motor.CanPerformActions)
                return;

            Handle(input.Ultimate, "Ultimate (Q)", 1);
            Handle(input.Reload, "Reload (R)", 2);
            Handle(input.Dash, "Dash (Shift)", 3);
            Handle(input.Skill1, "Skill1 (E)", 4);
            Handle(input.Skill2, "Skill2 (F)", 5);
            Handle(input.PrimaryFire, "PrimaryFire (LMB)", 6);
            Handle(input.SecondaryFire, "SecondaryFire (RMB)", 7);
        }

        private void SelectAction(Vector3 moveDirection, Quaternion heading)
        {
            if (combatant != null && !combatant.IsAlive)
            {
                ClearActions();
                return;
            }
            if (bufferedAction != PlayerAction.None && Time.timeAsDouble >= bufferExpiresAt)
                ClearBuffer();

            PlayerAction pressed = ReadPressedAction();
            if (pressed != PlayerAction.None &&
                (bufferedAction != PlayerAction.Dash || pressed == PlayerAction.Dash))
            {
                bufferedAction = pressed;
                bufferExpiresAt = Time.timeAsDouble + maxInputBufferTime;
            }

            // Only an otherwise eligible action may wait for an attack/dash delay.
            if (!motor.CanPerformActions)
            {
                if (maxInputBufferTime <= 0f) ClearBuffer();
                return;
            }
            if (bufferedAction != PlayerAction.None)
            {
                PlayerAction action = bufferedAction;
                ClearBuffer();
                if (action == PlayerAction.Dash)
                {
                    Vector3 direction = moveDirection.sqrMagnitude > 0.0001f ? moveDirection : heading * Vector3.forward;
                    if (motor.CanDash && (dashStaminaCost == 0f || stamina.TryConsume(dashStaminaCost)) &&
                        motor.TryDash(direction) && logInput)
                        Debug.Log($"[Player Dash] Started; stamina {stamina.Current}/{stamina.Max}", this);
                }
                else if (attacks != null)
                {
                    if (action == PlayerAction.Reload) attacks.RequestReload();
                    else attacks.RequestAttack(ToSlot(action));
                }
                return;
            }

            if (attacks == null || !attacks.isActiveAndEnabled) return;
            // Held repetition is a fallback, never a source of buffered requests.
            foreach (PlayerAttackBinding binding in attacks.AttackBindings)
            {
                if (binding.Slot == AttackSlot.PrimaryFire ||
                    (binding.Slot == AttackSlot.SecondaryFire && attacks.IsReloading)) continue;
                if (WantsRepeat(binding))
                {
                    attacks.RequestAttack(binding.Slot);
                    return;
                }
            }
            // Preserve level-triggered empty-magazine reload after any interruption.
            if (attacks.CurrentAmmo == 0) attacks.RequestReload();
            else if (WantsRepeat(attacks.GetBinding(AttackSlot.PrimaryFire)))
                attacks.RequestAttack(AttackSlot.PrimaryFire);
        }

        private PlayerAction ReadPressedAction()
        {
            if (input.Dash.Pressed && CanAcceptAction(PlayerAction.Dash)) return PlayerAction.Dash;
            // Preserve the existing non-primary binding order for same-frame ties.
            if (attacks != null && attacks.isActiveAndEnabled)
                foreach (PlayerAttackBinding binding in attacks.AttackBindings)
                    if (binding != null && binding.Slot != AttackSlot.PrimaryFire &&
                        ReadInput(binding.Slot).Pressed && CanAcceptAction(FromSlot(binding.Slot)))
                        return FromSlot(binding.Slot);
            if (input.Reload.Pressed && CanAcceptAction(PlayerAction.Reload)) return PlayerAction.Reload;
            if (input.PrimaryFire.Pressed && CanAcceptAction(PlayerAction.PrimaryFire)) return PlayerAction.PrimaryFire;
            return PlayerAction.None;
        }

        private bool CanAcceptAction(PlayerAction action)
        {
            // Do not reserve resources or skip grounding, cooldown, ammo or configuration checks.
            if (action == PlayerAction.Dash)
                return motor.CanDashIgnoringActionLock && (dashStaminaCost == 0f ||
                    (stamina.isActiveAndEnabled && stamina.CanConsume(dashStaminaCost)));
            if (attacks == null) return false;
            return action == PlayerAction.Reload ? attacks.CanReloadIgnoringActionLock :
                attacks.CanAttackIgnoringActionLock(ToSlot(action));
        }

        private bool WantsRepeat(PlayerAttackBinding binding) => binding != null && binding.Attack != null &&
            binding.Attack.Data != null && binding.Attack.Data.RepeatWhileHeld && ReadInput(binding.Slot).Held &&
            attacks.CanAttack(binding.Slot);

        private ButtonInputState ReadInput(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.PrimaryFire: return input.PrimaryFire;
                case AttackSlot.SecondaryFire: return input.SecondaryFire;
                case AttackSlot.Skill1: return input.Skill1;
                case AttackSlot.Skill2: return input.Skill2;
                case AttackSlot.Ultimate: return input.Ultimate;
                default: return default;
            }
        }

        private static PlayerAction FromSlot(AttackSlot slot) => slot switch
        {
            AttackSlot.PrimaryFire => PlayerAction.PrimaryFire,
            AttackSlot.SecondaryFire => PlayerAction.SecondaryFire,
            AttackSlot.Skill1 => PlayerAction.Skill1,
            AttackSlot.Skill2 => PlayerAction.Skill2,
            AttackSlot.Ultimate => PlayerAction.Ultimate,
            _ => PlayerAction.None
        };

        private static AttackSlot ToSlot(PlayerAction action) => action switch
        {
            PlayerAction.PrimaryFire => AttackSlot.PrimaryFire,
            PlayerAction.SecondaryFire => AttackSlot.SecondaryFire,
            PlayerAction.Skill1 => AttackSlot.Skill1,
            PlayerAction.Skill2 => AttackSlot.Skill2,
            PlayerAction.Ultimate => AttackSlot.Ultimate,
            _ => throw new System.ArgumentOutOfRangeException(nameof(action))
        };

        private void ClearBuffer()
        {
            bufferedAction = PlayerAction.None;
            bufferExpiresAt = 0d;
        }

        private void ClearActions()
        {
            ClearBuffer();
            if (attacks != null) attacks.ClearRequests();
        }

        private void OnDisable() => ClearActions();

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ClearActions();
        }

        private void Handle(ButtonInputState state, string label, int index)
        {
            if (state.Pressed)
            {
                Debug.Log($"[Player Input] {label}: Pressed", this);
                nextHeldLog[index] = Time.unscaledTime + heldLogInterval;
            }
            if (state.Held && Time.unscaledTime >= nextHeldLog[index])
            {
                Debug.Log($"[Player Input] {label}: Held", this);
                nextHeldLog[index] = Time.unscaledTime + heldLogInterval;
            }
            if (state.Released)
                Debug.Log($"[Player Input] {label}: Released", this);
            if (!state.Held)
                nextHeldLog[index] = 0f;
        }

        private void OnValidate()
        {
            dashStaminaCost = float.IsNaN(dashStaminaCost) || float.IsInfinity(dashStaminaCost)
                ? 0f : Mathf.Max(0f, dashStaminaCost);
            maxInputBufferTime = float.IsNaN(maxInputBufferTime) || float.IsInfinity(maxInputBufferTime)
                ? 0f : Mathf.Max(0f, maxInputBufferTime);
        }
    }
}
