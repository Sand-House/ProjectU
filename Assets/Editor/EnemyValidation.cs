using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class EnemyValidation
    {
        private const string Running = "ProjectU.EnemyValidation";
        static EnemyValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Enemy Validation").AddComponent<EnemyValidationDriver>();
            };
        }
        [MenuItem("ProjectU/Validation/Validate Enemies")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            EnemyAI[] saved = UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            if (saved.Length != 5 || saved.Any(enemy => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(enemy.gameObject) != TempEnemySetup.PrefabPath))
                throw new InvalidOperationException("The saved scene must contain five linked TempEnemy prefab instances.");
            Debug.Log("ENEMY EDIT VALIDATION PASSED: five linked prefab instances.");
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }
        internal static void Finish(int result)
        {
            SessionState.SetBool(Running, false);
            Time.timeScale = 1f; Time.captureDeltaTime = 0f;
            if (Application.isBatchMode) EditorApplication.Exit(result);
            else EditorApplication.ExitPlaymode();
        }
    }

    public sealed class EnemySustainProbe : EnemyAction
    {
        public int Ticks { get; private set; }
        public bool Cleaned { get; private set; }
        protected override ActionResult ExecuteCore(in ActionContext request) => ActionResult.Running;
        protected override void TickActive(float deltaTime) => Ticks++;
        protected override void Cleanup(ActionCancelReason reason) => Cleaned = true;
        public void Complete() => FinishActive();
    }

    [DefaultExecutionOrder(-175)]
    public sealed class EnemyValidationDriver : MonoBehaviour
    {
        private readonly List<string> checks = new List<string>();
        private double started;
        private GameObject arena;
        private readonly List<GameObject> fixtures = new List<GameObject>();
        private readonly List<ScriptableObject> dataCopies = new List<ScriptableObject>();

        private IEnumerator Start()
        {
            started = EditorApplication.timeSinceStartup;
            Time.captureDeltaTime = 1f / 60f;
            IEnumerator test = Validate();
            while (true)
            {
                bool more; object value = null;
                try { more = test.MoveNext(); if (more) value = test.Current; }
                catch (Exception exception)
                {
                    Debug.LogException(exception); Cleanup(); EnemyValidation.Finish(1); yield break;
                }
                if (!more) break;
                yield return value;
            }
            Debug.Log("ENEMY VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            Cleanup(); EnemyValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            EnemyAI[] demo = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            Check(demo.Length == 5, "scene has five enemies");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TempEnemySetup.PrefabPath);
            Check(prefab != null && prefab.activeSelf, "active TempEnemy prefab exists");
            Check(FindFirstObjectByType<NavMeshSurface>().navMeshData != null, "saved NavMesh is connected");
            Check(typeof(PlayerAction).BaseType == typeof(ActionBase) && typeof(EnemyAction).BaseType == typeof(ActionBase), "both actor action families share ActionBase");
            foreach (Type state in new[] { typeof(EnemyIdleState), typeof(EnemyChaseState), typeof(EnemyCombatState), typeof(EnemyDeadState) })
                Check(state.BaseType == typeof(EnemyStateBase), state.Name + " derives from EnemyStateBase");
            foreach (EnemyAI enemy in demo)
            {
                Check(enemy.GetComponent<CapsuleCollider>() != null && enemy.GetComponentInChildren<MeshRenderer>() != null,
                    enemy.name + " has capsule collision and mesh");
                Check(enemy.GetComponent<Mortal>().HP == 100 && enemy.GetComponent<Combatant>().Faction == Faction.Enemy, enemy.name + " health and faction");
                Check(enemy.GetComponent<EnemyAttackAction>().Attack is MeleeAttack && enemy.GetComponent<EnemyAttackAction>().RequiresGrounded &&
                    !enemy.GetComponent<EnemyAttackAction>().CanMove, enemy.name + " saved melee action configuration");
            }
            PlayerInputReader input = FindFirstObjectByType<PlayerInputReader>();
            input.enabled = false; input.GetComponent<PlayerController>().enabled = false;
            input.GetComponent<PlayerMotor>().enabled = false;
            Mortal playerHealth = input.GetComponent<Mortal>();
            Set(playerHealth, "hp", 1000);
            int sceneHits = 0;
            playerHealth.OnDamaged += (amount, attacker) => { if (attacker != null && attacker.GetComponent<EnemyAI>() != null) sceneHits++; };
            yield return null;
            Check(demo.All(enemy => enemy.Motor.IsGrounded), "all saved spawns are on NavMesh");
            Vector3[] initial = demo.Select(enemy => enemy.transform.position).ToArray();
            for (int frame = 0; frame < 420 && sceneHits == 0; frame++) yield return null;
            Check(sceneHits > 0 && playerHealth.HP < 1000, "saved scene AI senses chases and damages actual player");
            Check(demo.Where((enemy, index) => Vector3.Distance(enemy.transform.position, initial[index]) > 1f).Any(), "saved enemies actually navigate");
            EnemyValidationIsolation.DisableDemoEnemies();

            arena = new GameObject("Isolated Enemy Arena");
            arena.transform.position = new Vector3(100f, 0f, 100f);
            Box("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(24f, 0.2f, 24f));
            GameObject wall = Box("Blocking Wall", new Vector3(0f, 1.5f, 0f), new Vector3(4f, 3f, 0.6f));
            NavMeshSurface surface = arena.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            Combatant target = Target(new Vector3(100f, 1f, 106f));
            GameObject unit = Instantiate(prefab, new Vector3(100f, 0f, 94f), Quaternion.identity);
            fixtures.Add(unit);
            EnemyAI brain = unit.GetComponent<EnemyAI>();
            EnemyAttackAction action = unit.GetComponent<EnemyAttackAction>();
            EnemySensor sensor = brain.Sensor;
            sensor.SetPlayer(target);
            Set(sensor, "viewDistance", 20f);
            Physics.SyncTransforms();
            yield return null;
            Check(brain.State == EnemyState.Idle && !sensor.CanSee(target), "wall occludes initial sight");
            target.transform.position = new Vector3(100f, 1f, 97f); Physics.SyncTransforms();
            Check(sensor.CanSee(target), "front player is visible");
            target.transform.position = new Vector3(100f, 1f, 91f); Physics.SyncTransforms();
            Check(!sensor.CanSee(target), "rear player is outside field of view");
            target.transform.position = new Vector3(100f, 1f, 120f); Physics.SyncTransforms();
            Check(!sensor.CanSee(target), "player outside view distance is rejected");
            target.Faction = Faction.Enemy;
            Check(!sensor.CanSee(target), "friendly unit is not a player target");
            target.Faction = Faction.Player;
            target.transform.position = new Vector3(100f, 1f, 106f); Physics.SyncTransforms();
            unit.GetComponent<Mortal>().TakeDamage(1, target.gameObject);
            Check(brain.State == EnemyState.Chase && brain.Target == target, "damage acquires an occluded attacker");
            float lateral = 0f;
            for (int frame = 0; frame < 540 && target.Mortal.HP == 1000; frame++)
            {
                lateral = Mathf.Max(lateral, Mathf.Abs(unit.transform.position.x - 100f));
                yield return null;
            }
            Check(lateral > 2f, "NavMesh follows a route around the wall");
            Check(target.Mortal.HP < 1000 && brain.State == EnemyState.Combat, "enemy reaches and attacks target after routing");
            brain.Combat.CancelAll(ActionCancelReason.Explicit);
            Set(action, "cooldown", 0f);
            // New unit has clean timers for deterministic timing checks.
            Destroy(unit); yield return null;
            target.transform.position = new Vector3(106f, 1f, 97.5f);
            unit = Instantiate(prefab, new Vector3(106f, 0f, 96f), Quaternion.identity);
            fixtures.Add(unit); brain = unit.GetComponent<EnemyAI>(); action = unit.GetComponent<EnemyAttackAction>();
            brain.Sensor.SetPlayer(target); Set(action, "cooldown", 0f);
            Physics.SyncTransforms();
            for (int frame = 0; frame < 30 && action.Phase != ActionPhase.Windup; frame++) yield return null;
            Check(action.Phase == ActionPhase.Windup, "combat starts with windup");
            int beforeStrike = target.Mortal.HP;
            Vector3 frozen = unit.transform.position;
            Time.timeScale = 0f;
            for (int frame = 0; frame < 35; frame++) yield return null;
            Check(action.Phase == ActionPhase.Windup && target.Mortal.HP == beforeStrike && Vector3.Distance(frozen, unit.transform.position) < 0.001f,
                "pause freezes navigation windup and damage");
            Time.timeScale = 1f;
            for (int frame = 0; frame < 60 && target.Mortal.HP == beforeStrike; frame++) yield return null;
            Check(target.Mortal.HP == beforeStrike - 15, "melee deals exactly 15 once");
            Check(action.Phase == ActionPhase.Recovery && brain.GetComponent<Combatant>().IsMovementLocked, "recovery uses common movement lock");
            for (int frame = 0; frame < 20; frame++) yield return null;
            Check(target.Mortal.HP == beforeStrike - 15, "recovery cannot repeat damage");
            target.transform.position = new Vector3(110f, 1f, 97.5f); Physics.SyncTransforms();
            for (int frame = 0; frame < 60 && brain.State != EnemyState.Chase; frame++) yield return null;
            Check(brain.State == EnemyState.Chase, "leaving reach returns to Chase after recovery");
            target.gameObject.SetActive(false);
            yield return null; yield return null;
            Check(brain.State == EnemyState.Idle && brain.Target == null && !brain.Combat.IsBusy, "invalid target clears action and returns Idle");
            target.gameObject.SetActive(true);
            // A lethal rear hit must go directly to Dead and dispose the capsule once.
            var transitions = new List<EnemyState>();
            brain.OnStateChanged += state => transitions.Add(state);
            Mortal enemyHealth = unit.GetComponent<Mortal>();
            int deaths = 0; enemyHealth.OnDead += attacker => deaths++;
            enemyHealth.TakeDamage(999, target.gameObject);
            Check(brain.State == EnemyState.Dead && transitions.SequenceEqual(new[] { EnemyState.Dead }) && deaths == 1,
                "lethal hit enters Dead directly and raises one death");
            Check(!unit.GetComponent<CapsuleCollider>().enabled && !brain.Combat.IsBusy, "Dead clears collision and action");
            yield return null;
            Check(unit == null, "OnDead destroys enemy at end of frame");

            // Prove wall checks apply equally to player adapters and to both melee shapes.
            GameObject caster = new GameObject("Common Melee Caster"); fixtures.Add(caster);
            caster.transform.position = new Vector3(1000f, 1000f, 1000f);
            MeleeAttack melee = caster.AddComponent<MeleeAttack>();
            MeleeAttackData data = Instantiate(AssetDatabase.LoadAssetAtPath<MeleeAttackData>("Assets/Data/Attacks/StrikerMeleeAttack.asset"));
            dataCopies.Add(data); Set(melee, "data", data); Set(melee, "origin", caster.transform);
            PlayerAttackAction playerAction = PlayerActionValidationUtility.For(melee);
            Combatant victim = Target(caster.transform.position + new Vector3(0f, 1f, 1.6f));
            GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(blocker);
            blocker.transform.position = caster.transform.position + new Vector3(0f, 1f, 0.8f);
            blocker.transform.localScale = new Vector3(2f, 2f, 0.1f); Physics.SyncTransforms();
            Check(playerAction.TryStart(caster.transform, caster.transform.position + Vector3.forward), "player adapter accepts MeleeAttack");
            yield return null; yield return null;
            Check(victim.Mortal.HP == 1000, "sector melee cannot damage through world wall");
            Set(data, "shape", MeleeShape.Circle);
            Check(playerAction.TryStart(caster.transform, caster.transform.position + Vector3.forward), "circle melee request accepted");
            yield return null; yield return null;
            Check(victim.Mortal.HP == 1000, "circle melee also cannot damage through wall");
            blocker.GetComponent<Collider>().isTrigger = true; Physics.SyncTransforms();
            Check(playerAction.TryStart(caster.transform, caster.transform.position + Vector3.forward), "trigger-only obstruction request accepted");
            yield return null; yield return null;
            Check(victim.Mortal.HP == 985, "triggers do not occlude melee");
            playerAction.enabled = false;
            EnemyAttackAction generic = caster.AddComponent<EnemyAttackAction>(); Set(generic, "attack", melee);
            Set(generic, "windupTime", 0.15f); Set(generic, "recoveryTime", 0.1f);
            Check(generic.TryStartFor(victim), "enemy adapter executes same MeleeAttack");
            victim.transform.position += Vector3.right * 8f; Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);
            Check(victim.Mortal.HP == 985 && generic.Phase == ActionPhase.Recovery, "dodging committed windup misses and still recovers");
            yield return new WaitForSeconds(0.15f);
            victim.transform.position = caster.transform.position + new Vector3(0f, 1f, 1.6f); Physics.SyncTransforms();
            Check(generic.TryStartFor(victim), "second windup starts");
            int victimHP = victim.Mortal.HP;
            caster.GetComponent<Mortal>().TakeDamage(999, victim.gameObject);
            yield return new WaitForSeconds(0.3f);
            Check(victim.Mortal.HP == victimHP && !generic.IsActive, "death cancels pending strike before damage");

            GameObject ranged = new GameObject("Generic Enemy Ranged"); fixtures.Add(ranged);
            ranged.transform.position = caster.transform.position + Vector3.right * 20f;
            HitscanAttack hitscan = ranged.AddComponent<HitscanAttack>();
            Set(hitscan, "data", AssetDatabase.LoadAssetAtPath<HitscanAttackData>("Assets/Data/Attacks/TempPrimaryFire.asset"));
            Set(hitscan, "origin", ranged.transform);
            EnemyAttackAction rangedAction = ranged.AddComponent<EnemyAttackAction>(); Set(rangedAction, "attack", hitscan);
            Combatant rangedTarget = Target(ranged.transform.position + Vector3.forward * 3f);
            Physics.SyncTransforms();
            Check(rangedAction.TryStartFor(rangedTarget), "generic enemy action accepts hitscan");
            yield return null; yield return null;
            Check(rangedTarget.Mortal.HP == 970, "enemy hitscan applies configured damage");
            ProjectileAttack projectile = ranged.AddComponent<ProjectileAttack>();
            Set(projectile, "data", AssetDatabase.LoadAssetAtPath<ProjectileAttackData>("Assets/Data/Attacks/TempSecondaryFire.asset"));
            Set(projectile, "origin", ranged.transform);
            Set(projectile, "pool", FindFirstObjectByType<ProjectilePool>());
            Set(rangedAction, "attack", projectile);
            Check(rangedAction.TryStartFor(rangedTarget), "generic enemy action accepts projectile attack");
            for (int frame = 0; frame < 90 && rangedTarget.Mortal.HP == 970; frame++) yield return null;
            Check(rangedTarget.Mortal.HP == 920, "enemy projectile carries faction and deals 50 damage");
            rangedAction.enabled = false;
            EnemySustainProbe sustain = ranged.AddComponent<EnemySustainProbe>();
            Set(sustain, "canMove", false); Set(sustain, "recoveryTime", 0.1f);
            Check(sustain.TryStartFor(rangedTarget), "attack-free enemy action starts");
            yield return null; yield return null; yield return null;
            Check(sustain.Phase == ActionPhase.Active && sustain.Ticks > 0 && ranged.GetComponent<Combatant>().IsMovementLocked,
                "attack-free Active action ticks and participates in common locks");
            sustain.Complete();
            Check(sustain.Phase == ActionPhase.Recovery, "Active completion begins recovery");
            yield return new WaitForSeconds(0.15f);
            Check(sustain.Phase == ActionPhase.Ready && !ranged.GetComponent<Combatant>().IsMovementLocked, "recovery releases common lock");
            Check(sustain.TryStartFor(rangedTarget), "attack-free action restarts");
            yield return null; yield return null;
            sustain.enabled = false;
            Check(sustain.Cleaned && !ranged.GetComponent<Combatant>().IsMovementLocked, "disable cleans active action and releases locks");

            // A real player attack must feed the enemy's damage/death events, including with disabled AI.
            GameObject victimUnit = Instantiate(prefab, new Vector3(106f, 0f, 96f), Quaternion.identity);
            fixtures.Add(victimUnit);
            EnemyAI victimBrain = victimUnit.GetComponent<EnemyAI>();
            GameObject shooter = new GameObject("Player Shooting TempEnemy"); fixtures.Add(shooter);
            shooter.transform.position = new Vector3(106f, 1f, 91f);
            HitscanAttack playerShot = shooter.AddComponent<HitscanAttack>();
            shooter.GetComponent<Combatant>().Faction = Faction.Player;
            Set(playerShot, "data", AssetDatabase.LoadAssetAtPath<HitscanAttackData>("Assets/Data/Attacks/TempPrimaryFire.asset"));
            Set(playerShot, "origin", shooter.transform);
            PlayerAttackAction shotAction = PlayerActionValidationUtility.For(playerShot);
            Physics.SyncTransforms();
            Check(shotAction.TryStart(shooter.transform, victimUnit.transform.position + Vector3.up), "player fires at TempEnemy capsule");
            yield return null; yield return null;
            Check(victimUnit.GetComponent<Mortal>().HP == 70 && victimBrain.Target == shooter.GetComponent<Combatant>(),
                "actual rear shot removes 30 HP and acquires its player attacker");
            victimBrain.enabled = false;
            for (int shot = 0; shot < 3; shot++)
            {
                Check(shotAction.TryStart(shooter.transform, victimUnit.transform.position + Vector3.up), "follow-up shot " + shot + " accepted");
                yield return null; yield return null;
            }
            Check(victimUnit == null, "player attacks destroy TempEnemy even when its AI is disabled");
        }

        private GameObject Box(string name, Vector3 position, Vector3 scale)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name; item.transform.SetParent(arena.transform, false);
            item.transform.localPosition = position; item.transform.localScale = scale; return item;
        }
        private Combatant Target(Vector3 position)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Capsule); fixtures.Add(item);
            item.name = "Validation Player"; item.transform.position = position; item.layer = 2;
            Combatant combatant = item.AddComponent<Combatant>(); combatant.Faction = Faction.Player;
            Set(combatant.Mortal, "hp", 1000); return combatant;
        }
        private static void Set(object target, string name, object value) => PlayerActionValidationUtility.Set(target, name, value);
        private void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks.Add(label);
        }
        private void Update()
        {
            if (EditorApplication.timeSinceStartup - started > 120d)
            { Debug.LogError("Enemy validation timed out."); Cleanup(); EnemyValidation.Finish(1); }
        }
        private void Cleanup()
        {
            foreach (GameObject item in fixtures) if (item != null) Destroy(item);
            if (arena != null)
            {
                NavMeshSurface surface = arena.GetComponent<NavMeshSurface>();
                if (surface != null) { surface.RemoveData(); if (surface.navMeshData != null) Destroy(surface.navMeshData); }
                Destroy(arena);
            }
            foreach (ScriptableObject data in dataCopies) if (data != null) Destroy(data);
        }
    }
}
