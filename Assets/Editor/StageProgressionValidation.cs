using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class StageProgressionValidation
    {
        private const string Running = "ProjectU.StageProgressionValidation";
        internal const string Automatic = "ProjectU.StageProgressionValidation.Automatic";
        static StageProgressionValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Stage Progression Validation").AddComponent<StageProgressionValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Stage Progression")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            GameManager game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
            if (game == null || game.StageRunner == null || game.Stages.Count == 0 || game.Stages.Any(stage => stage == null))
                throw new InvalidOperationException("Saved game flow references are incomplete.");
            if (UnityEngine.Object.FindFirstObjectByType<StageFlowUI>() == null)
                throw new InvalidOperationException("Saved dummy reward UI is missing.");
            SessionState.SetBool(Automatic, false);
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        public static void RunSavedScene()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            SessionState.SetBool(Automatic, true);
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        public static void CompilePlayerScripts()
        {
            const string path = "Logs/DistributedStageSubscribers_20261106/PlayerScripts";
            System.IO.Directory.CreateDirectory(path);
            var settings = new ScriptCompilationSettings
            {
                group = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
                options = ScriptCompilationOptions.None
            };
            ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(settings, path);
            if (result.assemblies == null || !result.assemblies.Any(path => System.IO.Path.GetFileName(path) == "Assembly-CSharp.dll") ||
                !result.assemblies.Any(path => System.IO.Path.GetFileName(path) == "AYellowpaper.SerializedCollections.dll"))
                throw new InvalidOperationException("Player scripts did not compile.");
            Debug.Log("STAGE PLAYER SCRIPT COMPILATION PASSED: " + result.assemblies.Count + " assemblies.");
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

    public sealed class StageProgressionValidationDriver : MonoBehaviour
    {
        private readonly List<StageData> fixtures = new List<StageData>();
        private GameManager game;
        private StageRunner runner;
        private PlayerMortal player;
        private Mortal health;
        private StageFlowUI ui;
        private StageFlowInputProbe probe;
        private int checks;
        private double deadline;

        private IEnumerator Start()
        {
            deadline = EditorApplication.timeSinceStartup + 100d;
            Time.captureDeltaTime = 1f / 60f;
            IEnumerator tests = SessionState.GetBool(StageProgressionValidation.Automatic, false) ? ValidateSavedScene() : Validate();
            while (true)
            {
                bool more; object current = null;
                try
                {
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Stage validation timed out.");
                    more = tests.MoveNext();
                    if (more) current = tests.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Cleanup(); StageProgressionValidation.Finish(1); yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("STAGE PROGRESSION VALIDATION PASSED: " + checks + " checks.");
            Cleanup(); StageProgressionValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            game = FindFirstObjectByType<GameManager>();
            runner = game.StageRunner;
            player = FindFirstObjectByType<PlayerMortal>();
            health = player.GetComponent<Mortal>();
            ui = FindFirstObjectByType<StageFlowUI>();
            probe = gameObject.AddComponent<StageFlowInputProbe>();
            probe.Reader = player.GetComponent<PlayerInputReader>();
            var observedStates = new List<StageState>();
            bool statesMatchEvents = true;
            Action<StageState> stateListener = state => { observedStates.Add(state); statesMatchEvents &= game.State == state; };
            game.OnStageStateChanged += stateListener;
            int damageEvents = 0;
            Action<int, GameObject> damageListener = (amount, attacker) => damageEvents++;
            health.OnDamaged += damageListener;
            Check(health.IsInvincible, "initial BeforeStage protection is applied without a test override");
            Check(player.GetComponents<Mortal>().Length == 1 && health is PlayerMortal,
                "saved player has exactly one Mortal instance using the PlayerMortal subtype");
            var stageHandlers = ((Delegate)typeof(GameManager).GetField("OnStageStateChanged", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game)).GetInvocationList();
            var stageTargets = stageHandlers.Select(handler => handler.Target).ToArray();
            Check(stageTargets.Contains(player.GetComponent<PlayerController>()) && stageTargets.Contains(player.GetComponent<PlayerMotor>()) &&
                stageTargets.Contains(player.GetComponent<PlayerCombat>()) && stageTargets.Contains(player.GetComponent<PlayerAnimationBase>()) &&
                stageTargets.Contains(player.GetComponent<PlayerInputReader>()) && stageTargets.Contains(player),
                "player components subscribe their own handlers directly to the stage event");
            Check(player.GetComponents<PlayerActionBase>().All(action => stageTargets.Contains(action)),
                "each player action owns its own stage reset subscription");
            int protectedHP = health.HP;
            health.TakeDamage(int.MaxValue, null);
            Check(health.HP == protectedHP && !health.isDead && damageEvents == 0, "BeforeStage ignores lethal damage and damage notifications");
            game.enabled = false;
            Check(!health.IsInvincible, "disabled manager releases its stage protection to the original saved value");
            game.enabled = true;
            Check(health.IsInvincible && observedStates.Last() == StageState.BeforeStage,
                "manager reenable publishes current stage and restores preparation protection");
            Check(Enum.GetNames(typeof(StageState)).SequenceEqual(new[] { "BeforeStage", "OnStage", "AfterStage" }), "only three game phases exist");
            Check(game.State == StageState.BeforeStage && runner.RemainingEnemyCount == 0, "manual validation starts idle");
            Check(runner.SpawnManager != null && player != null, "saved runner and player references");
            Check(FindObjectsByType<EnemyAI>(FindObjectsSortMode.None).Length == 0, "no saved enemies");
            StageData source = game.Stages[0];
            string serializedSource = EditorJsonUtility.ToJson(source);
            yield return null;
            for (int frame = 0; frame < 8; frame++) yield return null;
            Check(runner.RemainingEnemyCount == 0 && game.State == StageState.BeforeStage, "waiting never spawns the first wave automatically");
            Vector3 startPosition = player.transform.position;
            probe.Move = Vector2.right;
            for (int frame = 0; frame < 8; frame++) yield return null;
            probe.Move = Vector2.zero;
            Check(Vector3.Distance(startPosition, player.transform.position) > 0.25f, "real Controller and Motor allow movement in BeforeStage");
            Capture("BeforeStage");
            var disabledUIObject = new GameObject("Disabled UI Fixture");
            disabledUIObject.SetActive(false);
            var disabledCanvas = Instantiate(ui.Canvas.gameObject);
            disabledCanvas.transform.SetParent(disabledUIObject.transform, false);
            var disabledUI = disabledUIObject.AddComponent<StageFlowUI>();
            EditorUtility.CopySerialized(ui, disabledUI);
            var disabledView = new SerializedObject(disabledUI);
            var viewField = disabledView.GetIterator();
            while (viewField.NextVisible(true))
            {
                if (viewField.propertyType != SerializedPropertyType.ObjectReference) continue;
                var original = viewField.objectReferenceValue;
                var originalTransform = original is GameObject node ? node.transform : (original as Component)?.transform;
                if (originalTransform == null || !originalTransform.IsChildOf(ui.Canvas.transform)) continue;
                string path = AnimationUtility.CalculateTransformPath(originalTransform, ui.Canvas.transform);
                var copy = path.Length == 0 ? disabledCanvas.transform : disabledCanvas.transform.Find(path);
                viewField.objectReferenceValue = original is GameObject ? (UnityEngine.Object)copy.gameObject : copy.GetComponent(original.GetType());
            }
            disabledView.ApplyModifiedPropertiesWithoutUndo();
            disabledUI.enabled = false;
            disabledUIObject.SetActive(true);
            Check(disabledUI.Canvas != null && !disabledUI.Canvas.gameObject.activeInHierarchy,
                "presenter disabled before Awake hides its assigned scene Canvas");
            Destroy(disabledUIObject);

            StageData waveStage = Stage(new WaveData(1, Group(2), Group(2)), new WaveData(0, Group(2)), new WaveData(999, Group(1)));
            int cleared = 0, countNotifications = 0;
            Action clearListener = () => cleared++;
            Action<int> countListener = value => countNotifications++;
            runner.OnStageCleared += clearListener;
            runner.OnRemainingEnemyCountChanged += countListener;
            Check(runner.TryBeginStage(waveStage), "begin first wave through actual spawner");
            Check(runner.CurrentWaveIndex == 0 && runner.RemainingEnemyCount == 4, "entire first batch is tracked");
            EnemyAI[] first = Enemies();
            foreach (EnemyAI enemy in first) enemy.enabled = false;
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(runner.RemainingEnemyCount == 4 && runner.CurrentWaveIndex == 0, "temporary AI disable preserves survivors");
            Check(!runner.TryBeginStage(source), "running stage cannot be replaced");
            first[0].GetComponent<Mortal>().Dead(player.gameObject);
            first[0].GetComponent<Mortal>().Dead(player.gameObject);
            Check(runner.RemainingEnemyCount == 3, "duplicate death removes the victim once rather than the attacker");
            first[1].GetComponent<Mortal>().Dead();
            first[2].GetComponent<Mortal>().Dead();
            Check(runner.CurrentWaveIndex == 0 && runner.RemainingEnemyCount == 1, "death callbacks do not spawn reentrantly");
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(runner.CurrentWaveIndex == 1 && runner.RemainingEnemyCount == 3, "threshold equality starts next wave including carryover");
            foreach (EnemyAI enemy in Enemies()) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(runner.CurrentWaveIndex == 2 && runner.RemainingEnemyCount == 1, "zero threshold advances with no survivors");
            Check(cleared == 0 && runner.IsRunning && !runner.HasPendingWave, "final wave ignores its threshold and waits for zero");
            EnemyAI final = Enemies().Single();
            final.gameObject.SetActive(false);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(runner.RemainingEnemyCount == 1, "inactive living object remains tracked");
            final.gameObject.SetActive(true);
            Destroy(final.gameObject);
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(runner.IsComplete && cleared == 1 && runner.RemainingEnemyCount == 0, "external destruction clears final wave once");
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(cleared == 1 && countNotifications >= 4, "clear is idempotent and count changes are published");

            EnemySpawner[] spawners = runner.SpawnManager.Spawners.ToArray();
            foreach (EnemySpawner spawner in spawners) spawner.enabled = false;
            Check(!runner.TryBeginStage(waveStage), "missing active spawners fail the initial wave");
            Check(runner.HasPendingWave && runner.CurrentWaveIndex == -1 && runner.RemainingEnemyCount == 0,
                "failed initial wave leaves index and count untouched");
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(runner.HasPendingWave && !runner.TryRetrySpawn(), "no automatic retry and explicit failure remains stable");
            foreach (EnemySpawner spawner in spawners) spawner.enabled = true;
            Check(runner.TryRetrySpawn() && runner.RemainingEnemyCount == 4, "explicit retry commits the pending initial wave");
            foreach (EnemySpawner spawner in spawners) spawner.enabled = false;
            foreach (EnemyAI enemy in Enemies().Take(3)) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(runner.HasPendingWave && runner.CurrentWaveIndex == 0 && runner.RemainingEnemyCount == 1,
                "failed later wave preserves prior wave index and survivor");
            foreach (EnemySpawner spawner in spawners) spawner.enabled = true;
            Check(runner.TryRetrySpawn() && runner.CurrentWaveIndex == 1 && runner.RemainingEnemyCount == 3,
                "later retry adds enemies without losing carryover");
            runner.AbortStage();
            Check(!runner.IsRunning && !runner.IsComplete && runner.RemainingEnemyCount == 0 && cleared == 1, "abort cleans ownership without clearing a stage");
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(Enemies().Length == 0, "abort destroys only its owned surviving objects");
            runner.OnStageCleared -= clearListener;
            runner.OnRemainingEnemyCountChanged -= countListener;

            StageData oneWave = Stage(new WaveData(0, Group(1)));
            ConfigureGame(oneWave, oneWave);
            int resets = 0, rewards = 0, ultimateGauge = 75;
            bool resetsObserveCleanup = true;
            Action<StageState> resetListener = state =>
            {
                if (state != StageState.AfterStage) return;
                resets++; ultimateGauge = 0;
                resetsObserveCleanup &= game.State == StageState.AfterStage &&
                    FindObjectsByType<ProjectilePool>(FindObjectsSortMode.None).All(value => value.ActiveCount == 0);
            };
            Action<long> rewardListener = id => rewards++;
            game.OnStageStateChanged += resetListener;
            game.OnRewardRequested += rewardListener;
            Time.timeScale = 0.5f;
            Check(game.TryStartStage(), "game starts first configured stage");
            Check(game.CurrentStageIndex == 0 && game.State == StageState.OnStage, "game owns stage progression state");
            Check(!health.IsInvincible && observedStates.Last() == StageState.OnStage, "OnStage event removes preparation invincibility");
            health.TakeDamage(12, null);
            Check(health.HP == protectedHP - 12 && damageEvents == 1, "OnStage receives actual damage and publishes damage once");
            Check(health.TryRestoreHealth(player.GetComponent<PlayerStat>().MaxHealth), "damage probe restores the living player's fixture HP");
            Check(!game.TryStartStage(), "duplicate game start is rejected");
            foreach (EnemyAI enemy in Enemies()) enemy.enabled = false;
            PlayerCombat combat = player.GetComponent<PlayerCombat>();
            ActionBase grenade = combat.GetAction(PlayerActionSlot.Skill1);
            Check(combat.TryStartAction(PlayerActionSlot.Skill1), "real grenade action accepted before stage clear");
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(grenade.CooldownRemaining > 0f, "real action has an earned cooldown");
            for (int frame = 0; frame < 130 && grenade.IsActive; frame++) yield return null;
            Check(!grenade.IsActive, "grenade recovery completes before next action");
            Check(combat.TryStartAction(PlayerActionSlot.PrimaryFire), "primary fire consumes real ammo");
            for (int frame = 0; frame < 3; frame++) yield return null;
            int ammo = combat.CurrentAmmo;
            Check(ammo == combat.MaxAmmo - 1, "primary shot reduced magazine");
            for (int frame = 0; frame < 40 && combat.GetAction(PlayerActionSlot.PrimaryFire).IsActive; frame++) yield return null;
            Check(combat.TryReload() && combat.IsReloading, "reload is active when stage clears");
            ProjectilePool pool = FindFirstObjectByType<ProjectilePool>();
            ProjectileBase prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectiles/Grenade.prefab").GetComponent<ProjectileBase>();
            Check(pool.Spawn(prefab, player.gameObject, new Vector3(0f, 8f, 0f), Vector3.up) != null, "hazard exists before stage cleanup");
            health.IsInvincible = false;
            health.TakeDamage(37, null);
            Check(health.HP == player.GetComponent<PlayerStat>().MaxHealth - 37, "player carries damage during stage");
            foreach (EnemyAI enemy in Enemies()) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.State == StageState.AfterStage && game.CurrentStageIndex == 0, "stage clear enters reward wait");
            Check(health.IsInvincible && observedStates.Last() == StageState.AfterStage, "AfterStage event restores player invincibility");
            protectedHP = health.HP;
            int damagesBeforeReward = damageEvents;
            health.TakeDamage(int.MaxValue, null);
            Check(health.HP == protectedHP && damageEvents == damagesBeforeReward && !health.isDead,
                "AfterStage reward modal rejects lethal damage");
            int resetsBeforeReenable = resets;
            int eventsBeforeReenable = observedStates.Count;
            player.enabled = false;
            player.enabled = true;
            Check(health.IsInvincible && player.GetComponent<Combatant>().IsGameplayBlocked,
                "reenabled player policy synchronizes AfterStage protection and modal input gate");
            Check(resets == resetsBeforeReenable && observedStates.Count == eventsBeforeReenable,
                "policy reenable neither heals again nor fabricates a stage transition");
            Check(resets == 1 && rewards == 1 && ultimateGauge == 0, "reset and future gauge extension run before one reward request");
            Check(health.HP == player.GetComponent<PlayerStat>().MaxHealth, "stage clear restores current maximum HP");
            Check(grenade.CooldownRemaining == 0f && grenade.Phase == ActionPhase.Ready, "stage clear resets cooldown and locks");
            Check(!combat.IsReloading && combat.CurrentAmmo == ammo, "reset cancels reload without refilling ammo");
            Check(pool.ActiveCount == 0 && Time.timeScale == 0.5f, "projectiles returned while reward preserves world time");
            Check(player.GetComponent<Combatant>().IsGameplayBlocked && !grenade.TryStart(), "gameplay gate rejects direct actions during reward");
            Check(ui.IsRewardVisible && ui.RewardButton.interactable, "actual dummy reward button is visible and selectable");
            Check(player.GetComponent<PlayerController>().BufferedAction == PlayerActionSlot.None &&
                !player.GetComponent<PlayerMotor>().IsDashing, "modal clears buffered input and dash movement");
            Capture("AfterStage-Reward");
            startPosition = player.transform.position;
            probe.Move = Vector2.right;
            float rewardWorldTime = Time.time;
            for (int frame = 0; frame < 8; frame++) yield return null;
            Check(Vector3.Distance(startPosition, player.transform.position) < 0.05f && Time.time > rewardWorldTime,
                "reward blocks player movement while world time continues");
            probe.Move = Vector2.zero;
            PlayerInputReader reader = player.GetComponent<PlayerInputReader>();
            reader.enabled = false;
            reader.enabled = true;
            Check(Cursor.lockState == CursorLockMode.None, "reader reactivation cannot capture cursor during reward");
            long firstRewardId = game.PendingRewardId;
            for (int frame = 0; frame < 5; frame++) yield return null;
            Check(rewards == 1 && game.CurrentStageIndex == 0, "reward wait never advances on elapsed time");
            Check(!game.CompleteReward(firstRewardId + 1), "wrong reward completion token rejected");
            ClickReward();
            Check(!ui.RewardButton.interactable && !game.CompleteReward(firstRewardId), "real UI submits reward completion only once");
            Check(game.CurrentStageIndex == 0, "reward callback queues rather than recursively starts the stage");
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.State == StageState.BeforeStage && game.CurrentStageIndex == 1 && runner.RemainingEnemyCount == 0,
                "reward completion prepares next stage without spawning");
            Check(Time.timeScale == 0.5f && !player.GetComponent<Combatant>().IsGameplayBlocked, "resume restores previous time scale and gameplay gate");
            Check(health.IsInvincible && observedStates.Last() == StageState.BeforeStage, "reward completion keeps BeforeStage invulnerable");
            Check(!ui.IsRewardVisible && (bool)typeof(PlayerInputReader).GetField("suppressPrimaryFire", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reader),
                "reward closes and its selection click cannot also shoot");
            Check(!game.CompleteReward(firstRewardId), "prior-stage completion cannot affect the next stage");
            for (int frame = 0; frame < 8; frame++) yield return null;
            Check(runner.RemainingEnemyCount == 0, "next stage waits indefinitely for a fresh start interaction");
            probe.PlaceNearStarter();
            yield return probe.HoldInteract(probe.StartButton.HoldDuration);
            Check(game.State == StageState.OnStage && runner.RemainingEnemyCount == 1, "central hold starts the prepared next stage");
            foreach (EnemyAI enemy in Enemies()) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.State == StageState.AfterStage && game.PendingRewardId != firstRewardId, "last stage also has its own reward request");
            Check(!game.CompleteReward(firstRewardId), "stale reward token cannot finish a newer request");
            long canceledReward = game.PendingRewardId;
            ui.enabled = false;
            Check(!game.IsChoosingReward && game.State == StageState.AfterStage && !player.GetComponent<Combatant>().IsGameplayBlocked,
                "presenter teardown releases input and preserves AfterStage");
            startPosition = player.transform.position;
            probe.Move = Vector2.right;
            for (int frame = 0; frame < 8; frame++) yield return null;
            probe.Move = Vector2.zero;
            Check(Vector3.Distance(startPosition, player.transform.position) > 0.1f, "real movement is allowed in AfterStage outside reward selection");
            Check(health.IsInvincible, "AfterStage protection remains active outside the reward modal");
            protectedHP = health.HP;
            health.TakeDamage(20, null);
            Check(health.HP == protectedHP, "freely moving AfterStage player ignores actual damage");
            Check(!game.CompleteReward(canceledReward), "canceled reward token is rejected");
            ui.enabled = true;
            probe.Interact = true;
            yield return null;
            probe.Interact = false;
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(ui.IsRewardVisible && game.IsChoosingReward && game.PendingRewardId != canceledReward, "AfterStage interaction reopens reward with a fresh token");
            ClickReward();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.Result == GameResult.Won && game.State == StageState.BeforeStage && resets == 2 && rewards == 3,
                "last reward returns BeforeStage and records final victory once");
            Check(!player.GetComponent<Combatant>().IsGameplayBlocked && !game.TryStartStage(), "victory allows free movement but cannot start beyond configured stages");
            Check(health.IsInvincible, "final victory retains BeforeStage protection");
            Check(resetsObserveCleanup, "stage observers receive AfterStage after world projectile cleanup");
            game.StopGame();
            Check(game.Result == GameResult.Stopped && Time.timeScale == 0.5f && runner.RemainingEnemyCount == 0, "stop releases suspension and clears stage ownership");
            Check(!game.CompleteReward(firstRewardId), "stopped run rejects old rewards");

            ConfigureGame(oneWave);
            Check(game.TryStartStage(), "placeholder reward game starts");
            foreach (EnemyAI enemy in Enemies()) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 7; frame++) yield return null;
            Check(game.State == StageState.AfterStage && game.Result == GameResult.None && ui.IsRewardVisible,
                "dummy reward never completes itself on elapsed time");
            long interruptedReward = game.PendingRewardId;
            game.StopGame();
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(!ui.IsRewardVisible && !game.CompleteReward(interruptedReward), "stop closes dummy UI and invalidates its token");

            foreach (EnemySpawner spawner in spawners) spawner.enabled = false;
            int eventsBeforeFailure = observedStates.Count;
            Check(!game.TryStartStage() && game.IsSpawnFailed, "game reports initial spawn failure");
            Check(health.IsInvincible && observedStates.Count == eventsBeforeFailure, "failed initial spawn preserves protection without emitting a fake phase change");
            Check(Time.timeScale == 0.5f && runner.CurrentWaveIndex == -1 && game.State == StageState.BeforeStage &&
                !player.GetComponent<Combatant>().IsGameplayBlocked, "initial spawn failure preserves free BeforeStage movement");
            Check(!game.TryStartStage(), "pending first wave requires explicit retry rather than duplicate start");
            foreach (EnemySpawner spawner in spawners) spawner.enabled = true;
            probe.PlaceNearStarter();
            yield return null;
            Check(!probe.StartButton.IsAvailable, "pending first-wave failure releases G for the retry path");
            probe.Interact = true;
            yield return null;
            yield return null;
            Check(game.State == StageState.OnStage && Time.timeScale == 0.5f, "a fresh G press retries the pending first wave");
            Check(!health.IsInvincible, "successful first-wave retry removes protection through the OnStage event");
            for (int frame = 0; frame < 3; frame++) yield return null;
            ActionBase skill = combat.GetAction(PlayerActionSlot.Skill2);
            Check(combat.TryStartAction(PlayerActionSlot.Skill2) && skill.Phase == ActionPhase.Windup, "pending action exists before manual stop");
            game.StopGame();
            Check(skill.Phase == ActionPhase.Ready && combat.GetComponent<PlayerController>().BufferedAction == PlayerActionSlot.None,
                "manual stop clears accepted actions and buffered input");
            for (int frame = 0; frame < 3; frame++) yield return null;

            Action<int> stopOnWave = index => game.StopGame();
            runner.OnWaveStarted += stopOnWave;
            Check(!game.TryStartStage() && game.Result == GameResult.Stopped && runner.RemainingEnemyCount == 0,
                "stop from a wave listener cannot resurrect the run");
            runner.OnWaveStarted -= stopOnWave;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Action<GameResult> stopOnResult = result => { if (result == GameResult.None) game.StopGame(); };
            game.OnResultChanged += stopOnResult;
            Check(!game.TryStartStage() && game.Result == GameResult.Stopped && runner.RemainingEnemyCount == 0,
                "stop from result listener cannot start a new stage reentrantly");
            game.OnResultChanged -= stopOnResult;
            Action<StageState> stopOnState = state => { if (state == StageState.OnStage) game.StopGame(); };
            game.OnStageStateChanged += stopOnState;
            Check(!game.TryStartStage() && game.Result == GameResult.Stopped && runner.RemainingEnemyCount == 0,
                "stop from phase listener cannot report a successful start");
            game.OnStageStateChanged -= stopOnState;
            for (int frame = 0; frame < 3; frame++) yield return null;

            ConfigureGame(Stage(new WaveData(0, Group(1)), new WaveData(0, Group(1))));
            Check(game.TryStartStage(), "later-wave failure fixture starts");
            foreach (EnemySpawner spawner in spawners) spawner.enabled = false;
            foreach (EnemyAI enemy in Enemies()) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.State == StageState.OnStage && game.IsSpawnFailed && runner.CurrentWaveIndex == 0,
                "later-wave failure keeps OnStage and the committed wave index");
            Check(Time.timeScale == 0f && player.GetComponent<Combatant>().IsGameplayBlocked,
                "later-wave failure suspends existing combat for explicit recovery");
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.IsSpawnFailed && !runner.IsComplete, "empty survivors during failed spawn cannot clear or auto-retry");
            foreach (EnemySpawner spawner in spawners) spawner.enabled = true;
            int eventsBeforeRetry = observedStates.Count;
            Check(game.TryRetrySpawn() && runner.CurrentWaveIndex == 1 && runner.RemainingEnemyCount == 1 && Time.timeScale == 0.5f,
                "later-wave retry restores previous time and commits pending wave");
            Check(!health.IsInvincible && observedStates.Count == eventsBeforeRetry, "same-stage retry restores input without publishing a new stage phase");
            Check(!player.GetComponent<Combatant>().IsGameplayBlocked, "successful later-wave retry restores gameplay input");
            game.StopGame();
            for (int frame = 0; frame < 3; frame++) yield return null;
            ConfigureGame(oneWave);

            int rewardsBeforeResetStop = rewards;
            Action<StageState> stopOnReset = state => { if (state == StageState.AfterStage) game.StopGame(); };
            game.OnStageStateChanged += stopOnReset;
            Check(game.TryStartStage(), "reset-listener stop fixture starts");
            foreach (EnemyAI enemy in Enemies()) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            game.OnStageStateChanged -= stopOnReset;
            Check(game.Result == GameResult.Stopped && game.State == StageState.BeforeStage && !game.IsChoosingReward &&
                rewards == rewardsBeforeResetStop && runner.RemainingEnemyCount == 0,
                "stop inside subscribed reset cancels superseded AfterStage and grants no reward");

            Check(game.TryStartStage(), "disabled-action subscription fixture starts");
            foreach (EnemyAI enemy in Enemies()) enemy.enabled = false;
            Check(combat.TryStartAction(PlayerActionSlot.Skill1), "fixture earns a real grenade cooldown");
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(grenade.CooldownRemaining > 0f, "cooldown exists before action unsubscribe");
            PlayerAnimationBase animation = player.GetComponent<PlayerAnimationBase>();
            animation.enabled = false;
            grenade.enabled = false;
            foreach (EnemyAI enemy in Enemies()) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(grenade.CooldownRemaining > 0f && game.State == StageState.AfterStage,
                "disabled action receives no hidden aggregate reset");
            grenade.enabled = true;
            Check(grenade.CooldownRemaining == 0f && grenade.Phase == ActionPhase.Ready,
                "reenabled action catches up its missed clear exactly once");
            Check(game.CancelRewardSelection(game.PendingRewardId), "fixture releases reward input gate");
            for (int frame = 0; frame < 2; frame++) yield return null;
            Check(combat.TryStartAction(PlayerActionSlot.Skill1), "free AfterStage can earn a new cooldown");
            for (int frame = 0; frame < 3; frame++) yield return null;
            grenade.enabled = false;
            grenade.enabled = true;
            Check(grenade.CooldownRemaining > 0f, "same-clear action reenable preserves newly earned cooldown");
            Check(combat.TryReload() && combat.IsReloading, "live reload starts while animation has a missed clear");
            animation.enabled = true;
            for (int frame = 0; frame < 12; frame++) yield return null;
            Animator animator = player.GetComponentInChildren<Animator>();
            int reloadState = Animator.StringToHash("Reloading");
            Check(animator.GetCurrentAnimatorStateInfo(1).shortNameHash == reloadState ||
                animator.GetNextAnimatorStateInfo(1).shortNameHash == reloadState,
                "animation catches up its missed clear before restoring the newer live reload visual");
            game.StopGame();
            for (int frame = 0; frame < 3; frame++) yield return null;

            int rewardsBeforeDeath = rewards, resetsBeforeDeath = resets;
            Check(game.TryStartStage(), "death-priority fixture starts");
            foreach (EnemyAI enemy in Enemies()) enemy.GetComponent<Mortal>().Dead();
            health.Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.Result == GameResult.Lost && rewards == rewardsBeforeDeath && resets == resetsBeforeDeath,
                "same-frame player death wins over clear and grants no recovery or reward");
            Check(health.isDead && health.HP == 0 && !health.TryRestoreHealth(100), "stage health recovery cannot revive player");
            Check(!game.TryStartStage() && !game.TryRetrySpawn(), "dead player cannot restart or retry");
            game.enabled = false;
            Check(Time.timeScale == 0.5f && game.Result == GameResult.Stopped, "manager disable releases its time-scale suspension");
            Check(EditorJsonUtility.ToJson(source) == serializedSource, "runtime progression preserves shared stage asset data");
            Check(statesMatchEvents && observedStates.Contains(StageState.BeforeStage) && observedStates.Contains(StageState.OnStage) &&
                observedStates.Contains(StageState.AfterStage), "stage event always reports the actual published phase");
            game.OnStageStateChanged -= resetListener;
            game.OnRewardRequested -= rewardListener;
            game.OnStageStateChanged -= stateListener;
            health.OnDamaged -= damageListener;
        }

        private IEnumerator ValidateSavedScene()
        {
            game = FindFirstObjectByType<GameManager>();
            runner = game.StageRunner;
            player = FindFirstObjectByType<PlayerMortal>();
            health = player.GetComponent<Mortal>();
            ui = FindFirstObjectByType<StageFlowUI>();
            probe = gameObject.AddComponent<StageFlowInputProbe>();
            probe.Reader = player.GetComponent<PlayerInputReader>();
            health.IsInvincible = true;
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.State == StageState.BeforeStage && game.CurrentStageIndex == 0 && runner.RemainingEnemyCount == 0,
                "saved SampleScene waits for interaction with no enemies");
            Check(ui != null && !ui.IsRewardVisible && !player.GetComponent<Combatant>().IsGameplayBlocked, "saved UI leaves preparation freely controllable");
            InputAction interaction = (InputAction)typeof(PlayerInputReader).GetField("interact", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(probe.Reader);
            Check(interaction.bindings.Any(binding => binding.path == "<Keyboard>/g"), "start interaction is bound to G without changing skill keys");
            Check(!typeof(StageFlowUI).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(field => field.FieldType == typeof(PlayerInputReader) || field.FieldType == typeof(PlayerCombat)),
                "stage presenter has no player input or combat dependency");
            Check(new SerializedObject(ui).FindProperty("input") == null,
                "stage presenter exposes no saved player input reference");
            PlayerController controller = player.GetComponent<PlayerController>();
            Check(new SerializedObject(controller).FindProperty("game") == null,
                "controller needs no new Inspector game binding");
            GameStarter starter = FindFirstObjectByType<GameStarter>();
            ButtonTrigger button = probe.StartButton;
            PlayerInteraction playerInteraction = player.GetComponent<PlayerInteraction>();
            PlayerHUD hud = player.GetComponent<PlayerHUD>();
            var prompt = hud.Canvas.GetComponentInChildren<InteractionPrompt>().GetComponent<UnityEngine.UI.Text>();
            Check(starter.transform.position == Vector3.zero && starter.GetComponentsInChildren<MeshRenderer>().Length > 0 &&
                button.TriggerRange.isTrigger && button.InputMode == ButtonTriggerInputMode.Hold && button.HoldDuration == 1f,
                "scene saves a central mesh and a one-second hold range");
            Check(button.OnTriggered.GetPersistentEventCount() == 1 && button.OnTriggered.GetPersistentTarget(0) == starter &&
                button.OnTriggered.GetPersistentMethodName(0) == nameof(GameStarter.StartGame),
                "the saved button event targets the central GameStarter");
            Check(!button.Contains(playerInteraction.InteractionPosition) && !prompt.enabled,
                "the player spawns outside the range and the central prompt is hidden");
            yield return probe.HoldInteract(button.HoldDuration);
            Check(game.State == StageState.BeforeStage && runner.RemainingEnemyCount == 0,
                "G held outside the range cannot start combat");
            probe.Move = Vector2.up;
            for (int frame = 0; frame < 90 && !button.Contains(playerInteraction.InteractionPosition); frame++) yield return null;
            probe.Move = Vector2.zero;
            yield return null;
            yield return null;
            Check(button.Contains(playerInteraction.InteractionPosition) && playerInteraction.CurrentButton == button &&
                prompt.enabled && prompt.text.Contains("길게 누르기"), "walking into range shows the hold instructions");
            Check(game.State == StageState.BeforeStage && runner.RemainingEnemyCount == 0,
                "range entry does not start combat");
            Check(!hud.Canvas.transform.Find("Crosshair").gameObject.activeSelf && !prompt.raycastTarget,
                "the center prompt is readable and does not intercept input");
            Check(!hud.Canvas.transform.Find("Safe Area/Interaction Prompt").gameObject.activeSelf,
                "the approach instruction hides once the center prompt is visible");
            Capture("Central-Device-Ready", hud.Canvas);
            probe.InteractHeld = true;
            probe.Interact = true;
            yield return new WaitForSeconds(button.HoldDuration * 0.4f);
            Check(game.State == StageState.BeforeStage && playerInteraction.IsHolding && prompt.text.Contains("%"),
                "a partial hold shows progress and keeps the stage ready");
            Capture("Central-Device-Holding", hud.Canvas);
            probe.InteractHeld = false;
            yield return new WaitForSeconds(button.HoldDuration);
            Check(game.State == StageState.BeforeStage && playerInteraction.HoldProgress == 0f,
                "releasing early resets progress and does not start combat");
            controller.enabled = false;
            yield return probe.HoldInteract(button.HoldDuration);
            Check(game.State == StageState.BeforeStage && runner.RemainingEnemyCount == 0,
                "enabled UI cannot execute G while the controller is disabled");
            controller.enabled = true;
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(game.State == StageState.BeforeStage,
                "controller reenable does not replay an ignored interaction");
            game.enabled = false;
            yield return probe.HoldInteract(button.HoldDuration);
            Check(runner.RemainingEnemyCount == 0, "unbound controller cannot start a disabled game");
            game.enabled = true;
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(game.State == StageState.BeforeStage && runner.RemainingEnemyCount == 0,
                "manager rebind waits for a fresh G input");
            ui.enabled = false;
            yield return probe.HoldInteract(button.HoldDuration);
            Check(game.State == StageState.OnStage, "the saved hold event starts combat");
            Check(!button.IsAvailable && !prompt.enabled && hud.Canvas.transform.Find("Crosshair").gameObject.activeSelf,
                "combat disables the start device, hides the prompt and restores the crosshair");
            Check(runner.CurrentWaveIndex == 0 && runner.RemainingEnemyCount == 4, "saved first wave spawns four enemies");
            Check(!ui.enabled && !ui.Canvas.gameObject.activeInHierarchy,
                "the central device starts combat independently of the disabled presenter");
            ui.enabled = true;
            for (int frame = 0; frame < 2; frame++) yield return null;
            Check(game.State == StageState.OnStage && runner.CurrentWaveIndex == 0 && runner.RemainingEnemyCount == 4,
                "presenter reenable does not repeat the start interaction");
            foreach (EnemyAI enemy in Enemies()) enemy.enabled = false;
            foreach (EnemyAI enemy in Enemies().Take(3)) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(runner.CurrentWaveIndex == 1 && runner.RemainingEnemyCount == 7, "saved second wave adds six to one carryover");
            foreach (EnemyAI enemy in Enemies()) enemy.enabled = false;
            foreach (EnemyAI enemy in Enemies().Take(6)) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(runner.CurrentWaveIndex == 2 && runner.RemainingEnemyCount == 9, "saved third wave adds eight to one carryover");
            foreach (EnemyAI enemy in Enemies()) enemy.GetComponent<Mortal>().Dead();
            for (int frame = 0; frame < 7; frame++) yield return null;
            Check(game.State == StageState.AfterStage && runner.IsComplete && runner.RemainingEnemyCount == 0 && ui.IsRewardVisible,
                "saved stage clears and shows real dummy reward UI");
            Check(Time.timeScale == 1f && player.GetComponent<Combatant>().IsGameplayBlocked, "saved reward blocks input without pausing world time");
            Capture("Saved-AfterStage-Reward");
            int clearedIndex = game.CurrentStageIndex;
            bool isFinalStage = clearedIndex == game.Stages.Count - 1;
            ClickReward();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.Result == (isFinalStage ? GameResult.Won : GameResult.None) &&
                game.CurrentStageIndex == (isFinalStage ? clearedIndex : clearedIndex + 1) &&
                game.State == StageState.BeforeStage && !ui.IsRewardVisible,
                "saved dummy reward selection closes UI and returns BeforeStage");
            Check(!player.GetComponent<Combatant>().IsGameplayBlocked && Time.timeScale == 1f, "saved completion restores gameplay input");
            Check(button.IsAvailable == !isFinalStage, "the device reopens only when another stage is waiting");
            Capture("Saved-Completed");
        }

        private StageData Stage(params WaveData[] waves)
        {
            StageData data = ScriptableObject.CreateInstance<StageData>();
            typeof(StageData).GetField("waves", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(data, waves);
            fixtures.Add(data);
            return data;
        }

        private static EnemySpawnGroup Group(int count)
            => new EnemySpawnGroup(new Dictionary<EnemyType, int> { { EnemyType.TempEnemy, count } });

        private static EnemyAI[] Enemies()
            => FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(enemy => !enemy.GetComponent<Mortal>().isDead).ToArray();

        private void ConfigureGame(params StageData[] stages)
        {
            var settings = new SerializedObject(game);
            SerializedProperty list = settings.FindProperty("stages"); list.arraySize = stages.Length;
            for (int index = 0; index < stages.Length; index++) list.GetArrayElementAtIndex(index).objectReferenceValue = stages[index];
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private void ClickReward()
        {
            Check(ui.IsRewardVisible && EventSystem.current != null, "reward has an active UI event system");
            Canvas.ForceUpdateCanvases();
            var rectangle = ui.RewardButton.GetComponent<RectTransform>();
            var click = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rectangle.TransformPoint(rectangle.rect.center))
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(click, hits);
            Check(hits.Count > 0 && hits[0].gameObject == ui.RewardButton.gameObject, "reward button is the top raycast target at its visible center");
            click.pointerCurrentRaycast = hits[0];
            ExecuteEvents.Execute(ui.RewardButton.gameObject, click, ExecuteEvents.pointerClickHandler);
        }

        private void Capture(string name, Canvas hudCanvas = null)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || ui == null) return;
            string folder = SessionState.GetBool(StageProgressionValidation.Automatic, false)
                ? "Logs/GameStarter_20261125/Screenshots"
                : "Logs/StageFlowRefactor_20261106/Screenshots";
            Directory.CreateDirectory(folder);
            Camera camera = player.GetComponent<PlayerController>().GetComponentInChildren<Camera>();
            if (camera == null) camera = FindFirstObjectByType<PlayerCamera>().GetComponent<Camera>();
            Canvas canvas = hudCanvas != null ? hudCanvas : ui.Canvas;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            bool scalerEnabled = scaler.enabled;
            float previousScale = canvas.scaleFactor;
            float previousPlaneDistance = canvas.planeDistance;
            scaler.enabled = false;
            canvas.scaleFactor = 1f;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + 0.1f;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(folder + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                canvas.planeDistance = previousPlaneDistance;
                canvas.scaleFactor = previousScale;
                scaler.enabled = scalerEnabled;
                target.Release();
                Destroy(target);
                Destroy(pixels);
            }
        }

        private void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("STAGE CHECK FAILED: " + label);
            checks++; Debug.Log("STAGE CHECK PASSED: " + label);
        }

        private void Cleanup()
        {
            if (game != null) game.StopGame();
            if (runner != null) runner.AbortStage();
            foreach (StageData data in fixtures) if (data != null) Destroy(data);
        }
    }

    // Exercise the real Controller/Motor and stage interaction after Reader.Update.
    [DefaultExecutionOrder(-150)]
    public sealed class StageFlowInputProbe : MonoBehaviour
    {
        public PlayerInputReader Reader;
        public Vector2 Move;
        public bool Interact;
        public bool InteractHeld;
        public ButtonTrigger StartButton => FindFirstObjectByType<GameStarter>().GetComponent<ButtonTrigger>();
        private static readonly ConstructorInfo Button = typeof(ButtonInputState).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();

        public void PlaceNearStarter()
        {
            Vector3 position = StartButton.TriggerRange.bounds.center + Vector3.back * 2.5f;
            position.y = Reader.transform.position.y;
            Reader.transform.position = position;
            Physics.SyncTransforms();
        }

        public IEnumerator HoldInteract(float duration)
        {
            Interact = InteractHeld = true;
            yield return new WaitForSeconds(duration + 0.1f);
            InteractHeld = false;
            yield return null;
        }

        private void Update()
        {
            if (Reader == null || Reader.GetComponent<Combatant>().IsGameplayBlocked || Reader.GetComponent<Mortal>().isDead) return;
            typeof(PlayerInputReader).GetProperty("IsGameplayInputEnabled").SetValue(Reader, true);
            typeof(PlayerInputReader).GetProperty("Move").SetValue(Reader, Move);
            typeof(PlayerInputReader).GetProperty("Interact").SetValue(Reader, Button.Invoke(new object[] { Interact, Interact || InteractHeld, false }));
            Interact = false;
        }
    }
}
