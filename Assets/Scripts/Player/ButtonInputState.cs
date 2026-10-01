using UnityEngine.InputSystem;

namespace ProjectU
{
    // Immutable per-frame snapshot. Reading it never consumes another component's input.
    public readonly struct ButtonInputState
    {
        public bool Pressed { get; }
        public bool Held { get; }
        public bool Released { get; }

        private ButtonInputState(bool pressed, bool held, bool released)
        {
            Pressed = pressed;
            Held = held;
            Released = released;
        }

        public static ButtonInputState Read(InputAction action) => new ButtonInputState(
            action.WasPressedThisFrame(), action.IsPressed(), action.WasReleasedThisFrame());
    }
}
