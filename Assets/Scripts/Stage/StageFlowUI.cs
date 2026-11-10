using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectU
{
    // Rarity selection only. Reward effects will be supplied by a future reward system.
    [DefaultExecutionOrder(350), DisallowMultipleComponent]
    public sealed class StageFlowUI : MonoBehaviour
    {
        [SerializeField] private GameManager game;
        [SerializeField] private PlayerInputReader input;
        [Header("Scene UI")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private GameObject modal;
        [SerializeField] private GameObject deathModal;
        [SerializeField] private GameObject retryPanel;
        [SerializeField] private Text status;
        [SerializeField] private Text rewardTitle;
        [SerializeField] private RewardCardView[] rewardCards = new RewardCardView[3];
        [SerializeField] private Button retry;
        [SerializeField] private Button restart;
        [SerializeField] private Text restartDescription;

        [Header("Reward Cards")]
        [Tooltip("Each card is rolled independently. Totals below 100 are normalized; all zero restores defaults.")]
        [SerializeField] private RewardRarityChances rarityChances = new RewardRarityChances();
        [SerializeField] private Color commonCardColor = new Color(0.38f, 0.41f, 0.46f, 1f);
        [SerializeField] private Color rareCardColor = new Color(0.12f, 0.42f, 0.82f, 1f);
        [SerializeField] private Color epicCardColor = new Color(0.52f, 0.24f, 0.76f, 1f);
        [SerializeField] private Color uniqueCardColor = new Color(0.95f, 0.72f, 0.12f, 1f);

        [Header("Phase Text")]
        [SerializeField] private string beforeStageLabel = "준비";
        [SerializeField] private string onStageLabel = "전투";
        [SerializeField] private string afterStageLabel = "클리어";
        [Header("Status Text")]
        [Tooltip("{0}: stage number, {1}: stage count, {2}: phase label.")]
        [TextArea, SerializeField] private string stageStatusFormat = "STAGE {0}  /  {1}   ·   {2}\n";
        [Tooltip("{0}: wave number, {1}: remaining enemy count.")]
        [SerializeField] private string waveStatusFormat = "WAVE {0}   ·   남은 적 {1}";
        [SerializeField] private string combatEndedStatus = "전투 종료";
        [SerializeField] private string choosingRewardStatus = "보상 선택 중 · 캐릭터 입력 잠금";
        [SerializeField] private string controlsEnabledStatus = "이동 · 점프 · 전투 조작 가능";
        [Header("Reward Text")]
        [Tooltip("{0}: cleared stage number.")]
        [SerializeField] private string rewardTitleFormat = "STAGE {0} 클리어";
        [Header("Restart Text")]
        [TextArea, SerializeField] private string restartingMessage = "첫 스테이지를 준비하고 있습니다…";
        [TextArea, SerializeField] private string deathMessage = "플레이어가 사망했습니다.\n첫 스테이지부터 다시 도전할 수 있습니다.";
        [TextArea, SerializeField] private string restartFailedMessage = "재시작하지 못했습니다. 다시 시도해 주세요.";

        private const string MissingSceneUIMessage = "StageFlowUI requires its scene Canvas and UI references.";

        private bool viewReady;
        private long displayedRewardId;
        private bool selectionSubmitted;
        private bool restartFailed;
        private StageState displayedState;

        public bool IsRewardVisible => modal != null && modal.activeInHierarchy;
        public Button RewardButton => rewardCards != null && rewardCards.Length > 0 ? rewardCards[0]?.Button : null;
        public IReadOnlyList<RewardCardView> RewardCards => rewardCards;
        public bool IsDeathVisible => deathModal != null && deathModal.activeInHierarchy;
        public Button RestartButton => restart;
        public Canvas Canvas => canvas;

        private void Awake()
        {
            OnValidate();
            viewReady = canvas != null && modal != null && deathModal != null && retryPanel != null &&
                status != null && rewardTitle != null && retry != null &&
                restart != null && restartDescription != null;
            viewReady &= rewardCards != null && rewardCards.Length == 3;
            if (viewReady)
                for (int i = 0; i < rewardCards.Length; i++)
                {
                    if (rewardCards[i] == null || !rewardCards[i].IsAssigned) { viewReady = false; break; }
                    for (int j = 0; j < i; j++)
                        if (rewardCards[i].Button == rewardCards[j].Button) viewReady = false;
                }
            if (!viewReady)
            {
                Debug.LogError(MissingSceneUIMessage, this);
                enabled = false;
                return;
            }
            canvas.gameObject.SetActive(isActiveAndEnabled);
        }

        private void OnValidate()
        {
            if (rarityChances == null) rarityChances = new RewardRarityChances();
            rarityChances.Validate();
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
                canvas.gameObject.SetActive(false);
                return;
            }
            canvas.gameObject.SetActive(true);
            RefreshPresentation();
        }

        private void HandleStageStateChanged(StageState state)
        {
            if (game == null || state != game.State || canvas == null) return;
            displayedState = state;
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            StageRunner runner = game.StageRunner;
            string phaseLabel = displayedState == StageState.BeforeStage ? beforeStageLabel :
                displayedState == StageState.OnStage ? onStageLabel : afterStageLabel;
            status.text = string.Format(stageStatusFormat, game.CurrentStageIndex + 1, game.Stages.Count, phaseLabel) +
                (game.Result == GameResult.Lost ? combatEndedStatus :
                 displayedState == StageState.OnStage && runner != null
                    ? string.Format(waveStatusFormat, runner.CurrentWaveIndex + 1, runner.RemainingEnemyCount)
                    : game.IsChoosingReward ? choosingRewardStatus : controlsEnabledStatus);
            retryPanel.SetActive(game.IsSpawnFailed && game.Result == GameResult.None);
            bool show = game.IsChoosingReward && game.Result == GameResult.None;
            if (show)
            {
                // Reopening the same view must not reroll an existing request.
                if (displayedRewardId != game.PendingRewardId)
                {
                    displayedRewardId = game.PendingRewardId;
                    selectionSubmitted = false;
                    foreach (RewardCardView card in rewardCards)
                    {
                        RewardRarity rarity = rarityChances.Roll();
                        card.Show(rarity, CardColor(rarity));
                    }
                }
                rewardTitle.text = string.Format(rewardTitleFormat, game.CurrentStageIndex + 1);
                bool opened = !modal.activeSelf;
                modal.SetActive(true);
                if (opened && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(RewardButton.gameObject);
            }
            else if (!show) modal.SetActive(false);
            bool showDeath = game.Result == GameResult.Lost;
            bool openedDeath = showDeath && !deathModal.activeSelf;
            if (openedDeath) restartFailed = false;
            deathModal.SetActive(showDeath);
            if (showDeath)
            {
                restart.interactable = !game.IsRestarting;
                restartDescription.text = game.IsRestarting ? restartingMessage :
                    !restartFailed ? deathMessage : restartFailedMessage;
                if (openedDeath && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(restart.gameObject);
            }
        }

        private Color CardColor(RewardRarity rarity)
        {
            switch (rarity)
            {
                case RewardRarity.Rare: return rareCardColor;
                case RewardRarity.Epic: return epicCardColor;
                case RewardRarity.Unique: return uniqueCardColor;
                default: return commonCardColor;
            }
        }

        public void SelectReward(int cardIndex)
        {
            if (!isActiveAndEnabled || !IsRewardVisible || selectionSubmitted || game == null ||
                cardIndex < 0 || cardIndex >= rewardCards.Length || !rewardCards[cardIndex].Button.interactable ||
                !game.CompleteReward(displayedRewardId)) return;
            selectionSubmitted = true;
            foreach (RewardCardView card in rewardCards) card.Button.interactable = false;
        }

        public void RestartGame()
        {
            if (!isActiveAndEnabled || game == null) return;
            if (!game.TryRestartGame()) { restartFailed = true; return; }
            restartFailed = false;
            restart.interactable = false;
        }

        public void RetrySpawn()
        {
            if (isActiveAndEnabled && game != null) game.TryRetrySpawn();
        }

        private void OnEnable()
        {
            if (!viewReady) return;
            canvas.gameObject.SetActive(true);
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
            if (canvas != null) canvas.gameObject.SetActive(false);
        }
    }
}
