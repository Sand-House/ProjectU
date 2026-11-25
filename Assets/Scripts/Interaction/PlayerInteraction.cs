using UnityEngine;

namespace ProjectU
{
    // Optional feature driven by PlayerController after action/ADS processing, using the existing input snapshot.
    [DisallowMultipleComponent, RequireComponent(typeof(PlayerController))]
    public sealed class PlayerInteraction : MonoBehaviour, IPlayerStageSubscriber
    {
        [SerializeField, Tooltip("Point tested against button ranges. Defaults to the CharacterController center, or this Transform.")]
        private Transform interactionOrigin;

        private PlayerController controller;
        private PlayerInputReader input;
        private Combatant combatant;
        private CharacterController body;
        private ButtonTrigger currentButton;
        private ButtonTriggerInputMode currentMode;
        private uint buttonVersion;
        private float heldTime;
        private bool holding;
        private bool triggered;
        private bool waitingForRelease;
        private int processedFrame = -1;
        private PlayerStageSubscription stageEvents;
        private PlayerStageSubscription StageEvents => stageEvents ?? (stageEvents = new PlayerStageSubscription(
            this, HandleStageStateChanged, HandleGameplayEnabledChanged, released: CancelInteraction));
        void IPlayerStageSubscriber.BindStageEvents(GameManager game) => StageEvents.Bind(game);
        void IPlayerStageSubscriber.UnbindStageEvents(GameManager game) => stageEvents?.Unbind(game);
        private void OnEnable() => stageEvents?.Enable();
        private void HandleStageStateChanged(StageState state) { if (StageEvents.IsCurrent(state)) CancelInteraction(); }
        private void HandleGameplayEnabledChanged(bool enabled) => CancelInteraction();

        public Vector3 InteractionPosition => interactionOrigin != null ? interactionOrigin.position :
            body != null ? body.transform.TransformPoint(body.center) : transform.position;
        public bool CanInteract => isActiveAndEnabled && controller != null && controller.isActiveAndEnabled &&
            input != null && input.isActiveAndEnabled && input.IsGameplayInputEnabled && Time.timeScale > 0f &&
            (combatant == null || (combatant.IsAlive && !combatant.IsGameplayBlocked && !combatant.IsInteractionBlocked));
        public ButtonTrigger CurrentButton => CanInteract && currentButton != null && currentButton.Contains(InteractionPosition)
            ? currentButton : null;
        public float HoldProgress => CurrentButton != null && currentButton.InputMode == ButtonTriggerInputMode.Hold
            ? triggered ? 1f : currentButton.HoldDuration <= 0f ? 0f : Mathf.Clamp01(heldTime / currentButton.HoldDuration)
            : 0f;
        public bool IsHolding => CurrentButton != null && holding && !triggered;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            input = GetComponent<PlayerInputReader>();
            combatant = GetComponent<Combatant>();
            body = GetComponent<CharacterController>();
        }

        // True reserves this frame's interaction for a local button, including while waiting for release.
        public bool ProcessInput(in ButtonInputState state)
        {
            if (!CanInteract) { CancelInteraction(); return false; }
            if (processedFrame == Time.frameCount) return CurrentButton != null;
            processedFrame = Time.frameCount;
            ButtonTrigger nearest = ButtonTrigger.FindNearest(InteractionPosition);
            if (nearest != currentButton || (nearest != null &&
                (nearest.InputMode != currentMode || nearest.AvailabilityVersion != buttonVersion)))
            {
                ResetHold();
                currentButton = nearest;
                if (nearest != null) { currentMode = nearest.InputMode; buttonVersion = nearest.AvailabilityVersion; }
            }
            if (waitingForRelease)
            {
                if (state.Released || !state.Held) waitingForRelease = false;
                else return currentButton != null;
            }
            if (currentButton == null) return false;

            if (currentMode == ButtonTriggerInputMode.Press)
            {
                if (state.Released || !state.Held) triggered = false;
                if (state.Pressed && !triggered) Trigger();
                return true;
            }

            if (state.Released || !state.Held) { ResetHold(); return true; }
            if (triggered) return true;
            if (state.Pressed)
            {
                holding = true;
                heldTime = 0f;
            }
            else if (holding) heldTime += Time.deltaTime;
            if (holding && heldTime >= currentButton.HoldDuration) Trigger();
            return true;
        }

        private void Trigger()
        {
            // Latch before invoking external code, which may change the stage or disable/destroy objects.
            triggered = true;
            holding = false;
            currentButton.Activate(this);
        }

        private void ResetHold() { heldTime = 0f; holding = triggered = false; }
        public void CancelInteraction()
        {
            if (holding || triggered) waitingForRelease = true;
            ResetHold();
            currentButton = null;
        }
        private void OnDisable() { stageEvents?.Disable(); CancelInteraction(); }
    }
}
