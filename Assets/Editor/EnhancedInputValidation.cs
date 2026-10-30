using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class EnhancedInputValidation
    {
        private const string Running = "ProjectU.EnhancedInputValidation";

        static EnhancedInputValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Enhanced Input Validation Driver").AddComponent<EnhancedInputValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Enhanced Input")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        internal static void Finish(int code)
        {
            SessionState.SetBool(Running, false);
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }

    // Inject per-frame snapshots between Reader and Controller; exercise the real scene executors.
    [DefaultExecutionOrder(-150)]
    public sealed class EnhancedInputValidationDriver : MonoBehaviour
    {
        private const int Primary = 1, Secondary = 2, Skill1 = 4, Skill2 = 8, Ultimate = 16,
            Reload = 32, Dash = 64, Jump = 128;
        private PlayerInputReader input;
        private PlayerController controller;
        private PlayerCombat attacks;
        private PlayerMotor motor;
        private PlayerStamina stamina;
        private int pressed;
        private int held;
        private Vector2 movement;
        private bool gameplayEnabled = true;
        private int dashCount;
        private int reloadCount;
        private readonly int[] shots = new int[5];
        private readonly List<string> checks = new List<string>();
        private double started;

        private IEnumerator Start()
        {
            IEnumerator validation = Validate();
            while (true)
            {
                object current = null;
                bool more;
                try { more = validation.MoveNext(); if (more) current = validation.Current; }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EnhancedInputValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("ENHANCED INPUT VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            EnhancedInputValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            started = EditorApplication.timeSinceStartup;
            Time.captureDeltaTime = 1f / 60f;
            input = UnityEngine.Object.FindFirstObjectByType<PlayerInputReader>();
            controller = input.GetComponent<PlayerController>();
            attacks = input.GetComponent<PlayerCombat>();
            motor = input.GetComponent<PlayerMotor>();
            stamina = input.GetComponent<PlayerStamina>();
            SetField(controller, "logInput", false);
            Check((float)GetField(controller, "maxInputBufferTime") == 1f, "saved buffer limit is one game second");
            stamina.RecoveryRate = 0f;
            attacks.enabled = false;
            var bindings = new List<PlayerAttackBinding>(attacks.AttackBindings);
            foreach (AttackSlot slot in new[] { AttackSlot.Skill2, AttackSlot.Ultimate })
            {
                var extra = input.gameObject.AddComponent<HitscanAttack>();
                SetField(extra, "data", Instantiate((HitscanAttackData)bindings[0].Attack.Data));
                var binding = new PlayerAttackBinding();
                SetField(binding, "slot", slot);
                SetField(binding, "attack", extra);
                SetField(binding, "origin", bindings[0].Origin);
                bindings.Add(binding);
            }
            foreach (PlayerAttackBinding binding in bindings)
            {
                AttackData data = Instantiate(binding.Attack.Data);
                SetField(data, "windupTime", 0.12f);
                SetField(data, "recoveryTime", 0.18f);
                SetField(data, "cooldown", 0f);
                SetField(binding.Attack, "data", data);
            }
            SetField(attacks, "attackBindings", bindings.ToArray());
            attacks.enabled = true;
            attacks.AttackExecuted += slot => shots[(int)slot]++;
            attacks.OnReloadStarted += () => reloadCount++;
            motor.OnDashExecuted += () => dashCount++;
            yield return new WaitForSeconds(0.2f);

            // A dash pressed during windup must survive lower-priority presses and held fire.
            int primaryBefore = shots[0];
            int dashBefore = dashCount;
            held = Primary;
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Dash;
            yield return new WaitForSeconds(0.05f);
            float remaining = controller.BufferRemaining;
            Check(controller.BufferedAction == PlayerAction.Dash && stamina.Current == stamina.Max && !motor.IsDashing,
                "windup stores dash without cancelling attack or consuming stamina");
            pressed = Reload | Skill1 | Secondary;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerAction.Dash && controller.BufferRemaining < remaining,
                "lower-priority presses cannot overwrite or refresh a queued dash");
            yield return new WaitForSeconds(0.2f);
            Check(dashCount == dashBefore + 1 && shots[0] == primaryBefore + 1 && motor.IsDashing,
                "buffered dash starts before the next held primary shot");
            Check(stamina.Current == stamina.Max - 25f && controller.BufferedAction == PlayerAction.None,
                "dash consumes stamina once and clears the only reservation");
            yield return new WaitForSeconds(0.7f);
            Check(shots[0] > primaryBefore + 1, "held primary resumes after dash recovery");
            yield return ResetCase();

            primaryBefore = TotalShots();
            dashBefore = dashCount;
            pressed = Primary | Secondary | Skill1 | Skill2 | Ultimate | Reload | Dash | Jump;
            yield return new WaitForSeconds(0.08f);
            Check(dashCount == dashBefore + 1 && TotalShots() == primaryBefore && motor.VerticalSpeed <= 0f,
                "idle simultaneous inputs choose dash before every attack reload and jump");
            yield return new WaitForSeconds(0.6f);
            Check(TotalShots() == primaryBefore && controller.BufferedAction == PlayerAction.None,
                "losing simultaneous presses do not become a second reservation");
            yield return ResetCase();

            // All five attack slots accept one short tap during another action's delay.
            int[] masks = { Primary, Secondary, Skill1, Skill2, Ultimate };
            for (int i = 0; i < masks.Length; i++)
            {
                pressed = Primary;
                yield return new WaitForSeconds(0.05f);
                int before = shots[i];
                // The blocker has not executed yet when the target is Primary.
                if (i == 0) before++;
                pressed = masks[i];
                yield return new WaitForSeconds(0.05f);
                Check(controller.BufferedAction == (PlayerAction)((int)PlayerAction.PrimaryFire + i),
                    ((AttackSlot)i) + " tap is retained after release during windup");
                yield return new WaitForSeconds(0.55f);
                Check(shots[i] == before + 1 && controller.BufferedAction == PlayerAction.None,
                    ((AttackSlot)i) + " deferred tap executes exactly once");
                yield return ResetCase();
            }

            int skillBefore = shots[2], secondaryBefore = shots[1], reloadBefore = reloadCount;
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Skill1;
            yield return new WaitForSeconds(0.05f);
            pressed = Secondary;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerAction.SecondaryFire, "latest ordinary press replaces the one previous reservation");
            pressed = Reload;
            yield return new WaitForSeconds(0.25f);
            Check(attacks.IsReloading && reloadCount == reloadBefore + 1 && shots[1] == secondaryBefore && shots[2] == skillBefore,
                "queued R wins replacement and discarded attacks never run");
            yield return ResetCase();

            skillBefore = shots[2];
            pressed = Dash;
            yield return new WaitForSeconds(0.25f);
            Check(motor.IsDashPostDelay, "dash movement has entered recovery");
            pressed = Skill1;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerAction.Skill1 && shots[2] == skillBefore,
                "dash recovery retains one skill without executing it early");
            yield return new WaitForSeconds(0.45f);
            Check(shots[2] == skillBefore + 1, "queued skill executes after dash recovery");
            yield return ResetCase();

            primaryBefore = shots[0];
            pressed = Dash;
            yield return new WaitForSeconds(0.05f);
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerAction.PrimaryFire && motor.IsDashing,
                "dash movement also retains a tap until its recovery finishes");
            yield return new WaitForSeconds(0.65f);
            Check(shots[0] == primaryBefore + 1, "dash movement tap executes once after the complete lock");
            yield return ResetCase();

            SetField(controller, "maxInputBufferTime", 0.07f);
            secondaryBefore = shots[1];
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Secondary;
            yield return new WaitForSeconds(0.12f);
            Check(controller.BufferedAction == PlayerAction.None, "maximum buffer duration expires during an unfinished delay");
            yield return new WaitForSeconds(0.4f);
            Check(shots[1] == secondaryBefore, "expired tap does not execute when the delay ends");
            yield return ResetCase();

            SetField(controller, "maxInputBufferTime", 0f);
            primaryBefore = shots[0];
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Secondary;
            yield return new WaitForSeconds(0.4f);
            Check(shots[0] == primaryBefore + 1 && shots[1] == secondaryBefore && controller.BufferedAction == PlayerAction.None,
                "zero limit permits immediate input while disabling deferred input");
            yield return ResetCase();

            dashBefore = dashCount;
            stamina.TryConsume(stamina.Current - 10f);
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Dash;
            yield return new WaitForSeconds(0.35f);
            Check(dashCount == dashBefore && controller.BufferedAction == PlayerAction.None && stamina.Current == 10f,
                "execution-time stamina failure discards dash without consumption");
            stamina.Recover(100f);
            yield return new WaitForSeconds(0.6f);
            Check(dashCount == dashBefore, "discarded dash does not wait for resource recovery");
            yield return ResetCase();

            skillBefore = shots[2];
            SetField(bindings[2].Attack.Data, "cooldown", 1f);
            pressed = Skill1;
            yield return new WaitForSeconds(0.2f);
            pressed = Skill1;
            yield return new WaitForSeconds(0.2f);
            Check(shots[2] == skillBefore + 1 && controller.BufferedAction == PlayerAction.None,
                "execution-time cooldown failure discards the queued skill");
            yield return new WaitForSeconds(1f);
            Check(shots[2] == skillBefore + 1, "discarded skill never waits for cooldown recovery");
            SetField(bindings[2].Attack.Data, "cooldown", 0f);
            yield return ResetCase();

            secondaryBefore = shots[1];
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            held = Secondary;
            yield return new WaitForSeconds(0.08f);
            held = 0;
            Check(controller.BufferedAction == PlayerAction.None, "held repetition alone never creates a reservation");
            yield return new WaitForSeconds(0.35f);
            Check(shots[1] == secondaryBefore, "releasing a held action before unlock leaves no delayed shot");
            yield return ResetCase();

            // Movement/jump remain immediate and independent from the buffered combat action.
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            movement = Vector2.right;
            Vector3 position = input.transform.position;
            pressed = Jump | Dash;
            yield return new WaitForSeconds(0.08f);
            Check(motor.VerticalSpeed > 0f && input.transform.position.x > position.x + 0.1f &&
                controller.BufferedAction == PlayerAction.Dash, "movement and grounded jump work while dash is queued during attack");
            yield return ResetCase();

            foreach (string interruption in new[] { "Pause", "Cursor", "Reader", "Controller", "Focus" })
            {
                dashBefore = dashCount;
                pressed = Primary;
                yield return new WaitForSeconds(0.05f);
                pressed = Dash;
                yield return new WaitForSeconds(0.05f);
                Check(controller.BufferedAction == PlayerAction.Dash, interruption + " probe has a pending dash");
                if (interruption == "Pause") Time.timeScale = 0f;
                if (interruption == "Cursor")
                {
                    gameplayEnabled = false;
                    input.GetType().GetMethod("SetCursorCaptured", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(input, new object[] { false });
                }
                if (interruption == "Reader") input.enabled = false;
                if (interruption == "Controller") controller.enabled = false;
                if (interruption == "Focus")
                {
                    gameplayEnabled = false;
                    input.SendMessage("OnApplicationFocus", false);
                }
                for (int frame = 0; frame < 3; frame++) yield return null;
                Check(controller.BufferedAction == PlayerAction.None, interruption + " clears the reservation");
                Time.timeScale = 1f;
                input.enabled = true;
                controller.enabled = true;
                gameplayEnabled = true;
                Cursor.lockState = CursorLockMode.Locked;
                yield return new WaitForSeconds(0.7f);
                Check(dashCount == dashBefore, interruption + " resume does not execute stale input");
                yield return ResetCase();
            }

            dashBefore = dashCount;
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Dash;
            yield return new WaitForSeconds(0.05f);
            input.GetComponent<Mortal>().Dead();
            yield return new WaitForSeconds(0.5f);
            Check(dashCount == dashBefore && controller.BufferedAction == PlayerAction.None,
                "death discards pending input and prevents deferred execution");
        }

        private IEnumerator ResetCase()
        {
            pressed = held = 0;
            movement = Vector2.zero;
            controller.enabled = false;
            attacks.CancelReload();
            while (!motor.CanPerformActions) yield return null;
            SetField(attacks, "currentAmmo", 29);
            SetField(controller, "maxInputBufferTime", 1f);
            stamina.Recover(100f);
            CharacterController body = input.GetComponent<CharacterController>();
            body.enabled = false;
            input.transform.position = new Vector3(0f, 1.05f, 0f);
            SetField(motor, "verticalSpeed", 0f);
            body.enabled = true;
            Physics.SyncTransforms();
            controller.enabled = true;
            yield return new WaitForSeconds(0.12f);
        }

        private int TotalShots()
        {
            int total = 0;
            foreach (int count in shots) total += count;
            return total;
        }

        private void Update()
        {
            if (input == null) return;
            SetInput("IsGameplayInputEnabled", gameplayEnabled && input.isActiveAndEnabled && Time.timeScale > 0f);
            if (EditorApplication.timeSinceStartup - started > 90d)
            {
                Debug.LogError("Enhanced input validation timed out.");
                EnhancedInputValidation.Finish(1);
            }
            SetInput("Move", movement);
            SetInput("Look", Vector2.zero);
            string[] names = { "PrimaryFire", "SecondaryFire", "Skill1", "Skill2", "Ultimate", "Reload", "Dash", "Jump" };
            for (int i = 0; i < names.Length; i++)
                SetInput(names[i], (ButtonInputState)Activator.CreateInstance(typeof(ButtonInputState),
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new object[] { (pressed & (1 << i)) != 0, (held & (1 << i)) != 0, false }, null));
            pressed = 0;
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message +
                $"; input={input.IsGameplayInputEnabled}, focus={Application.isFocused}, cursor={Cursor.lockState}, " +
                $"buffer={controller.BufferedAction}, phase={attacks.GetBinding(AttackSlot.PrimaryFire).Attack.Phase}, " +
                $"dashes={dashCount}, shots={TotalShots()}, stamina={stamina.Current}");
            checks.Add(message);
        }

        private void SetInput(string name, object value) => typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);
        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void SetField(object target, string name, object value)
        {
            Type type = target.GetType();
            FieldInfo field;
            while ((field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)) == null) type = type.BaseType;
            field.SetValue(target, value);
        }
    }
}
