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
            GameFlowValidationUtility.DisableAutomaticGameFlow();
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
    public sealed class PlayerActionHoldProbe : PlayerActionBase
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Skill2;
        public PlayerActionStage ExecutionStage { get; set; } = PlayerActionStage.AfterAim;
        public override PlayerActionStage Stage => ExecutionStage;
        public override bool RequiresHeldInput => true;
        protected override bool UsesDelayLock => false;
        protected override bool BlocksCore(PlayerActionSlot slot) => false;
        public int Starts { get; private set; }
        public int Cleanups { get; private set; }
        protected override bool CheckRequirements(in ActionContext request) => true;
        protected override ActionResult ExecuteCore(in ActionContext request)
        {
            Starts++;
            return ActionResult.Running;
        }
        protected override void Cleanup(ActionCancelReason reason) => Cleanups++;
    }

    // A non-ADS stance on an ordinary skill slot exercises generic input routing.
    public sealed class PlayerActionStanceProbe : PlayerActionBase
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Skill1;
        public override PlayerActionStage Stage => PlayerActionStage.BeforeMotor;
        public override bool RequiresHeldInput => true;
        public PlayerActionSlot BlockedSlot { get; set; } = PlayerActionSlot.None;
        public int Starts { get; private set; }
        public int Cleanups { get; private set; }
        protected override bool UsesDelayLock => false;
        protected override bool BlocksCore(PlayerActionSlot slot) => slot == BlockedSlot;
        protected override bool CheckRequirements(in ActionContext request) => true;
        protected override ActionResult ExecuteCore(in ActionContext request) { Starts++; return ActionResult.Running; }
        protected override void Cleanup(ActionCancelReason reason) => Cleanups++;
    }

    // Selective blocking must not turn every action into an exclusive current action.
    public sealed class PlayerActionSelectiveProbe : PlayerActionBase
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Ultimate;
        protected override bool BlocksCore(PlayerActionSlot slot) => slot == PlayerActionSlot.Skill1;
        protected override bool CheckRequirements(in ActionContext request) => true;
        protected override ActionResult ExecuteCore(in ActionContext request) => ActionResult.Running;
    }

    [DefaultExecutionOrder(-150)]
    public sealed class PlayerActionValidationDriver : MonoBehaviour
    {
        private const int SecondaryInput = 1, Skill1Input = 2, ReloadInput = 4, UltimateInput = 8;
        private PlayerInputReader input;
        private PlayerController controller;
        private PlayerCombat combat;
        private PlayerAttackActionBase primary;
        private PlayerActionHoldProbe hold;
        private bool primaryPress, holdPress, holdHeld, holdRelease;
        private int extraPressed, extraHeld;
        private bool gameplay = true;
        private int shots;
        private double startedAt;
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
            primary = (PlayerAttackActionBase)combat.GetAction(PlayerActionSlot.PrimaryFire);
            controller.enabled = false;
            Check(combat.ActionBindings.Count == 7, "saved scene binds seven action slots");
            string[] names = { "SoilderPrimaryFireAction", "SoilderSecondaryFireAction", "SoilderSkill1Action",
                "SoilderSkill2Action", "SoilderUltimateAction", "SoilderDashAction", "SoilderReloadAction" };
            for (int index = 0; index < names.Length; index++)
                Check(combat.GetAction((PlayerActionSlot)index).GetType().Name == names[index], names[index] + " is wired");
            foreach (string field in new[] { "windupTime", "recoveryTime", "cooldown", "repeatWhileHeld" })
            {
                Check(typeof(AttackDataBase).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) == null,
                    field + " removed from attack data");
                Type fieldOwner = field == "repeatWhileHeld" ? typeof(PlayerActionBase) : typeof(ActionBase);
                Check(fieldOwner.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) != null,
                    field + " belongs to " + fieldOwner.Name);
            }
            Check(typeof(AttackBase).GetMethod("TryAttack") == null && typeof(AttackBase).GetMethod("LateUpdate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
                "attack has no public scheduling API or independent timer loop");
            Check(combat.GetAction(PlayerActionSlot.Skill2) is SoilderSkill2Action &&
                combat.GetAction(PlayerActionSlot.Ultimate) is SoilderUltimateAction &&
                combat.GetBinding(PlayerActionSlot.Skill2).Attack == null && combat.GetBinding(PlayerActionSlot.Ultimate).Attack == null,
                "demo skills use actions without attack executors");
            Check(primary.RecoveryTime == 0.25f && primary.RepeatWhileHeld &&
                combat.GetAction(PlayerActionSlot.Skill1).RecoveryTime == 1f && combat.GetAction(PlayerActionSlot.Skill1).Cooldown == 6f,
                "saved action timing migrated exactly");
            Check(combat.ActionBindings.All(binding => binding.Action.RequiresHeldInput ==
                (binding.Slot == PlayerActionSlot.SecondaryFire)),
                "saved ADS requires Held while other actions retain ordinary input");
            combat.AttackExecuted += slot => { if (slot == PlayerActionSlot.PrimaryFire) shots++; };
            PlayerActionValidationUtility.Set(primary, "windupTime", 0f);
            PlayerActionValidationUtility.Set(primary, "recoveryTime", 0.2f);
            hold = input.gameObject.AddComponent<PlayerActionHoldProbe>();
            Check(hold.TryStart(), "attack-free held action accepts direct request");
            yield return null;
            Check(hold.Phase == ActionPhase.Active && combat.CanStart(PlayerActionSlot.PrimaryFire),
                "held action remains active and permits firing");
            Check(combat.TryStartAction(PlayerActionSlot.PrimaryFire), "fire starts alongside held effect");
            yield return null;
            Check(shots == 1 && hold.Phase == ActionPhase.Active, "shot and held effect execute independently");
            Check(!combat.CanStart(PlayerActionSlot.PrimaryFire), "shot recovery still blocks a second shot");
            hold.Release();
            Check(hold.Phase == ActionPhase.Ready && hold.Cleanups == 1,
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
            Check(hold.Phase == ActionPhase.Active, "controller starts an action with no Attack reference");
            primaryPress = true;
            yield return null;
            yield return null;
            Check(shots == 2 && hold.Phase == ActionPhase.Active, "controller permits held effect and fire together");
            holdHeld = false;
            holdRelease = true;
            yield return null;
            yield return null;
            Check(hold.Phase == ActionPhase.Ready && hold.Cleanups == 2,
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
            Check(controller.BufferedAction == PlayerActionSlot.None && hold.Starts == startsBefore,
                "held action waits for an attack delay without reserving input");
            holdHeld = false;
            holdRelease = true;
            yield return null;
            yield return new WaitForSeconds(0.25f);
            Check(controller.BufferedAction == PlayerActionSlot.None && hold.Starts == startsBefore,
                "release before the delay ends prevents later activation");
            holdHeld = true;
            yield return null;
            yield return null;
            Check(hold.Phase == ActionPhase.Active, "Held alone starts a hold on the default execution stage");
            gameplay = false;
            yield return null;
            yield return null;
            Check(hold.Phase == ActionPhase.Ready && controller.BufferedAction == PlayerActionSlot.None,
                "input interruption cleans held state without requiring Released");
            holdHeld = false;
            gameplay = true;
            yield return null;

            IEnumerator heldChecks = ValidateHeldInput();
            while (heldChecks.MoveNext()) yield return heldChecks.Current;

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
                    committedState = motor.IsDashing && combat.GetAction(PlayerActionSlot.Dash).Phase == ActionPhase.Active;
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

        private IEnumerator ValidateHeldInput()
        {
            yield return new WaitForSeconds(0.25f);
            PlayerActionBinding[] savedBindings = combat.ActionBindings.ToArray();
            combat.enabled = false;
            var stance = input.gameObject.AddComponent<PlayerActionStanceProbe>();
            var binding = new PlayerActionBinding();
            PlayerActionValidationUtility.Set(binding, "slot", PlayerActionSlot.Skill1);
            PlayerActionValidationUtility.Set(binding, "action", stance);
            var bindings = savedBindings.ToArray();
            bindings[(int)PlayerActionSlot.Skill1] = binding;
            PlayerActionValidationUtility.Set(combat, "actionBindings", bindings);
            combat.enabled = true;
            Check(stance.RequiresHeldInput && !input.GetComponent<PlayerADS>().IsAiming && !stance.RepeatWhileHeld,
                "RequiresHeldInput supplies held lifetime without ADS or repeat-fire traits");

            int shotsBefore = shots;
            extraHeld = Skill1Input;
            primaryPress = true;
            yield return null;
            yield return null;
            Check(stance.Phase == ActionPhase.Active && stance.Starts == 1 && shots == shotsBefore + 1 &&
                controller.BufferedAction == PlayerActionSlot.None && !input.GetComponent<PlayerADS>().IsAiming,
                "held-only Skill1 starts alongside a same-frame primary shot");
            PlayerActionValidationUtility.Set(stance, "repeatWhileHeld", true);
            yield return new WaitForSeconds(0.25f);
            Check(stance.Starts == 1 && controller.BufferedAction == PlayerActionSlot.None,
                "RepeatWhileHeld does not duplicate or reserve an active holding action");
            primaryPress = true;
            yield return null;
            yield return null;
            extraHeld = 0;
            yield return null;
            yield return null;
            Check(stance.Phase == ActionPhase.Ready && stance.Cleanups == 1 && primary.Phase == ActionPhase.Recovery,
                "loss of Held releases a holding action during primary recovery without Released");
            extraPressed = Skill1Input;
            yield return null;
            yield return null;
            Check(stance.Starts == 1 && controller.BufferedAction == PlayerActionSlot.None,
                "press without Held cannot start or reserve a holding action");
            yield return new WaitForSeconds(0.25f);

            PlayerActionValidationUtility.Set(controller, "maxInputBufferTime", 0.05f);
            primaryPress = true;
            yield return null;
            yield return null;
            extraHeld = Skill1Input;
            yield return null;
            yield return null;
            Check(stance.Phase == ActionPhase.Ready && controller.BufferedAction == PlayerActionSlot.None,
                "holding input respects an attack delay without occupying the reservation");
            yield return new WaitForSeconds(0.25f);
            Check(stance.Phase == ActionPhase.Active && stance.Starts == 2,
                "continued skill hold enters after a delay longer than the input buffer lifetime");
            PlayerActionValidationUtility.Set(controller, "maxInputBufferTime", 1f);
            gameplay = false;
            yield return null;
            yield return null;
            Check(stance.Phase == ActionPhase.Ready && stance.Cleanups == 2,
                "input interruption cancels a non-ADS holding action");
            extraHeld = 0;
            gameplay = true;
            yield return null;

            stance.BlockedSlot = PlayerActionSlot.Ultimate;
            primaryPress = true;
            yield return null;
            yield return null;
            extraPressed = UltimateInput;
            yield return null;
            yield return null;
            Check(controller.BufferedAction == PlayerActionSlot.Ultimate, "ordinary action retains delay buffering");
            extraHeld = Skill1Input;
            yield return new WaitForSeconds(0.25f);
            Check(stance.IsActive && !input.GetComponent<PlayerADS>().IsAiming && controller.BufferedAction == PlayerActionSlot.None &&
                !combat.GetAction(PlayerActionSlot.Ultimate).IsActive,
                "non-ADS persistent restriction discards its blocked buffered slot");
            primaryPress = true;
            yield return null;
            yield return null;
            extraPressed = ReloadInput;
            yield return null;
            yield return null;
            Check(controller.BufferedAction == PlayerActionSlot.Reload && stance.IsActive,
                "ordinary input preserves a permitted reservation and does not cancel the active hold");
            yield return new WaitForSeconds(0.25f);
            Check(combat.IsReloading && stance.IsActive,
                "permitted reload executes alongside the holding stance");
            combat.CancelReload();
            extraHeld = 0;
            yield return null;
            yield return null;

            stance.BlockedSlot = PlayerActionSlot.None;
            hold.ExecutionStage = PlayerActionStage.BeforeMotor;
            int stanceStartsBefore = stance.Starts;
            int holdStartsBefore = hold.Starts;
            shotsBefore = shots;
            extraHeld = Skill1Input;
            holdHeld = true;
            primaryPress = true;
            yield return null;
            yield return null;
            Check(stance.IsActive && hold.IsActive && stance.Starts == stanceStartsBefore + 1 &&
                hold.Starts == holdStartsBefore + 1 && shots == shotsBefore + 1,
                "multiple holding skill slots can enter alongside an ordinary action when restrictions allow");
            holdHeld = false;
            yield return null;
            yield return null;
            Check(!hold.IsActive && stance.IsActive, "holding bindings have separate release lifetimes");
            extraHeld = 0;
            yield return new WaitForSeconds(0.25f);

            PlayerActionValidationUtility.UseSecondaryAttackFixture(combat);
            PlayerActionBase secondary = combat.GetAction(PlayerActionSlot.SecondaryFire);
            PlayerActionValidationUtility.Set(secondary, "repeatWhileHeld", false);
            int secondaryExecutions = 0;
            Action secondaryListener = () => secondaryExecutions++;
            secondary.OnExecuted += secondaryListener;
            extraHeld = SecondaryInput;
            yield return new WaitForSeconds(0.08f);
            Check(!secondary.RequiresHeldInput && secondaryExecutions == 0,
                "ordinary SecondaryFire is not automatically treated as a holding action");
            extraPressed = SecondaryInput;
            yield return new WaitForSeconds(0.08f);
            Check(secondaryExecutions == 1, "ordinary SecondaryFire still executes through normal pressed selection");
            secondary.OnExecuted -= secondaryListener;
            extraHeld = extraPressed = 0;
            hold.ExecutionStage = PlayerActionStage.AfterAim;
            combat.enabled = false;
            secondary.enabled = false;
            stance.enabled = false;
            PlayerActionValidationUtility.Set(combat, "actionBindings", savedBindings);
            combat.enabled = true;
            yield return new WaitForSeconds(0.25f);
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
            SetInput("SecondaryFire", Snapshot((extraPressed & SecondaryInput) != 0, (extraHeld & SecondaryInput) != 0, false));
            SetInput("Skill1", Snapshot((extraPressed & Skill1Input) != 0, (extraHeld & Skill1Input) != 0, false));
            SetInput("Reload", Snapshot((extraPressed & ReloadInput) != 0, false, false));
            SetInput("Ultimate", Snapshot((extraPressed & UltimateInput) != 0, false, false));
            primaryPress = holdPress = holdRelease = false;
            extraPressed = 0;
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
