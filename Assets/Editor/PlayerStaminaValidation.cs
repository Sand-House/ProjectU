using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class PlayerStaminaValidation
    {
        private const string Running = "ProjectU.PlayerStaminaValidation";

        static PlayerStaminaValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player Stamina Validation Driver").AddComponent<PlayerStaminaValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player Stamina")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            GameFlowValidationUtility.DisableAutomaticGameFlow();
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        public static void Finish(int code)
        {
            SessionState.SetBool(Running, false);
            Time.timeScale = 1f;
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
            else
                EditorApplication.ExitPlaymode();
        }
    }

    // Inject snapshots after Reader.Update, so batch focus cannot hide Controller behavior.
    [DefaultExecutionOrder(-150)]
    public sealed class PlayerStaminaValidationDriver : MonoBehaviour
    {
        private PlayerInputReader input;
        private PlayerStamina stamina;
        private PlayerStat stat;
        private PlayerMotor motor;
        private Vector3 initialPosition;
        private int phase;
        private int frames;
        private int notifications;
        private float phaseTime;
        private float previous;
        private float pauseRemaining;
        private double wallStarted;
        private bool finished;

        private void Start()
        {
            EnemyValidationIsolation.DisableDemoEnemies();
            try
            {
                wallStarted = EditorApplication.timeSinceStartup;
                input = UnityEngine.Object.FindFirstObjectByType<PlayerInputReader>();
                stamina = input.GetComponent<PlayerStamina>();
                stat = input.GetComponent<PlayerStat>();
                motor = input.GetComponent<PlayerMotor>();
                // Isolate resource timing from the full dash/post-delay validation.
                var motorSettings = new SerializedObject(motor);
                stat.DashDistance = 0.1f;
                motorSettings.FindProperty("dashDuration").floatValue = 0.01f;
                PlayerActionValidationUtility.Set(input.GetComponent<SoilderDashAction>(), "recoveryTime", 0f);
                motorSettings.ApplyModifiedPropertiesWithoutUndo();
                Check(stamina != null && stat != null && stamina.enabled, "Saved scene resource components.");
                foreach (Transform item in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject) == 0, "Missing script: " + item.name);
                var controller = new SerializedObject(input.GetComponent<PlayerController>());
                controller.FindProperty("maxInputBufferTime").floatValue = 0f;
                controller.ApplyModifiedPropertiesWithoutUndo();
                var actions = (UnityEngine.InputSystem.InputActionMap)typeof(PlayerInputReader)
                    .GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);
                var dash = actions.FindAction("Dash", true);
                Check(dash.bindings.Count == 2 && dash.bindings[0].path == "<Keyboard>/leftShift" &&
                    dash.bindings[1].path == "<Keyboard>/rightShift", "Both Shift bindings.");
                Check(stat.AttackMultiplier == 1f && stat.MaxHealth == 100 && stat.MaxMagazine == 30 &&
                    stat.AttackSpeed == 1f && stat.MoveSpeed == 6f && stamina.Current == 100f && stamina.Max == 100f,
                    "Scene defaults and six stats.");

                // Use an isolated resource to check API edge cases without altering scene defaults.
                var unit = new GameObject("Stamina API Validation");
                unit.SetActive(false);
                var testStat = unit.AddComponent<PlayerStat>();
                var resource = unit.AddComponent<PlayerStamina>();
                var serialized = new SerializedObject(resource);
                serialized.FindProperty("current").floatValue = 40f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                unit.SetActive(true);
                Check(resource.Current == 40f, "Inspector starting amount is preserved.");
                Check(resource.CanConsume(25f) && resource.Current == 40f, "Validation is read-only.");
                foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    Check(!resource.CanConsume(invalid) && !resource.TryConsume(invalid) && resource.Recover(invalid) == 0f,
                        "Invalid amount is ignored.");
                Check(resource.TryConsume(25f) && resource.Current == 15f, "Exact resource consumption.");
                float wait = resource.RecoveryDelayRemaining;
                Check(!resource.TryConsume(25f) && resource.Current == 15f && resource.RecoveryDelayRemaining == wait,
                    "Insufficient resource preserves amount and wait.");
                Check(resource.Recover(200f) == 85f && resource.Current == 100f && resource.RecoveryDelayRemaining == wait,
                    "Explicit recovery clamps to max and preserves wait.");
                Check(resource.TryConsume(100f) && resource.Current == 0f, "Exact depletion.");
                resource.enabled = false;
                resource.Recover(10f);
                Check(!resource.TryConsume(1f) && resource.Current == 10f, "Disabled component rejects consumption.");
                resource.enabled = true;
                testStat.MaxStamina = 5f;
                Check(resource.Current == 5f && resource.Max == 5f, "Lower maximum clamps reads immediately.");
                // One update commits the clamp before increasing the maximum again.
                typeof(PlayerStamina).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resource, null);
                testStat.MaxStamina = 150f;
                Check(resource.Current == 5f, "Higher maximum does not refill.");
                testStat.AttackMultiplier = -1f;
                testStat.MaxHealth = 0;
                testStat.MaxMagazine = 0;
                testStat.MaxStamina = float.NaN;
                testStat.MoveSpeed = -1f;
                testStat.AttackSpeed = float.PositiveInfinity;
                resource.RecoveryDelay = -1f;
                resource.RecoveryRate = float.NaN;
                Check(testStat.AttackMultiplier == 0f && testStat.MaxHealth == 1 &&
                    testStat.MaxMagazine == 1 && testStat.MaxStamina == 0f && testStat.MoveSpeed == 0f &&
                    testStat.AttackSpeed == 0.01f && resource.RecoveryDelay == 0f && resource.RecoveryRate == 0f,
                    "Invalid stat and recovery settings are clamped.");
                UnityEngine.Object.Destroy(unit);

                stamina.OnChanged += (current, max) =>
                {
                    notifications++;
                    Check(current >= 0f && current <= max && current == stamina.Current, "Consistent change notification.");
                };
                phaseTime = Time.time;
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void Update()
        {
            if (finished)
                return;
            try
            {
                Check(EditorApplication.timeSinceStartup - wallStarted < 60, "Validation timed out.");
                SetInput("IsGameplayInputEnabled", input.isActiveAndEnabled && Time.timeScale > 0f);
                SetInput("Move", Vector2.zero);
                SetInput("Jump", default(ButtonInputState));
                SetInput("Dash", default(ButtonInputState));
                SetInput("PrimaryFire", default(ButtonInputState));
                SetInput("SecondaryFire", default(ButtonInputState));
                if (phase == 0 && Time.time - phaseTime > 0.2f)
                {
                    initialPosition = input.transform.position;
                    stamina.RecoveryDelay = 0.3f;
                    stamina.RecoveryRate = 40f;
                    SetInput("Dash", Snapshot(true, true, false));
                    phase = 1;
                    frames = 0;
                }
                else if (phase == 1)
                    SetInput("Dash", Snapshot(false, true, false));
                else if (phase == 2 && Time.time - phaseTime > 0.15f)
                {
                    Check(stamina.Current == 75f, "No recovery before wait.");
                    SetInput("Dash", Snapshot(true, true, false));
                    phase = 3;
                    frames = 0;
                }
                else if (phase == 6 && Time.time - phaseTime > 0.4f)
                {
                    previous = stamina.Current;
                    phase = 7;
                    phaseTime = Time.time;
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void LateUpdate()
        {
            if (finished)
                return;
            try
            {
                frames++;
                if (phase == 1)
                {
                    Check(stamina.Current == 75f, "Press consumes once; holding does not repeat.");
                    // Headless batches can run three frames before the short dash finishes.
                    if (frames >= 3 && motor.CanPerformActions)
                    {
                        Vector3 displacement = input.transform.position - initialPosition;
                        displacement.y = 0f;
                        Check(Mathf.Abs(displacement.magnitude - 0.1f) < 0.02f, "Shift also requests the configured dash displacement.");
                        phase = 2;
                        phaseTime = Time.time;
                    }
                }
                else if (phase == 3)
                {
                    Check(stamina.Current == 50f && stamina.RecoveryDelayRemaining == stamina.RecoveryDelay,
                        "Second press consumes without cooldown and restarts wait.");
                    phase = 4;
                    phaseTime = Time.time;
                }
                else if (phase == 4)
                {
                    Check(stamina.Current == 50f, "Second consumption resets recovery delay.");
                    if (Time.time - phaseTime > 0.15f)
                    {
                        previous = stamina.Current;
                        pauseRemaining = stamina.RecoveryDelayRemaining;
                        Time.timeScale = 0f;
                        phase = 5;
                        frames = 0;
                    }
                }
                else if (phase == 5)
                {
                    Check(stamina.Current == previous && stamina.RecoveryDelayRemaining == pauseRemaining,
                        "Pause freezes amount and remaining delay.");
                    if (frames >= 5)
                    {
                        Time.timeScale = 1f;
                        phase = 6;
                        phaseTime = Time.time;
                    }
                }
                else if (phase == 7 && Time.time - phaseTime >= 0.5f)
                {
                    float expected = (Time.time - phaseTime) * stamina.RecoveryRate;
                    Check(stamina.Current > previous && Mathf.Abs((stamina.Current - previous) - expected) < 3f,
                        "Automatic recovery follows configured game-time rate.");
                    Check(stamina.TryConsume(25f), "Can consume during recovery.");
                    stamina.RecoveryDelay = 0f;
                    stamina.RecoveryRate = 0f;
                    previous = stamina.Current;
                    phase = 8;
                    phaseTime = Time.time;
                }
                else if (phase == 8 && Time.time - phaseTime > 0.4f)
                {
                    Check(stamina.Current == previous, "Zero rate disables automatic recovery.");
                    stamina.RecoveryRate = 10000f;
                    phase = 9;
                }
                else if (phase == 9 && stamina.Current == stamina.Max)
                {
                    stat.MoveSpeed = 3f;
                    Check(motor.MoveSpeed == 3f, "Motor uses PlayerStat.MoveSpeed.");
                    Check(notifications >= 4, "Resource changes emit notifications.");
            Debug.Log("PLAYER STAMINA VALIDATION PASSED: stats, scene references, both Shift bindings, press/hold, insufficient/invalid consumption, recovery delay/reset/rate/clamp, pause/resume, max changes, Inspector starting amount, events, movement-speed source, configured dash displacement.");
                    finished = true;
                    PlayerStaminaValidation.Finish(0);
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void SetInput(string name, object value)
        {
            typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);
        }

        private static ButtonInputState Snapshot(bool pressed, bool held, bool released)
        {
            return (ButtonInputState)Activator.CreateInstance(typeof(ButtonInputState),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { pressed, held, released }, null);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private void Fail(Exception exception)
        {
            finished = true;
            Debug.LogException(exception);
            PlayerStaminaValidation.Finish(1);
        }
    }
}
