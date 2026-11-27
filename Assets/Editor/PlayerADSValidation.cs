using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class PlayerADSValidation
    {
        private const string Running = "ProjectU.PlayerADSValidation";
        static PlayerADSValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("ADS Validation").AddComponent<PlayerADSValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player ADS")]
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

    // Alternate-character fixtures have no Soldier slot or attack assumptions.
    public sealed class PlayerADSCompositionProbe : PlayerActionBase
    {
        public PlayerActionSlot ActionSlot { get; set; } = PlayerActionSlot.Skill1;
        public bool UseADS { get; set; } = true;
        public int Executions { get; private set; }
        public override PlayerActionSlot Slot => ActionSlot;
        public override PlayerActionStage Stage => PlayerActionStage.BeforeMotor;
        public override bool RequiresHeldInput => UseADS;
        protected override bool UsesDelayLock => false;
        protected override bool BlocksCore(PlayerActionSlot slot) => false;
        private PlayerADS ADS => GetComponent<PlayerADS>();
        protected override bool CheckRequirements(in ActionContext request) => !UseADS || (ADS != null && ADS.CanBegin(this));
        protected override void OnAccepted(in ActionContext request) { if (UseADS) ADS.TryBegin(this); }
        protected override ActionResult ExecuteCore(in ActionContext request)
        {
            Executions++;
            return UseADS ? ActionResult.Running : ActionResult.Completed;
        }
        protected override void Cleanup(ActionCancelReason reason) { if (UseADS) ADS?.End(this); }
    }

    public sealed class PlayerADSEffectProbe : MonoBehaviour, IMovementSpeedModifier, IInteractionBlocker
    {
        public float MovementSpeedMultiplier => 0.8f;
        public bool BlocksInteraction => true;
    }

    // Feed immutable input snapshots between the real Reader and Controller updates.
    [DefaultExecutionOrder(-150)]
    public sealed class PlayerADSValidationDriver : MonoBehaviour
    {
        private const int Primary = 1, Secondary = 2, Skill1 = 4, Skill2 = 8,
            Ultimate = 16, Reload = 32, Dash = 64, Jump = 128, Interact = 256;
        private PlayerInputReader input;
        private PlayerController controller;
        private PlayerCombat combat;
        private PlayerMotor motor;
        private PlayerCamera view;
        private SoilderSecondaryFireAction ads;
        private PlayerADS stance;
        private HitscanAttack rifle;
        private PlayerStat stat;
        private int pressed, held, shots, secondaryShots;
        private Vector2 movement;
        private bool gameplay = true;
        private float fov, baseSpread;
        private double started;
        private readonly List<string> checks = new List<string>();
        private readonly List<string> screenshots = new List<string>();

        private IEnumerator Start()
        {
            started = EditorApplication.timeSinceStartup;
            Time.captureDeltaTime = 1f / 60f;
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
                    SaveResults(false);
                    PlayerADSValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            SaveResults(true);
            Debug.Log("PLAYER ADS VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            PlayerADSValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            input = FindFirstObjectByType<PlayerInputReader>();
            controller = input.GetComponent<PlayerController>();
            combat = input.GetComponent<PlayerCombat>();
            motor = input.GetComponent<PlayerMotor>();
            stat = input.GetComponent<PlayerStat>();
            view = FindFirstObjectByType<PlayerCamera>();
            ads = combat.GetAction(PlayerActionSlot.SecondaryFire) as SoilderSecondaryFireAction;
            stance = input.GetComponent<PlayerADS>();
            rifle = combat.GetBinding(PlayerActionSlot.PrimaryFire).Attack as HitscanAttack;
            PlayerActionBase primary = combat.GetAction(PlayerActionSlot.PrimaryFire);
            fov = view.GetComponent<Camera>().fieldOfView;
            baseSpread = ((HitscanAttackData)rifle.Data).SpreadAngle;
            Check(stance != null && typeof(PlayerCombat).GetProperty("AimAction") == null &&
                typeof(PlayerCombat).GetProperty("IsAiming") == null,
                "saved optional ADS feature is separate from the common combat API");
            combat.AttackExecuted += slot => { if (slot == PlayerActionSlot.PrimaryFire) shots++; if (slot == PlayerActionSlot.SecondaryFire) secondaryShots++; };
            Check(ads != null && combat.GetBinding(PlayerActionSlot.SecondaryFire).Attack == null && !ads.IsAttackAction,
                "saved secondary action is ADS without TempSecondaryFire execution");
            Check(ads.RequiresHeldInput &&
                ads.Stage == PlayerActionStage.BeforeMotor && ads.CanMove && !ads.RequiresGrounded,
                "saved stance is held, enters before movement and permits airborne movement");
            Check(ads.WindupTime == 0f && ads.RecoveryTime == 0f && ads.Cooldown == 0f,
                "saved stance has no attack timing delay");
            Check(stance.TransitionTime == 0.25f && stance.MoveSpeedMultiplier == 0.5f && stance.SpreadAngleMultiplier == 0.5f,
                "Inspector defaults save 0.25 seconds and half speed/spread");
            yield return new WaitForSeconds(0.2f);
            Check(!stance.IsAiming && view.AimBlend == 0f && Near(motor.MoveSpeed, stat.MoveSpeed), "initial hip stance");
            Capture("Hip");

            movement = Vector2.right;
            yield return new WaitForSeconds(0.15f);
            Check(Near(motor.HorizontalVelocity.magnitude, stat.MoveSpeed, 0.1f), "actual normal movement uses base speed");
            movement = Vector2.zero;
            int shotsBefore = shots, ammoBefore = combat.CurrentAmmo;
            held = Secondary | Primary;
            pressed = Primary;
            yield return new WaitForSeconds(0.1f);
            Check(stance.IsAiming && view.AimBlend > 0f && view.AimBlend < 1f, "held-only input enters a partial smooth transition");
            Check(view.IsAimTransitioning && shots == shotsBefore && combat.CurrentAmmo == ammoBefore &&
                controller.BufferedAction == PlayerActionSlot.None,
                "same-frame ADS and primary input cannot fire or reserve a shot while entering");
            Check(!combat.CanStart(PlayerActionSlot.PrimaryFire) && !combat.CanBuffer(PlayerActionSlot.PrimaryFire) &&
                !combat.TryStartAction(PlayerActionSlot.PrimaryFire) && !primary.TryStart(),
                "entry blocks eligibility, buffering and direct primary calls");
            held = Secondary;
            Check(input.transform.InverseTransformPoint(input.GetComponent<PlayerAim>().Muzzle.parent.position).z > 0f,
                "rifle stays ahead of the player during camera entry");
            Capture("Entering");
            yield return new WaitForSeconds(0.19f);
            Check(view.AimBlend == 1f && stance.IsAiming, "entry reaches full ADS after 0.25 seconds");
            Check(!view.IsAimTransitioning && combat.CanStart(PlayerActionSlot.PrimaryFire) && shots == shotsBefore,
                "completed entry permits firing without replaying a released transition tap");
            Check(view.GetComponent<Camera>().fieldOfView == fov, "ADS preserves field of view with no magnification");
            Check(secondaryShots == 0 && FindFirstObjectByType<ProjectilePool>().ActiveCount == 0,
                "holding secondary emits no attack event or temporary bullet");
            Check(Near(motor.MoveSpeed, stat.MoveSpeed * 0.5f) && !input.GetComponent<Combatant>().IsMovementLocked,
                "ADS reduces speed without locking movement");
            Capture("ADS");
            Capture("ADS-4x3", 1024, 768);
            Transform weapon = input.GetComponent<PlayerAim>().Muzzle.parent;
            Check(Vector3.Angle(weapon.forward, view.transform.forward) < 0.01f &&
                Mathf.Abs(view.transform.InverseTransformPoint(weapon.position).x) < 0.001f,
                "rifle aligns centrally with the camera sight line");
            movement = Vector2.right;
            yield return new WaitForSeconds(0.15f);
            Check(Near(motor.HorizontalVelocity.magnitude, stat.MoveSpeed * 0.5f, 0.1f), "actual ADS movement uses reduced speed");
            movement = Vector2.zero;
            pressed = Jump;
            yield return new WaitForSeconds(0.06f);
            Check(!motor.IsGrounded && motor.VerticalSpeed > 0f && stance.IsAiming, "jump remains available while aiming");
            yield return new WaitForSeconds(0.8f);

            foreach (PlayerActionSlot slot in new[] { PlayerActionSlot.Skill1, PlayerActionSlot.Skill2, PlayerActionSlot.Ultimate, PlayerActionSlot.Dash, PlayerActionSlot.Reload })
            {
                Set(combat, "currentAmmo", 20);
                Check(!combat.CanStart(slot) && !combat.CanBuffer(slot) && !combat.TryStartAction(slot) && !combat.GetAction(slot).TryStart(),
                    "ADS blocks input eligibility, buffering and direct calls for " + slot);
            }
            float staminaBefore = input.GetComponent<PlayerStamina>().Current;
            pressed = Skill1 | Skill2 | Ultimate | Dash | Reload;
            yield return new WaitForSeconds(0.12f);
            Check(controller.BufferedAction == PlayerActionSlot.None && !combat.IsReloading && !motor.IsDashing &&
                input.GetComponent<PlayerStamina>().Current == staminaBefore, "forbidden presses are discarded without resources or reservations");

            var spreadData = Instantiate((HitscanAttackData)rifle.Data);
            Set(spreadData, "spreadStartShot", 1);
            Set(rifle, "data", spreadData);
            shotsBefore = shots;
            held = Secondary | Primary;
            yield return new WaitForSeconds(0.85f);
            Check(shots >= shotsBefore + 3 && stance.IsAiming, "primary held fire repeats alongside ADS");
            Check(Near(rifle.LastSpreadAngle, baseSpread * 0.5f) && rifle.SpreadAngleMultiplier == 1f,
                "actual shots use half spread and restore the temporary execution modifier");
            Vector3 shotAxis = (combat.GetAimPoint() - rifle.LastAttackPosition).normalized;
            Check(Vector3.Angle(shotAxis, rifle.LastShotDirection) <= baseSpread * 0.5f + 0.05f,
                "actual hitscan direction stays inside the reduced spread cone");
            Check(((HitscanAttackData)rifle.Data).SpreadAngle == baseSpread && stat.MoveSpeed == 6f,
                "ADS preserves base stats and shared attack configuration");
            held = Secondary;
            yield return new WaitForSeconds(0.3f);
            Set(stance, "moveSpeedMultiplier", 0.25f);
            Set(stance, "spreadAngleMultiplier", 0f);
            Check(Near(motor.MoveSpeed, stat.MoveSpeed * 0.25f), "runtime Inspector movement ratio is reflected without overwriting stats");
            pressed = Primary;
            yield return new WaitForSeconds(0.05f);
            Check(rifle.LastSpreadAngle == 0f, "zero ADS spread ratio removes the cone on actual shots");
            Set(stance, "moveSpeedMultiplier", 0.5f);
            Set(stance, "spreadAngleMultiplier", 0.5f);
            yield return new WaitForSeconds(0.3f);

            shotsBefore = shots;
            ammoBefore = combat.CurrentAmmo;
            held = Primary;
            pressed = Primary;
            yield return new WaitForSeconds(0.1f);
            Check(!stance.IsAiming && view.AimBlend > 0f && view.AimBlend < 1f && Near(motor.MoveSpeed, stat.MoveSpeed),
                "release immediately restores gameplay ratios while the camera exits smoothly");
            Check(view.IsAimTransitioning && shots == shotsBefore && combat.CurrentAmmo == ammoBefore &&
                controller.BufferedAction == PlayerActionSlot.None && !combat.CanBuffer(PlayerActionSlot.PrimaryFire) &&
                !combat.TryStartAction(PlayerActionSlot.PrimaryFire) && !primary.TryStart(),
                "exit blocks pressed, held, buffered and direct primary fire without ammo use");
            Check(input.transform.InverseTransformPoint(input.GetComponent<PlayerAim>().Muzzle.parent.position).z > 0f,
                "rifle stays ahead of the player during camera exit");
            Capture("Exiting");
            float reverseStart = view.AimBlend;
            held = Secondary | Primary;
            pressed = Primary;
            yield return new WaitForSeconds(0.04f);
            Check(stance.IsAiming && view.AimBlend > 0f && view.AimBlend < 1f && Mathf.Abs(view.AimBlend - reverseStart) < 0.3f,
                "repress reverses smoothly from the current camera position");
            Check(view.IsAimTransitioning && shots == shotsBefore && controller.BufferedAction == PlayerActionSlot.None,
                "reversing an exit keeps primary fire blocked");
            held = Secondary;
            float reversedBlend = view.AimBlend;
            yield return new WaitForSeconds(0.05f);
            Check(view.AimBlend > reversedBlend, "reversed transition moves toward ADS on subsequent frames");
            yield return new WaitForSeconds(0.22f);
            Check(view.AimBlend == 1f, "reversed entry completes in the configured duration");
            Check(!view.IsAimTransitioning && shots == shotsBefore,
                "reversal finishes without replaying transition taps");
            held = 0;
            yield return new WaitForSeconds(0.29f);
            Check(view.AimBlend == 0f && !stance.IsAiming && controller.BufferedAction == PlayerActionSlot.None,
                "release returns fully and forbidden inputs never replay");
            Capture("Returned");
            pressed = Primary;
            yield return new WaitForSeconds(0.06f);
            Check(Near(rifle.LastSpreadAngle, baseSpread), "hip shots recover the full original spread");
            held = Secondary;
            yield return new WaitForSeconds(0.04f);
            Check(primary.Phase == ActionPhase.Recovery && !stance.IsAiming && !combat.CanStart(PlayerActionSlot.SecondaryFire) &&
                !combat.TryStartAction(PlayerActionSlot.SecondaryFire) && !ads.TryStart(),
                "primary recovery blocks held ADS and direct entry requests");
            yield return new WaitForSeconds(0.25f);
            Check(stance.IsAiming, "continued hold enters ADS after primary recovery ends");
            held = 0;
            yield return new WaitForSeconds(0.35f);

            // Exercise a nonzero windup as well as recovery, with an independent cooldown.
            float savedWindup = primary.WindupTime, savedRecovery = primary.RecoveryTime, savedCooldown = primary.Cooldown;
            Set(primary, "windupTime", 0.2f);
            Set(primary, "recoveryTime", 0.2f);
            Set(primary, "cooldown", 1f);
            shotsBefore = shots;
            pressed = Primary;
            yield return new WaitForSeconds(0.04f);
            Check(primary.Phase == ActionPhase.Windup && shots == shotsBefore, "primary fixture has an unexecuted windup");
            held = Secondary;
            yield return new WaitForSeconds(0.08f);
            Check(primary.Phase == ActionPhase.Windup && !stance.IsAiming && view.AimBlend == 0f &&
                !combat.CanStart(PlayerActionSlot.SecondaryFire) && !combat.TryStartAction(PlayerActionSlot.SecondaryFire) && !ads.TryStart(),
                "primary windup blocks held ADS and direct entry requests without moving the camera");
            Check(shots == shotsBefore && controller.BufferedAction == PlayerActionSlot.None && Near(motor.MoveSpeed, stat.MoveSpeed),
                "rejected ADS does not interrupt the shot, reserve an action or apply an aiming speed ratio");
            yield return new WaitForSeconds(0.16f);
            Check(primary.Phase == ActionPhase.Recovery && shots == shotsBefore + 1 && !stance.IsAiming &&
                !combat.TryStartAction(PlayerActionSlot.SecondaryFire) && !ads.TryStart(),
                "held ADS stays blocked when windup changes to recovery");
            yield return new WaitForSeconds(0.18f);
            Check(primary.Phase == ActionPhase.Ready && primary.CooldownRemaining > 0f && stance.IsAiming,
                "continued hold enters ADS after both delays while cooldown alone remains");
            held = 0;
            yield return new WaitForSeconds(0.8f);

            Set(primary, "cooldown", savedCooldown);
            pressed = Primary;
            yield return new WaitForSeconds(0.04f);
            held = Secondary;
            yield return new WaitForSeconds(0.06f);
            Check(primary.Phase == ActionPhase.Windup && !stance.IsAiming, "short ADS hold is rejected during windup");
            held = 0;
            yield return new WaitForSeconds(0.4f);
            Check(!stance.IsAiming && view.AimBlend == 0f && controller.BufferedAction == PlayerActionSlot.None,
                "releasing during a primary delay cannot cause delayed ADS entry");
            Set(primary, "windupTime", savedWindup);
            Set(primary, "recoveryTime", savedRecovery);

            // A delayed skill holds a reservation before ADS begins; it must be discarded.
            pressed = Primary;
            yield return new WaitForSeconds(0.04f);
            pressed = Skill1;
            yield return new WaitForSeconds(0.04f);
            Check(controller.BufferedAction == PlayerActionSlot.Skill1, "fixture reserves a skill during shot recovery");
            held = Secondary;
            yield return new WaitForSeconds(0.04f);
            Check(!stance.IsAiming && controller.BufferedAction == PlayerActionSlot.Skill1,
                "ADS waits for primary recovery without bypassing its lock to clear a skill reservation");
            yield return new WaitForSeconds(0.22f);
            Check(stance.IsAiming && controller.BufferedAction == PlayerActionSlot.None, "entering ADS discards a previously buffered forbidden action");
            held = 0;
            yield return new WaitForSeconds(0.35f);
            Check(!combat.GetAction(PlayerActionSlot.Skill1).IsActive, "discarded skill stays discarded after release");

            Set(combat, "currentAmmo", 0);
            held = Secondary | Primary;
            yield return new WaitForSeconds(0.4f);
            Check(stance.IsAiming && !combat.IsReloading && combat.CurrentAmmo == 0, "empty magazine cannot auto reload while ADS is held");
            held = 0;
            yield return new WaitForSeconds(0.08f);
            Check(combat.IsReloading, "empty magazine auto reload resumes after release");
            held = Secondary;
            yield return new WaitForSeconds(0.06f);
            Check(!stance.IsAiming && combat.IsReloading, "existing reload prevents ADS entry");
            combat.CancelReload();
            Set(combat, "currentAmmo", combat.MaxAmmo);
            yield return new WaitForSeconds(0.3f);
            Check(stance.IsAiming, "continued hold enters when the existing reload ends");

            gameplay = false;
            yield return new WaitForSeconds(0.3f);
            Check(!stance.IsAiming && view.AimBlend == 0f, "cursor/input interruption releases ADS and camera");
            gameplay = true;
            yield return new WaitForSeconds(0.3f);
            Check(stance.IsAiming, "held input can resume after the gameplay gate reopens");
            ads.enabled = false;
            yield return new WaitForSeconds(0.3f);
            Check(!stance.IsAiming && Near(motor.MoveSpeed, stat.MoveSpeed) && view.AimBlend == 0f, "disabled ADS releases all ratios and camera");
            ads.enabled = true;
            yield return new WaitForSeconds(0.3f);
            combat.enabled = false;
            yield return new WaitForSeconds(0.3f);
            Check(!stance.IsAiming && view.AimBlend == 0f, "disabled combat releases ADS");
            combat.enabled = true;
            held = 0;
            yield return new WaitForSeconds(0.1f);

            held = Secondary;
            yield return new WaitForSeconds(0.3f);
            Set(primary, "windupTime", 0.2f);
            shotsBefore = shots;
            ammoBefore = combat.CurrentAmmo;
            pressed = Primary;
            yield return new WaitForSeconds(0.04f);
            Check(primary.Phase == ActionPhase.Windup && stance.IsAiming,
                "settled ADS accepts a primary windup before exit");
            held = 0;
            yield return new WaitForSeconds(0.06f);
            Check(view.IsAimTransitioning && primary.Phase == ActionPhase.Windup,
                "releasing ADS can begin a camera exit during an accepted shot windup");
            yield return new WaitForSeconds(0.24f);
            Check(primary.Phase == ActionPhase.Ready && shots == shotsBefore && combat.CurrentAmmo == ammoBefore &&
                primary.CooldownRemaining == 0f && !view.IsAimTransitioning,
                "execution recheck rejects a shot whose windup finishes during exit without ammo or cooldown use");
            Set(primary, "windupTime", savedWindup);

            Set(stance, "transitionTime", 0f);
            shotsBefore = shots;
            held = Secondary | Primary;
            pressed = Primary;
            yield return new WaitForSeconds(0.04f);
            Check(stance.IsAiming && view.AimBlend == 1f, "zero transition time enters immediately");
            Check(!view.IsAimTransitioning && shots == shotsBefore + 1,
                "zero-duration entry permits same-frame primary fire");
            held = Secondary;
            yield return new WaitForSeconds(0.3f);
            shotsBefore = shots;
            held = Primary;
            pressed = Primary;
            yield return new WaitForSeconds(0.04f);
            Check(!stance.IsAiming && view.AimBlend == 0f, "zero transition time exits immediately");
            Check(!view.IsAimTransitioning && shots == shotsBefore + 1,
                "zero-duration exit permits same-frame primary fire");
            held = 0;
            yield return new WaitForSeconds(0.3f);
            Set(stance, "transitionTime", 0.5f);
            shotsBefore = shots;
            held = Secondary | Primary;
            yield return new WaitForSeconds(0.28f);
            Check(view.AimBlend > 0f && view.AimBlend < 1f, "a longer configured transition remains in progress at 0.28 seconds");
            Check(view.IsAimTransitioning && shots == shotsBefore && controller.BufferedAction == PlayerActionSlot.None,
                "held primary waits throughout a longer entry without reserving input");
            yield return new WaitForSeconds(0.27f);
            Check(view.AimBlend == 1f, "a longer configured transition completes at 0.5 seconds");
            Check(!view.IsAimTransitioning && shots == shotsBefore + 1 && Near(rifle.LastSpreadAngle, baseSpread * 0.5f),
                "continued primary hold resumes after entry with ADS spread");
            shotsBefore = shots;
            held = Primary;
            yield return new WaitForSeconds(0.28f);
            Check(view.IsAimTransitioning && shots == shotsBefore && controller.BufferedAction == PlayerActionSlot.None,
                "continued primary hold is blocked while leaving ADS");
            yield return new WaitForSeconds(0.27f);
            Check(!view.IsAimTransitioning && view.AimBlend == 0f && shots == shotsBefore + 1 && Near(rifle.LastSpreadAngle, baseSpread),
                "continued primary hold resumes after exit with normal spread");
            held = 0;
            yield return new WaitForSeconds(0.3f);
            Set(stance, "transitionTime", -1f);
            Set(stance, "moveSpeedMultiplier", float.NaN);
            Set(stance, "spreadAngleMultiplier", 8f);
            Check(stance.TransitionTime == 0f && stance.MoveSpeedMultiplier == 0f && stance.SpreadAngleMultiplier == 1f,
                "negative, nonfinite and out of range Inspector values are safely clamped");
            Set(stance, "transitionTime", 0.25f);
            Set(stance, "moveSpeedMultiplier", 0.5f);
            Set(stance, "spreadAngleMultiplier", 0.5f);

            // Use the real manager's subscriptions, phase changes and reward gate.
            IEnumerator composition = ValidateComposition();
            while (composition.MoveNext()) yield return composition.Current;

            GameManager game = FindFirstObjectByType<GameManager>();
            StageFlowUI flow = FindFirstObjectByType<StageFlowUI>();
            ButtonTrigger startButton = FindFirstObjectByType<GameStarter>().GetComponent<ButtonTrigger>();
            Vector3 startPosition = startButton.TriggerRange.bounds.center + Vector3.back * 2.5f;
            startPosition.y = input.transform.position.y;
            input.transform.position = startPosition;
            Physics.SyncTransforms();
            game.enabled = true;
            flow.enabled = true;
            held = Secondary | Interact;
            pressed = Interact;
            yield return new WaitForSeconds(0.06f);
            Check(stance.IsAiming && game.State == StageState.BeforeStage,
                "controller checks G after same-frame ADS entry");
            yield return new WaitForSeconds(0.24f);
            pressed = Interact;
            yield return new WaitForSeconds(startButton.HoldDuration + 0.1f);
            Check(stance.IsAiming && game.State == StageState.BeforeStage, "ADS blocks G interaction");
            held = 0;
            flow.enabled = false;
            yield return new WaitForSeconds(0.3f);
            Check(game.State == StageState.BeforeStage, "ADS-blocked G is not replayed after release");
            pressed = held = Interact;
            yield return new WaitForSeconds(startButton.HoldDuration + 0.1f);
            Check(game.State == StageState.OnStage && !flow.enabled,
                "the central hold starts a stage after ADS release without the presenter");
            flow.enabled = true;
            held = Secondary;
            yield return new WaitForSeconds(0.3f);
            Check(stance.IsAiming && game.State == StageState.OnStage, "ADS works in a live stage");
            for (int index = 0; index < 20 && game.State == StageState.OnStage; index++)
            {
                foreach (EnemyAI enemy in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) enemy.GetComponent<Mortal>().Dead();
                yield return new WaitForSeconds(0.08f);
            }
            Check(game.IsChoosingReward && !stance.IsAiming && !game.IsGameplayEnabled, "stage clear and reward gate cancel ADS");
            yield return new WaitForSeconds(0.3f);
            Check(view.AimBlend == 0f && Near(motor.MoveSpeed, stat.MoveSpeed), "reward gate restores camera and movement ratio");
            held = 0;
            Check(game.CompleteReward(game.PendingRewardId), "reward completion is accepted");
            yield return new WaitForSeconds(0.08f);
            Check(game.State == StageState.BeforeStage && game.IsGameplayEnabled, "normal phase and input return after reward");
            held = Secondary;
            yield return new WaitForSeconds(0.3f);
            Check(stance.IsAiming, "ADS can enter again after reward");
            input.GetComponent<Mortal>().Dead();
            yield return null;
            yield return null;
            Check(!stance.IsAiming && Near(motor.MoveSpeed, stat.MoveSpeed) && game.Result == GameResult.Lost,
                "death clears stance and ratios while preserving Lost handling");
            yield return new WaitForSecondsRealtime(0.35f);
            Check(view.AimBlend == 0f, "death camera exits smoothly even while the Lost state pauses gameplay time");
            Check(secondaryShots == 0 && view.GetComponent<Camera>().fieldOfView == fov, "no secondary shot or zoom occurs throughout ADS tests");
        }

        private IEnumerator ValidateComposition()
        {
            held = pressed = 0;
            yield return new WaitForSeconds(0.35f);
            var bare = new GameObject("Character without ADS");
            bare.SetActive(false);
            bare.AddComponent<Combatant>();
            var plainCombat = bare.AddComponent<PlayerCombat>();
            var secondary = bare.AddComponent<PlayerADSCompositionProbe>();
            secondary.ActionSlot = PlayerActionSlot.SecondaryFire;
            secondary.UseADS = false;
            var plainBinding = new PlayerActionBinding();
            Set(plainBinding, "slot", secondary.Slot);
            Set(plainBinding, "action", secondary);
            Set(plainCombat, "actionBindings", new[] { plainBinding });
            bare.SetActive(true);
            int executions = 0;
            plainCombat.ActionExecuted += _ => executions++;
            Check(bare.GetComponent<PlayerADS>() == null && plainCombat.isActiveAndEnabled &&
                plainCombat.TryStartAction(PlayerActionSlot.SecondaryFire),
                "another character accepts a non-ADS secondary action with no ADS component");
            Check(executions == 1 && secondary.Phase == ActionPhase.Ready &&
                bare.GetComponent<Combatant>().MovementSpeedMultiplier == 1f && !bare.GetComponent<Combatant>().IsInteractionBlocked,
                "non-ADS secondary execution has no movement or interaction side effects");
            Destroy(bare);

            PlayerActionBinding skillBinding = combat.GetBinding(PlayerActionSlot.Skill1);
            PlayerActionBinding secondaryBinding = combat.GetBinding(PlayerActionSlot.SecondaryFire);
            PlayerActionBase savedSkill = skillBinding.Action;
            PlayerActionBase savedSecondary = secondaryBinding.Action;
            var alternate = input.gameObject.AddComponent<PlayerADSCompositionProbe>();
            var alternateSecondary = input.gameObject.AddComponent<PlayerADSCompositionProbe>();
            alternateSecondary.ActionSlot = PlayerActionSlot.SecondaryFire;
            alternateSecondary.UseADS = false;
            combat.enabled = false;
            Set(skillBinding, "action", alternate);
            Set(secondaryBinding, "action", alternateSecondary);
            combat.enabled = true;
            held = Skill1;
            yield return new WaitForSeconds(0.3f);
            Check(alternate.IsActive && !ads.IsActive && stance.IsAiming && view.AimBlend == 1f &&
                Near(motor.MoveSpeed, stat.MoveSpeed * 0.5f),
                "held Skill1 requests full ADS independently of the secondary slot");
            pressed = Secondary;
            yield return new WaitForSeconds(0.05f);
            Check(alternateSecondary.Executions == 1 && !alternateSecondary.IsActive && alternate.IsActive && stance.IsAiming,
                "the same character can run an unrelated secondary action alongside Skill1 ADS");
            Check(input.GetComponent<Combatant>().IsInteractionBlocked,
                "Skill1 ADS blocks interaction through the common capability query");
            stance.End(ads);
            Check(stance.IsAiming && !stance.CanBegin(ads),
                "an unrelated action cannot release or replace the active ADS owner");

            var effect = input.gameObject.AddComponent<PlayerADSEffectProbe>();
            Check(Near(motor.MoveSpeed, stat.MoveSpeed * 0.5f * 0.8f),
                "ADS composes with another movement modifier without overwriting base stats");
            float savedBonus = stat.MoveSpeedBonus;
            float upgradedSpeed = stat.MoveSpeed + 2f;
            stat.MoveSpeedBonus += 2f;
            Check(Near(motor.MoveSpeed, upgradedSpeed * 0.5f * 0.8f),
                "speed upgrades remain live while independent modifiers are active");
            held = 0;
            yield return new WaitForSeconds(0.3f);
            Check(!stance.IsAiming && !alternate.IsActive && view.AimBlend == 0f &&
                Near(motor.MoveSpeed, stat.MoveSpeed * 0.8f) && input.GetComponent<Combatant>().IsInteractionBlocked,
                "releasing ADS preserves another component's movement and interaction restrictions");
            effect.enabled = false;
            Check(Near(motor.MoveSpeed, stat.MoveSpeed) && !input.GetComponent<Combatant>().IsInteractionBlocked,
                "disabled generic modifiers stop contributing immediately");
            stat.MoveSpeedBonus = savedBonus;
            Destroy(effect);

            // Acceptance timing, including a nonzero windup, is intentionally preserved.
            Set(alternate, "windupTime", 0.4f);
            held = Skill1;
            yield return new WaitForSeconds(0.05f);
            Check(alternate.Phase == ActionPhase.Windup && stance.IsAiming && Near(motor.MoveSpeed, stat.MoveSpeed * 0.5f),
                "ADS effects still begin on acceptance during a configured action windup");
            held = 0;
            yield return new WaitForSeconds(0.3f);
            Check(!stance.IsAiming && alternate.Phase == ActionPhase.Ready && view.AimBlend == 0f,
                "releasing before execution cleans up ADS without delayed replay");
            Set(alternate, "windupTime", 0f);
            held = Skill1;
            yield return new WaitForSeconds(0.3f);
            stance.enabled = false;
            Check(!alternate.IsActive && !stance.IsAiming && Near(motor.MoveSpeed, stat.MoveSpeed),
                "disabling the feature cancels its requesting action and removes gameplay effects");
            yield return new WaitForSeconds(0.3f);
            Check(view.AimBlend == 0f, "disabled feature retains its configured smooth camera exit");
            held = 0;
            stance.enabled = true;
            yield return new WaitForSeconds(0.05f);
            Check(!stance.IsAiming && !alternate.IsActive, "reenabling ADS does not replay a released request");
            combat.enabled = false;
            Set(skillBinding, "action", savedSkill);
            Set(secondaryBinding, "action", savedSecondary);
            combat.enabled = true;
            Destroy(alternate);
            Destroy(alternateSecondary);
        }

        private void Update()
        {
            if (input == null) return;
            if (EditorApplication.timeSinceStartup - started > 120d)
            {
                Debug.LogError("ADS validation timed out.");
                PlayerADSValidation.Finish(1);
                return;
            }
            bool enabledInput = gameplay && input.isActiveAndEnabled && Time.timeScale > 0f &&
                !input.GetComponent<Combatant>().IsGameplayBlocked;
            SetInput("IsGameplayInputEnabled", enabledInput);
            SetInput("Move", enabledInput ? movement : Vector2.zero);
            SetInput("Look", Vector2.zero);
            string[] names = { "PrimaryFire", "SecondaryFire", "Skill1", "Skill2", "Ultimate", "Reload", "Dash", "Jump", "Interact" };
            for (int i = 0; i < names.Length; i++)
                SetInput(names[i], (ButtonInputState)Activator.CreateInstance(typeof(ButtonInputState), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { enabledInput && (pressed & (1 << i)) != 0, enabledInput && (held & (1 << i)) != 0, false }, null));
            pressed = 0;
        }

        private void Capture(string name, int width = 1280, int height = 720)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            const string folder = "Logs/ADSComponent_20261111/Screenshots";
            Directory.CreateDirectory(folder);
            Camera camera = view.GetComponent<Camera>();
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(folder + "/" + name + ".png", pixels.EncodeToPNG());
                screenshots.Add(name);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                target.Release();
                Destroy(target); Destroy(pixels);
            }
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message + $"; aim={stance.IsAiming}, blend={view.AimBlend}, ammo={combat.CurrentAmmo}, phase={ads.Phase}");
            checks.Add(message);
        }
        private void SaveResults(bool passed)
        {
            const string folder = "Logs/ADSComponent_20261111";
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder + "/Results.json", JsonUtility.ToJson(new Results { Passed = passed, Checks = checks.ToArray(), Screenshots = screenshots.ToArray() }, true));
        }
        [Serializable] private sealed class Results { public bool Passed; public string[] Checks; public string[] Screenshots; }
        private static bool Near(float a, float b, float epsilon = 0.001f) => Mathf.Abs(a - b) <= epsilon;
        private static void Set(object target, string name, object value) => PlayerActionValidationUtility.Set(target, name, value);
        private void SetInput(string name, object value) => typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);
    }
}
