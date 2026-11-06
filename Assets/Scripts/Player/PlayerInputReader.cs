using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectU
{
    [DefaultExecutionOrder(-200)]
    public sealed class PlayerInputReader : MonoBehaviour, IPlayerStageSubscriber
    {
        private InputActionMap actions;
        private InputAction move;
        private InputAction look;
        private InputAction jump;
        private InputAction interact;
        private InputAction ultimate;
        private InputAction reload;
        private InputAction dash;
        private InputAction skill1;
        private InputAction skill2;
        private InputAction primaryFire;
        private InputAction secondaryFire;
        private bool suppressPrimaryFire = true;
        private InputAction releaseCursor;
        private InputAction captureCursor;
        private bool skipLook;
        private bool gameplayBlocked;
        private PlayerStageSubscription stageEvents;
        private PlayerStageSubscription StageEvents => stageEvents ?? (stageEvents = new PlayerStageSubscription(
            this, HandleStageStateChanged, HandleGameplayEnabledChanged));
        void IPlayerStageSubscriber.BindStageEvents(GameManager game) => StageEvents.Bind(game);
        void IPlayerStageSubscriber.UnbindStageEvents(GameManager game) => stageEvents?.Unbind(game);
        private void HandleStageStateChanged(StageState state) { if (StageEvents.IsCurrent(state)) ClearInput(); }
        private void HandleGameplayEnabledChanged(bool enabled) => SetGameplayBlocked(!enabled);

        public bool IsGameplayInputEnabled { get; private set; }

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public ButtonInputState Jump { get; private set; }
        public ButtonInputState Interact { get; private set; }
        public ButtonInputState Ultimate { get; private set; }
        public ButtonInputState Reload { get; private set; }
        public ButtonInputState Dash { get; private set; }
        public ButtonInputState Skill1 { get; private set; }
        public ButtonInputState Skill2 { get; private set; }
        public ButtonInputState PrimaryFire { get; private set; }
        public ButtonInputState SecondaryFire { get; private set; }

        private void Awake()
        {
            actions = new InputActionMap("PlayerControls");
            move = actions.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            look = actions.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            jump = actions.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            interact = actions.AddAction("Interact", InputActionType.Button, "<Keyboard>/g");
            ultimate = actions.AddAction("Ultimate", InputActionType.Button, "<Keyboard>/q");
            reload = actions.AddAction("Reload", InputActionType.Button, "<Keyboard>/r");
            dash = actions.AddAction("Dash", InputActionType.Button, "<Keyboard>/leftShift");
            dash.AddBinding("<Keyboard>/rightShift");
            skill1 = actions.AddAction("Skill1", InputActionType.Button, "<Keyboard>/e");
            skill2 = actions.AddAction("Skill2", InputActionType.Button, "<Keyboard>/f");
            primaryFire = actions.AddAction("PrimaryFire", InputActionType.Button, "<Mouse>/leftButton");
            secondaryFire = actions.AddAction("SecondaryFire", InputActionType.Button, "<Mouse>/rightButton");
            // Recognize a button already held when the action map is re-enabled.
            foreach (var action in actions.actions)
                action.wantsInitialStateCheck = true;
            releaseCursor = actions.AddAction("ReleaseCursor", InputActionType.Button, "<Keyboard>/escape");
            captureCursor = actions.AddAction("CaptureCursor", InputActionType.Button, "<Mouse>/leftButton");
        }

        private void OnEnable()
        {
            actions.Enable();
            SetCursorCaptured(!gameplayBlocked);
            stageEvents?.Enable();
        }

        private void Update()
        {
            ClearInput();
            if (gameplayBlocked || !Application.isFocused || Time.timeScale == 0f)
                return;

            if (releaseCursor.WasPressedThisFrame())
                SetCursorCaptured(false);
            else if (captureCursor.WasPressedThisFrame() && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorCaptured(true);
                suppressPrimaryFire = true;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            IsGameplayInputEnabled = true;
            Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            Look = skipLook ? Vector2.zero : look.ReadValue<Vector2>();
            skipLook = false;
            Jump = ButtonInputState.Read(jump);
            Interact = ButtonInputState.Read(interact);
            Ultimate = ButtonInputState.Read(ultimate);
            Reload = ButtonInputState.Read(reload);
            Dash = ButtonInputState.Read(dash);
            Skill1 = ButtonInputState.Read(skill1);
            Skill2 = ButtonInputState.Read(skill2);
            SecondaryFire = ButtonInputState.Read(secondaryFire);
            // The click used to recapture the cursor must not also fire a weapon.
            if (suppressPrimaryFire)
            {
                if (!primaryFire.IsPressed())
                    suppressPrimaryFire = false;
            }
            else
                PrimaryFire = ButtonInputState.Read(primaryFire);
        }

        private void SetCursorCaptured(bool captured)
        {
            if (!captured) ClearInput();
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
            skipLook = true;
        }

        public void SetGameplayBlocked(bool blocked)
        {
            if (gameplayBlocked == blocked) return;
            gameplayBlocked = blocked;
            ClearInput();
            suppressPrimaryFire = true;
            SetCursorCaptured(!blocked && isActiveAndEnabled && Application.isFocused);
        }

        private void ClearInput()
        {
            IsGameplayInputEnabled = false;
            Move = Look = Vector2.zero;
            Jump = Interact = Ultimate = Reload = Dash = Skill1 = Skill2 = PrimaryFire = SecondaryFire = default;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                ClearInput();
                SetCursorCaptured(false);
            }
        }

        private void OnDisable()
        {
            stageEvents?.Disable();
            actions.Disable();
            ClearInput();
            SetCursorCaptured(false);
        }

        private void OnDestroy() => actions.Dispose();
    }
}
