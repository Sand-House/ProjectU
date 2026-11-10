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
        private bool viewReady;
        private long displayedRewardId;
        private bool selectionSubmitted;
        private bool restartFailed;
        private StageState displayedState;
        private string phaseLabel;

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
                Debug.LogError("StageFlowUI requires its scene Canvas and UI references.", this);
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
            retryPanel.SetActive(game.IsSpawnFailed && game.Result == GameResult.None);
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
