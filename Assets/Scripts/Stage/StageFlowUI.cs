using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ProjectU
{
    // Temporary presentation only. Reward effects will be supplied by a future reward system.
    [DefaultExecutionOrder(350), DisallowMultipleComponent]
    public sealed class StageFlowUI : MonoBehaviour
    {
        [SerializeField] private GameManager game;
        [SerializeField] private PlayerInputReader input;
        private GameObject canvasRoot;
        private GameObject modal;
        private GameObject deathModal;
        private Text status;
        private Text rewardTitle;
        private Button selectReward;
        private Button retry;
        private Button restart;
        private Text restartDescription;
        private Font font;
        private bool ownsFont;
        private long displayedRewardId;
        private bool selectionSubmitted;
        private bool restartFailed;
        private StageState displayedState;
        private string phaseLabel;

        public bool IsRewardVisible => modal != null && modal.activeInHierarchy;
        public Button RewardButton => selectReward;
        public bool IsDeathVisible => deathModal != null && deathModal.activeInHierarchy;
        public Button RestartButton => restart;
        public Canvas Canvas => canvasRoot != null ? canvasRoot.GetComponent<Canvas>() : null;

        private void Awake()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 20);
            ownsFont = font != null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasRoot = new GameObject("Stage Flow Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform hud = Panel("Stage Status", canvasRoot.transform, new Color(0.035f, 0.065f, 0.1f, 0.92f));
            Place(hud, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(350f, 98f));
            status = Label("Status", hud, "", 20, TextAnchor.MiddleLeft);
            Stretch(status.rectTransform, 18f, 12f);

            RectTransform retryPanel = Panel("Retry Spawn", canvasRoot.transform, new Color(0f, 0f, 0f, 0f));
            Place(retryPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 64f));
            retry = MakeButton(retryPanel, "생성 재시도", new Color(0.75f, 0.38f, 0.12f));
            Stretch(retry.GetComponent<RectTransform>(), 0f, 0f);
            retry.onClick.AddListener(() => game.TryRetrySpawn());
            retryPanel.gameObject.SetActive(false);

            RectTransform backdrop = Panel("Reward Modal", canvasRoot.transform, new Color(0f, 0.015f, 0.035f, 0.72f));
            Stretch(backdrop, 0f, 0f);
            backdrop.GetComponent<Image>().raycastTarget = true;
            modal = backdrop.gameObject;
            RectTransform card = Panel("Reward Card", backdrop, new Color(0.055f, 0.095f, 0.15f, 1f));
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580f, 320f));
            rewardTitle = Label("Title", card, "스테이지 클리어", 30, TextAnchor.MiddleCenter);
            Place(rewardTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(520f, 58f));
            Text description = Label("Description", card,
                "더미 보상\n선택하면 보상 창을 닫고 준비 단계로 돌아갑니다.\n현재 능력치는 변경되지 않습니다.", 19, TextAnchor.MiddleCenter);
            description.color = new Color(0.73f, 0.81f, 0.89f);
            Place(description.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(520f, 112f));
            selectReward = MakeButton(card, "보상 선택 후 계속", new Color(0.12f, 0.49f, 0.62f));
            Place(selectReward.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(420f, 58f));
            selectReward.onClick.AddListener(SelectReward);
            modal.SetActive(false);

            RectTransform deathBackdrop = Panel("Death Modal", canvasRoot.transform, new Color(0.025f, 0f, 0.01f, 0.78f));
            Stretch(deathBackdrop, 0f, 0f);
            deathBackdrop.GetComponent<Image>().raycastTarget = true;
            deathModal = deathBackdrop.gameObject;
            RectTransform deathCard = Panel("Death Card", deathBackdrop, new Color(0.12f, 0.055f, 0.075f, 1f));
            Place(deathCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580f, 320f));
            Text deathTitle = Label("Title", deathCard, "작전 실패", 30, TextAnchor.MiddleCenter);
            Place(deathTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(520f, 58f));
            restartDescription = Label("Description", deathCard, "", 19, TextAnchor.MiddleCenter);
            restartDescription.color = new Color(0.89f, 0.78f, 0.81f);
            Place(restartDescription.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(520f, 112f));
            restart = MakeButton(deathCard, "첫 스테이지로 돌아가기", new Color(0.65f, 0.23f, 0.3f));
            Place(restart.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(420f, 58f));
            restart.onClick.AddListener(RestartGame);
            deathModal.SetActive(false);
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject events = new GameObject("Stage UI Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(canvasRoot.transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            canvasRoot.SetActive(isActiveAndEnabled);
        }

        private void Update()
        {
            if (game == null || input == null || !game.isActiveAndEnabled ||
                !input.IsGameplayInputEnabled || !input.Interact.Pressed) return;
            if (game.IsSpawnFailed) game.TryRetrySpawn();
            else if (game.State == StageState.BeforeStage) game.TryStartStage();
            else if (game.State == StageState.AfterStage) game.TryOpenReward();
        }

        private void LateUpdate()
        {
            if (game == null || !game.isActiveAndEnabled)
            {
                canvasRoot.SetActive(false);
                return;
            }
            canvasRoot.SetActive(true);
            RefreshPresentation();
        }

        private void HandleStageStateChanged(StageState state)
        {
            if (game == null || state != game.State || canvasRoot == null) return;
            displayedState = state;
            phaseLabel = state == StageState.BeforeStage ? "준비" : state == StageState.OnStage ? "전투" : "클리어";
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            StageRunner runner = game.StageRunner;
            status.text = $"STAGE {game.CurrentStageIndex + 1}  /  {game.Stages.Count}   ·   {phaseLabel}\n" +
                (game.Result == GameResult.Lost ? "전투 종료" :
                 displayedState == StageState.OnStage && runner != null
                    ? $"WAVE {runner.CurrentWaveIndex + 1}   ·   남은 적 {runner.RemainingEnemyCount}"
                    : game.IsChoosingReward ? "보상 선택 중 · 캐릭터 입력 잠금" : "이동 · 점프 · 전투 조작 가능");
            retry.transform.parent.gameObject.SetActive(game.IsSpawnFailed && game.Result == GameResult.None);
            bool show = game.IsChoosingReward && game.Result == GameResult.None;
            if (show && (!modal.activeSelf || displayedRewardId != game.PendingRewardId))
            {
                displayedRewardId = game.PendingRewardId;
                selectionSubmitted = false;
                selectReward.interactable = true;
                rewardTitle.text = $"STAGE {game.CurrentStageIndex + 1} 클리어";
                modal.SetActive(true);
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(selectReward.gameObject);
            }
            else if (!show) modal.SetActive(false);
            bool showDeath = game.Result == GameResult.Lost;
            bool openedDeath = showDeath && !deathModal.activeSelf;
            if (openedDeath) restartFailed = false;
            deathModal.SetActive(showDeath);
            if (showDeath)
            {
                restart.interactable = !game.IsRestarting;
                restartDescription.text = game.IsRestarting ? "첫 스테이지를 준비하고 있습니다…" :
                    !restartFailed ? "플레이어가 사망했습니다.\n첫 스테이지부터 다시 도전할 수 있습니다." :
                    "재시작하지 못했습니다. 다시 시도해 주세요.";
                if (openedDeath && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(restart.gameObject);
            }
        }

        private void SelectReward()
        {
            if (selectionSubmitted || game == null || !game.CompleteReward(displayedRewardId)) return;
            selectionSubmitted = true;
            selectReward.interactable = false;
        }

        private void RestartGame()
        {
            if (game == null) return;
            if (!game.TryRestartGame()) { restartFailed = true; return; }
            restartFailed = false;
            restart.interactable = false;
        }

        private void OnEnable()
        {
            if (canvasRoot != null) canvasRoot.SetActive(true);
            if (game == null) return;
            game.OnStageStateChanged += HandleStageStateChanged;
            HandleStageStateChanged(game.State);
        }
        private void OnDisable()
        {
            if (game != null)
            {
                game.OnStageStateChanged -= HandleStageStateChanged;
                game.CancelRewardSelection(game.PendingRewardId);
            }
            if (canvasRoot != null) canvasRoot.SetActive(false);
        }
        private void OnDestroy()
        {
            if (canvasRoot != null) Destroy(canvasRoot);
            if (ownsFont && font != null) Destroy(font);
        }

        private RectTransform Panel(string name, Transform parent, Color color)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(Image));
            node.transform.SetParent(parent, false);
            Image image = node.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return node.GetComponent<RectTransform>();
        }

        private Text Label(string name, Transform parent, string text, int size, TextAnchor alignment)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(Text));
            node.transform.SetParent(parent, false);
            Text label = node.GetComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = new Color(0.94f, 0.97f, 1f);
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        private Button MakeButton(Transform parent, string title, Color color)
        {
            RectTransform node = Panel(title, parent, color);
            node.GetComponent<Image>().raycastTarget = true;
            Button button = node.gameObject.AddComponent<Button>();
            button.targetGraphic = node.GetComponent<Image>();
            Text label = Label("Label", node, title, 22, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 10f, 4f);
            return button;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, float horizontal, float vertical)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontal, vertical);
            rect.offsetMax = new Vector2(-horizontal, -vertical);
        }
    }
}
