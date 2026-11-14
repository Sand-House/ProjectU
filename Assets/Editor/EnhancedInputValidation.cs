using System.Linq;
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
            GameFlowValidationUtility.DisableAutomaticGameFlow();
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
            EnemyValidationIsolation.DisableDemoEnemies();
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
            PlayerActionValidationUtility.UseSecondaryAttackFixture(attacks);
            motor = input.GetComponent<PlayerMotor>();
            stamina = input.GetComponent<PlayerStamina>();
            Check((float)GetField(controller, "maxInputBufferTime") == 1f, "saved buffer limit is one game second");
            stamina.RecoveryRate = 0f;
            attacks.enabled = false;
            var bindings = new List<PlayerActionBinding>(attacks.ActionBindings.Where(item => item.Attack != null).ToList());
            foreach (PlayerActionSlot slot in new[] { PlayerActionSlot.Skill2, PlayerActionSlot.Ultimate })
            {
                var extra = input.gameObject.AddComponent<HitscanAttack>();
                SetField(extra, "data", Instantiate((HitscanAttackData)bindings[0].Attack.Data));
                var binding = new PlayerActionBinding();
                SetField(binding, "slot", slot);
                SetField(binding, "attack", extra);
                SetField(binding, "origin", bindings[0].Origin);
                bindings.Add(binding);
            }
            foreach (PlayerActionBinding binding in bindings)
            {
                AttackDataBase data = Instantiate(binding.Attack.Data);
                SetField(data, "windupTime", 0.12f);
                SetField(data, "recoveryTime", 0.18f);
                SetField(data, "cooldown", 0f);
                SetField(binding.Attack, "data", data);
            }
            SetField(attacks, "actionBindings", bindings.ToArray());
            attacks.enabled = true;
            attacks.AttackExecuted += slot => shots[(int)slot]++;
            attacks.OnReloadStarted += () => reloadCount++;
            motor.OnDashExecuted += () => dashCount++;
            yield return new WaitForSeconds(0.2f);

            // Land before the attack unlocks: an airborne press must never become a reservation.
            SetField(bindings[0].Attack.Data, "recoveryTime", 1f);
            int airborneDashBefore = dashCount;
            pressed = Primary | Jump;
            yield return new WaitForSeconds(0.08f);
            Check(!motor.IsGrounded && !motor.CanPerformActions, "airborne buffer probe has an active attack delay");
            pressed = Dash;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerActionSlot.None && stamina.Current == stamina.Max,
                "airborne dash is rejected when pressed during an attack delay");
            yield return new WaitForSeconds(0.85f);
            Check(motor.IsGrounded && !motor.CanPerformActions,
                "airborne buffer probe lands before the attack recovery finishes");
            yield return new WaitForSeconds(0.3f);
            Check(dashCount == airborneDashBefore && controller.BufferedAction == PlayerActionSlot.None &&
                stamina.Current == stamina.Max, "airborne press never dashes after landing and attack recovery");
            SetField(bindings[0].Attack.Data, "recoveryTime", 0.18f);
            yield return ResetCase();

            // Invalid new presses must neither replace nor extend an eligible reservation.
            ProjectileAttack secondaryAttack = (ProjectileAttack)bindings[1].Attack;
            ProjectilePool secondaryPool = secondaryAttack.Pool;
            Transform secondaryOrigin = bindings[1].Origin;
            var invalidInputs = new (int Mask, Action Invalidate, Action Restore, string Label)[]
            {
                (Dash, () => stamina.TryConsume(stamina.Current - 10f), () => stamina.Recover(100f), "insufficient stamina"),
                (Dash, () => stamina.enabled = false, () => stamina.enabled = true, "disabled stamina"),
                (Primary, () => SetField(attacks, "currentAmmo", 0), () => SetField(attacks, "currentAmmo", 29), "empty magazine"),
                (Reload, () => SetField(attacks, "currentAmmo", attacks.MaxAmmo), () => SetField(attacks, "currentAmmo", 29), "full magazine"),
                (Secondary, () => secondaryAttack.enabled = false, () => secondaryAttack.enabled = true, "disabled attack"),
                (Secondary, () => SetField(bindings[1], "origin", null), () => SetField(bindings[1], "origin", secondaryOrigin), "missing origin"),
                (Secondary, () => secondaryAttack.Pool = null, () => secondaryAttack.Pool = secondaryPool, "unconfigured projectile"),
                (Skill2, () => SetField(bindings[3].Action, "cooldownEndsAt", Time.timeAsDouble + 0.1d),
                    () => SetField(bindings[3].Action, "cooldownEndsAt", 0d), "active cooldown")
            };
            foreach (var invalidInput in invalidInputs)
            {
                int totalBefore = TotalShots(), ultimateBefore = shots[4];
                int invalidDashBefore = dashCount, invalidReloadBefore = reloadCount;
                pressed = Skill1;
                yield return new WaitForSeconds(0.03f);
                pressed = Ultimate;
                yield return new WaitForSeconds(0.03f);
                Check(controller.BufferedAction == PlayerActionSlot.Ultimate,
                    invalidInput.Label + " probe has an eligible ordinary reservation");
                float eligibleRemaining = controller.BufferRemaining;
                invalidInput.Invalidate();
                pressed = invalidInput.Mask;
                yield return null;
                Check(controller.BufferedAction == PlayerActionSlot.Ultimate && controller.BufferRemaining < eligibleRemaining,
                    invalidInput.Label + " press cannot replace or extend an existing reservation");
                invalidInput.Restore();
                yield return new WaitForSeconds(0.65f);
                Check(shots[4] == ultimateBefore + 1 && TotalShots() == totalBefore + 2 &&
                    dashCount == invalidDashBefore && reloadCount == invalidReloadBefore,
                    invalidInput.Label + " becoming valid before unlock never executes the rejected input");
                yield return ResetCase();
            }

            // A dash pressed during windup must survive lower-priority presses and held fire.
            int primaryBefore = shots[0];
            int dashBefore = dashCount;
            held = Primary;
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Dash;
            yield return new WaitForSeconds(0.05f);
            float remaining = controller.BufferRemaining;
            Check(controller.BufferedAction == PlayerActionSlot.Dash && stamina.Current == stamina.Max && !motor.IsDashing,
                "windup stores dash without cancelling attack or consuming stamina");
            pressed = Reload | Skill1 | Secondary;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerActionSlot.Dash && controller.BufferRemaining < remaining,
                "lower-priority presses cannot overwrite or refresh a queued dash");
            yield return new WaitForSeconds(0.2f);
            Check(dashCount == dashBefore + 1 && shots[0] == primaryBefore + 1 && motor.IsDashing,
                "buffered dash starts before the next held primary shot");
            Check(stamina.Current == stamina.Max - 25f && controller.BufferedAction == PlayerActionSlot.None,
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
            Check(TotalShots() == primaryBefore && controller.BufferedAction == PlayerActionSlot.None,
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
                Check(controller.BufferedAction == (PlayerActionSlot)((int)PlayerActionSlot.PrimaryFire + i),
                    ((PlayerActionSlot)i) + " tap is retained after release during windup");
                yield return new WaitForSeconds(0.55f);
                Check(shots[i] == before + 1 && controller.BufferedAction == PlayerActionSlot.None,
                    ((PlayerActionSlot)i) + " deferred tap executes exactly once");
                yield return ResetCase();
            }

            int skillBefore = shots[2], secondaryBefore = shots[1], reloadBefore = reloadCount;
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Skill1;
            yield return new WaitForSeconds(0.05f);
            pressed = Secondary;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerActionSlot.SecondaryFire, "latest ordinary press replaces the one previous reservation");
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
            Check(controller.BufferedAction == PlayerActionSlot.Skill1 && shots[2] == skillBefore,
                "dash recovery retains one skill without executing it early");
            yield return new WaitForSeconds(0.45f);
            Check(shots[2] == skillBefore + 1, "queued skill executes after dash recovery");
            yield return ResetCase();

            primaryBefore = shots[0];
            pressed = Dash;
            yield return new WaitForSeconds(0.05f);
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerActionSlot.PrimaryFire && motor.IsDashing,
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
            Check(controller.BufferedAction == PlayerActionSlot.None, "maximum buffer duration expires during an unfinished delay");
            yield return new WaitForSeconds(0.4f);
            Check(shots[1] == secondaryBefore, "expired tap does not execute when the delay ends");
            yield return ResetCase();

            SetField(controller, "maxInputBufferTime", 0f);
            primaryBefore = shots[0];
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Secondary;
            yield return new WaitForSeconds(0.4f);
            Check(shots[0] == primaryBefore + 1 && shots[1] == secondaryBefore && controller.BufferedAction == PlayerActionSlot.None,
                "zero limit permits immediate input while disabling deferred input");
            yield return ResetCase();

            dashBefore = dashCount;
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Dash;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerActionSlot.Dash && stamina.Current == stamina.Max,
                "eligible dash is buffered without reserving stamina");
            stamina.TryConsume(stamina.Current - 10f);
            yield return new WaitForSeconds(0.3f);
            Check(dashCount == dashBefore && controller.BufferedAction == PlayerActionSlot.None && stamina.Current == 10f,
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
            yield return null;
            Check(controller.BufferedAction == PlayerActionSlot.None, "active cooldown rejects input before creating a reservation");
            yield return new WaitForSeconds(0.2f);
            Check(shots[2] == skillBefore + 1 && controller.BufferedAction == PlayerActionSlot.None,
                "rejected cooldown input leaves no queued skill");
            yield return new WaitForSeconds(1f);
            Check(shots[2] == skillBefore + 1, "discarded skill never waits for cooldown recovery");
            SetField(bindings[2].Attack.Data, "cooldown", 0f);
            yield return ResetCase();

            int skill2Before = shots[3];
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            pressed = Skill2;
            yield return new WaitForSeconds(0.05f);
            Check(controller.BufferedAction == PlayerActionSlot.Skill2, "ready skill is eligible when its input arrives");
            SetField(bindings[3].Action, "cooldownEndsAt", Time.timeAsDouble + 0.5d);
            yield return new WaitForSeconds(0.35f);
            Check(shots[3] == skill2Before && controller.BufferedAction == PlayerActionSlot.None,
                "cooldown acquired after buffering is rechecked and consumes the reservation");
            SetField(bindings[3].Action, "cooldownEndsAt", 0d);
            yield return ResetCase();

            secondaryBefore = shots[1];
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            held = Secondary;
            yield return new WaitForSeconds(0.08f);
            held = 0;
            Check(controller.BufferedAction == PlayerActionSlot.None, "held repetition alone never creates a reservation");
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
                controller.BufferedAction == PlayerActionSlot.Dash, "movement and grounded jump work while dash is queued during attack");
            dashBefore = dashCount;
            yield return new WaitForSeconds(0.3f);
            Check(!motor.IsGrounded && motor.CanPerformActions && controller.BufferedAction == PlayerActionSlot.None &&
                dashCount == dashBefore && stamina.Current == stamina.Max,
                "buffered dash is discarded without stamina use when attack unlocks in the air");
            movement = Vector2.zero;
            yield return new WaitForSeconds(0.6f);
            Check(motor.IsGrounded && dashCount == dashBefore && stamina.Current == stamina.Max,
                "discarded buffered dash does not execute after landing");
            yield return ResetCase();

            foreach (string interruption in new[] { "Pause", "Cursor", "Reader", "Controller", "Focus" })
            {
                dashBefore = dashCount;
                pressed = Primary;
                yield return new WaitForSeconds(0.05f);
                pressed = Dash;
                yield return new WaitForSeconds(0.05f);
                Check(controller.BufferedAction == PlayerActionSlot.Dash, interruption + " probe has a pending dash");
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
                Check(controller.BufferedAction == PlayerActionSlot.None, interruption + " clears the reservation");
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
            Check(dashCount == dashBefore && controller.BufferedAction == PlayerActionSlot.None,
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
                $"buffer={controller.BufferedAction}, phase={attacks.GetBinding(PlayerActionSlot.PrimaryFire).Action.Phase}, " +
                $"dashes={dashCount}, shots={TotalShots()}, stamina={stamina.Current}");
            checks.Add(message);
        }

        private void SetInput(string name, object value) => typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);
        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void SetField(object target, string name, object value)
        { PlayerActionValidationUtility.Set(target, name, value); }
    }
}
