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
        private PlayerAttackController launcher;
        private PlayerAimController aim;
        private HitscanAttack primaryAttack;
        private ProjectileAttack secondaryAttack;
        private ProjectileAttack grenadeAttack;
        private PlayerInputReader alternativeInput;
        private int alternativePressMask;
        private int alternativeHeldMask;
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
            launcher = input.GetComponent<PlayerAttackController>();
            aim = input.GetComponent<PlayerAimController>();
            primaryAttack = launcher.GetBinding(AttackSlot.PrimaryFire).Attack as HitscanAttack;
            secondaryAttack = launcher.GetBinding(AttackSlot.SecondaryFire).Attack as ProjectileAttack;
            grenadeAttack = launcher.GetBinding(AttackSlot.Skill1).Attack as ProjectileAttack;
            animationController = input.GetComponent<PlayerAnimationController>();
            stat = input.GetComponent<PlayerStat>();
            pool = UnityEngine.Object.FindFirstObjectByType<ProjectilePool>();
            Check(launcher.enabled && input.GetComponent<Combatant>().Faction == Faction.Player && input.GetComponent<Mortal>().HP == 100,
                "saved player combat identity and HP");
            Check(primaryAttack != null && secondaryAttack != null && grenadeAttack != null &&
                secondaryAttack != grenadeAttack && input.GetComponents<Attack>().Length == 3,
                "three distinct saved attack components");
            Check(launcher.AttackBindings.Count == 3 &&
                primaryAttack.Data.RepeatWhileHeld &&
                secondaryAttack.Data.RepeatWhileHeld &&
                !grenadeAttack.Data.RepeatWhileHeld &&
                launcher.GetBinding(AttackSlot.Skill2) == null && launcher.GetBinding(AttackSlot.Ultimate) == null,
                "saved attack assets retain repeat policies and unused slots remain empty");
            Check(primaryAttack.Data.name == "TempPrimaryFire" && primaryAttack.Data.Damage == 30f &&
                secondaryAttack.Data.name == "TempSecondaryFire" && secondaryAttack.Data.Damage == 50f &&
                grenadeAttack.Data.name == "TempGrenade" && grenadeAttack.Data.Damage == 100f,
                "requested asset names and base damage");
            var bulletData = (ProjectileAttackData)secondaryAttack.Data;
            var grenadeData = (ProjectileAttackData)grenadeAttack.Data;
            Check(bulletData.ProjectilePrefab is ProjectileBullet && grenadeData.ProjectilePrefab is ProjectileGrenade &&
                bulletData.LaunchAngle == 0f && grenadeData.LaunchAngle == 12f &&
                secondaryAttack.Pool == pool && grenadeAttack.Pool == pool,
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
            Vector3 muzzle = aim.Muzzle.position;
            Check(aim.Muzzle.name == "Muzzle" && aim.Muzzle.parent.name == "Prototype Rifle",
                "saved muzzle marker is attached to the rifle");
            camera.transform.SetPositionAndRotation(muzzle - Vector3.forward * 5f, Quaternion.identity);
            root = new GameObject("Player Attack Validation Targets");
            Combatant enemy = Target("Enemy", muzzle + Vector3.forward * 4f, Faction.Enemy);
            Combatant friendly = Target("Friendly", muzzle + Vector3.forward * 2f, Faction.Player);
            Physics.SyncTransforms();

            launcher.AttackExecuted += slot =>
            {
                if (slot == AttackSlot.PrimaryFire) { primaryShots++; weaponShots++; }
                if (slot == AttackSlot.SecondaryFire) { secondaryShots++; weaponShots++; }
                if (slot != AttackSlot.Skill1) return;
                grenades++;
                // Keep this isolated damage probe in place; normal prefab ballistics are tested separately.
                Rigidbody body = grenadeAttack.LastProjectile.GetComponent<Rigidbody>();
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
            Check(Vector3.Distance(primaryAttack.LastAttackPosition, muzzle) < 0.001f,
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
            Check(!secondaryAttack.LastProjectile.IsActive, "secondary bullet returns after enemy impact");
            Check(Vector3.Distance(secondaryAttack.LastAttackPosition, muzzle) < 0.001f,
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
            Check(blastTarget.Mortal.HP == 200 && grenadeAttack.LastProjectile.IsActive,
                "grenade does not damage before fuse expires");
            Check(Vector3.Distance(grenadeAttack.LastAttackPosition, muzzle) < 0.001f,
                "grenade receives caller-supplied muzzle position");
            yield return new WaitForSeconds(2.6f);
            Check(blastTarget.Mortal.HP == 100 && blastNotifications == 1 && friendly.Mortal.HP == 200,
                "grenade explodes for exactly 100 damage once and excludes allies");
            Check(!grenadeAttack.LastProjectile.IsActive && pool.ActiveCount == 0, "grenade effect completes and returns");
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
            AttackData primaryData = primaryAttack.Data;
            AttackData secondaryData = secondaryAttack.Data;
            AttackData timedPrimary = TimedData(primaryData, 0.12f);
            AttackData timedSecondary = TimedData(secondaryData, 0.16f);
            SetData(primaryAttack, timedPrimary);
            SetData(secondaryAttack, timedSecondary);
            var primaryTimes = new List<double>();
            var secondaryTimes = new List<double>();
            Action recordPrimary = () => primaryTimes.Add(Time.timeAsDouble);
            Action recordSecondary = () => secondaryTimes.Add(Time.timeAsDouble);
            primaryAttack.OnExecuted += recordPrimary;
            secondaryAttack.OnExecuted += recordSecondary;
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
            primaryAttack.OnExecuted -= recordPrimary;
            secondaryAttack.OnExecuted -= recordSecondary;
            SetData(primaryAttack, primaryData);
            SetData(secondaryAttack, secondaryData);
            Destroy(timedPrimary);
            Destroy(timedSecondary);

            input.GetComponent<Mortal>().Dead();
            heldMask = 7;
            pressMask = 7;
            // Already launched bullets still travel after the owner dies.
            yield return new WaitForSeconds(0.1f + 4f / bulletData.ProjectilePrefab.Speed);
            Check(primaryShots == releasedPrimary && secondaryShots == releasedSecondary && grenades == 1 && pool.ActiveCount == 0,
                "dead player cannot execute pressed or held attacks");
            heldMask = 0;

            // A second actor has no Soldier visual, animation, motor, stamina or stat component.
            var actor = new GameObject("Alternative Character");
            actor.SetActive(false);
            actor.transform.SetParent(root.transform);
            actor.transform.position = input.transform.position + Vector3.right * 30f;
            actor.AddComponent<Combatant>().Faction = Faction.Player;
            alternativeInput = actor.AddComponent<PlayerInputReader>();
            var melee = actor.AddComponent<MeleeAttack>();
            var ranged = actor.AddComponent<HitscanAttack>();
            var projectile = actor.AddComponent<ProjectileAttack>();
            var alternativeLauncher = actor.AddComponent<PlayerAttackController>();
            var meleeData = ScriptableObject.CreateInstance<MeleeAttackData>();
            SetData(melee, meleeData);
            var rangedData = (HitscanAttackData)TimedData(primaryData, 0.1f);
            SetData(ranged, rangedData);
            var projectileData = Instantiate(secondaryData);
            SetRepeat(projectileData, false);
            SetData(projectile, projectileData);
            var serializedProjectile = new SerializedObject(projectile);
            serializedProjectile.FindProperty("pool").objectReferenceValue = pool;
            serializedProjectile.ApplyModifiedPropertiesWithoutUndo();
            Transform meleeOrigin = Marker(actor.transform, "Melee Origin", new Vector3(0.35f, 0f, 0.1f));
            Transform handOrigin = Marker(actor.transform, "Hand Origin", new Vector3(-0.35f, 0.5f, 0.2f));
            Transform shoulderOrigin = Marker(actor.transform, "Shoulder Origin", new Vector3(0.6f, 0.8f, 0.1f));
            var configuration = new SerializedObject(alternativeLauncher);
            configuration.FindProperty("aimCamera").objectReferenceValue = camera;
            SerializedProperty bindings = configuration.FindProperty("attackBindings");
            bindings.arraySize = 3;
            SetBinding(bindings.GetArrayElementAtIndex(0), AttackSlot.PrimaryFire, melee, meleeOrigin);
            SetBinding(bindings.GetArrayElementAtIndex(1), AttackSlot.Skill2, ranged, handOrigin);
            SetBinding(bindings.GetArrayElementAtIndex(2), AttackSlot.Ultimate, projectile, shoulderOrigin);
            configuration.ApplyModifiedPropertiesWithoutUndo();
            var executedSlots = new List<AttackSlot>();
            alternativeLauncher.AttackExecuted += slot => executedSlots.Add(slot);
            actor.SetActive(true);
            camera.transform.SetPositionAndRotation(actor.transform.position + new Vector3(0f, 0.8f, -5f), Quaternion.identity);
            Combatant meleeTarget = Target("Alternative Melee Target", actor.transform.position + new Vector3(0.35f, 1f, 1.2f), Faction.Enemy);
            Combatant rangedTarget = Target("Alternative Ranged Target", actor.transform.position + new Vector3(0f, 0.8f, 8f), Faction.Enemy);
            rangedTarget.Mortal.IsInvincible = true;
            Physics.SyncTransforms();
            Check(alternativeLauncher.enabled && actor.GetComponent<Animator>() == null && actor.GetComponent<PlayerStat>() == null,
                "same launcher works without Soldier components or mandatory attack slots");

            alternativePressMask = alternativeHeldMask = 1;
            yield return new WaitForSeconds(0.4f);
            Check(executedSlots.Count == 1 && executedSlots[0] == AttackSlot.PrimaryFire && meleeTarget.Mortal.HP == 180,
                "primary accepts MeleeAttack and press-only configuration prevents held repeats");
            Check(Vector3.Distance(melee.LastAttackPosition, meleeOrigin.position) < 0.001f,
                "melee receives its independently configured origin");
            // Keep this nearby melee target out of the later shoulder projectile's path.
            meleeTarget.gameObject.SetActive(false);
            Physics.SyncTransforms();
            alternativeHeldMask = 16;
            yield return new WaitForSeconds(0.4f);
            Check(executedSlots.Count >= 3 && executedSlots.FindAll(slot => slot == AttackSlot.Skill2).Count >= 2,
                "Skill2 reads F and can repeat from Held alone");
            Check(Vector3.Distance(ranged.LastAttackPosition, handOrigin.position) < 0.001f && ranged.DamageMultiplier == 1f,
                "Skill2 uses its hand origin and default multiplier without PlayerStat");

            // Only the runtime data copy changes; bindings and saved assets stay untouched.
            SetRepeat(rangedData, false);
            yield return new WaitForSeconds(0.1f);
            int nonRepeatingShots = executedSlots.FindAll(slot => slot == AttackSlot.Skill2).Count;
            yield return new WaitForSeconds(0.25f);
            Check(executedSlots.FindAll(slot => slot == AttackSlot.Skill2).Count == nonRepeatingShots,
                "changing AttackData repeat policy stops held requests after pending windup completes");
            alternativePressMask = 16;
            yield return new WaitForSeconds(0.15f);
            Check(executedSlots.FindAll(slot => slot == AttackSlot.Skill2).Count == nonRepeatingShots + 1,
                "press still executes once with the same binding and nonrepeating AttackData");
            Check(!meleeData.RepeatWhileHeld && primaryData.RepeatWhileHeld && secondaryData.RepeatWhileHeld,
                "new attack data defaults to press-only and runtime copies preserve original asset policies");

            alternativeHeldMask = 0;
            yield return new WaitForSeconds(0.1f);
            alternativePressMask = alternativeHeldMask = 32;
            yield return new WaitForSeconds(0.2f);
            Check(executedSlots.FindAll(slot => slot == AttackSlot.Ultimate).Count == 1 && projectile.LastProjectile != null &&
                Vector3.Distance(projectile.LastAttackPosition, shoulderOrigin.position) < 0.001f,
                "Ultimate reads Q, executes once while held and uses a separate origin");
            alternativeHeldMask = 0;

            // Re-enabling must replace subscriptions, not accumulate execution notifications.
            alternativeLauncher.enabled = false;
            alternativeLauncher.enabled = true;
            meleeTarget.gameObject.SetActive(true);
            Physics.SyncTransforms();
            int beforeReenableShot = executedSlots.Count;
            alternativePressMask = 1;
            yield return new WaitForSeconds(0.15f);
            Check(executedSlots.Count == beforeReenableShot + 1 && meleeTarget.Mortal.HP == 160,
                "reenabling subscribes once and retains configured attacks");
            Check(ranged.TryAttack(handOrigin, alternativeLauncher.GetAimPoint()) && ranged.Phase == AttackPhase.Windup,
                "bound attack accepts a pending windup before disable");
            alternativeLauncher.enabled = false;
            int beforeCancellation = executedSlots.Count;
            yield return new WaitForSeconds(0.1f);
            Check(executedSlots.Count == beforeCancellation && ranged.Phase != AttackPhase.Windup,
                "disabling cancels pending bound attacks and removes event handlers");
            alternativeInput = null;
            actor.SetActive(false);
            pool.ReturnAll();
            Destroy(meleeData);
            Destroy(rangedData);
            Destroy(projectileData);
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
            if (alternativeInput != null)
            {
                SetAlternativeInput("PrimaryFire", 1);
                SetAlternativeInput("SecondaryFire", 2);
                SetAlternativeInput("Skill1", 4);
                SetAlternativeInput("Skill2", 16);
                SetAlternativeInput("Ultimate", 32);
                alternativePressMask = 0;
            }
        }

        private void SetAlternativeInput(string name, int mask) => typeof(PlayerInputReader).GetProperty(name).SetValue(alternativeInput,
            State((alternativePressMask & mask) != 0, (alternativeHeldMask & mask) != 0));

        private static Transform Marker(Transform parent, string name, Vector3 localPosition)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.localPosition = localPosition;
            return marker;
        }

        private static void SetBinding(SerializedProperty binding, AttackSlot slot, Attack attack, Transform origin)
        {
            binding.FindPropertyRelative("slot").enumValueIndex = (int)slot;
            binding.FindPropertyRelative("attack").objectReferenceValue = attack;
            binding.FindPropertyRelative("origin").objectReferenceValue = origin;
        }

        private static void SetRepeat(AttackData data, bool repeat)
        {
            var serialized = new SerializedObject(data);
            serialized.FindProperty("repeatWhileHeld").boolValue = repeat;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
