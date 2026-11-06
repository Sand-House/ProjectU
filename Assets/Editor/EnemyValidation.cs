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
            GameFlowValidationUtility.DisableAutomaticGameFlow();
            EnemyAI[] saved = UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            if (saved.Length != 0 || UnityEngine.Object.FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None).Length != 4)
                throw new InvalidOperationException("The saved stage must contain four spawners and no enemies.");
            EnemyStatData stats = AssetDatabase.LoadAssetAtPath<EnemyStatData>(TempEnemySetup.StatPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TempEnemySetup.PrefabPath);
            if (stats == null || prefab == null || prefab.GetComponent<EnemyStat>().Data != stats)
                throw new InvalidOperationException("TempEnemy must reference its shared stat data.");
            Debug.Log("ENEMY EDIT VALIDATION PASSED: empty stage, four spawners and shared prefab stat data.");
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
            Check(FindObjectsByType<EnemyAI>(FindObjectsSortMode.None).Length == 0, "stage starts without enemies");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TempEnemySetup.PrefabPath);
            Check(prefab != null && prefab.activeSelf, "active TempEnemy prefab exists");
            // Combat regression fixtures are created only by this validation, never saved to the stage.
            Vector3[] demoPositions = { new Vector3(-8f, 0f, 8f), new Vector3(0f, 0f, 11f), new Vector3(8f, 0f, 8f),
                new Vector3(-12f, 0f, -8f), new Vector3(12f, 0f, -8f) };
            EnemyAI[] demo = new EnemyAI[demoPositions.Length];
            for (int index = 0; index < demo.Length; index++)
            {
                GameObject fixture = Instantiate(prefab, demoPositions[index], Quaternion.LookRotation(-demoPositions[index]));
                fixtures.Add(fixture);
                demo[index] = fixture.GetComponent<EnemyAI>();
            }
            Check(FindFirstObjectByType<NavMeshSurface>().navMeshData != null, "saved NavMesh is connected");
            Check(typeof(PlayerAction).BaseType == typeof(ActionBase) && typeof(EnemyAction).BaseType == typeof(ActionBase), "both actor action families share ActionBase");
            foreach (Type state in new[] { typeof(EnemyIdleState), typeof(EnemyChaseState), typeof(EnemyCombatState), typeof(EnemyDeadState) })
                Check(state.BaseType == typeof(EnemyStateBase), state.Name + " derives from EnemyStateBase");
            foreach (EnemyAI enemy in demo)
            {
                Check(enemy.GetComponent<CapsuleCollider>() != null && enemy.GetComponentInChildren<MeshRenderer>() != null,
                    enemy.name + " has capsule collision and mesh");
                Check(enemy.GetComponent<Mortal>().HP == 100 && enemy.GetComponent<Combatant>().Faction == Faction.Enemy, enemy.name + " health and faction");
                EnemyStat stat = enemy.GetComponent<EnemyStat>();
                Check(stat.IsInitialized && stat.MaxHealth == 100 && Mathf.Approximately(stat.MoveSpeed, 3.5f) &&
                    Mathf.Approximately(enemy.GetComponent<NavMeshAgent>().speed, stat.MoveSpeed), enemy.name + " initialized data and navigation speed");
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
            Check(demo.All(enemy => enemy.Motor.IsGrounded), "all combat fixtures are on NavMesh");
            Vector3[] initial = demo.Select(enemy => enemy.transform.position).ToArray();
            for (int frame = 0; frame < 420 && sceneHits == 0; frame++) yield return null;
            Check(sceneHits > 0 && playerHealth.HP < 1000, "scene combat fixtures sense chase and damage actual player");
            Check(demo.Where((enemy, index) => Vector3.Distance(enemy.transform.position, initial[index]) > 1f).Any(), "combat fixtures actually navigate");
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

            IEnumerator statTests = ValidateStats(prefab);
            while (statTests.MoveNext()) yield return statTests.Current;
        }

        private IEnumerator ValidateStats(GameObject prefab)
        {
            EnemyStatData saved = AssetDatabase.LoadAssetAtPath<EnemyStatData>(TempEnemySetup.StatPath);
            Check(saved.MaxHealth == 100 && Mathf.Approximately(saved.MoveSpeed, 3.5f) &&
                saved.AttackMultiplier == 1f && saved.AttackSpeed == 1f, "saved stat asset preserves demo balance");
            EnemyStatData data = Instantiate(saved); dataCopies.Add(data);
            Set(data, "maxHealth", -10); Set(data, "moveSpeed", -1f);
            Set(data, "attackMultiplier", float.NaN); Set(data, "attackSpeed", float.PositiveInfinity);
            Check(data.MaxHealth == 1 && data.MoveSpeed == 0f && data.AttackMultiplier == 0f && data.AttackSpeed == 0.01f,
                "stat data clamps negative and nonfinite values");
            Set(data, "moveSpeed", float.NaN); Set(data, "attackMultiplier", float.PositiveInfinity); Set(data, "attackSpeed", 0f);
            Check(data.MoveSpeed == 0f && data.AttackMultiplier == 0f && data.AttackSpeed == 0.01f,
                "stat data rejects nonfinite speed and zero attack speed");
            Set(data, "maxHealth", 240); Set(data, "moveSpeed", 2f);
            Set(data, "attackMultiplier", 2f); Set(data, "attackSpeed", 2f);

            var group = new GameObject("Stat Initialization Fixtures"); fixtures.Add(group); group.SetActive(false);
            GameObject first = Instantiate(prefab, new Vector3(106f, 0f, 94f), Quaternion.identity, group.transform);
            GameObject second = Instantiate(prefab, new Vector3(110f, 0f, 94f), Quaternion.identity, group.transform);
            foreach (GameObject unit in new[] { first, second })
            {
                Set(unit.GetComponent<EnemyStat>(), "data", data);
                unit.GetComponent<EnemySensor>().enabled = false;
                // Conflicting component values prove the asset is authoritative on activation.
                Set(unit.GetComponent<Mortal>(), "hp", 17);
                unit.GetComponent<NavMeshAgent>().speed = 9f;
            }
            group.SetActive(true);
            EnemyStat stat = first.GetComponent<EnemyStat>();
            Mortal health = first.GetComponent<Mortal>();
            EnemyAI brain = first.GetComponent<EnemyAI>();
            EnemyAttackAction action = first.GetComponent<EnemyAttackAction>();
            NavMeshAgent agent = first.GetComponent<NavMeshAgent>();
            Check(stat.IsInitialized && brain.enabled && stat.MaxHealth == 240 && health.HP == 240,
                "custom health data overrides Mortal before AI activation");
            Check(agent.speed == 2f && action.SpeedMultiplier == 2f && stat.AttackMultiplier == 2f,
                "custom data configures movement and attack multipliers before first request");
            health.TakeDamage(30, null);
            Check(health.HP == 210 && second.GetComponent<Mortal>().HP == 240 && data.MaxHealth == 240,
                "shared data keeps current HP independent per enemy");
            group.SetActive(false); group.SetActive(true);
            stat.enabled = false; stat.enabled = true;
            Check(health.HP == 210 && stat.MaxHealth == 240 && second.GetComponent<Mortal>().HP == 240,
                "object and stat reactivation do not refill health");
            Set(data, "maxHealth", 480); Set(data, "moveSpeed", 1f);
            Set(data, "attackMultiplier", 3f); Set(data, "attackSpeed", 4f);
            Check(stat.MaxHealth == 240 && stat.MoveSpeed == 2f && stat.AttackMultiplier == 2f && stat.AttackSpeed == 2f,
                "live units keep their spawn snapshot when shared data changes");
            var laterGroup = new GameObject("Later Stat Fixture"); fixtures.Add(laterGroup); laterGroup.SetActive(false);
            GameObject later = Instantiate(prefab, new Vector3(110f, 0f, 98f), Quaternion.identity, laterGroup.transform);
            Set(later.GetComponent<EnemyStat>(), "data", data);
            later.GetComponent<EnemySensor>().enabled = false;
            laterGroup.SetActive(true);
            Check(later.GetComponent<Mortal>().HP == 480 && later.GetComponent<NavMeshAgent>().speed == 1f &&
                later.GetComponent<EnemyAttackAction>().SpeedMultiplier == 4f, "later spawns use the changed data");

            yield return null;
            Vector3 start = first.transform.position;
            brain.Motor.Chase(start + Vector3.forward * 5f);
            float maximumVelocity = 0f;
            for (int frame = 0; frame < 60; frame++)
            {
                yield return null;
                maximumVelocity = Mathf.Max(maximumVelocity, agent.velocity.magnitude);
            }
            Check(Vector3.Distance(start, first.transform.position) > 0.5f && maximumVelocity > 1f && maximumVelocity <= 2.05f,
                "NavMesh actually moves within the data speed limit");
            brain.Motor.Stop();
            first.transform.rotation = Quaternion.identity;
            Combatant target = Target(first.transform.position + new Vector3(0f, 1f, 1.5f));
            Physics.SyncTransforms();
            Set(action, "damageMultiplier", 1.5f);
            int before = target.Mortal.HP;
            double acceptedAt = Time.timeAsDouble;
            Check(action.TryStartFor(target), "custom stat attack starts");
            while (Time.timeAsDouble - acceptedAt < 0.15d) yield return null;
            Check(target.Mortal.HP == before && action.Phase == ActionPhase.Windup, "scaled windup still delays damage");
            for (int frame = 0; frame < 60 && target.Mortal.HP == before; frame++) yield return null;
            double elapsed = Time.timeAsDouble - acceptedAt;
            Check(target.Mortal.HP == before - 45, "damage combines AttackData 15, stat 2 and action 1.5 multipliers");
            Check(elapsed >= 0.24d && elapsed < 0.35d, "attack speed halves 0.5 second windup");
            Check(action.Phase == ActionPhase.Recovery && action.RecoveryRemaining > 0.3f && action.RecoveryRemaining <= 0.401f,
                "attack speed halves 0.8 second recovery");
            Check(action.CooldownRemaining > 0.5f && action.CooldownRemaining <= 0.601f,
                "attack speed halves 1.2 second cooldown");
            yield return new WaitForSeconds(0.65f);
            Check(target.Mortal.HP == before - 45 && action.Phase == ActionPhase.Ready,
                "scaled recovery completes without duplicate damage");
            int deaths = 0; health.OnDead += source => deaths++;
            health.TakeDamage(999, null);
            Check(health.HP == 0 && health.isDead && deaths == 1 && brain.State == EnemyState.Dead,
                "data initialized health retains death state and event behavior");
            yield return null;
            Check(first == null && second.GetComponent<Mortal>().HP == 240, "one shared-data enemy dies without changing another");
            Check(saved.MaxHealth == 100 && saved.MoveSpeed == 3.5f && saved.AttackMultiplier == 1f && saved.AttackSpeed == 1f,
                "stat tests never mutate the saved data asset");
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
