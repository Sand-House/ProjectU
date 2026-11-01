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
    public static class PlayerActionRestrictionValidation
    {
        private const string Running = "ProjectU.PlayerActionRestrictionValidation";
        static PlayerActionRestrictionValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player Action Restriction Validation").AddComponent<PlayerActionRestrictionValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player Action Restrictions")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        internal static void Finish(int result)
        {
            SessionState.SetBool(Running, false);
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            if (Application.isBatchMode) EditorApplication.Exit(result);
            else EditorApplication.ExitPlaymode();
        }
    }

    public sealed class PlayerActionMovementProbe : PlayerAction
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Skill2;
        protected override bool UsesDelayLock => false;
        protected override bool BlocksCore(PlayerActionSlot slot) => false;
        protected override bool CheckRequirements(in ActionContext request) => true;
        protected override ActionResult ExecuteCore(in ActionContext request) => ActionResult.Running;
    }

    [DefaultExecutionOrder(-150)]
    public sealed class PlayerActionRestrictionValidationDriver : MonoBehaviour
    {
        private PlayerInputReader input;
        private PlayerController controller;
        private PlayerMotor motor;
        private PlayerCombat combat;
        private PlayerAction skill, ultimate, dash, primary;
        private int pressed;
        private Vector2 movement;
        private int skillExecutions, ultimateExecutions, skillLogs, ultimateLogs, attackExecutions, failures;
        private double started;
        private readonly List<string> checks = new List<string>();

        private IEnumerator Start()
        {
            EnemyValidationIsolation.DisableDemoEnemies();
            IEnumerator validation = Validate();
            while (true)
            {
                bool more;
                object current = null;
                try { more = validation.MoveNext(); if (more) current = validation.Current; }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    PlayerActionRestrictionValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("PLAYER ACTION RESTRICTION VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            PlayerActionRestrictionValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            started = EditorApplication.timeSinceStartup;
            Time.captureDeltaTime = 1f / 60f;
            input = FindFirstObjectByType<PlayerInputReader>();
            controller = input.GetComponent<PlayerController>();
            motor = input.GetComponent<PlayerMotor>();
            combat = input.GetComponent<PlayerCombat>();
            skill = combat.GetAction(PlayerActionSlot.Skill2);
            ultimate = combat.GetAction(PlayerActionSlot.Ultimate);
            dash = combat.GetAction(PlayerActionSlot.Dash);
            primary = combat.GetAction(PlayerActionSlot.PrimaryFire);
            Application.logMessageReceived += ObserveLog;
            combat.ActionExecuted += slot =>
            {
                if (slot == PlayerActionSlot.Skill2) skillExecutions++;
                if (slot == PlayerActionSlot.Ultimate) ultimateExecutions++;
            };
            combat.AttackExecuted += _ => attackExecutions++;
            skill.OnFailed += () => failures++;
            yield return new WaitForSeconds(0.15f);

            Check(skill.RequiresGrounded && skill.CanMove && skill.WindupTime == 0.5f && skill.RecoveryTime == 1f,
                "saved Skill2 is ground-only and movable with 0.5/1 second delays");
            Check(!ultimate.RequiresGrounded && !ultimate.CanMove && ultimate.WindupTime == 0.5f && ultimate.RecoveryTime == 1f,
                "saved Ultimate locks movement and supports airborne use with 0.5/1 second delays");
            Check(dash.RequiresGrounded && dash.CanMove && !primary.RequiresGrounded && primary.CanMove,
                "saved dash stays grounded and existing attacks keep movement and airborne use");
            var probe = input.gameObject.AddComponent<PlayerActionMovementProbe>();
            Check(probe.CanMove && !probe.RequiresGrounded, "new actions default to movable without a ground requirement");
            Destroy(probe);

            Position(10f);
            Check(!motor.IsGrounded && !skill.CanBuffer && !skill.CanStart && !skill.TryStart() &&
                !combat.TryStartAction(PlayerActionSlot.Skill2), "common ground condition rejects direct and combat skill requests in air");
            PlayerStamina stamina = input.GetComponent<PlayerStamina>();
            stamina.RecoveryRate = 0f;
            float resource = stamina.Current;
            Check(!dash.CanBuffer && !motor.CanDash && !motor.TryDash(Vector3.forward) && stamina.Current == resource,
                "common ground condition rejects dash without stamina use");
            // Turning off the action flag proves Motor no longer owns the grounding rule.
            Set(dash, "requiresGrounded", false);
            Check(motor.TryDash(Vector3.forward) && motor.IsDashing, "action setting permits airborne dash without a Motor ground gate");
            dash.enabled = false;
            dash.enabled = true;
            Set(dash, "requiresGrounded", true);
            yield return null;

            Position(1.05f);
            yield return new WaitForSeconds(0.1f);
            Check(motor.IsGrounded && skill.CanBuffer && skill.CanStart, "ground contact enables Skill2");
            Check(skill.TryStart(), "grounded Skill2 enters windup");
            Position(10f);
            yield return new WaitForSeconds(0.55f);
            Check(failures == 1 && skillExecutions == 0 && skillLogs == 0 && skill.Phase == ActionPhase.Ready &&
                skill.CooldownRemaining == 0f, "leaving ground during windup fails execution without log recovery or cooldown");

            // Exercise receipt and consumption through the real Controller buffer.
            Position(1.05f);
            Set(primary, "recoveryTime", 0.35f);
            Check(combat.TryStartAction(PlayerActionSlot.PrimaryFire), "attack starts buffer eligibility scenario");
            yield return null;
            yield return null;
            Position(10f);
            pressed = 1;
            yield return null;
            yield return null;
            Check(controller.BufferedAction == PlayerActionSlot.None, "airborne F is rejected while another action is delayed");
            yield return new WaitForSeconds(0.4f);
            Position(1.05f);
            Check(combat.TryStartAction(PlayerActionSlot.PrimaryFire), "attack starts grounded reservation scenario");
            yield return null;
            yield return null;
            pressed = 1;
            yield return null;
            yield return null;
            Check(controller.BufferedAction == PlayerActionSlot.Skill2, "grounded F buffers during attack recovery");
            Position(10f);
            yield return new WaitForSeconds(0.4f);
            Check(controller.BufferedAction == PlayerActionSlot.None && skillExecutions == 0 && skillLogs == 0,
                "buffered ground-only skill is consumed and discarded when execution is airborne");
            Position(1.05f);
            yield return new WaitForSeconds(0.1f);
            Check(skillExecutions == 0 && skill.Phase == ActionPhase.Ready, "discarded skill does not execute on landing");

            int attacksBefore = attackExecutions;
            movement = Vector2.right;
            Vector3 position = input.transform.position;
            pressed = 1;
            yield return null;
            yield return null;
            Check(skill.Phase == ActionPhase.Windup && skillLogs == 0, "F enters windup without an early execution log");
            yield return new WaitForSeconds(0.2f);
            Check(HorizontalDistance(position) > 0.2f && motor.CanMove && motor.CanJump, "movable Skill2 permits movement and jump during windup");
            Time.timeScale = 0f;
            for (int index = 0; index < 5; index++) yield return null;
            Check(skill.Phase == ActionPhase.Windup && skillLogs == 0, "pause freezes demo skill windup");
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.35f);
            Check(skillExecutions == 1 && skillLogs == 1 && skill.Phase == ActionPhase.Recovery && attackExecutions == attacksBefore,
                "Skill2 logs and reports one action success after windup without an attack event");
            position = input.transform.position;
            yield return new WaitForSeconds(0.15f);
            Check(HorizontalDistance(position) > 0.2f && motor.CanJump, "movable Skill2 permits movement and jump during recovery");
            movement = Vector2.zero;
            yield return new WaitForSeconds(1f);

            // A queued immobile action must not lock movement before acceptance.
            Check(combat.TryStartAction(PlayerActionSlot.PrimaryFire), "attack starts Ultimate reservation scenario");
            yield return null;
            yield return null;
            movement = Vector2.right;
            pressed = 2;
            yield return null;
            yield return null;
            Check(controller.BufferedAction == PlayerActionSlot.Ultimate && motor.CanMove, "queued Ultimate does not lock movement");
            while (ultimate.Phase == ActionPhase.Ready) yield return null;
            position = input.transform.position;
            Check(!motor.CanMove && !motor.CanJump && ultimate.BlocksMovement, "accepted Ultimate locks movement and jump during windup");
            pressed = 4;
            yield return new WaitForSeconds(0.2f);
            Check(HorizontalDistance(position) < 0.001f && motor.VerticalSpeed <= 0f && motor.HorizontalVelocity.sqrMagnitude < 0.001f,
                "WASD and jump commands cannot move the grounded casting player");
            Time.timeScale = 0f;
            for (int index = 0; index < 5; index++) yield return null;
            Check(!motor.CanMove && ultimateExecutions == 0, "pause preserves movement lock without executing Ultimate");
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.35f);
            Check(ultimateExecutions == 1 && ultimateLogs == 1 && ultimate.Phase == ActionPhase.Recovery &&
                !motor.CanMove && !motor.CanJump && HorizontalDistance(position) < 0.001f,
                "Ultimate logs once after windup and keeps movement locked through recovery");
            yield return new WaitForSeconds(1.1f);
            Check(motor.CanMove && motor.CanJump && HorizontalDistance(position) > 0.1f,
                "movement and jump resume after recovery without a stale lock");

            movement = Vector2.zero;
            Position(10f);
            Check(ultimate.CanBuffer && ultimate.CanStart && ultimate.TryStart(), "Ultimate without a ground requirement accepts airborne use");
            float height = input.transform.position.y;
            yield return new WaitForSeconds(0.15f);
            Check(!motor.CanMove && input.transform.position.y < height - 0.05f, "immobile airborne skill preserves gravity");
            Check(ultimate.Cancel() && motor.CanMove, "cancelling windup immediately releases its movement lock");
            yield return null;

            Position(1.05f);
            var first = input.gameObject.AddComponent<PlayerActionMovementProbe>();
            var second = input.gameObject.AddComponent<PlayerActionMovementProbe>();
            Set(first, "canMove", false);
            Set(second, "canMove", false);
            Check(first.TryStart() && second.TryStart(), "unbound nonblocking actions accept concurrent requests");
            yield return null;
            yield return null;
            Check(first.Phase == ActionPhase.Active && second.Phase == ActionPhase.Active && !motor.CanMove,
                "movement restriction also covers Active actions without a delay lock or binding");
            first.Cancel();
            Check(!motor.CanMove, "cancelling one of two immobile actions preserves the other lock");
            second.enabled = false;
            Check(motor.CanMove, "disabling the remaining action releases movement restriction");
            Destroy(first);
            Destroy(second);
            yield return null;

            // Skill acceptance retains the existing reload cancellation rule.
            Set(combat, "currentAmmo", 29);
            Check(combat.TryReload() && combat.IsReloading, "partial magazine starts reload before demo skill");
            yield return null;
            Check(skill.TryStart() && !combat.IsReloading && combat.CurrentAmmo == 29,
                "grounded demo skill acceptance cancels reload without refilling ammo");
            skill.Cancel();
            yield return null;
            Check(ultimate.TryStart() && !motor.CanMove, "Ultimate can start again after cancellation");
            input.GetComponent<Mortal>().Dead(null);
            Check(motor.CanMove, "death releases action-owned movement restriction");
        }

        private void Position(float height)
        {
            CharacterController body = input.GetComponent<CharacterController>();
            body.enabled = false;
            input.transform.position = new Vector3(0f, height, 0f);
            Set(motor, "verticalSpeed", 0f);
            body.enabled = true;
            Physics.SyncTransforms();
            body.Move(Vector3.down * 0.1f);
        }

        private float HorizontalDistance(Vector3 position)
            => Vector2.Distance(new Vector2(position.x, position.z), new Vector2(input.transform.position.x, input.transform.position.z));

        private void Update()
        {
            if (input == null) return;
            if (EditorApplication.timeSinceStartup - started > 90d)
            {
                Debug.LogError("Action restriction validation timed out.");
                PlayerActionRestrictionValidation.Finish(1);
            }
            SetInput("IsGameplayInputEnabled", input.isActiveAndEnabled && Time.timeScale > 0f);
            SetInput("Move", movement);
            SetInput("Look", Vector2.zero);
            foreach (string name in new[] { "PrimaryFire", "SecondaryFire", "Skill1", "Reload", "Dash" })
                SetInput(name, default(ButtonInputState));
            SetInput("Skill2", Snapshot((pressed & 1) != 0));
            SetInput("Ultimate", Snapshot((pressed & 2) != 0));
            SetInput("Jump", Snapshot((pressed & 4) != 0));
            pressed = 0;
        }

        private void ObserveLog(string message, string trace, LogType type)
        {
            if (message == "[Player Action] Skill2 (F): Executed") skillLogs++;
            if (message == "[Player Action] Ultimate (Q): Executed") ultimateLogs++;
        }
        private void OnDestroy() => Application.logMessageReceived -= ObserveLog;
        private static void Set(object target, string field, object value) => PlayerActionValidationUtility.Set(target, field, value);
        private void SetInput(string name, object value) => typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);
        private static ButtonInputState Snapshot(bool pressed)
            => (ButtonInputState)Activator.CreateInstance(typeof(ButtonInputState), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { pressed, pressed, false }, null);
        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }
    }
}
