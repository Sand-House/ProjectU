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
    public static class PlayerReloadValidation
    {
        private const string Running = "ProjectU.PlayerReloadValidation";

        static PlayerReloadValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player Reload Validation Driver").AddComponent<PlayerReloadValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player Reload")]
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

    [DefaultExecutionOrder(-150)]
    public sealed class PlayerReloadValidationDriver : MonoBehaviour
    {
        private PlayerInputReader input;
        private PlayerCombat attacks;
        private PlayerStat stat;
        private PlayerMotor motor;
        private Animator animator;
        private int pressMask;
        private int heldMask;
        private int primaryShots;
        private int secondaryShots;
        private int skills;
        private int starts;
        private int completions;
        private bool validEvents = true;
        private double reloadStarted;
        private double reloadElapsed;
        private double wallStarted;
        private readonly List<string> checks = new List<string>();

        private IEnumerator Start()
        {
            EnemyValidationIsolation.DisableDemoEnemies();
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
                    PlayerReloadValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("PLAYER RELOAD VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            PlayerReloadValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            wallStarted = EditorApplication.timeSinceStartup;
            Time.captureDeltaTime = 1f / 60f;
            input = UnityEngine.Object.FindFirstObjectByType<PlayerInputReader>();
            attacks = input.GetComponent<PlayerCombat>();
            PlayerActionValidationUtility.UseSecondaryAttackFixture(attacks);
            stat = input.GetComponent<PlayerStat>();
            motor = input.GetComponent<PlayerMotor>();
            animator = input.GetComponentInChildren<Animator>();
            // Isolate reload mechanics from deferred input; the buffer suite tests delayed R.
            SetField(input.GetComponent<PlayerController>(), "maxInputBufferTime", 0f);
            Check(attacks.CurrentAmmo == 30 && attacks.MaxAmmo == stat.MaxMagazine && !attacks.IsReloading,
                "saved player starts with a full primary magazine");
            Check(stat.ReloadDuration == 3.3f && stat.ReloadSpeedMultiplier == 1f && stat.DashDistance == 5f,
                "saved reload and migrated dash stats");
            attacks.OnReloadStarted += () =>
            {
                starts++;
                reloadStarted = Time.timeAsDouble;
                validEvents &= attacks.IsReloading && attacks.CurrentAmmo < attacks.MaxAmmo;
            };
            attacks.OnReloadCompleted += () =>
            {
                completions++;
                reloadElapsed = Time.timeAsDouble - reloadStarted;
                validEvents &= !attacks.IsReloading && attacks.CurrentAmmo == attacks.MaxAmmo;
            };
            attacks.AttackExecuted += slot =>
            {
                if (slot == PlayerActionSlot.PrimaryFire) primaryShots++;
                else if (slot == PlayerActionSlot.SecondaryFire) secondaryShots++;
                else skills++;
            };
            AttackBase primary = attacks.GetBinding(PlayerActionSlot.PrimaryFire).Attack;
            AttackBase secondary = attacks.GetBinding(PlayerActionSlot.SecondaryFire).Attack;
            AttackBase grenade = attacks.GetBinding(PlayerActionSlot.Skill1).Attack;
            var primaryData = Instantiate((HitscanAttackData)primary.Data);
            SetField(primaryData, "recoveryTime", 0f);
            SetField(primaryData, "spreadAngle", 0f);
            SetField(primary, "data", primaryData);
            yield return new WaitForSeconds(0.1f);

            pressMask = 8;
            yield return new WaitForSeconds(0.1f);
            Check(starts == 0 && !attacks.TryReload(), "R and API reject a full magazine without events");
            pressMask = 1;
            yield return new WaitForSeconds(0.1f);
            Check(primaryShots == 1 && attacks.CurrentAmmo == 29, "successful primary consumes exactly one round");
            pressMask = 2;
            yield return new WaitForSeconds(0.1f);
            Check(secondaryShots == 1 && attacks.CurrentAmmo == 29, "secondary does not consume primary ammo");
            pressMask = 8;
            yield return new WaitForSeconds(0.1f);
            Check(!attacks.IsReloading && starts == 0, "secondary recovery blocks manual reload");
            yield return new WaitForSeconds(0.5f);
            Check(!attacks.IsReloading, "blocked manual input is not buffered");

            stat.ReloadSpeedMultiplier = 2f;
            pressMask = 8;
            yield return new WaitForSeconds(0.15f);
            Check(attacks.IsReloading && starts == 1 && !attacks.TryReload(), "partial R starts once and rejects duplicate requests");
            Check(animator.GetCurrentAnimatorStateInfo(1).IsName("Upper Body.Reloading") &&
                Mathf.Abs(animator.GetCurrentAnimatorStateInfo(1).speedMultiplier - 2f) < 0.001f && animator.speed == 1f,
                "reload event plays only the reload state at the stat multiplier");
            int previousPrimary = primaryShots;
            int previousSecondary = secondaryShots;
            heldMask = pressMask = 3;
            yield return new WaitForSeconds(0.3f);
            Check(primaryShots == previousPrimary && secondaryShots == previousSecondary && attacks.CurrentAmmo == 29,
                "pressed and held primary/secondary are blocked throughout reload");
            heldMask = 0;
            yield return new WaitForSeconds(0.9f);
            Check(attacks.IsReloading && completions == 0 && attacks.CurrentAmmo == 29, "ammo is unchanged before reload duration");
            yield return new WaitForSeconds(0.5f);
            Check(!attacks.IsReloading && attacks.CurrentAmmo == 30 && completions == 1 &&
                Mathf.Abs((float)reloadElapsed - 3.3f / 2f) < 0.04f, "duration divided by multiplier refills once");

            // The final primary recovery blocks secondary fire and the empty-magazine reload.
            SetField(primaryData, "recoveryTime", 0.25f);
            heldMask = 1;
            while (attacks.CurrentAmmo > 1) yield return null;
            while (attacks.CurrentAmmo > 0) yield return null;
            heldMask = 0;
            pressMask = 3;
            yield return new WaitForSeconds(0.1f);
            Check(attacks.CurrentAmmo == 0 && !attacks.IsReloading && PlayerActionValidationUtility.For(primary).Phase == ActionPhase.Recovery && starts == 1,
                "last primary recovery blocks secondary fire and defers automatic reload");
            while (starts < 2) yield return null;
            Check(primaryShots == previousPrimary + 30 && attacks.CurrentAmmo == 0 && attacks.IsReloading,
                "empty primary cannot overfire and retries after primary recovery without R");
            SetField(primaryData, "recoveryTime", 0f);
            int completedBeforeCancel = completions;
            pressMask = 16;
            yield return new WaitForSeconds(0.1f);
            Check(!attacks.IsReloading && motor.IsDashing && attacks.CurrentAmmo == 0 && completions == completedBeforeCancel,
                "dash cancels empty reload without refilling or completing");
            yield return new WaitForSeconds(0.2f);
            Check(!attacks.IsReloading && starts == 2, "dash post-delay keeps empty auto reload blocked");
            while (!attacks.IsReloading) yield return null;
            Check(starts == 3 && attacks.CurrentAmmo == 0, "empty reload retries continuously after dash lock ends");

            // Nonzero skill windup/recovery prove the retry waits for the whole action, not cooldown/fuse.
            var grenadeData = Instantiate((ProjectileAttackData)grenade.Data);
            SetField(grenadeData, "windupTime", 0.12f);
            SetField(grenadeData, "recoveryTime", 0.3f);
            SetField(grenadeData, "cooldown", 1f);
            SetField(grenade, "data", grenadeData);
            pressMask = 4;
            yield return new WaitForSeconds(0.05f);
            Check(!attacks.IsReloading && PlayerActionValidationUtility.For(grenade).Phase == ActionPhase.Windup && attacks.CurrentAmmo == 0,
                "accepted skill cancels reload immediately at windup start");
            yield return new WaitForSeconds(0.2f);
            Check(!attacks.IsReloading && skills == 1 && PlayerActionValidationUtility.For(grenade).Phase == ActionPhase.Recovery,
                "skill recovery blocks empty auto reload");
            while (!attacks.IsReloading) yield return null;
            Check(starts == 4 && PlayerActionValidationUtility.For(grenade).CooldownRemaining > 0f && attacks.CurrentAmmo == 0,
                "empty auto reload retries after skill recovery while cooldown/fuse continue");
            yield return new WaitForSeconds(0.2f);
            int beforeRejectedSkill = starts;
            pressMask = 4;
            yield return new WaitForSeconds(0.05f);
            Check(attacks.IsReloading && starts == beforeRejectedSkill && skills == 1,
                "rejected cooldown skill does not cancel an active reload");

            float remaining = attacks.ReloadRemaining;
            Time.timeScale = 0f;
            for (int i = 0; i < 5; i++) yield return null;
            Check(attacks.IsReloading && attacks.ReloadRemaining == remaining && completions == completedBeforeCancel,
                "pause freezes reload timer and completion");
            Time.timeScale = 1f;
            while (attacks.IsReloading) yield return null;
            Check(attacks.CurrentAmmo == 30 && completions == completedBeforeCancel + 1,
                "resumed reload completes with full magazine");

            // Manual partial cancellation never gets the automatic empty-magazine retry.
            pressMask = 1;
            yield return new WaitForSeconds(0.1f);
            pressMask = 8;
            yield return new WaitForSeconds(0.1f);
            int partial = attacks.CurrentAmmo;
            pressMask = 16;
            yield return new WaitForSeconds(0.7f);
            Check(!attacks.IsReloading && attacks.CurrentAmmo == partial,
                "dash-cancelled partial magazine stays unchanged without automatic refill");

            // All skill slots use the same accepted-start rule, independent of Soldier animation.
            attacks.enabled = false;
            var bindings = new List<PlayerActionBinding>(attacks.ActionBindings.Where(item => item.Attack != null).ToList());
            foreach (PlayerActionSlot slot in new[] { PlayerActionSlot.Skill2, PlayerActionSlot.Ultimate })
            {
                var attack = input.gameObject.AddComponent<HitscanAttack>();
                SetField(attack, "data", primaryData);
                var binding = new PlayerActionBinding();
                SetField(binding, "slot", slot);
                SetField(binding, "attack", attack);
                SetField(binding, "origin", attacks.GetBinding(PlayerActionSlot.PrimaryFire).Origin);
                bindings.Add(binding);
            }
            SetField(attacks, "actionBindings", bindings.ToArray());
            attacks.enabled = true;
            foreach (int skillMask in new[] { 32, 64 })
            {
                pressMask = 8;
                yield return new WaitForSeconds(0.1f);
                Check(attacks.IsReloading, "partial reload begins before extra skill slot " + skillMask);
                pressMask = skillMask;
                yield return new WaitForSeconds(0.1f);
                Check(!attacks.IsReloading && attacks.CurrentAmmo == partial,
                    "skill slot cancels partial reload without spending ammo: " + skillMask);
            }

            // Multiplier upgrades affect current work and state playback together.
            stat.ReloadSpeedMultiplier = 1f;
            pressMask = 8;
            yield return new WaitForSeconds(0.3f);
            float slowRemaining = attacks.ReloadRemaining;
            stat.ReloadSpeedMultiplier = 3f;
            yield return new WaitForSeconds(0.1f);
            Check(attacks.ReloadRemaining < slowRemaining / 3f && animator.GetFloat("ReloadSpeedMultiplier") == 3f,
                "live reload upgrade changes remaining time and animation speed together");
            stat.MaxMagazine = 40;
            while (attacks.IsReloading) yield return null;
            Check(attacks.CurrentAmmo == 40, "reload fills the latest upgraded magazine capacity");
            stat.MaxMagazine = 5;
            yield return null;
            Check(attacks.CurrentAmmo == 5 && !attacks.IsReloading, "capacity decrease clamps ammo without refilling");
            stat.MaxMagazine = 6;

            // Reload cannot interrupt primary windup; explicit cancellation must not spend ammo.
            SetField(primaryData, "windupTime", 0.2f);
            pressMask = 1;
            yield return new WaitForSeconds(0.05f);
            Check(PlayerActionValidationUtility.For(primary).Phase == ActionPhase.Windup && attacks.CurrentAmmo == 5, "primary reserves no ammo before execution");
            pressMask = 8;
            yield return new WaitForSeconds(0.1f);
            Check(PlayerActionValidationUtility.For(primary).Phase == ActionPhase.Windup && !attacks.IsReloading && attacks.CurrentAmmo == 5,
                "manual reload is rejected during primary windup without cancelling it");
            Check(PlayerActionValidationUtility.For(primary).Cancel() && attacks.CurrentAmmo == 5,
                "explicit primary cancellation does not consume ammo");
            attacks.enabled = false;
            attacks.enabled = true;
            var lostOrigin = new GameObject("Temporary Reload Test Origin");
            lostOrigin.transform.position = input.transform.position + Vector3.up;
            Check(PlayerActionValidationUtility.For(primary).TryStart(lostOrigin.transform, attacks.GetAimPoint()), "primary accepts temporary origin");
            Destroy(lostOrigin);
            yield return new WaitForSeconds(0.3f);
            Check(attacks.CurrentAmmo == 5, "failed execution does not consume ammo");

            pressMask = 8;
            yield return new WaitForSeconds(0.1f);
            int startsBeforeDisable = starts;
            attacks.enabled = false;
            Check(!attacks.IsReloading && attacks.CurrentAmmo == 5, "disable cancels reload and preserves ammo");
            attacks.enabled = true;
            pressMask = 8;
            yield return new WaitForSeconds(0.1f);
            Check(starts == startsBeforeDisable + 1, "reenable does not duplicate reload-start subscriptions");
            input.GetComponent<Mortal>().Dead();
            yield return new WaitForSeconds(0.1f);
            Check(!attacks.IsReloading && !attacks.TryReload() && attacks.CurrentAmmo == 5,
                "death cancels reload and blocks new reload attempts");
            Check(validEvents, "start and completion events observe committed state");
        }

        private void Update()
        {
            if (input == null) return;
            if (EditorApplication.timeSinceStartup - wallStarted > 60d)
            {
                Debug.LogError("Reload validation timed out.");
                PlayerReloadValidation.Finish(1);
            }
            SetInput("IsGameplayInputEnabled", input.isActiveAndEnabled && Time.timeScale > 0f);
            SetInput("Move", Vector2.zero);
            SetInput("Look", Vector2.zero);
            SetInput("Jump", default(ButtonInputState));
            SetInput("PrimaryFire", Snapshot((pressMask & 1) != 0, (heldMask & 1) != 0));
            SetInput("SecondaryFire", Snapshot((pressMask & 2) != 0, (heldMask & 2) != 0));
            SetInput("Skill1", Snapshot((pressMask & 4) != 0, false));
            SetInput("Reload", Snapshot((pressMask & 8) != 0, false));
            SetInput("Dash", Snapshot((pressMask & 16) != 0, false));
            SetInput("Skill2", Snapshot((pressMask & 32) != 0, false));
            SetInput("Ultimate", Snapshot((pressMask & 64) != 0, false));
            pressMask = 0;
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
        { PlayerActionValidationUtility.Set(target, name, value); }
    }
}
