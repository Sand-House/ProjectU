using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectU
{
    // Temporary presentation only. Reward effects will be supplied by a future reward system.
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
        [SerializeField] private Button selectReward;
        [SerializeField] private Button retry;
        [SerializeField] private Button restart;
        [SerializeField] private Text restartDescription;

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
        public Button RewardButton => selectReward;
        public bool IsDeathVisible => deathModal != null && deathModal.activeInHierarchy;
        public Button RestartButton => restart;
        public Canvas Canvas => canvas;

        private void Awake()
        {
            viewReady = canvas != null && modal != null && deathModal != null && retryPanel != null &&
                status != null && rewardTitle != null && selectReward != null && retry != null &&
                restart != null && restartDescription != null;
            if (!viewReady)
            {
                Debug.LogError(MissingSceneUIMessage, this);
                enabled = false;
                return;
            }
            canvas.gameObject.SetActive(isActiveAndEnabled);
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
            if (show && (!modal.activeSelf || displayedRewardId != game.PendingRewardId))
            {
                displayedRewardId = game.PendingRewardId;
                selectionSubmitted = false;
                selectReward.interactable = true;
                rewardTitle.text = string.Format(rewardTitleFormat, game.CurrentStageIndex + 1);
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
                restartDescription.text = game.IsRestarting ? restartingMessage :
                    !restartFailed ? deathMessage : restartFailedMessage;
                if (openedDeath && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(restart.gameObject);
            }
        }

        public void SelectReward()
        {
            if (!isActiveAndEnabled || selectionSubmitted || game == null || !game.CompleteReward(displayedRewardId)) return;
            selectionSubmitted = true;
            selectReward.interactable = false;
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
