using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectU.Editor
{
    public static class PlayerInputValidation
    {
        private const string PendingKey = "ProjectU.InputValidation.Pending";

        // Batch-only integration check with isolated synthetic devices.
        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run this input check in batch mode.");
            PlayerPrototypeSetup.Validate();
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void RegisterValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
                    return;
                SessionState.SetBool(PendingKey, false);
                try
                {
                    CheckInputs();
                    EditorApplication.Exit(0);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EditorApplication.Exit(1);
                }
            };
        }

        private static void CheckInputs()
        {
            // Batch mode has no focused Game view. Use temporary settings for synthetic events.
            // Replacing unsaved default settings destroys them, so retain a copy for restoration.
            var originalSettings = UnityEngine.Object.Instantiate(InputSystem.settings);
            var testSettings = UnityEngine.Object.Instantiate(originalSettings);
            testSettings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = testSettings;
            var obj = new GameObject("Input validation");
            obj.SetActive(false);
            var reader = obj.AddComponent<PlayerInputReader>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(PlayerInputReader).GetMethod("Awake", flags).Invoke(reader, null);
                var map = (InputActionMap)typeof(PlayerInputReader).GetField("actions", flags).GetValue(reader);
                map.devices = new InputDevice[] { keyboard, mouse };
                map.Enable();
                InputSystem.Update();

                var names = new[] { "Jump", "Ultimate", "Reload", "Skill1", "Skill1", "Skill2" };
                var keys = new[] { Key.Space, Key.Q, Key.R, Key.LeftShift, Key.RightShift, Key.E };
                for (int i = 0; i < names.Length; i++)
                {
                    var action = map.FindAction(names[i], true);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys[i]));
                    InputSystem.Update();
                    Check(action, true, true, false);
                    InputSystem.Update();
                    Check(action, false, true, false);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    InputSystem.Update();
                    Check(action, false, false, true);
                    InputSystem.Update();
                    Check(action, false, false, false);
                }

                foreach (var button in new[] { MouseButton.Left, MouseButton.Right })
                {
                    var action = map.FindAction(button == MouseButton.Left ? "PrimaryFire" : "SecondaryFire", true);
                    InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(button));
                    InputSystem.Update();
                    Check(action, true, true, false);
                    InputSystem.Update();
                    Check(action, false, true, false);
                    InputSystem.QueueStateEvent(mouse, new MouseState());
                    InputSystem.Update();
                    Check(action, false, false, true);
                    InputSystem.Update();
                }

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D, Key.E));
                InputSystem.Update();
                InputSystem.Update();
                if (map["Move"].ReadValue<Vector2>().sqrMagnitude < 0.99f)
                    throw new Exception("Held movement was lost.");
                Check(map["Skill2"], false, true, false);
                map.Disable();
                map.Enable();
                InputSystem.Update();
                if (!ButtonInputState.Read(map["Skill2"]).Held)
                    throw new Exception("Held input was lost after re-enabling.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(obj);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
                InputSystem.settings = originalSettings;
                UnityEngine.Object.DestroyImmediate(testSettings);
            }
            Debug.Log("PLAYER_INPUT_VALIDATION_OK: keyboard/mouse press, hold, release, simultaneous movement, re-enable.");
        }

        private static void Check(InputAction action, bool pressed, bool held, bool released)
        {
            var state = ButtonInputState.Read(action);
            if (state.Pressed != pressed || state.Held != held || state.Released != released)
                throw new Exception($"{action.name}: expected {pressed}/{held}/{released}, got {state.Pressed}/{state.Held}/{state.Released}");
        }
    }
}
