using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class RewardCardValidation
    {
        private const string Running = "ProjectU.RewardCardValidation";
        internal static int Checks;

        static RewardCardValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Reward Card Validation").AddComponent<RewardCardValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Reward Cards")]
        public static void Run()
        {
            Checks = 0;
            ValidateChances();
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            StageFlowUI ui = UnityEngine.Object.FindFirstObjectByType<StageFlowUI>();
            Check(ui != null && ui.RewardCards.Count == 3 && ui.RewardCards.All(card => card.IsAssigned), "three saved card views are assigned");
            var settings = new SerializedObject(ui);
            for (int index = 0; index < 3; index++)
            {
                Button button = ui.RewardCards[index].Button;
                Check(button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentTarget(0) == ui &&
                    button.onClick.GetPersistentMethodName(0) == "SelectReward", "card has one saved selection binding: " + index);
                string path = "m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_IntArgument";
                Check(new SerializedObject(button).FindProperty(path).intValue == index, "saved card index is correct: " + index);
            }
            Check(settings.FindProperty("rarityChances.common").intValue == 50 &&
                settings.FindProperty("rarityChances.rare").intValue == 30 &&
                settings.FindProperty("rarityChances.epic").intValue == 15 &&
                settings.FindProperty("rarityChances.unique").intValue == 5, "scene saves 50/30/15/5 defaults");
            Check(ui.Canvas.GetComponentsInChildren<Text>(true).All(text => text.font != null && EditorUtility.IsPersistent(text.font)), "all card fonts persist in the scene");
            SessionState.SetInt(Running + ".Checks", Checks);
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        private static RewardRarityChances Chances(string json)
        {
            var chances = new RewardRarityChances();
            JsonUtility.FromJsonOverwrite(json, chances);
            chances.Validate();
            return chances;
        }

        private static void ValidateChances()
        {
            UnityEngine.Random.State previous = UnityEngine.Random.state;
            try
            {
                var defaults = new RewardRarityChances();
                Check(defaults.Total == 100, "default budget is 100 percent");
                var normalized = Chances("{\"common\":25,\"rare\":15,\"epic\":8,\"unique\":2}");
                Check(normalized.Total == 50, "valid totals below 100 stay unchanged");
                foreach (RewardRarityChances chances in new[] { defaults, normalized })
                {
                    UnityEngine.Random.InitState(87621);
                    const int samples = 100000;
                    int[] counts = new int[4];
                    for (int i = 0; i < samples; i++) counts[(int)chances.Roll()]++;
                    int[] weights = { chances.Common, chances.Rare, chances.Epic, chances.Unique };
                    for (int i = 0; i < 4; i++)
                        Check(Mathf.Abs(counts[i] / (float)samples - weights[i] / (float)chances.Total) < 0.006f,
                            "normalized distribution matches input: total=" + chances.Total + ", rarity=" + i + ", count=" + counts[i]);
                }
                string[] names = { "common", "rare", "epic", "unique" };
                for (int index = 0; index < 4; index++)
                {
                    var one = Chances("{" + string.Join(",", names.Select((name, i) => "\"" + name + "\":" + (i == index ? "1" : "0"))) + "}");
                    bool onlyExpected = true;
                    for (int roll = 0; roll < 100; roll++) onlyExpected &= (int)one.Roll() == index;
                    Check(onlyExpected, "zero weight rarities cannot appear: " + names[index]);
                }
                var zero = Chances("{\"common\":0,\"rare\":0,\"epic\":0,\"unique\":0}");
                Check(zero.Common == 50 && zero.Rare == 30 && zero.Epic == 15 && zero.Unique == 5, "all zero restores default probabilities");
                var invalid = Chances("{\"common\":-5,\"rare\":2147483647,\"epic\":50,\"unique\":50}");
                Check(invalid.Total <= 100 && invalid.Common == 0 && invalid.Rare == 100 && invalid.Epic == 0 && invalid.Unique == 0,
                    "malformed serialized probabilities cannot exceed the budget or overflow");
            }
            finally { UnityEngine.Random.state = previous; }
        }

        internal static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("REWARD CHECK FAILED: " + message);
            Checks++;
            Debug.Log("REWARD CHECK PASSED: " + message);
        }

        internal static void Finish(int code)
        {
            SessionState.SetBool(Running, false);
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }

        internal static void RestoreChecks() => Checks = SessionState.GetInt(Running + ".Checks", 0);
    }

    public sealed class RewardCardValidationDriver : MonoBehaviour
    {
        private StageFlowUI ui;

        private IEnumerator Start()
        {
            RewardCardValidation.RestoreChecks();
            Time.captureDeltaTime = 1f / 60f;
            IEnumerator tests = Validate();
            double deadline = EditorApplication.timeSinceStartup + 90d;
            while (true)
            {
                bool more; object current = null;
                try
                {
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Reward validation timed out.");
                    more = tests.MoveNext();
                    if (more) current = tests.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    RewardCardValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("REWARD CARD VALIDATION PASSED: " + RewardCardValidation.Checks + " checks.");
            RewardCardValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            var game = FindFirstObjectByType<GameManager>();
            ui = FindFirstObjectByType<StageFlowUI>();
            var player = FindFirstObjectByType<PlayerMortal>();
            var stats = player.GetComponent<PlayerStat>();
            var combat = player.GetComponent<PlayerCombat>();
            var settings = new SerializedObject(game);
            var stages = settings.FindProperty("stages");
            StageData stage = game.Stages[0];
            stages.arraySize = 5;
            for (int i = 0; i < 5; i++) stages.GetArrayElementAtIndex(i).objectReferenceValue = stage;
            settings.ApplyModifiedPropertiesWithoutUndo();
            for (int i = 0; i < 3; i++) yield return null;
            RewardCardValidation.Check(!ui.IsRewardVisible && game.State == StageState.BeforeStage, "preparation hides all reward cards");
            string[] names = { "common", "rare", "epic", "unique" };
            for (int step = 0; step < 5; step++)
            {
                if (step > 0)
                {
                    var view = new SerializedObject(ui);
                    for (int index = 0; index < 4; index++) view.FindProperty("rarityChances." + names[index]).intValue = step - 1 == index ? 25 : 0;
                    view.ApplyModifiedPropertiesWithoutUndo();
                }
                RewardCardValidation.Check(game.TryStartStage(), "stage starts: " + step);
                for (int frame = 0; frame < 25 && game.State == StageState.OnStage; frame++)
                {
                    foreach (EnemyAI enemy in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) enemy.GetComponent<Mortal>().Dead();
                    yield return null;
                }
                yield return null; yield return null;
                RewardCardValidation.Check(ui.IsRewardVisible && game.IsChoosingReward && !game.IsGameplayEnabled && Time.timeScale == 1f,
                    "clear opens three cards and gates gameplay without stopping time: " + step);
                RewardCardValidation.Check(ui.RewardCards.All(card => card.Button.isActiveAndEnabled && card.Button.interactable), "all three cards are selectable: " + step);
                if (step > 0)
                    RewardCardValidation.Check(ui.RewardCards.All(card => (int)card.Rarity == step - 1), "one-hot configuration appears on all three independent cards: " + names[step - 1]);
                foreach (RewardCardView card in ui.RewardCards)
                {
                    RewardCardValidation.Check(card.Button.GetComponentsInChildren<Text>().Single(text => text.name == "Rarity").text == card.Rarity.ToString(), "card label matches its rarity");
                    Color expected = new SerializedObject(ui).FindProperty(names[(int)card.Rarity] + "CardColor").colorValue;
                    RewardCardValidation.Check(card.Button.GetComponent<Image>().color == expected, "card color matches its rarity");
                }
                var rarities = ui.RewardCards.Select(card => card.Rarity).ToArray();
                UnityEngine.Random.State randomState = UnityEngine.Random.state;
                for (int frame = 0; frame < 5; frame++) yield return null;
                RewardCardValidation.Check(rarities.SequenceEqual(ui.RewardCards.Select(card => card.Rarity)) && randomState.Equals(UnityEngine.Random.state), "presentation refresh never rerolls the current request");
                Capture(step == 0 ? "Default" : names[step - 1], 1280, 720);
                if (step == 4) Capture("Unique-4x3", 1024, 768);
                long id = game.PendingRewardId;
                float attack = stats.AttackMultiplier;
                int health = player.HP, ammo = combat.CurrentAmmo;
                ui.SelectReward(-1); ui.SelectReward(3);
                RewardCardValidation.Check(ui.RewardCards.All(card => card.Button.interactable), "invalid card indices cannot submit a choice");
                Click(step % 3);
                RewardCardValidation.Check(ui.RewardCards.All(card => !card.Button.interactable) && !game.CompleteReward(id), "one click locks all cards and rejects duplicate completion");
                RewardCardValidation.Check(stats.AttackMultiplier == attack && player.HP == health && combat.CurrentAmmo == ammo, "card selection grants no actual stats, health or ammunition");
                for (int frame = 0; frame < 4; frame++) yield return null;
                RewardCardValidation.Check(game.State == StageState.BeforeStage && !ui.IsRewardVisible && game.IsGameplayEnabled,
                    "selected card returns to preparation and restores input: " + step);
                RewardCardValidation.Check(!game.CompleteReward(id), "previous card request is invalid after returning to preparation");
            }
            RewardCardValidation.Check(game.Result == GameResult.Won, "final card preserves the final victory result");
        }

        private void Click(int index)
        {
            Canvas.ForceUpdateCanvases();
            var button = ui.RewardCards[index].Button;
            var rect = (RectTransform)button.transform;
            var click = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(click, hits);
            RewardCardValidation.Check(hits.Count > 0 && hits[0].gameObject == button.gameObject, "card is the top raycast target at its center: " + index);
            click.pointerCurrentRaycast = hits[0];
            ExecuteEvents.Execute(button.gameObject, click, ExecuteEvents.pointerClickHandler);
        }

        private void Capture(string name, int width, int height)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            const string folder = "Logs/RewardCards_20261110/Screenshots";
            Directory.CreateDirectory(folder);
            Camera camera = FindFirstObjectByType<PlayerCamera>().GetComponent<Camera>();
            Canvas canvas = ui.Canvas;
            var scaler = canvas.GetComponent<CanvasScaler>();
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                scaler.enabled = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.1f;
                canvas.scaleFactor = Mathf.Sqrt(width / 1280f * height / 720f);
                Canvas.ForceUpdateCanvases();
                Text title = (Text)new SerializedObject(ui).FindProperty("rewardTitle").objectReferenceValue;
                Rect titleBounds = ScreenBounds(title.rectTransform, camera);
                var cardBounds = new List<Rect>();
                foreach (RewardCardView card in ui.RewardCards)
                {
                    Rect bounds = ScreenBounds((RectTransform)card.Button.transform, camera);
                    RewardCardValidation.Check(bounds.xMin >= 0f && bounds.yMin >= 0f && bounds.xMax <= width && bounds.yMax <= height, "render keeps card on screen: " + name);
                    RewardCardValidation.Check(!bounds.Overlaps(titleBounds) && cardBounds.All(other => !other.Overlaps(bounds)), "cards do not cover the title or each other: " + name);
                    cardBounds.Add(bounds);
                }
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(folder + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                scaler.enabled = true;
                target.Release(); Destroy(target); Destroy(pixels);
            }
        }

        private static Rect ScreenBounds(RectTransform rect, Camera camera)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
