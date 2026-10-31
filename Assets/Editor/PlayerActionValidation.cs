using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class PlayerActionValidation
    {
        private const string Running = "ProjectU.PlayerActionValidation";
        static PlayerActionValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player Action Validation").AddComponent<PlayerActionValidationDriver>();
            };
        }
        [MenuItem("ProjectU/Validation/Validate Player Actions")]
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

    // An attack-free, nonblocking held action exercises the extension contract.
    public sealed class PlayerActionHoldProbe : PlayerAction
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Skill2;
        public override bool RequiresHeldInput => true;
        protected override bool UsesDelayLock => false;
        protected override bool BlocksCore(PlayerActionSlot slot) => false;
        public int Starts { get; private set; }
        public int Cleanups { get; private set; }
        protected override bool CheckRequirements(in PlayerActionContext request) => true;
        protected override PlayerActionResult ExecuteCore(in PlayerActionContext request)
        {
            Starts++;
            return PlayerActionResult.Running;
        }
        protected override void Cleanup(PlayerActionCancelReason reason) => Cleanups++;
    }

    // Selective blocking must not turn every action into an exclusive current action.
    public sealed class PlayerActionSelectiveProbe : PlayerAction
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Ultimate;
        protected override bool BlocksCore(PlayerActionSlot slot) => slot == PlayerActionSlot.Skill1;
        protected override bool CheckRequirements(in PlayerActionContext request) => true;
        protected override PlayerActionResult ExecuteCore(in PlayerActionContext request) => PlayerActionResult.Running;
    }

    [DefaultExecutionOrder(-150)]
    public sealed class PlayerActionValidationDriver : MonoBehaviour
    {
        private PlayerInputReader input;
        private PlayerController controller;
        private PlayerCombat combat;
        private PlayerAttackAction primary;
        private PlayerActionHoldProbe hold;
        private bool primaryPress, holdPress, holdHeld, holdRelease;
        private bool gameplay = true;
        private int shots;
        private double startedAt;
        private readonly List<string> checks = new List<string>();

        private IEnumerator Start()
        {
            IEnumerator validation = Validate();
            while (true)
            {
                bool more;
                object current = null;
                try { more = validation.MoveNext(); if (more) current = validation.Current; }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    PlayerActionValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("PLAYER ACTION VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            PlayerActionValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            startedAt = EditorApplication.timeSinceStartup;
            Time.captureDeltaTime = 1f / 60f;
            input = UnityEngine.Object.FindFirstObjectByType<PlayerInputReader>();
            controller = input.GetComponent<PlayerController>();
            combat = input.GetComponent<PlayerCombat>();
            primary = (PlayerAttackAction)combat.GetAction(PlayerActionSlot.PrimaryFire);
            controller.enabled = false;
            Check(combat.ActionBindings.Count == 7, "saved scene binds seven action slots");
            string[] names = { "SoilderPrimaryFireAction", "SoilderSecondaryFireAction", "SoilderSkill1Action",
                "SoilderSkill2Action", "SoilderUltimateAction", "SoilderDashAction", "SoilderReloadAction" };
            for (int index = 0; index < names.Length; index++)
                Check(combat.GetAction((PlayerActionSlot)index).GetType().Name == names[index], names[index] + " is wired");
            foreach (string field in new[] { "windupTime", "recoveryTime", "cooldown", "repeatWhileHeld" })
            {
                Check(typeof(AttackData).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) == null,
                    field + " removed from attack data");
                Check(typeof(PlayerAction).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) != null,
                    field + " belongs to PlayerAction");
            }
            Check(typeof(Attack).GetMethod("TryAttack") == null && typeof(Attack).GetMethod("LateUpdate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
                "attack has no public scheduling API or independent timer loop");
            Check(combat.GetAction(PlayerActionSlot.Skill2) is SoilderSkill2Action &&
                combat.GetAction(PlayerActionSlot.Ultimate) is SoilderUltimateAction &&
                combat.GetBinding(PlayerActionSlot.Skill2).Attack == null && combat.GetBinding(PlayerActionSlot.Ultimate).Attack == null,
                "demo skills use actions without attack executors");
            Check(primary.RecoveryTime == 0.25f && primary.RepeatWhileHeld &&
                combat.GetAction(PlayerActionSlot.Skill1).RecoveryTime == 1f && combat.GetAction(PlayerActionSlot.Skill1).Cooldown == 6f,
                "saved action timing migrated exactly");
            combat.AttackExecuted += slot => { if (slot == PlayerActionSlot.PrimaryFire) shots++; };
            PlayerActionValidationUtility.Set(primary, "windupTime", 0f);
            PlayerActionValidationUtility.Set(primary, "recoveryTime", 0.2f);
            hold = input.gameObject.AddComponent<PlayerActionHoldProbe>();
            Check(hold.TryStart(), "attack-free held action accepts direct request");
            yield return null;
            Check(hold.Phase == PlayerActionPhase.Active && combat.CanStart(PlayerActionSlot.PrimaryFire),
                "held action remains active and permits firing");
            Check(combat.TryStartAction(PlayerActionSlot.PrimaryFire), "fire starts alongside held effect");
            yield return null;
            Check(shots == 1 && hold.Phase == PlayerActionPhase.Active, "shot and held effect execute independently");
            Check(!combat.CanStart(PlayerActionSlot.PrimaryFire), "shot recovery still blocks a second shot");
            hold.Release();
            Check(hold.Phase == PlayerActionPhase.Ready && hold.Cleanups == 1,
                "release cleans held effect while attack recovery is locked");
            yield return new WaitForSeconds(0.25f);

            combat.enabled = false;
            var bindings = combat.ActionBindings.ToArray();
            var holdBinding = new PlayerActionBinding();
            PlayerActionValidationUtility.Set(holdBinding, "slot", PlayerActionSlot.Skill2);
            PlayerActionValidationUtility.Set(holdBinding, "action", hold);
            bindings[(int)PlayerActionSlot.Skill2] = holdBinding;
            PlayerActionValidationUtility.Set(combat, "actionBindings", bindings);
            combat.enabled = true;
            controller.enabled = true;
            holdHeld = holdPress = true;
            yield return null;
            yield return null;
            Check(hold.Phase == PlayerActionPhase.Active, "controller starts an action with no Attack reference");
            primaryPress = true;
            yield return null;
            yield return null;
            Check(shots == 2 && hold.Phase == PlayerActionPhase.Active, "controller permits held effect and fire together");
            holdHeld = false;
            holdRelease = true;
            yield return null;
            yield return null;
            Check(hold.Phase == PlayerActionPhase.Ready && hold.Cleanups == 2,
                "controller delivers release during another action's recovery");
            yield return new WaitForSeconds(0.25f);

            var selective = input.gameObject.AddComponent<PlayerActionSelectiveProbe>();
            Check(selective.TryStart(), "selective effect starts without an attack");
            yield return null;
            Check(!combat.CanStart(PlayerActionSlot.Skill1) && combat.CanStart(PlayerActionSlot.PrimaryFire),
                "selective effect blocks only its configured slot");
            primaryPress = true;
            yield return null;
            yield return null;
            Check(shots == 3, "controller dispatches a permitted slot while another slot is delayed");
            selective.Cancel();
            yield return new WaitForSeconds(0.25f);

            int startsBefore = hold.Starts;
            primaryPress = true;
            yield return null;
            yield return null;
            holdHeld = holdPress = true;
            yield return null;
            yield return null;
            Check(controller.BufferedAction == PlayerActionSlot.Skill2, "held action can wait for an attack delay");
            holdHeld = false;
            holdRelease = true;
            yield return null;
            yield return new WaitForSeconds(0.25f);
            Check(controller.BufferedAction == PlayerActionSlot.None && hold.Starts == startsBefore,
                "released buffered hold is discarded before activation");
            holdHeld = holdPress = true;
            yield return null;
            yield return null;
            Check(hold.Phase == PlayerActionPhase.Active, "new held press starts after discarded reservation");
            gameplay = false;
            yield return null;
            yield return null;
            Check(hold.Phase == PlayerActionPhase.Ready && controller.BufferedAction == PlayerActionSlot.None,
                "input interruption cleans held state without requiring Released");
            holdHeld = false;
            gameplay = true;
            yield return null;

            PlayerStamina stamina = input.GetComponent<PlayerStamina>();
            PlayerMotor motor = input.GetComponent<PlayerMotor>();
            stamina.RecoveryRate = 0f;
            stamina.Recover(100f);
            float prior = stamina.Current, delay = stamina.RecoveryDelayRemaining;
            int dashes = 0;
            motor.OnDashExecuted += () => dashes++;
            Check(!motor.TryDash(Vector3.zero) && stamina.Current == prior && stamina.RecoveryDelayRemaining == delay && dashes == 0,
                "invalid direct dash changes neither movement resource recovery nor events");
            bool committedState = false;
            stamina.OnChanged += (current, max) =>
            {
                if (current == prior - 25f)
                    committedState = motor.IsDashing && combat.GetAction(PlayerActionSlot.Dash).Phase == PlayerActionPhase.Active;
            };
            Check(motor.TryDash(Vector3.forward), "public motor dash passes through the action");
            Check(committedState && dashes == 1 && stamina.Current == prior - 25f,
                "resource notification observes committed dash state and exactly one event");
            motor.enabled = false;
            Check(!motor.IsDashing && !motor.IsDashActionLocked,
                "motor teardown clears an active dash without stale action state");
            motor.enabled = true;
            yield return null;
            yield return null;
            Check(motor.TryDash(Vector3.forward), "dash can start again after motor teardown");
            while (motor.IsDashing) yield return null;
            Check(motor.IsDashPostDelay, "dash has entered action-owned recovery");
            motor.enabled = false;
            Check(!motor.IsDashPostDelay && !motor.IsDashActionLocked,
                "motor teardown also clears a recovery-only action lock");
            motor.enabled = true;
            yield return new WaitForSeconds(0.6f);
            Check(!motor.IsDashActionLocked && combat.CanStart(PlayerActionSlot.PrimaryFire),
                "action owns dash recovery and releases the lock");
        }

        private void Update()
        {
            if (input == null) return;
            if (EditorApplication.timeSinceStartup - startedAt > 60d)
            {
                Debug.LogError("Player action validation timed out.");
                PlayerActionValidation.Finish(1);
            }
            SetInput("IsGameplayInputEnabled", gameplay && input.isActiveAndEnabled && Time.timeScale > 0f);
            SetInput("Move", Vector2.zero);
            SetInput("Look", Vector2.zero);
            foreach (string name in new[] { "SecondaryFire", "Skill1", "Ultimate", "Reload", "Dash", "Jump" })
                SetInput(name, default(ButtonInputState));
            SetInput("PrimaryFire", Snapshot(primaryPress, false, false));
            SetInput("Skill2", Snapshot(holdPress, holdHeld, holdRelease));
            primaryPress = holdPress = holdRelease = false;
        }

        private void SetInput(string name, object value) => typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);
        private static ButtonInputState Snapshot(bool pressed, bool held, bool released)
            => (ButtonInputState)Activator.CreateInstance(typeof(ButtonInputState), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { pressed, held, released }, null);
        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }
    }
}
