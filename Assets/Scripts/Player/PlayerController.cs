using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerMotor))]
    [RequireComponent(typeof(PlayerStamina))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private PlayerCameraController playerCamera;
        [SerializeField] private bool logInput = true;
        [SerializeField, Min(0.1f), Tooltip("Seconds between held-input logs for each action.")]
        private float heldLogInterval = 0.5f;
        [SerializeField, Min(0f), Tooltip("Stamina consumed per Dash press. Dash movement is not implemented yet.")]
        private float dashStaminaCost = 25f;

        private PlayerInputReader input;
        private PlayerMotor motor;
        private PlayerStamina stamina;
        private readonly float[] nextHeldLog = new float[8];

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            motor = GetComponent<PlayerMotor>();
            stamina = GetComponent<PlayerStamina>();
            if (playerCamera == null)
            {
                Debug.LogError("PlayerController requires a Player Camera reference.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            Quaternion heading = Quaternion.Euler(0f, playerCamera.Yaw, 0f);
            Vector3 moveDirection = heading * new Vector3(input.Move.x, 0f, input.Move.y);
            motor.SetMoveCommand(moveDirection, playerCamera.Yaw);
            if (input.Jump.Pressed)
                motor.RequestJump();

            // Resource-only preview: no dash movement, invincibility or cooldown.
            if (input.Dash.Pressed && Time.deltaTime > 0f && stamina.TryConsume(dashStaminaCost) && logInput)
                Debug.Log($"[Player Stamina] Consumed {dashStaminaCost}: {stamina.Current}/{stamina.Max}", this);

            if (!logInput)
                return;

            Handle(input.Jump, "Jump (Space)", 0);
            Handle(input.Ultimate, "Ultimate (Q)", 1);
            Handle(input.Reload, "Reload (R)", 2);
            Handle(input.Dash, "Dash (Shift)", 3);
            Handle(input.Skill1, "Skill1 (E)", 4);
            Handle(input.Skill2, "Skill2 (F)", 5);
            Handle(input.PrimaryFire, "PrimaryFire (LMB)", 6);
            Handle(input.SecondaryFire, "SecondaryFire (RMB)", 7);
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
        }
    }
}
