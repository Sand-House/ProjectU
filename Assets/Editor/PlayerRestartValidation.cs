using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class PlayerRestartValidation
    {
        private const string Running = "ProjectU.PlayerRestartValidation";
        static PlayerRestartValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player Restart Validation").AddComponent<PlayerRestartValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player Restart")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        public static void CompilePlayerScripts()
        {
            const string path = "Logs/PlayerRestart_20261106/PlayerScripts";
            Directory.CreateDirectory(path);
            var settings = new ScriptCompilationSettings
            {
                group = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
                options = ScriptCompilationOptions.None
            };
            ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(settings, path);
            if (result.assemblies == null || !result.assemblies.Any(file => Path.GetFileName(file) == "Assembly-CSharp.dll"))
                throw new InvalidOperationException("Player scripts did not compile.");
            Debug.Log("RESTART PLAYER SCRIPT COMPILATION PASSED: " + result.assemblies.Count + " assemblies.");
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

    public sealed class PlayerRestartValidationDriver : MonoBehaviour
    {
        private GameManager game;
        private StageFlowUI ui;
        private PlayerMortal player;
        private StageFlowInputProbe probe;
        private Mortal health;
        private float[] startingStats;
        private Vector3 startingPosition;
        private Quaternion startingRotation;
        private float startingStamina;
        private float startingYaw;
        private StageData[] startingStages;
        private int checks;
        private int sceneLoads;
        private double deadline;
        private long invalidatedReward;

        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += SceneLoaded;
            deadline = EditorApplication.timeSinceStartup + 150d;
            Time.captureDeltaTime = 1f / 60f;
            IEnumerator tests = Validate();
            while (true)
            {
                bool more; object current = null;
                try
                {
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Restart validation timed out.");
                    more = tests.MoveNext();
                    if (more) current = tests.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Cleanup(); PlayerRestartValidation.Finish(1); yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("PLAYER RESTART VALIDATION PASSED: " + checks + " checks; " + sceneLoads + " actual scene reloads.");
            Cleanup(); PlayerRestartValidation.Finish(0);
        }

        private void BindScene()
        {
            game = FindFirstObjectByType<GameManager>();
            ui = FindFirstObjectByType<StageFlowUI>();
            player = FindFirstObjectByType<PlayerMortal>();
            health = player.GetComponent<Mortal>();
            if (probe == null) probe = gameObject.AddComponent<StageFlowInputProbe>();
            probe.Reader = player.GetComponent<PlayerInputReader>();
            probe.Move = Vector2.zero;
            probe.Interact = false;
            probe.InteractHeld = false;
        }

        private IEnumerator Validate()
        {
            BindScene();
            startingStats = StatValues(player.GetComponent<PlayerStat>());
            startingPosition = player.transform.position;
            startingRotation = player.transform.rotation;
            startingStamina = player.GetComponent<PlayerStamina>().Current;
            startingYaw = FindFirstObjectByType<PlayerCamera>().Yaw;
            startingStages = game.Stages.ToArray();
            health.IsInvincible = true;
            Check(!game.TryRestartGame(), "live preparation cannot request a death restart");
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(!ui.IsDeathVisible && game.State == StageState.BeforeStage, "death UI stays hidden before death");

            // Reach stage two without changing the saved scene's stage list.
            var settings = new SerializedObject(game);
            SerializedProperty stages = settings.FindProperty("stages");
            stages.arraySize = 2;
            stages.GetArrayElementAtIndex(1).objectReferenceValue = stages.GetArrayElementAtIndex(0).objectReferenceValue;
            settings.ApplyModifiedPropertiesWithoutUndo();
            probe.PlaceNearStarter();
            yield return probe.HoldInteract(probe.StartButton.HoldDuration);
            Check(game.State == StageState.OnStage, "central hold starts the first fixture stage");
            while (game.State == StageState.OnStage)
            {
                KillEnemies();
                for (int frame = 0; frame < 4; frame++) yield return null;
            }
            Check(ui.IsRewardVisible, "first stage reaches the real reward UI");
            invalidatedReward = game.PendingRewardId;
            Click(ui.RewardButton);
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(game.CurrentStageIndex == 1 && game.State == StageState.BeforeStage, "fixture reaches second-stage preparation");
            yield return probe.HoldInteract(probe.StartButton.HoldDuration);
            foreach (EnemyAI enemy in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) enemy.enabled = false;
            Check(game.CurrentStageIndex == 1 && game.State == StageState.OnStage, "death fixture is actually on the second stage");

            PlayerStat stats = player.GetComponent<PlayerStat>();
            stats.MaxHealth = 250;
            stats.MaxMagazine = 12;
            stats.AttackMultiplier = 3f;
            stats.MoveSpeed = 9f;
            PlayerStamina stamina = player.GetComponent<PlayerStamina>();
            stamina.RecoveryRate = 0f;
            Check(stamina.TryConsume(75f), "fixture depletes stamina before restart");
            PlayerCombat combat = player.GetComponent<PlayerCombat>();
            Check(combat.TryStartAction(PlayerActionSlot.PrimaryFire), "fixture fires real primary ammunition");
            for (int frame = 0; frame < 25; frame++) yield return null;
            Check(combat.CurrentAmmo < combat.MaxAmmo, "fixture has a depleted magazine");
            Check(combat.TryReload() && combat.IsReloading, "fixture has an unfinished reload before death");
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(startingPosition + new Vector3(5f, 0f, 3f), Quaternion.Euler(0f, 110f, 0f));
            controller.enabled = true;
            Check(Vector3.Distance(player.transform.position, startingPosition) > 4f, "fixture moves away from saved spawn");
            ProjectilePool pool = FindFirstObjectByType<ProjectilePool>();
            ProjectileBase prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectiles/Grenade.prefab").GetComponent<ProjectileBase>();
            Check(pool.Spawn(prefab, player.gameObject, new Vector3(0f, 8f, 0f), Vector3.up) != null, "fixture has a live projectile before death");
            int deaths = 0;
            health.OnDead += attacker => deaths++;
            health.IsInvincible = false;
            Time.timeScale = 0.5f;
            health.TakeDamage(health.HP, null);
            health.Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(deaths == 1 && health.isDead && health.HP == 0, "lethal damage publishes exactly one death");
            Check(game.Result == GameResult.Lost && Time.timeScale == 0f, "death suspends combat and records Lost");
            Check(ui.IsDeathVisible && !ui.IsRewardVisible && ui.RestartButton.interactable, "death opens selectable restart UI and closes rewards");
            Check(EventSystem.current.currentSelectedGameObject == ui.RestartButton.gameObject, "restart button receives UI focus");
            Check(player.GetComponent<Combatant>().IsGameplayBlocked && Cursor.lockState == CursorLockMode.None, "death blocks gameplay but releases the UI cursor");
            Check(!combat.IsReloading && !game.TryStartStage() && !game.TryRetrySpawn(), "death cancels reload and rejects combat continuation");
            Check(game.StageRunner.RemainingEnemyCount == 0 && pool.ActiveCount == 0, "death cleans owned enemies and projectiles");
            Check(!game.CompleteReward(invalidatedReward), "death rejects the previous stage reward token");
            Capture("Death-SecondStage");

            GameManager previousGame = game;
            PlayerMortal previousPlayer = player;
            int previousLoads = sceneLoads;
            Click(ui.RestartButton);
            Check(game.IsRestarting && !ui.RestartButton.interactable && !game.TryRestartGame(), "UI submits only one scene reload and disables duplicate selection");
            Check(Time.timeScale == 0.5f && player.GetComponent<Combatant>().IsGameplayBlocked, "reload restores prior time scale while the dead player stays blocked");
            while (previousGame != null || FindFirstObjectByType<GameManager>() == null) yield return null;
            BindScene();
            for (int frame = 0; frame < 6; frame++) yield return null;
            Check(sceneLoads == previousLoads + 1 && previousPlayer == null, "restart actually replaces the gameplay scene and old player once");
            CheckFreshRun();
            Check(game.Stages.SequenceEqual(startingStages), "reload restores the saved stage list");
            Check(StatValues(player.GetComponent<PlayerStat>()).SequenceEqual(startingStats),
                "reload restores all runtime player stat changes to the saved values");
            Check(Quaternion.Angle(player.transform.rotation, startingRotation) < 0.1f &&
                Mathf.Abs(Mathf.DeltaAngle(FindFirstObjectByType<PlayerCamera>().Yaw, startingYaw)) < 0.1f,
                "reload restores saved player facing and camera heading");
            Check(!game.CompleteReward(invalidatedReward), "fresh preparation cannot accept a stale reward completion");
            Capture("Restarted-BeforeStage");
            Vector3 position = player.transform.position;
            probe.Move = Vector2.right;
            for (int frame = 0; frame < 10; frame++) yield return null;
            probe.Move = Vector2.zero;
            Check(Vector3.Distance(player.transform.position, position) > 0.2f, "restarted player moves through the real Controller and Motor");
            Check(game.State == StageState.BeforeStage && game.StageRunner.RemainingEnemyCount == 0, "restart does not auto-start the first wave");

            // Die with the reward modal open, then restart again to detect stale UI/ownership.
            health.IsInvincible = true;
            probe.PlaceNearStarter();
            yield return probe.HoldInteract(probe.StartButton.HoldDuration);
            Check(game.State == StageState.OnStage && game.StageRunner.RemainingEnemyCount == 4, "fresh central hold starts the first wave after restart");
            while (game.State == StageState.OnStage)
            {
                KillEnemies();
                for (int frame = 0; frame < 4; frame++) yield return null;
            }
            Check(ui.IsRewardVisible && game.IsChoosingReward, "restarted run can clear and request rewards normally");
            invalidatedReward = game.PendingRewardId;
            health.Dead();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(ui.IsDeathVisible && !ui.IsRewardVisible && !game.IsChoosingReward && !game.CompleteReward(invalidatedReward),
                "death during reward selection replaces its modal and invalidates the request");
            Capture("Death-DuringReward");
            previousGame = game;
            previousLoads = sceneLoads;
            Click(ui.RestartButton);
            while (previousGame != null || FindFirstObjectByType<GameManager>() == null) yield return null;
            BindScene();
            for (int frame = 0; frame < 6; frame++) yield return null;
            Check(sceneLoads == previousLoads + 1, "second death also queues exactly one actual scene reload");
            CheckFreshRun();
            Check(FindObjectsByType<GameManager>(FindObjectsSortMode.None).Length == 1 &&
                FindObjectsByType<StageFlowUI>(FindObjectsSortMode.None).Length == 1 &&
                FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1,
                "repeated restart leaves one manager, UI and event system");
            Check(!game.TryRestartGame(), "fresh run rejects the old restart command");
        }

        private void CheckFreshRun()
        {
            Check(game.State == StageState.BeforeStage && game.Result == GameResult.None && game.CurrentStageIndex == 0 && !game.IsRestarting,
                "new run returns to first-stage BeforeStage with no result or pending restart");
            Check(!health.isDead && health.HP == player.GetComponent<PlayerStat>().MaxHealth, "new player starts alive at saved maximum HP");
            Check(health.IsInvincible, "scene restart initializes first-stage protection through the stage subscription");
            Check(player.GetComponent<PlayerCombat>().CurrentAmmo == player.GetComponent<PlayerCombat>().MaxAmmo &&
                Mathf.Approximately(player.GetComponent<PlayerStamina>().Current, startingStamina), "new run restores starting magazine and stamina");
            Check(player.GetComponents<ActionBase>().All(action => action.CooldownRemaining <= 0f && action.Phase == ActionPhase.Ready),
                "new run starts without action locks or cooldowns");
            Check(Vector3.Distance(player.transform.position, startingPosition) < 0.15f, "new run restores saved player spawn position");
            Check(!ui.IsDeathVisible && !ui.IsRewardVisible && !player.GetComponent<Combatant>().IsGameplayBlocked,
                "new run hides both modals and enables gameplay");
            Check(game.PendingRewardId == 0 && game.StageRunner.RemainingEnemyCount == 0 &&
                FindFirstObjectByType<ProjectilePool>().ActiveCount == 0 && Time.timeScale == 0.5f,
                "new run has no stale reward, enemy or projectile and retains the prior play speed");
            Check(player.GetComponent<PlayerController>().BufferedAction == PlayerActionSlot.None, "new player has no buffered input from the dead run");
        }

        private void Click(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Check(hits.Count > 0 && hits[0].gameObject == button.gameObject, "visible UI button is the top raycast target");
            pointer.pointerCurrentRaycast = hits[0];
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        private static void KillEnemies()
        {
            foreach (EnemyAI enemy in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) enemy.GetComponent<Mortal>().Dead();
        }

        private static float[] StatValues(PlayerStat stats) => new[]
        {
            stats.AttackMultiplier, stats.MaxHealth, stats.MaxStamina, stats.AttackSpeed, stats.MoveSpeed,
            stats.MaxMagazine, stats.ReloadDuration, stats.ReloadSpeedMultiplier, stats.DashDistance
        };

        private void SceneLoaded(Scene scene, LoadSceneMode mode) { if (scene.path == "Assets/Scenes/SampleScene.unity") sceneLoads++; }

        private void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            const string folder = "Logs/PlayerRestart_20261106/Screenshots";
            Directory.CreateDirectory(folder);
            Camera camera = FindFirstObjectByType<PlayerCamera>().GetComponent<Camera>();
            Canvas canvas = ui.Canvas;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            float scale = canvas.scaleFactor;
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
                canvas.scaleFactor = scale;
                scaler.enabled = true;
                target.Release();
                Destroy(target);
                Destroy(pixels);
            }
        }

        private void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("RESTART CHECK FAILED: " + label);
            checks++; Debug.Log("RESTART CHECK PASSED: " + label);
        }

        private void Cleanup()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (game != null) game.StopGame();
        }
    }
}
