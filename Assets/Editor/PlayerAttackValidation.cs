using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class PlayerAttackValidation
    {
        private const string Running = "ProjectU.PlayerAttackValidation";

        static PlayerAttackValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player Attack Validation Driver").AddComponent<PlayerAttackValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player Attacks")]
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
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }

    // Feed the saved scene's Reader snapshots after Reader.Update, as in animation validation.
    [DefaultExecutionOrder(-150)]
    public sealed class PlayerAttackValidationDriver : MonoBehaviour
    {
        private PlayerInputReader input;
        private PlayerProjectileLauncher launcher;
        private PlayerAnimationController animationController;
        private PlayerStat stat;
        private ProjectilePool pool;
        private GameObject root;
        private int pressMask;
        private int heldMask;
        private int primaryShots;
        private int secondaryShots;
        private int grenades;
        private int weaponShots;
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
                    PlayerAttackValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("PLAYER ATTACK VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            if (root != null) Destroy(root);
            PlayerAttackValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            input = UnityEngine.Object.FindFirstObjectByType<PlayerInputReader>();
            launcher = input.GetComponent<PlayerProjectileLauncher>();
            animationController = input.GetComponent<PlayerAnimationController>();
            stat = input.GetComponent<PlayerStat>();
            pool = UnityEngine.Object.FindFirstObjectByType<ProjectilePool>();
            Check(launcher.enabled && input.GetComponent<Combatant>().Faction == Faction.Player && input.GetComponent<Mortal>().HP == 100,
                "saved player combat identity and HP");
            Check(launcher.PrimaryAttack != null && launcher.SecondaryAttack != null && launcher.GrenadeAttack != null &&
                launcher.SecondaryAttack != launcher.GrenadeAttack && input.GetComponents<Attack>().Length == 3,
                "three distinct saved attack components");
            Check(launcher.PrimaryAttack.Data.name == "TempPrimaryFire" && launcher.PrimaryAttack.Data.Damage == 30f &&
                launcher.SecondaryAttack.Data.name == "TempSecondaryFire" && launcher.SecondaryAttack.Data.Damage == 50f &&
                launcher.GrenadeAttack.Data.name == "TempGrenade" && launcher.GrenadeAttack.Data.Damage == 100f,
                "requested asset names and base damage");
            var bulletData = (ProjectileAttackData)launcher.SecondaryAttack.Data;
            var grenadeData = (ProjectileAttackData)launcher.GrenadeAttack.Data;
            Check(bulletData.ProjectilePrefab is ProjectileBullet && grenadeData.ProjectilePrefab is ProjectileGrenade &&
                bulletData.LaunchAngle == 0f && grenadeData.LaunchAngle == 12f &&
                launcher.SecondaryAttack.Pool == pool && launcher.GrenadeAttack.Pool == pool,
                "original bullet and grenade prefabs, angles and pool references");
            var actions = (InputActionMap)typeof(PlayerInputReader).GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);
            Check(actions.FindAction("PrimaryFire").bindings[0].path == "<Mouse>/leftButton" &&
                actions.FindAction("SecondaryFire").bindings[0].path == "<Mouse>/rightButton" &&
                actions.FindAction("Skill1").bindings[0].path == "<Keyboard>/e", "physical button bindings");

            input.GetComponent<PlayerController>().enabled = false;
            input.GetComponent<PlayerMotor>().enabled = false;
            // Keep this input/damage probe still; animated muzzle tracking is validated separately.
            input.GetComponent<PlayerAimController>().enabled = false;
            UnityEngine.Object.FindFirstObjectByType<PlayerCameraController>().enabled = false;
            input.transform.SetPositionAndRotation(new Vector3(1000f, 1000f, 1000f), Quaternion.identity);
            Camera camera = UnityEngine.Object.FindFirstObjectByType<PlayerCameraController>().GetComponent<Camera>();
            Vector3 muzzle = launcher.Muzzle.position;
            Check(launcher.Muzzle.name == "Muzzle" && launcher.Muzzle.parent.name == "Prototype Rifle",
                "saved muzzle marker is attached to the rifle");
            camera.transform.SetPositionAndRotation(muzzle - Vector3.forward * 5f, Quaternion.identity);
            root = new GameObject("Player Attack Validation Targets");
            Combatant enemy = Target("Enemy", muzzle + Vector3.forward * 4f, Faction.Enemy);
            Combatant friendly = Target("Friendly", muzzle + Vector3.forward * 2f, Faction.Player);
            Physics.SyncTransforms();

            launcher.PrimaryFired += () => primaryShots++;
            launcher.SecondaryFired += () => secondaryShots++;
            launcher.WeaponFired += () => weaponShots++;
            launcher.GrenadeThrown += () =>
            {
                grenades++;
                // Keep this isolated damage probe in place; normal prefab ballistics are tested separately.
                Rigidbody body = launcher.GrenadeAttack.LastProjectile.GetComponent<Rigidbody>();
                body.linearVelocity = Vector3.zero;
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
            };

            pressMask = 1;
            yield return new WaitForSeconds(0.15f);
            Check(primaryShots == 1 && secondaryShots == 0 && grenades == 0 && weaponShots == 1,
                "left click executes only primary and gun animation event");
            Check(enemy.Mortal.HP == 170 && friendly.Mortal.HP == 200, "primary applies exactly 30 hostile hitscan damage");
            Check(pool.ActiveCount == 0, "primary creates no physical bullet");
            Check(Vector3.Distance(launcher.PrimaryAttack.LastAttackPosition, muzzle) < 0.001f,
                "primary receives actual muzzle position from launcher");
            yield return new WaitForSeconds(0.15f);
            Check(primaryShots == 1, "released primary does not repeat after a short tap");

            pressMask = 8;
            yield return new WaitForSeconds(0.08f);
            Check(animationController.IsReloading, "visual reload can start");
            pressMask = 2;
            yield return new WaitForSeconds(0.2f + 4f / bulletData.ProjectilePrefab.Speed);
            Check(primaryShots == 1 && secondaryShots == 1 && grenades == 0 && weaponShots == 2,
                "right click launches only secondary bullet and gun animation event");
            Check(enemy.Mortal.HP == 120 && friendly.Mortal.HP == 200,
                "secondary applies exactly 50 damage and passes friendly");
            Check(!animationController.IsReloading, "secondary interrupts visual reload");
            Check(!launcher.SecondaryAttack.LastProjectile.IsActive, "secondary bullet returns after enemy impact");
            Check(Vector3.Distance(launcher.SecondaryAttack.LastAttackPosition, muzzle) < 0.001f,
                "secondary receives actual muzzle position from launcher");

            Combatant blastTarget = Target("Blast Target", muzzle + Vector3.right, Faction.Enemy);
            int blastNotifications = 0;
            blastTarget.Mortal.OnDamaged += (amount, attacker) =>
            {
                Check(amount == 100 && attacker == input.gameObject, "explosion damage and attacker attribution");
                blastNotifications++;
            };
            Physics.SyncTransforms();
            heldMask = 4;
            pressMask = 4;
            yield return new WaitForSeconds(0.2f);
            Check(grenades == 1 && primaryShots == 1 && secondaryShots == 1 && weaponShots == 2,
                "E launches only grenade without gun animation event");
            Check(blastTarget.Mortal.HP == 200 && launcher.GrenadeAttack.LastProjectile.IsActive,
                "grenade does not damage before fuse expires");
            Check(Vector3.Distance(launcher.GrenadeAttack.LastAttackPosition, muzzle) < 0.001f,
                "grenade receives caller-supplied muzzle position");
            yield return new WaitForSeconds(2.6f);
            Check(blastTarget.Mortal.HP == 100 && blastNotifications == 1 && friendly.Mortal.HP == 200,
                "grenade explodes for exactly 100 damage once and excludes allies");
            Check(!launcher.GrenadeAttack.LastProjectile.IsActive && pool.ActiveCount == 0, "grenade effect completes and returns");
            Check(grenades == 1, "holding E does not repeat grenade throws");
            heldMask = 0;

            Time.timeScale = 0f;
            pressMask = 7;
            yield return new WaitForSecondsRealtime(0.1f);
            Check(primaryShots == 1 && secondaryShots == 1 && grenades == 1, "pause suppresses all three input attacks");
            Time.timeScale = 1f;

            // A fresh target verifies that the saved PlayerStat multiplier reaches the attack.
            enemy.gameObject.SetActive(false);
            Combatant scaledTarget = Target("Scaled Damage", muzzle + Vector3.forward * 4f, Faction.Enemy);
            stat.AttackMultiplier = 2f;
            Physics.SyncTransforms();
            pressMask = 1;
            yield return new WaitForSeconds(0.1f);
            Check(scaledTarget.Mortal.HP == 140 && primaryShots == 2, "player stat multiplier applies to connected attack");
            stat.AttackMultiplier = 1f;

            // Runtime-only data copies exercise held fire with nonzero timings; never save them.
            scaledTarget.Mortal.IsInvincible = true;
            AttackData primaryData = launcher.PrimaryAttack.Data;
            AttackData secondaryData = launcher.SecondaryAttack.Data;
            AttackData timedPrimary = TimedData(primaryData, 0.12f);
            AttackData timedSecondary = TimedData(secondaryData, 0.16f);
            SetData(launcher.PrimaryAttack, timedPrimary);
            SetData(launcher.SecondaryAttack, timedSecondary);
            var primaryTimes = new List<double>();
            var secondaryTimes = new List<double>();
            Action recordPrimary = () => primaryTimes.Add(Time.timeAsDouble);
            Action recordSecondary = () => secondaryTimes.Add(Time.timeAsDouble);
            launcher.PrimaryFired += recordPrimary;
            launcher.SecondaryFired += recordSecondary;
            pressMask = 8;
            yield return new WaitForSeconds(0.08f);
            Check(animationController.IsReloading, "reload starts before held-only fire");
            // No Pressed snapshot: each weapon must continue from Held alone.
            heldMask = 3;
            yield return new WaitForSeconds(0.65f);
            Check(primaryTimes.Count >= 3 && secondaryTimes.Count >= 2,
                "held-only primary and secondary both repeat without new presses");
            Check(!animationController.IsReloading, "held fire interrupts visual reload");
            Check(RespectsTiming(primaryTimes, timedPrimary) && RespectsTiming(secondaryTimes, timedSecondary),
                "held shots respect windup recovery and cooldown");
            pressMask = 8;
            yield return new WaitForSeconds(0.08f);
            Check(!animationController.IsReloading, "held fire takes priority over reload input");

            Time.timeScale = 0f;
            int pausedPrimary = primaryShots;
            int pausedSecondary = secondaryShots;
            yield return new WaitForSecondsRealtime(0.12f);
            Check(primaryShots == pausedPrimary && secondaryShots == pausedSecondary, "pause stops held fire");
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.25f);
            Check(primaryShots > pausedPrimary && secondaryShots > pausedSecondary, "held fire resumes after pause");

            heldMask = 0;
            // A windup accepted before release may finish, then no new shots may start.
            yield return new WaitForSeconds(0.08f);
            int releasedPrimary = primaryShots;
            int releasedSecondary = secondaryShots;
            yield return new WaitForSeconds(0.25f);
            Check(primaryShots == releasedPrimary && secondaryShots == releasedSecondary,
                "release stops both repeat requests after pending windup completes");
            launcher.PrimaryFired -= recordPrimary;
            launcher.SecondaryFired -= recordSecondary;
            SetData(launcher.PrimaryAttack, primaryData);
            SetData(launcher.SecondaryAttack, secondaryData);
            Destroy(timedPrimary);
            Destroy(timedSecondary);

            input.GetComponent<Mortal>().Dead();
            heldMask = 7;
            pressMask = 7;
            // Already launched bullets still travel after the owner dies.
            yield return new WaitForSeconds(0.1f + 4f / bulletData.ProjectilePrefab.Speed);
            Check(primaryShots == releasedPrimary && secondaryShots == releasedSecondary && grenades == 1 && pool.ActiveCount == 0,
                "dead player cannot execute pressed or held attacks");
        }

        private void Update()
        {
            if (input == null) return;
            SetInput("Move", Vector2.zero);
            SetInput("Look", Vector2.zero);
            SetInput("Jump", default(ButtonInputState));
            SetInput("Reload", State((pressMask & 8) != 0, false));
            SetInput("PrimaryFire", State((pressMask & 1) != 0, (heldMask & 1) != 0));
            SetInput("SecondaryFire", State((pressMask & 2) != 0, (heldMask & 2) != 0));
            SetInput("Skill1", State((pressMask & 4) != 0, (heldMask & 4) != 0));
            pressMask = 0;
        }

        private Combatant Target(string name, Vector3 position, Faction faction)
        {
            var target = new GameObject(name);
            target.transform.SetParent(root.transform);
            target.transform.position = position;
            target.AddComponent<BoxCollider>().size = Vector3.one * 0.6f;
            Combatant combatant = target.AddComponent<Combatant>();
            combatant.Faction = faction;
            var serialized = new SerializedObject(combatant.Mortal);
            serialized.FindProperty("hp").intValue = 200;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return combatant;
        }

        private void SetInput(string name, object value) => typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);
        // Pressed-only snapshots also cover taps released within a single input frame.
        private static ButtonInputState State(bool pressed, bool held) => (ButtonInputState)typeof(ButtonInputState)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(bool), typeof(bool), typeof(bool) }, null).Invoke(new object[] { pressed, held, false });

        private static AttackData TimedData(AttackData source, float cooldown)
        {
            AttackData data = Instantiate(source);
            var serialized = new SerializedObject(data);
            serialized.FindProperty("windupTime").floatValue = 0.03f;
            serialized.FindProperty("recoveryTime").floatValue = 0.05f;
            serialized.FindProperty("cooldown").floatValue = cooldown;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return data;
        }

        private static void SetData(Attack attack, AttackData data)
        {
            var serialized = new SerializedObject(attack);
            serialized.FindProperty("data").objectReferenceValue = data;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool RespectsTiming(List<double> times, AttackData data)
        {
            double interval = Math.Max(data.RecoveryTime, data.Cooldown) + data.WindupTime;
            for (int i = 1; i < times.Count; i++)
                if (times[i] - times[i - 1] < interval - 0.0001d) return false;
            return true;
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Player attack validation failed: " + message);
            checks.Add(message);
        }
    }
}
