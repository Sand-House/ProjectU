using UnityEngine;
using UnityEngine.UI;

namespace ProjectU
{
    [DefaultExecutionOrder(400), DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerStat), typeof(PlayerCombat))]
    public sealed class PlayerHUD : MonoBehaviour
    {
        [SerializeField] private GameManager game;
        [SerializeField] private Color healthColor = new Color(0.94f, 0.97f, 1f);
        [SerializeField] private Color lowHealthColor = new Color(1f, 0.28f, 0.24f);
        [SerializeField] private Color crosshairColor = Color.white;

        private Mortal health;
        private PlayerStat stat;
        private PlayerCombat combat;
        private GameObject canvasRoot;
        private RectTransform safeArea;
        private GameObject healthWidget;
        private GameObject ammoWidget;
        private GameObject crosshair;
        private Text healthNumber;
        private Text ammoNumber;
        private Text prompt;
        private Font promptFont;
        private Image healthFill;
        private Font font;
        private bool ownsFont;
        private int displayedHealth = -1;
        private int displayedMaxHealth = -1;
        private int displayedAmmo = -1;
        private int displayedMaxAmmo = -1;
        private Rect displayedSafeArea;
        private Vector2Int displayedScreenSize;

        public Canvas Canvas => canvasRoot != null ? canvasRoot.GetComponent<Canvas>() : null;

        private void Awake()
        {
            health = GetComponent<Mortal>();
            stat = GetComponent<PlayerStat>();
            combat = GetComponent<PlayerCombat>();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Malgun Gothic" }, 54);
            ownsFont = font != null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            canvasRoot = new GameObject("Player HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasRoot.transform.SetParent(transform, false);
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 90; // StageFlowUI's reward and death modals render above the HUD.
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            safeArea = Node("Safe Area", canvasRoot.transform);
            BuildHealth();
            BuildAmmo();
            BuildPrompt();
            BuildCrosshair();
            RefreshPresentation();
            canvasRoot.SetActive(isActiveAndEnabled);
        }

        private void BuildHealth()
        {
            RectTransform widget = Node("Health Widget", safeArea);
            Place(widget, Vector2.zero, Vector2.zero, new Vector2(44f, 36f), new Vector2(256f, 92f));
            healthWidget = widget.gameObject;
            healthNumber = Label("Current Health", widget, 54, TextAnchor.LowerLeft);
            Place(healthNumber.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0f, 28f), new Vector2(256f, 64f));

            Image track = Block("Health Track", widget, new Color(0.025f, 0.04f, 0.065f, 0.8f));
            Place(track.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0f, 8f), new Vector2(252f, 18f));
            RectTransform inset = Node("Bar Interior", track.transform);
            Stretch(inset, 2f);
            healthFill = Block("Health Fill", inset, healthColor);
            Stretch(healthFill.rectTransform, 0f);
            for (int i = 1; i < 10; i++)
            {
                Image divider = Block("Division " + i, inset, new Color(0.025f, 0.04f, 0.065f, 0.85f));
                RectTransform rect = divider.rectTransform;
                rect.anchorMin = new Vector2(i / 10f, 0f);
                rect.anchorMax = new Vector2(i / 10f, 1f);
                rect.sizeDelta = new Vector2(2f, 0f);
                rect.anchoredPosition = Vector2.zero;
            }
        }

        private void BuildAmmo()
        {
            RectTransform widget = Node("Ammo Widget", safeArea);
            Place(widget, Vector2.right, Vector2.right, new Vector2(-44f, 44f), new Vector2(260f, 76f));
            ammoWidget = widget.gameObject;
            ammoNumber = Label("Magazine Ammo", widget, 54, TextAnchor.LowerRight);
            Stretch(ammoNumber.rectTransform, 0f);
        }

        private void BuildPrompt()
        {
            Image hint = Block("Interaction Prompt", safeArea, new Color(0.035f, 0.065f, 0.1f, 0.92f));
            Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(440f, 64f));
            promptFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 20);
            prompt = Node("Prompt", hint.transform).gameObject.AddComponent<Text>();
            prompt.font = promptFont != null ? promptFont : font;
            prompt.fontSize = 20;
            prompt.color = new Color(0.94f, 0.97f, 1f);
            prompt.alignment = TextAnchor.MiddleCenter;
            prompt.raycastTarget = false;
            Stretch(prompt.rectTransform, 0f);
            prompt.rectTransform.offsetMin = new Vector2(12f, 8f);
            prompt.rectTransform.offsetMax = new Vector2(-12f, -8f);
        }

        private void BuildCrosshair()
        {
            RectTransform root = Node("Crosshair", canvasRoot.transform);
            Place(root, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, new Vector2(32f, 32f));
            crosshair = root.gameObject;
            CrosshairPart(root, "Top", new Vector2(0f, 8f), new Vector2(2f, 7f));
            CrosshairPart(root, "Bottom", new Vector2(0f, -8f), new Vector2(2f, 7f));
            CrosshairPart(root, "Left", new Vector2(-8f, 0f), new Vector2(7f, 2f));
            CrosshairPart(root, "Right", new Vector2(8f, 0f), new Vector2(7f, 2f));
            CrosshairPart(root, "Center", Vector2.zero, new Vector2(2f, 2f));
        }

        private void CrosshairPart(Transform parent, string name, Vector2 position, Vector2 size)
        {
            Image outline = Block(name + " Outline", parent, new Color(0f, 0f, 0f, 0.8f));
            Place(outline.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, position, size + Vector2.one * 2f);
            Image line = Block(name, outline.transform, crosshairColor);
            Place(line.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, size);
        }

        private void LateUpdate() => RefreshPresentation();

        private void RefreshPresentation()
        {
            RefreshSafeArea();
            RefreshPrompt();
            bool hasHealth = health != null && stat != null;
            healthWidget.SetActive(hasHealth);
            ammoWidget.SetActive(combat != null);
            if (hasHealth)
            {
                int current = Mathf.Max(0, health.HP);
                int maximum = Mathf.Max(1, stat.MaxHealth);
                if (current != displayedHealth || maximum != displayedMaxHealth)
                {
                    displayedHealth = current;
                    displayedMaxHealth = maximum;
                    healthNumber.text = current.ToString();
                    float ratio = Mathf.Clamp01((float)current / maximum);
                    healthFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
                    Color color = ratio <= 0.3f ? lowHealthColor : healthColor;
                    healthNumber.color = healthFill.color = color;
                }
            }
            if (combat != null)
            {
                int current = combat.CurrentAmmo;
                int maximum = combat.MaxAmmo;
                if (current != displayedAmmo || maximum != displayedMaxAmmo)
                {
                    displayedAmmo = current;
                    displayedMaxAmmo = maximum;
                    // Reserve ammo is unlimited; the denominator is the magazine capacity.
                    ammoNumber.text = $"{current}<size=28><color=#CBD4DF> / {maximum}</color></size>";
                }
            }
            crosshair.SetActive(hasHealth && !health.isDead && combat != null && combat.isActiveAndEnabled &&
                (game == null || (game.isActiveAndEnabled && game.IsGameplayEnabled)));
        }

        private void RefreshPrompt()
        {
            bool show = game != null && game.isActiveAndEnabled;
            prompt.transform.parent.gameObject.SetActive(show);
            if (!show) return;
            string message = game.Result == GameResult.Won ? "모든 스테이지 클리어" :
                game.Result == GameResult.Lost ? "플레이어 사망" :
                game.IsChoosingReward ? "보상을 선택하여 계속하세요" :
                game.IsSpawnFailed ? "적 생성 실패 · 설정 확인 후 재시도" :
                game.State == StageState.BeforeStage ? "[G] 상호작용 · 스테이지 시작" :
                game.State == StageState.AfterStage ? "[G] 보상 선택" : "적을 처치하여 다음 웨이브로 진행하세요";
            if (prompt.text != message) prompt.text = message;
        }

        private void RefreshSafeArea()
        {
            Rect area = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size.x <= 0 || size.y <= 0 || (area == displayedSafeArea && size == displayedScreenSize)) return;
            displayedSafeArea = area;
            displayedScreenSize = size;
            safeArea.anchorMin = new Vector2(area.xMin / size.x, area.yMin / size.y);
            safeArea.anchorMax = new Vector2(area.xMax / size.x, area.yMax / size.y);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }

        private void OnEnable()
        {
            if (canvasRoot == null) return;
            RefreshPresentation();
            canvasRoot.SetActive(true);
        }

        private void OnDisable()
        {
            if (canvasRoot != null) canvasRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (canvasRoot != null) Destroy(canvasRoot);
            if (ownsFont && font != null) Destroy(font);
            if (promptFont != null) Destroy(promptFont);
        }

        private static RectTransform Node(string name, Transform parent)
        {
            var node = new GameObject(name, typeof(RectTransform));
            node.transform.SetParent(parent, false);
            return node.GetComponent<RectTransform>();
        }

        private static Image Block(string name, Transform parent, Color color)
        {
            RectTransform node = Node(name, parent);
            Image image = node.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text Label(string name, Transform parent, int size, TextAnchor alignment)
        {
            RectTransform node = Node(name, parent);
            Text label = node.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = FontStyle.BoldAndItalic;
            label.alignment = alignment;
            label.color = healthColor;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            Shadow shadow = node.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
            return label;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
        }
    }
}
