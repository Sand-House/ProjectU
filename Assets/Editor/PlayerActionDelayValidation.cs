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
    public static class PlayerActionDelayValidation
    {
        private const string Running = "ProjectU.PlayerActionDelayValidation";

        static PlayerActionDelayValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player Action Delay Validation Driver").AddComponent<PlayerActionDelayValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player Action Delay")]
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

    [DefaultExecutionOrder(-150)]
    public sealed class PlayerActionDelayValidationDriver : MonoBehaviour
    {
        private PlayerInputReader input;
        private PlayerCombat attacks;
        private PlayerMotor motor;
        private PlayerStamina stamina;
        private Combatant owner;
        private int pressMask;
        private int heldMask;
        private bool otherActions;
        private Vector2 movement;
        private int executions;
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
                    PlayerActionDelayValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("PLAYER ACTION DELAY VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            PlayerActionDelayValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            started = EditorApplication.timeSinceStartup;
            Time.captureDeltaTime = 1f / 60f;
            input = UnityEngine.Object.FindFirstObjectByType<PlayerInputReader>();
            attacks = input.GetComponent<PlayerCombat>();
            motor = input.GetComponent<PlayerMotor>();
            stamina = input.GetComponent<PlayerStamina>();
            owner = input.GetComponent<Combatant>();
            SetField(input.GetComponent<PlayerController>(), "logInput", false);
            // This suite isolates immediate action locks. EnhancedInputValidation covers buffering.
            SetField(input.GetComponent<PlayerController>(), "maxInputBufferTime", 0f);
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
                AttackData data = Instantiate(binding.Attack.Data);
                SetField(data, "windupTime", 0.18f);
                SetField(data, "recoveryTime", 0.24f);
                SetField(data, "cooldown", 0.9f);
                SetField(binding.Attack, "data", data);
            }
            SetField(attacks, "actionBindings", bindings.ToArray());
            attacks.enabled = true;
            attacks.AttackExecuted += slot => executions++;
            yield return new WaitForSeconds(0.2f);

            foreach (PlayerActionBinding binding in bindings)
            {
                ResetPosition();
                while (!motor.IsGrounded) yield return null;
                stamina.Recover(100f);
                int baseline = executions;
                Attack attack = binding.Attack;
                Check(PlayerActionValidationUtility.For(attack).TryStart(binding.Origin, attacks.GetAimPoint()) && owner.IsPerformingAttack &&
                    !motor.CanPerformActions && !motor.CanDash, binding.Slot + " immediately locks actions at windup start");
                Check(!attacks.TryReload() && !motor.TryDash(Vector3.forward), binding.Slot + " blocks reload and direct dash APIs");
                foreach (PlayerActionBinding rival in bindings)
                    if (rival != binding)
                        Check(!PlayerActionValidationUtility.For(rival.Attack).TryStart(rival.Origin, attacks.GetAimPoint()), binding.Slot + " blocks direct " + rival.Slot);
                movement = Vector2.right;
                Vector3 before = input.transform.position;
                otherActions = true;
                pressMask = 31;
                yield return new WaitForSeconds(0.08f);
                Check(PlayerActionValidationUtility.For(attack).Phase == PlayerActionPhase.Windup && executions == baseline && !attacks.IsReloading &&
                    !motor.IsDashing && stamina.Current == 100f && motor.VerticalSpeed > 0f && motor.CanJump,
                    binding.Slot + " windup permits jump and rejects attack dash reload without cancelling or consuming stamina");
                Check(Vector3.ProjectOnPlane(input.transform.position - before, Vector3.up).magnitude > 0.2f,
                    binding.Slot + " windup permits horizontal movement during jump");
                yield return new WaitForSeconds(0.15f);
                Check(PlayerActionValidationUtility.For(attack).Phase == PlayerActionPhase.Recovery && executions == baseline + 1 && !motor.CanPerformActions,
                    binding.Slot + " executes once and keeps the shared lock during recovery");
                // Land independently so a second jump proves recovery permits new input too.
                otherActions = false;
                ResetPosition();
                while (!motor.IsGrounded) yield return null;
                Check(PlayerActionValidationUtility.For(attack).Phase == PlayerActionPhase.Recovery && !motor.CanPerformActions && motor.CanJump,
                    binding.Slot + " recovery permits a new jump request while the action lock remains");
                before = input.transform.position;
                otherActions = true;
                pressMask = 31;
                foreach (PlayerActionBinding rival in bindings)
                    if (rival != binding)
                        Check(!PlayerActionValidationUtility.For(rival.Attack).CanStart, binding.Slot + " recovery blocks " + rival.Slot);
                Check(!attacks.TryReload() && !motor.TryDash(Vector3.forward), binding.Slot + " recovery blocks direct actions");
                yield return new WaitForSeconds(0.08f);
                Check(executions == baseline + 1 && !motor.IsDashing && stamina.Current == 100f &&
                    motor.VerticalSpeed > 0f && Vector3.ProjectOnPlane(input.transform.position - before, Vector3.up).magnitude > 0.2f,
                    binding.Slot + " recovery permits movement and jump while rejecting attack dash reload input");
                float ascendingSpeed = motor.VerticalSpeed;
                motor.RequestJump();
                yield return null;
                Check(motor.VerticalSpeed < ascendingSpeed, binding.Slot + " airborne jump requests cannot cause a second jump");
                otherActions = false;
                movement = Vector2.zero;
                while (owner.IsPerformingAttack) yield return null;
                Check(motor.CanPerformActions && !motor.IsGrounded && !motor.CanDash &&
                    PlayerActionValidationUtility.For(attack).CooldownRemaining > 0f && !PlayerActionValidationUtility.For(attack).CanStart && attacks.CanReload,
                    binding.Slot + " cooldown alone permits combat actions and reload while airborne dash remains blocked");
                yield return new WaitForSeconds(0.06f);
                Check(executions == baseline + 1 && !attacks.IsReloading, binding.Slot + " rejected taps are not buffered");
            }

            // All five buttons on an idle frame reserve only one attack, even with zero delays.
            foreach (PlayerActionBinding binding in bindings)
            {
                SetField(binding.Attack.Data, "windupTime", 0f);
                SetField(binding.Attack.Data, "recoveryTime", 0f);
                SetField(binding.Attack.Data, "cooldown", 0f);
            }
            yield return new WaitForSeconds(1f);
            bool reentrantBlocked = true;
            foreach (PlayerActionBinding binding in bindings)
                binding.Attack.OnExecuted += () =>
                {
                    reentrantBlocked &= owner.IsPerformingAttack && !motor.CanDash && !attacks.TryReload();
                    foreach (PlayerActionBinding rival in bindings)
                        reentrantBlocked &= !PlayerActionValidationUtility.For(rival.Attack).TryStart(rival.Origin, attacks.GetAimPoint());
                };
            int simultaneous = executions;
            pressMask = 31;
            yield return new WaitForSeconds(0.06f);
            Check(executions == simultaneous + 1 && reentrantBlocked, "zero-delay simultaneous and reentrant requests execute only one attack");
            heldMask = 3;
            yield return new WaitForSeconds(0.15f);
            heldMask = 0;
            Check(executions > simultaneous + 2 && reentrantBlocked, "held input retries after the shared lock ends");
            yield return new WaitForSeconds(0.06f);

            Attack primary = bindings[0].Attack;
            Attack rivalAttack = bindings[1].Attack;
            SetField(primary.Data, "windupTime", 0.2f);
            SetField(primary.Data, "recoveryTime", 0.3f);
            input.GetComponent<PlayerStat>().AttackSpeed = 2f;
            PlayerActionValidationUtility.For(primary).SpeedMultiplier = 2f;
            Check(PlayerActionValidationUtility.For(primary).TryStart(bindings[0].Origin, attacks.GetAimPoint()), "speed-scaled windup accepts request");
            int pauseBaseline = executions;
            Time.timeScale = 0f;
            for (int i = 0; i < 5; i++) yield return null;
            Check(PlayerActionValidationUtility.For(primary).Phase == PlayerActionPhase.Windup && owner.IsPerformingAttack && executions == pauseBaseline,
                "pause freezes windup and preserves action lock");
            Check(!motor.CanJump, "paused windup rejects jump requests");
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.12f);
            Check(PlayerActionValidationUtility.For(primary).Phase == PlayerActionPhase.Recovery && executions == pauseBaseline + 1, "attack speed scales windup and execution");
            float remaining = PlayerActionValidationUtility.For(primary).RecoveryRemaining;
            Time.timeScale = 0f;
            for (int i = 0; i < 5; i++) yield return null;
            Check(Mathf.Abs(PlayerActionValidationUtility.For(primary).RecoveryRemaining - remaining) < 0.0001f && !motor.CanDash && !motor.CanJump,
                "pause freezes recovery and preserves action lock");
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.17f);
            Check(!owner.IsPerformingAttack && motor.CanPerformActions, "speed-scaled recovery unlocks without an extra timer");

            Check(PlayerActionValidationUtility.For(primary).TryStart(bindings[0].Origin, attacks.GetAimPoint()) && PlayerActionValidationUtility.For(primary).Cancel() &&
                !owner.IsPerformingAttack && PlayerActionValidationUtility.For(rivalAttack).CanStart, "explicit cancellation releases the shared windup lock");
            var lostOrigin = new GameObject("Lost Action Delay Origin");
            Check(PlayerActionValidationUtility.For(primary).TryStart(lostOrigin.transform, attacks.GetAimPoint()), "failure probe accepts temporary origin");
            int failedBaseline = executions;
            Destroy(lostOrigin);
            yield return new WaitForSeconds(0.15f);
            Check(executions == failedBaseline && !owner.IsPerformingAttack && motor.CanDash,
                "failed execution releases the shared lock without recovery");
            Check(PlayerActionValidationUtility.For(primary).TryStart(bindings[0].Origin, attacks.GetAimPoint()), "disable probe starts windup");
            PlayerActionValidationUtility.For(primary).enabled = false;
            Check(!owner.IsPerformingAttack && PlayerActionValidationUtility.For(rivalAttack).CanStart, "disabled attack cancels windup and cannot leave a stale lock");
            PlayerActionValidationUtility.For(primary).enabled = true;
            Check(PlayerActionValidationUtility.For(primary).TryStart(bindings[0].Origin, attacks.GetAimPoint()), "death probe starts windup");
            input.GetComponent<Mortal>().Dead();
            yield return new WaitForSeconds(0.15f);
            Check(executions == failedBaseline && !owner.IsPerformingAttack && !PlayerActionValidationUtility.For(rivalAttack).CanStart,
                "death cancels pending execution and prevents new attacks");
        }

        private void Update()
        {
            if (input == null) return;
            if (EditorApplication.timeSinceStartup - started > 60d)
            {
                Debug.LogError("Action delay validation timed out.");
                PlayerActionDelayValidation.Finish(1);
            }
            SetInput("IsGameplayInputEnabled", input.isActiveAndEnabled && Time.timeScale > 0f);
            SetInput("Move", movement);
            SetInput("Look", Vector2.zero);
            SetInput("PrimaryFire", Snapshot((pressMask & 1) != 0, (heldMask & 1) != 0));
            SetInput("SecondaryFire", Snapshot((pressMask & 2) != 0, (heldMask & 2) != 0));
            SetInput("Skill1", Snapshot((pressMask & 4) != 0, false));
            SetInput("Skill2", Snapshot((pressMask & 8) != 0, false));
            SetInput("Ultimate", Snapshot((pressMask & 16) != 0, false));
            SetInput("Jump", Snapshot(otherActions, otherActions));
            SetInput("Dash", Snapshot(otherActions, otherActions));
            SetInput("Reload", Snapshot(otherActions, false));
            pressMask = 0;
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }

        private void ResetPosition()
        {
            CharacterController body = input.GetComponent<CharacterController>();
            body.enabled = false;
            input.transform.position = new Vector3(0f, 1.05f, 0f);
            SetField(motor, "verticalSpeed", 0f);
            body.enabled = true;
            Physics.SyncTransforms();
        }

        private void SetInput(string name, object value) => typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);
        private static ButtonInputState Snapshot(bool pressed, bool held) =>
            (ButtonInputState)Activator.CreateInstance(typeof(ButtonInputState), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { pressed, held, false }, null);

        private static void SetField(object target, string name, object value)
        { PlayerActionValidationUtility.Set(target, name, value); }
    }
}
