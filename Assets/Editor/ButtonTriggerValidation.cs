using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class ButtonTriggerValidation
    {
        private const string Running = "ProjectU.ButtonTriggerValidation";

        static ButtonTriggerValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Button Trigger Validation").AddComponent<ButtonTriggerValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Button Triggers")]
        public static void Run() => Begin(false);

        [MenuItem("ProjectU/Validation/Render Button Trigger Prompts")]
        public static void RunVisual() => Begin(true);

        internal static bool RenderPrompts => SessionState.GetBool(Running + ".Render", false);

        private static void Begin(bool render)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            GameFlowValidationUtility.DisableAutomaticGameFlow();
            SessionState.SetBool(Running, true);
            SessionState.SetBool(Running + ".Render", render);
            EditorApplication.EnterPlaymode();
        }

        internal static void Finish(int code)
        {
            SessionState.SetBool(Running, false);
            SessionState.SetBool(Running + ".Render", false);
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }

    public sealed class ButtonInteractionBlockerProbe : MonoBehaviour, IInteractionBlocker
    {
        public bool BlocksInteraction => true;
    }

    // Inject snapshots between Reader and Controller; all objects below exist only in the unsaved Play scene.
    [DefaultExecutionOrder(-150)]
    public sealed class ButtonTriggerValidationDriver : MonoBehaviour
    {
        private PlayerInputReader input;
        private PlayerController controller;
        private PlayerInteraction interaction;
        private ButtonTrigger button;
        private Text label;
        private InteractionPrompt prompt;
        private bool pressed, held, released;
        private bool gameplayEnabled = true;
        private int activations, checks;
        private double deadline;

        private IEnumerator Start()
        {
            deadline = EditorApplication.timeSinceStartup + 90d;
            IEnumerator tests = Validate();
            while (true)
            {
                bool more;
                object current = null;
                try
                {
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Button validation timed out.");
                    more = tests.MoveNext();
                    if (more) current = tests.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    ButtonTriggerValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("BUTTON TRIGGER VALIDATION PASSED: " + checks + " checks.");
            ButtonTriggerValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            Time.captureDeltaTime = 1f / 60f;
            EnemyValidationIsolation.DisableDemoEnemies();
            input = FindFirstObjectByType<PlayerInputReader>();
            controller = input.GetComponent<PlayerController>();
            input.GetComponent<PlayerMotor>().enabled = false;
            input.GetComponent<PlayerCombat>().enabled = false;
            interaction = input.GetComponent<PlayerInteraction>();
            FindFirstObjectByType<GameStarter>().gameObject.SetActive(false);
            Check(interaction != null, "generic fixtures use the saved player interaction in isolation");
            button = CreateButton("Meshless Button", interaction.InteractionPosition, ButtonTriggerInputMode.Press);
            button.OnTriggered.AddListener(() => activations++);
            CreatePrompt();
            yield return Step(false, false);
            Check(interaction.CurrentButton == button && label.enabled && label.text.Contains("[G]"), "entering range selects button and shows G prompt");
            Check(activations == 0, "entering range never activates button");
            Check(button.GetComponentInChildren<Renderer>() == null && button.GetComponent<Rigidbody>() == null,
                "meshless range works without a Rigidbody");
            Vector3 screen = RectTransformUtility.WorldToScreenPoint(null, label.rectTransform.position);
            Check(Mathf.Abs(screen.x - Screen.width * 0.5f) < 1f && Mathf.Abs(screen.y - Screen.height * 0.5f) < 1f,
                "prompt is at screen center");
            Check(!label.raycastTarget, "prompt never intercepts UI input");
            Check(label.alignment == TextAnchor.MiddleCenter && label.rectTransform.rect.width == 600f &&
                label.rectTransform.rect.height == 80f, "prompt defaults center and size its text area");
            CapturePrompt("Press", 1280, 720);

            yield return Step(true, true);
            Check(activations == 1, "press button emits one event on press");
            yield return Wait(0.35f);
            Check(activations == 1, "holding a press button does not repeat");
            yield return Step(false, false, true);
            yield return Step(true, false, true);
            Check(activations == 2, "a short press and release in one snapshot still activates press mode");
            yield return Step(false, false);
            Set(button, "inputMode", ButtonTriggerInputMode.Hold);
            Set(button, "holdDuration", 0.3f);
            yield return Step(false, false);
            Check(label.text.Contains("길게 누르기"), "hold button shows hold instructions");
            CapturePrompt("Hold-Instructions", 1280, 720);
            yield return Step(true, true);
            yield return Wait(0.1f);
            Check(activations == 2 && interaction.HoldProgress > 0f && interaction.HoldProgress < 1f && label.text.Contains("%"),
                "partial hold displays progress without an event");
            CapturePrompt("Hold-Progress", 1280, 720);
            CapturePrompt("Hold-Progress-4x3", 960, 720);
            yield return Step(false, false, true);
            Check(interaction.HoldProgress == 0f && !interaction.IsHolding && activations == 2, "release cancels partial hold");
            yield return Step(true, true);
            yield return Wait(0.32f);
            Check(activations == 3 && interaction.HoldProgress == 1f, "full hold activates exactly once");
            yield return Wait(0.4f);
            Check(activations == 3, "completed hold waits for release rather than repeating");
            yield return Step(false, false, true);
            yield return Step(true, true);
            yield return Wait(0.32f);
            Check(activations == 4, "release then hold permits a new activation");
            yield return Step(false, false, true);
            yield return Step(true, true);
            yield return Wait(0.1f);
            Vector3 home = button.transform.position;
            button.transform.position = home + Vector3.right * 10f;
            Physics.SyncTransforms();
            yield return Wait(0.35f);
            Check(interaction.CurrentButton == null && !label.enabled && interaction.HoldProgress == 0f && activations == 4,
                "leaving range hides prompt and cancels hold");
            button.transform.position = home;
            Physics.SyncTransforms();
            yield return Wait(0.35f);
            Check(interaction.CurrentButton == button && activations == 4 && interaction.HoldProgress == 0f,
                "entering range with G already held does not start a hold");

            yield return StartPartialHold();
            button.enabled = false;
            button.enabled = true;
            yield return Wait(0.35f);
            Check(activations == 4 && interaction.HoldProgress == 0f, "same-frame button disable and enable discards progress");
            yield return StartPartialHold();
            button.Interactable = false;
            yield return Step(false, true);
            Check(!label.enabled && interaction.CurrentButton == null, "non-interactable button hides prompt");
            button.Interactable = true;
            yield return Wait(0.35f);
            Check(activations == 4, "restoring availability while held does not activate");

            yield return StartPartialHold();
            button.TriggerRange.enabled = false;
            yield return Wait(0.35f);
            Check(interaction.CurrentButton == null && activations == 4, "disabled range cannot be used");
            button.TriggerRange.enabled = true;
            yield return Wait(0.35f);
            Check(activations == 4, "restoring range requires a fresh press");

            yield return StartPartialHold();
            Time.timeScale = 0f;
            yield return Step(false, true);
            Check(!label.enabled && interaction.HoldProgress == 0f, "pause clears hold and hides prompt");
            Time.timeScale = 1f;
            yield return Wait(0.35f);
            Check(activations == 4, "resuming while held does not replay cancelled interaction");
            yield return StartPartialHold();
            gameplayEnabled = false;
            yield return Step(false, true);
            Check(interaction.CurrentButton == null && !label.enabled, "gameplay gate cancels interaction");
            gameplayEnabled = true;
            yield return Wait(0.35f);
            Check(activations == 4, "input recovery does not replay a cancelled hold");

            yield return StartPartialHold();
            var blocker = input.gameObject.AddComponent<ButtonInteractionBlockerProbe>();
            yield return Wait(0.35f);
            Check(interaction.CurrentButton == null && !label.enabled && activations == 4, "interaction blockers cancel hold like ADS");
            blocker.enabled = false;
            yield return Wait(0.35f);
            Check(activations == 4, "unblocking while held requires release");

            yield return StartPartialHold();
            controller.enabled = false;
            yield return Step(false, true);
            Check(!label.enabled && interaction.HoldProgress == 0f, "controller disable clears hold");
            controller.enabled = true;
            yield return Wait(0.35f);
            Check(activations == 4, "controller enable while held cannot activate");
            yield return StartPartialHold();
            interaction.enabled = false;
            yield return Step(false, true);
            Check(!label.enabled, "interaction component disable hides prompt");
            interaction.enabled = true;
            yield return Wait(0.35f);
            Check(activations == 4, "interaction component enable discards old hold");
            yield return StartPartialHold();
            input.enabled = false;
            yield return Step(false, true);
            Check(interaction.CurrentButton == null && activations == 4, "reader disable clears interaction");
            input.enabled = true;
            yield return Wait(0.35f);
            Check(activations == 4, "reader enable does not preserve hold progress");

            yield return Step(false, false, true);
            Set(button, "holdDuration", 0f);
            yield return Step(true, true);
            Check(activations == 5, "zero-duration hold activates on its first held press");
            yield return Wait(0.2f);
            Check(activations == 5, "zero-duration hold still cannot repeat while held");

            yield return Step(false, false, true);
            Set(button, "holdDuration", 0.3f);
            button.transform.position = home + Vector3.right * 0.6f;
            ButtonTrigger closer = CreateButton("Closer Button", home, ButtonTriggerInputMode.Press);
            int closerCount = 0;
            closer.OnTriggered.AddListener(() => closerCount++);
            Physics.SyncTransforms();
            yield return Step(true, true);
            Check(interaction.CurrentButton == closer && closerCount == 1 && activations == 5,
                "overlapping ranges choose only the nearest button");
            closer.gameObject.SetActive(false);
            yield return Wait(0.35f);
            Check(interaction.CurrentButton == button && activations == 5, "changing targets while held cannot activate the next button");

            yield return StartPartialHold();
            controller.SendMessage("OnApplicationFocus", false);
            yield return Wait(0.35f);
            Check(activations == 5 && interaction.HoldProgress == 0f, "focus loss cancels an in-progress hold");
            yield return StartPartialHold();
            typeof(PlayerInteraction).GetMethod("HandleGameplayEnabledChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(interaction, new object[] { false });
            yield return Wait(0.35f);
            Check(activations == 5, "stage gameplay notifications cancel a hold even within one frame");

            yield return Step(false, false, true);
            button.gameObject.SetActive(false);
            ButtonTrigger childRangeButton = CreateButton("Child Range", home, ButtonTriggerInputMode.Press, true);
            int childCount = 0;
            childRangeButton.OnTriggered.AddListener(() => childCount++);
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(childRangeButton.transform, false);
            mesh.transform.localPosition = Vector3.up * 5f;
            Physics.SyncTransforms();
            yield return Step(true, true);
            Check(childCount == 1 && interaction.CurrentButton == childRangeButton,
                "child box range and optional mesh work independently of mesh position");
            childRangeButton.OnTriggered.AddListener(() => childRangeButton.enabled = false);
            yield return Step(false, false, true);
            yield return Step(true, true);
            Check(childCount == 2 && interaction.CurrentButton == null && !label.enabled,
                "activation listener may disable the button safely");
            childRangeButton.enabled = true;
            yield return Step(false, false, true);
            childRangeButton.OnTriggered.RemoveAllListeners();
            childRangeButton.OnTriggered.AddListener(() => { childCount++; interaction.CancelInteraction(); });
            yield return Step(true, true);
            yield return Wait(0.2f);
            Check(childCount == 3, "listener cancellation cannot repeat events or fall through to another button");
            UnityEngine.Object.Destroy(childRangeButton.gameObject);
            yield return Step(false, false, true);
            Check(interaction.CurrentButton == null && !label.enabled, "destroyed target clears prompt and selection");
            prompt.enabled = false;
            Check(!label.enabled, "prompt component disable hides its text");
            Check(FindFirstObjectByType<GameManager>()?.State == StageState.BeforeStage,
                "generic button events do not start a stage by themselves");

            // Exercise the generic event with an enabled stage manager.
            prompt.enabled = true;
            button.transform.position = home;
            button.gameObject.SetActive(true);
            Set(button, "inputMode", ButtonTriggerInputMode.Hold);
            Set(button, "holdDuration", 0.3f);
            GameManager game = FindFirstObjectByType<GameManager>();
            game.enabled = true;
            Physics.SyncTransforms();
            yield return StartPartialHold();
            Check(game.State == StageState.BeforeStage, "partial hold does not request a stage start");
            yield return Wait(0.32f);
            Check(game.State == StageState.BeforeStage && activations == 6,
                "button with no stage listener still does not start an enabled manager");
            button.OnTriggered.AddListener(() => game.TryStartStage());
            yield return StartPartialHold();
            Check(game.State == StageState.BeforeStage, "subscribed stage start also waits for full hold");
            yield return Wait(0.32f);
            Check(game.State == StageState.OnStage && activations == 7,
                "an external event listener can start the actual stage on completed hold");
            yield return Wait(0.35f);
            Check(activations == 7, "stage callbacks during activation do not replay the held input");
            yield return StartPartialHold();
            input.GetComponent<Mortal>().Dead();
            yield return Step(false, true);
            Check(interaction.CurrentButton == null && !label.enabled && activations == 7,
                "player death cancels interaction and hides prompt");
        }

        private ButtonTrigger CreateButton(string name, Vector3 position, ButtonTriggerInputMode mode, bool childRange = false)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            root.transform.position = position;
            GameObject area = root;
            if (childRange) { area = new GameObject("Range"); area.transform.SetParent(root.transform, false); }
            Collider range;
            if (childRange) { var box = area.AddComponent<BoxCollider>(); box.size = Vector3.one * 4f; range = box; }
            else { var sphere = area.AddComponent<SphereCollider>(); sphere.radius = 2f; range = sphere; }
            range.isTrigger = true;
            var result = root.AddComponent<ButtonTrigger>();
            Set(result, "triggerRange", range);
            Set(result, "inputMode", mode);
            root.SetActive(true);
            Physics.SyncTransforms();
            return result;
        }

        private void CreatePrompt()
        {
            var canvas = new GameObject("Validation Prompt Canvas", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var textObject = new GameObject("Interaction Prompt", typeof(RectTransform));
            textObject.SetActive(false);
            textObject.transform.SetParent(canvas.transform, false);
            label = textObject.AddComponent<Text>();
            label.fontSize = 28;
            var hud = FindFirstObjectByType<PlayerHUD>();
            label.font = ((Text)typeof(PlayerHUD).GetField("prompt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud)).font;
            prompt = textObject.AddComponent<InteractionPrompt>();
            Set(prompt, "player", interaction);
            // SendMessage would also call Text.Reset and overwrite the prompt's own defaults.
            typeof(InteractionPrompt).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(prompt, null);
            textObject.SetActive(true);
        }

        private void CapturePrompt(string name, int width, int height)
        {
            if (!ButtonTriggerValidation.RenderPrompts) return;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Prompt rendering requires a graphics device.");
            const string folder = "Logs/ButtonTrigger_20261125/Screenshots";
            Directory.CreateDirectory(folder);
            Canvas canvas = label.GetComponentInParent<Canvas>();
            var cameraObject = new GameObject("Prompt Render Camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.07f);
            camera.cullingMask = 1 << 5;
            canvas.gameObject.layer = label.gameObject.layer = 5;
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.1f;
                Canvas.ForceUpdateCanvases();
                Vector3 point = camera.WorldToScreenPoint(label.rectTransform.position);
                Check(Mathf.Abs(point.x - width * 0.5f) < 1f && Mathf.Abs(point.y - height * 0.5f) < 1f,
                    "render centers prompt at " + name);
                Check(label.preferredWidth <= label.rectTransform.rect.width && label.preferredHeight <= label.rectTransform.rect.height,
                    "render fits the complete text at " + name);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                int visiblePixels = 0;
                foreach (Color32 pixel in pixels.GetPixels32()) if (pixel.r > 160 && pixel.g > 160 && pixel.b > 160) visiblePixels++;
                Check(visiblePixels > 100, "render contains visible text at " + name);
                File.WriteAllBytes(folder + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                target.Release();
                Destroy(target); Destroy(pixels); Destroy(cameraObject);
            }
        }

        private IEnumerator StartPartialHold()
        {
            yield return Step(false, false, true);
            yield return Step(true, true);
            yield return Wait(0.1f);
        }

        private IEnumerator Step(bool press, bool hold, bool release = false)
        {
            pressed = press; held = hold; released = release;
            yield return null;
            yield return null;
        }

        private IEnumerator Wait(float seconds) { yield return new WaitForSeconds(seconds); yield return null; }

        private void Update()
        {
            if (input == null) return;
            typeof(PlayerInputReader).GetProperty("IsGameplayInputEnabled").SetValue(input,
                gameplayEnabled && input.isActiveAndEnabled && Time.timeScale > 0f);
            var state = (ButtonInputState)Activator.CreateInstance(typeof(ButtonInputState),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { pressed, held, released }, null);
            typeof(PlayerInputReader).GetProperty("Interact").SetValue(input, state);
            pressed = released = false;
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message + "; activations=" + activations + ", progress=" + interaction?.HoldProgress);
            checks++;
            Debug.Log("BUTTON CHECK " + checks + ": " + message);
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
