using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class PlayerHUDValidation
    {
        private const string Running = "ProjectU.PlayerHUDValidation";

        static PlayerHUDValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player HUD Validation").AddComponent<PlayerHUDValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player HUD")]
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
            Time.captureDeltaTime = 0f;
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }

    public sealed class PlayerHUDValidationDriver : MonoBehaviour
    {
        private const string Folder = "Logs/HUDPromptMigration_20261107";
        private int checks;

        private IEnumerator Start()
        {
            Time.captureDeltaTime = 1f / 60f;
            IEnumerator tests = Validate();
            double deadline = EditorApplication.timeSinceStartup + 90d;
            while (true)
            {
                bool more; object current = null;
                try
                {
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("HUD validation timed out.");
                    more = tests.MoveNext();
                    if (more) current = tests.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    PlayerHUDValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("PLAYER HUD VALIDATION PASSED: " + checks + " checks.");
            PlayerHUDValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            PlayerHUD hud = FindFirstObjectByType<PlayerHUD>();
            StageFlowUI flow = FindFirstObjectByType<StageFlowUI>();
            GameManager game = FindFirstObjectByType<GameManager>();
            Check(hud != null && flow != null && game != null, "saved scene connects HUD and stage UI");
            Mortal health = hud.GetComponent<Mortal>();
            PlayerStat stat = hud.GetComponent<PlayerStat>();
            PlayerCombat combat = hud.GetComponent<PlayerCombat>();
            hud.GetComponent<PlayerController>().enabled = false;
            for (int i = 0; i < 4; i++) yield return null;
            Text hp = hud.GetComponentsInChildren<Text>(true).Single(label => label.name == "Current Health");
            Text ammo = hud.GetComponentsInChildren<Text>(true).Single(label => label.name == "Magazine Ammo");
            Text prompt = hud.GetComponentsInChildren<Text>(true).Single(label => label.name == "Prompt");
            Check(flow.Canvas.transform.Find("Interaction Prompt") == null, "only PlayerHUD owns the interaction prompt");
            Check(prompt.text == "[G] 상호작용 · 스테이지 시작", "HUD shows the preparation prompt");
            flow.enabled = false;
            yield return null; yield return null;
            Check(prompt.isActiveAndEnabled, "prompt stays visible independently of StageFlowUI");
            flow.enabled = true;
            Image fill = hud.GetComponentsInChildren<Image>(true).Single(image => image.name == "Health Fill");
            RectTransform crosshair = (RectTransform)hud.Canvas.transform.Find("Crosshair");
            Check(hp.text == health.HP.ToString() && Mathf.Approximately(fill.rectTransform.anchorMax.x, 1f), "initial health is full");
            Check(ammo.text.StartsWith(combat.CurrentAmmo + "<") && ammo.text.Contains(" / " + combat.MaxAmmo), "initial magazine uses actual capacity");
            Check(hud.Canvas.sortingOrder < flow.Canvas.sortingOrder && hud.Canvas.GetComponent<GraphicRaycaster>() == null &&
                hud.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), "HUD cannot intercept reward or restart clicks");
            Canvas.ForceUpdateCanvases();
            Vector2 center = RectTransformUtility.WorldToScreenPoint(null, crosshair.position);
            Check(Vector2.Distance(center, new Vector2(Screen.width, Screen.height) * 0.5f) < 1f, "crosshair matches the camera's screen center");
            Capture(hud, flow, "FullHealth", 1280, 720);

            health.IsInvincible = false;
            health.TakeDamage(health.HP - 25, null);
            yield return null; yield return null;
            Check(hp.text == "25" && Mathf.Approximately(fill.rectTransform.anchorMax.x, 25f / stat.MaxHealth), "damage updates number and bar");
            Check(fill.color.r > fill.color.g * 2f && hp.color == fill.color, "critical health has a visible red warning");
            Capture(hud, flow, "LowHealth", 1280, 720);
            stat.MaxHealth = 200;
            yield return null; yield return null;
            Check(hp.text == "25" && Mathf.Approximately(fill.rectTransform.anchorMax.x, 0.125f), "maximum health changes rescale without healing");
            health.TryRestoreHealth(stat.MaxHealth);
            yield return null; yield return null;
            Check(hp.text == "200" && Mathf.Approximately(fill.rectTransform.anchorMax.x, 1f), "explicit recovery refreshes HUD");

            int beforeShot = combat.CurrentAmmo;
            Check(combat.TryStartAction(PlayerActionSlot.PrimaryFire), "real primary shot is accepted");
            for (int i = 0; i < 25; i++) yield return null;
            Check(combat.CurrentAmmo == beforeShot - 1 && ammo.text.StartsWith(combat.CurrentAmmo + "<"), "successful shot updates magazine display");
            stat.ReloadSpeedMultiplier = 10f;
            Check(combat.TryReload(), "real reload is accepted");
            for (int i = 0; i < 30; i++) yield return null;
            Check(!combat.IsReloading && combat.CurrentAmmo == combat.MaxAmmo && ammo.text.StartsWith(combat.MaxAmmo + "<"), "reload completion refreshes magazine");
            stat.MaxMagazine = 45;
            yield return null; yield return null;
            Check(ammo.text.Contains(" / 45") && combat.CurrentAmmo == beforeShot, "capacity changes update total without refilling");
            hud.enabled = false;
            Check(!hud.Canvas.gameObject.activeSelf, "disabling HUD hides its canvas");
            health.TakeDamage(20, null);
            hud.enabled = true;
            Check(hud.Canvas.gameObject.activeSelf && hp.text == health.HP.ToString(), "reactivation synchronizes current values");

            Capture(hud, flow, "Wide", 1920, 1080);
            Capture(hud, flow, "Standard", 1024, 768);
            Check(game.TryStartStage(), "stage starts for reward integration");
            yield return null; yield return null;
            Check(prompt.text == "적을 처치하여 다음 웨이브로 진행하세요", "HUD shows the combat prompt");
            for (int frame = 0; frame < 12 && game.State == StageState.OnStage; frame++)
            {
                foreach (EnemyAI enemy in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) enemy.GetComponent<Mortal>().Dead();
                yield return null;
            }
            yield return null; yield return null;
            Check(game.IsChoosingReward && flow.IsRewardVisible && !crosshair.gameObject.activeSelf, "reward modal hides crosshair");
            Check(prompt.text == "보상을 선택하여 계속하세요", "HUD shows the reward prompt");
            Check(hp.text == stat.MaxHealth.ToString(), "stage clear recovery is reflected");
            Capture(hud, flow, "Reward", 1280, 720);
            flow.RewardButton.onClick.Invoke();
            for (int i = 0; i < 4; i++) yield return null;
            Check(!flow.IsRewardVisible && crosshair.gameObject.activeSelf, "reward selection restores crosshair");
            Check(prompt.text == "[G] 상호작용 · 스테이지 시작", "HUD restores the preparation prompt after reward selection");
            health.Dead();
            yield return null; yield return null;
            Check(hp.text == "0" && Mathf.Approximately(fill.rectTransform.anchorMax.x, 0f) &&
                flow.IsDeathVisible && !crosshair.gameObject.activeSelf, "death shows empty health and hides crosshair");
            Check(prompt.text == "플레이어 사망", "HUD shows the death prompt");
            Capture(hud, flow, "Death", 1280, 720);
        }

        private void Capture(PlayerHUD hud, StageFlowUI flow, string name, int width, int height)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Directory.CreateDirectory(Folder);
            Camera camera = FindFirstObjectByType<PlayerCamera>().GetComponent<Camera>();
            Canvas[] canvases = { hud.Canvas, flow.Canvas };
            var modes = canvases.Select(canvas => canvas.renderMode).ToArray();
            var scales = canvases.Select(canvas => canvas.scaleFactor).ToArray();
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                for (int i = 0; i < canvases.Length; i++)
                {
                    Canvas canvas = canvases[i];
                    canvas.GetComponent<CanvasScaler>().enabled = false;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = camera.nearClipPlane + 0.1f;
                    canvas.scaleFactor = Mathf.Sqrt(width / 1280f * height / 720f);
                }
                Canvas.ForceUpdateCanvases();
                Rect healthBounds = ScreenBounds(hud.Canvas.transform.Find("Safe Area/Health Widget"), camera);
                Rect ammoBounds = ScreenBounds(hud.Canvas.transform.Find("Safe Area/Ammo Widget"), camera);
                Rect promptBounds = ScreenBounds(hud.Canvas.transform.Find("Safe Area/Interaction Prompt"), camera);
                Check(!healthBounds.Overlaps(promptBounds) && !ammoBounds.Overlaps(promptBounds), name + " prompt does not cover health or ammo");
                Check(healthBounds.xMin >= 0f && healthBounds.yMin >= 0f && ammoBounds.xMax <= width && ammoBounds.yMin >= 0f,
                    name + " corner widgets remain on screen");
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Folder + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].renderMode = modes[i];
                    canvases[i].worldCamera = null;
                    canvases[i].scaleFactor = scales[i];
                    canvases[i].GetComponent<CanvasScaler>().enabled = true;
                }
                target.Release();
                Destroy(target); Destroy(pixels);
            }
        }

        private static Rect ScreenBounds(Transform node, Camera camera)
        {
            var corners = new Vector3[4];
            ((RectTransform)node).GetWorldCorners(corners);
            Vector2 minimum = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 maximum = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("HUD check failed: " + message);
            checks++;
            Debug.Log("HUD PASS " + checks + ": " + message);
        }
    }
}
