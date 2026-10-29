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
    public static class PlayerDashValidation
    {
        private const string Running = "ProjectU.PlayerDashValidation";

        static PlayerDashValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player Dash Validation Driver").AddComponent<PlayerDashValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player Dash")]
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

    // Feed snapshots after Reader.Update; inspect outcomes after all LateUpdate consumers.
    [DefaultExecutionOrder(-150)]
    public sealed class PlayerDashValidationDriver : MonoBehaviour
    {
        private PlayerInputReader input;
        private PlayerMotor motor;
        private PlayerStamina stamina;
        private PlayerAnimationController animationController;
        private PlayerAttackController attacks;
        private PlayerCameraController cameraController;
        private Vector2 movement;
        private bool dashPress;
        private bool dashHeld;
        private bool otherActions;
        private bool reloadPress;
        private bool grenadePress;
        private bool fireHeld;
        private int executed;
        private int grenadeExecutions;
        private int cancelled;
        private int dashExecutions;
        private bool dashEventStateValid = true;
        private double started;
        private readonly List<string> checks = new List<string>();

        private IEnumerator Start()
        {
            IEnumerator validation = Validate();
            while (true)
            {
                bool more;
                object current = null;
                try
                {
                    more = validation.MoveNext();
                    if (more) current = validation.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    PlayerDashValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("PLAYER DASH VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            PlayerDashValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            started = EditorApplication.timeSinceStartup;
            Time.captureDeltaTime = 1f / 60f;
            input = UnityEngine.Object.FindFirstObjectByType<PlayerInputReader>();
            motor = input.GetComponent<PlayerMotor>();
            stamina = input.GetComponent<PlayerStamina>();
            animationController = input.GetComponent<PlayerAnimationController>();
            attacks = input.GetComponent<PlayerAttackController>();
            cameraController = UnityEngine.Object.FindFirstObjectByType<PlayerCameraController>();
            var settings = new SerializedObject(motor);
            Check(input.GetComponent<PlayerStat>().DashDistance == 5f &&
                settings.FindProperty("dashDuration").floatValue == 0.2f &&
                settings.FindProperty("dashPostDelay").floatValue == 0.3f, "saved Inspector dash settings");
            SetField(input.GetComponent<PlayerController>(), "logInput", false);
            stamina.RecoveryRate = 0f;
            motor.OnDashExecuted += () =>
            {
                dashExecutions++;
                dashEventStateValid &= motor.IsDashing && !motor.CanPerformActions;
            };
            attacks.AttackExecuted += slot =>
            {
                executed++;
                if (slot == AttackSlot.Skill1) grenadeExecutions++;
            };
            foreach (PlayerAttackBinding binding in attacks.AttackBindings)
                binding.Attack.OnCancelled += () => cancelled++;
            gameObject.AddComponent<PlayerDashValidationObserver>().Driver = this;
            yield return new WaitForSeconds(0.2f);

            // Verify idle/forward/left/back/right/diagonal with rotated camera headings.
            Vector2[] directions = { Vector2.zero, Vector2.up, Vector2.left, Vector2.down, Vector2.right,
                new Vector2(-1f, -1f).normalized };
            foreach (Vector2 direction in directions)
            {
                ResetPosition();
                stamina.Recover(100f);
                float yaw = direction == Vector2.zero ? 90f : 35f;
                typeof(PlayerCameraController).GetProperty("Yaw").SetValue(cameraController, yaw);
                movement = direction;
                Vector3 expected = Quaternion.Euler(0f, yaw, 0f) *
                    (direction == Vector2.zero ? Vector3.forward : new Vector3(direction.x, 0f, direction.y));
                Vector3 before = input.transform.position;
                int dashBaseline = dashExecutions;
                dashPress = dashHeld = true;
                yield return null;
                Check(motor.IsDashing && !motor.CanPerformActions && !motor.CanJump && stamina.Current == 75f,
                    "press starts one dash and consumes 25: " + direction);
                Check(dashExecutions == dashBaseline + 1 && dashEventStateValid,
                    "OnDashExecuted fires once after dash state is initialized: " + direction);
                // Stop normal movement, then turn the camera mid-dash to prove direction is captured.
                movement = Vector2.zero;
                typeof(PlayerCameraController).GetProperty("Yaw").SetValue(cameraController, yaw + 80f);
                while (motor.IsDashing) yield return null;
                Vector3 displacement = input.transform.position - before;
                displacement.y = 0f;
                Check(Vector3.Distance(displacement, expected * 5f) < 0.03f,
                    "five-meter fixed direction: " + direction + " actual=" + displacement);
                Check(motor.IsDashPostDelay && !motor.CanPerformActions && motor.CanJump,
                    "post-delay permits jump while blocking other actions");
                while (!motor.CanPerformActions) yield return null;
                yield return new WaitForSeconds(0.1f);
                Check(stamina.Current == 75f && dashExecutions == dashBaseline + 1,
                    "held Shift never repeats or emits another dash event after recovery");
                dashHeld = false;
            }

            // Same-frame priority, pending attack/reload cancellation, spam and recovery movement.
            ResetPosition();
            stamina.Recover(100f);
            SetField(attacks, "currentAmmo", attacks.MaxAmmo - 1);
            reloadPress = true;
            yield return null;
            yield return null;
            Check(animationController.IsReloading, "reload begins before dash");
            Attack primary = attacks.GetBinding(AttackSlot.PrimaryFire).Attack;
            var windupData = Instantiate((HitscanAttackData)primary.Data);
            SetField(windupData, "windupTime", 0.1f);
            SetField(primary, "data", windupData);
            Check(primary.TryAttack(attacks.GetBinding(AttackSlot.PrimaryFire).Origin,
                attacks.GetAimPoint()) && primary.Phase != AttackPhase.Windup,
                "reload immediately cancels a direct primary request");
            int baseline = executed;
            int eventBaseline = dashExecutions;
            dashPress = dashHeld = otherActions = true;
            yield return null;
            yield return null;
            Check(!animationController.IsReloading && primary.Phase != AttackPhase.Windup && cancelled == 1,
                "dash cancels reload after its primary guard rejected pending fire");
            Check(motor.VerticalSpeed <= 0f && stamina.Current == 75f && executed == baseline,
                "dash wins over simultaneous jump, fire, grenade, reload and repeated Shift");
            while (motor.IsDashing) yield return null;
            Animator animator = input.GetComponentInChildren<Animator>();
            Check(animator.GetCurrentAnimatorStateInfo(1).IsName("Upper Body.Rifle Aim"),
                "dash event transitions the upper-body animation to rifle aim");
            float aimTime = animator.GetCurrentAnimatorStateInfo(1).normalizedTime;
            Vector3 recoveryStart = input.transform.position;
            movement = Vector2.right;
            float recoveryStarted = Time.time;
            Check(motor.CanJump, "dash post-delay accepts jump requests");
            motor.RequestJump();
            yield return null;
            Check(motor.IsDashPostDelay && motor.VerticalSpeed > 0f && executed == baseline &&
                stamina.Current == 75f && !animationController.IsReloading,
                "jump executes during dash post-delay without unlocking attacks reload or another dash");
            while (!motor.CanPerformActions) yield return null;
            Check(Time.time - recoveryStarted >= 0.28f &&
                Vector3.ProjectOnPlane(input.transform.position - recoveryStart, Vector3.up).magnitude > 1f &&
                executed == baseline && stamina.Current == 75f && !animationController.IsReloading,
                "post-delay permits WASD and jump while rejecting other actions without spending stamina");
            Check(dashExecutions == eventBaseline + 1 &&
                animator.GetCurrentAnimatorStateInfo(1).normalizedTime > aimTime,
                "rejected dash presses emit no event and rifle aim continues without restarting");
            otherActions = false;
            movement = Vector2.zero;
            dashHeld = false;
            fireHeld = true;
            yield return new WaitForSeconds(0.2f);
            Check(executed > baseline, "held fire resumes after action lock");
            fireHeld = false;
            while (!motor.CanPerformActions) yield return null;
            stamina.Recover(100f);
            while (!primary.CanAttack) yield return null;
            Check(primary.TryAttack(attacks.GetBinding(AttackSlot.PrimaryFire).Origin, attacks.GetAimPoint()) &&
                primary.Phase == AttackPhase.Windup, "primary windup is accepted after reload cancellation");
            int cancellationsBeforeDash = cancelled;
            dashPress = true;
            yield return null;
            Check(!motor.IsDashing && stamina.Current == 100f, "attack windup rejects a dash without spending stamina");
            yield return null;
            Check(primary.Phase == AttackPhase.Windup && cancelled == cancellationsBeforeDash,
                "rejected dash preserves the accepted primary windup");
            while (!motor.CanPerformActions) yield return null;
            dashPress = true;
            yield return null;
            Check(motor.IsDashing && stamina.Current == 75f, "fresh dash press succeeds after attack recovery");
            while (!motor.CanPerformActions) yield return null;

            ResetPosition();
            stamina.TryConsume(stamina.Current - 10f);
            float wait = stamina.RecoveryDelayRemaining;
            Vector3 insufficientStart = input.transform.position;
            eventBaseline = dashExecutions;
            dashPress = true;
            yield return null;
            Vector3 insufficientDisplacement = input.transform.position - insufficientStart;
            insufficientDisplacement.y = 0f;
            Check(!motor.IsDashing && stamina.Current == 10f &&
                insufficientDisplacement.magnitude < 0.02f &&
                stamina.RecoveryDelayRemaining <= wait && dashExecutions == eventBaseline,
                "insufficient stamina rejects dash without displacement or event");

            // Isolate visual interruption from the saved grenade's recovery/cooldown.
            Attack grenade = attacks.GetBinding(AttackSlot.Skill1).Attack;
            var visualGrenadeData = Instantiate((ProjectileAttackData)grenade.Data);
            SetField(visualGrenadeData, "recoveryTime", 0f);
            SetField(visualGrenadeData, "cooldown", 0f);
            SetField(grenade, "data", visualGrenadeData);
            // A dash cancels grenade visuals once the gameplay action lock has ended.
            ResetPosition();
            typeof(PlayerCameraController).GetProperty("Yaw").SetValue(cameraController, 0f);
            stamina.Recover(100f);
            int grenadeBaseline = grenadeExecutions;
            grenadePress = true;
            yield return new WaitForSeconds(0.12f);
            Check(grenadeExecutions == grenadeBaseline + 1 && animationController.IsThrowingGrenade,
                "Skill1 still launches once and starts the grenade animation");
            dashPress = true;
            yield return new WaitForSeconds(0.12f);
            Check(!animationController.IsThrowingGrenade &&
                animator.GetCurrentAnimatorStateInfo(1).IsName("Upper Body.Rifle Aim") &&
                grenadeExecutions == grenadeBaseline + 1,
                "dash interrupts an active grenade animation without another launch");
            while (!motor.CanPerformActions) yield return null;
            grenadePress = true;
            yield return new WaitForSeconds(0.12f);
            Check(grenadeExecutions == grenadeBaseline + 2 && animationController.IsThrowingGrenade,
                "a fresh Skill1 press works after dash recovery");

            // A launch in LateUpdate can leave Skill1 pending until the next Animator evaluation.
            animator.SetTrigger("Skill1");
            Check(motor.TryDash(Vector3.forward), "dash accepts a pending grenade visual trigger");
            yield return new WaitForSeconds(0.12f);
            Check(!animationController.IsThrowingGrenade &&
                animator.GetCurrentAnimatorStateInfo(1).IsName("Upper Body.Rifle Aim"),
                "dash clears the pending Skill1 trigger instead of replaying the throw");
            while (!motor.CanPerformActions) yield return null;
            Check(!animationController.IsThrowingGrenade && grenadeExecutions == grenadeBaseline + 2,
                "grenade animation and launch do not replay after dash recovery");

            // CharacterController must stop at walls, even when duration is shorter than one frame.
            ResetPosition();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Dash Validation Wall";
            wall.transform.position = input.transform.position + new Vector3(0f, 1f, 2f);
            wall.transform.localScale = new Vector3(10f, 5f, 0.2f);
            Physics.SyncTransforms();
            typeof(PlayerCameraController).GetProperty("Yaw").SetValue(cameraController, 0f);
            SetField(motor, "dashDuration", 0.005f);
            SetField(motor, "dashPostDelay", 0f);
            stamina.Recover(100f);
            dashPress = true;
            yield return null;
            yield return null;
            Check(input.transform.position.z > 0.5f && input.transform.position.z < 1.8f &&
                motor.CanPerformActions, "short dash respects wall collision and zero post-delay");
            Destroy(wall);
            yield return null;

            // Capture distance/time settings at start, and freeze both phases when paused.
            ResetPosition();
            stamina.Recover(100f);
            input.GetComponent<PlayerStat>().DashDistance = 3f;
            SetField(motor, "dashDuration", 0.4f);
            SetField(motor, "dashPostDelay", 0.25f);
            dashPress = true;
            yield return null;
            Vector3 pausePosition = input.transform.position;
            float remaining = GetField<float>(motor, "dashRemaining");
            Time.timeScale = 0f;
            eventBaseline = dashExecutions;
            Check(!motor.TryDash(Vector3.forward) && dashExecutions == eventBaseline,
                "paused dash request emits no event");
            for (int i = 0; i < 4; i++) yield return null;
            Check(input.transform.position == pausePosition && GetField<float>(motor, "dashRemaining") == remaining &&
                !motor.CanPerformActions, "pause freezes dash position and duration");
            Time.timeScale = 1f;
            while (motor.IsDashing) yield return null;
            Check(Mathf.Abs(input.transform.position.z - 3f) < 0.03f, "custom distance and longer duration");
            remaining = GetField<float>(motor, "dashPostDelayRemaining");
            Time.timeScale = 0f;
            for (int i = 0; i < 4; i++) yield return null;
            Check(GetField<float>(motor, "dashPostDelayRemaining") == remaining && !motor.CanPerformActions && !motor.CanJump,
                "pause freezes post-delay");
            Time.timeScale = 1f;
            while (!motor.CanPerformActions) yield return null;

            // An inactive visual component must unsubscribe, then synchronize on re-enable.
            animationController.enabled = false;
            animator.Play("Upper Body.Reloading", 1, 0f);
            animator.Update(0f);
            stamina.Recover(100f);
            dashPress = true;
            yield return null;
            Check(animator.GetCurrentAnimatorStateInfo(1).IsName("Upper Body.Reloading"),
                "disabled animation controller does not receive dash events");
            animationController.enabled = true;
            yield return new WaitForSeconds(0.08f);
            Check(animator.GetCurrentAnimatorStateInfo(1).IsName("Upper Body.Rifle Aim"),
                "reenabled animation controller synchronizes an ongoing dash");
            while (!motor.CanPerformActions) yield return null;

            // The event may arrive before Animation.Update re-enables a paused Animator.
            reloadPress = true;
            yield return null;
            yield return null;
            Check(animationController.IsReloading, "reload begins before paused dash-event probe");
            Time.timeScale = 0f;
            for (int i = 0; i < 3; i++) yield return null;
            stamina.Recover(100f);
            dashPress = true;
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.08f);
            Check(!animationController.IsReloading && animator.enabled &&
                animator.GetCurrentAnimatorStateInfo(1).IsName("Upper Body.Rifle Aim"),
                "dash event on the resume frame cancels reload and restores rifle aim");
            while (!motor.CanPerformActions) yield return null;
            stamina.Recover(100f);
            dashPress = true;
            yield return null;
            motor.enabled = false;
            Check(!motor.IsDashing && !motor.IsDashPostDelay && motor.CanPerformActions && !motor.CanJump,
                "disabling motor clears dash and action lock");
            eventBaseline = dashExecutions;
            Check(!motor.TryDash(Vector3.forward) && dashExecutions == eventBaseline,
                "disabled motor rejects dash without an event");
            Time.captureDeltaTime = 0f;
        }

        private void Update()
        {
            if (input == null) return;
            bool actionPress = otherActions && (!motor.CanPerformActions || dashPress);
            SetInput("Move", movement);
            SetInput("Dash", Snapshot(dashPress || actionPress, dashHeld || actionPress));
            SetInput("Jump", Snapshot(actionPress, actionPress));
            SetInput("Reload", Snapshot(reloadPress || actionPress, actionPress));
            SetInput("PrimaryFire", Snapshot(actionPress, fireHeld || actionPress));
            SetInput("SecondaryFire", Snapshot(actionPress, actionPress));
            SetInput("Skill1", Snapshot(grenadePress || actionPress, actionPress));
            SetInput("Skill2", Snapshot(actionPress, actionPress));
            SetInput("Ultimate", Snapshot(actionPress, actionPress));
            dashPress = reloadPress = grenadePress = false;
        }

        internal void Observe()
        {
            if (input == null) return;
            if (EditorApplication.timeSinceStartup - started > 90d)
            {
                Debug.LogError("Dash validation timed out.");
                PlayerDashValidation.Finish(1);
            }
        }

        private void ResetPosition()
        {
            CharacterController body = input.GetComponent<CharacterController>();
            body.enabled = false;
            input.transform.position = new Vector3(0f, 1.05f, 0f);
            body.enabled = true;
            Physics.SyncTransforms();
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }

        private void SetInput(string name, object value) => typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);

        private static ButtonInputState Snapshot(bool pressed, bool held) =>
            (ButtonInputState)Activator.CreateInstance(typeof(ButtonInputState), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { pressed, held, false }, null);

        private static void SetField(object target, string name, object value)
        {
            Type type = target.GetType();
            FieldInfo field;
            while ((field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)) == null)
                type = type.BaseType;
            field.SetValue(target, value);
        }

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    [DefaultExecutionOrder(200)]
    public sealed class PlayerDashValidationObserver : MonoBehaviour
    {
        internal PlayerDashValidationDriver Driver;
        private void LateUpdate() => Driver.Observe();
    }
}
