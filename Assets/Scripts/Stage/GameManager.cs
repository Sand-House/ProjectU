using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectU
{
    public enum StageState { BeforeStage, OnStage, AfterStage }
    public enum GameResult { None, Won, Lost, Stopped }

    [DefaultExecutionOrder(300), DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private StageData[] stages = Array.Empty<StageData>();
        [SerializeField] private StageRunner stageRunner;
        [SerializeField] private PlayerMortal player;
        [SerializeField] private ProjectilePool[] projectilePools = Array.Empty<ProjectilePool>();
        private bool suspended;
        private float previousTimeScale;
        private bool rewardCompleted;
        private int rewardCompletedFrame;
        private long rewardSequence;
        private int sessionRevision;
        private bool startingStage;
        private bool stoppingGame;
        private readonly List<IPlayerStageSubscriber> playerSubscribers = new List<IPlayerStageSubscriber>();

        public IReadOnlyList<StageData> Stages => stages;
        public StageRunner StageRunner => stageRunner;
        public StageState State { get; private set; } = StageState.BeforeStage;
        public GameResult Result { get; private set; }
        public int CurrentStageIndex { get; private set; }
        public long PendingRewardId { get; private set; }
        public bool IsChoosingReward => PendingRewardId != 0;
        public bool IsRestarting { get; private set; }
        public bool IsGameplayEnabled { get; private set; } = true;
        public int StageClearSequence { get; private set; }
        public bool IsSpawnFailed => stageRunner != null && stageRunner.IsRunning && stageRunner.HasPendingWave;
        public string LastError { get; private set; }
        public event Action<StageState> OnStageStateChanged;
        public event Action<GameResult> OnResultChanged;
        public event Action<long> OnRewardRequested;
        public event Action<bool> OnGameplayEnabledChanged;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            // World cleanup precedes player recovery and presentation subscribers.
            OnStageStateChanged += HandleStageStateChanged;
            if (player != null)
                foreach (MonoBehaviour component in player.GetComponents<MonoBehaviour>())
                    if (component is IPlayerStageSubscriber subscriber)
                    {
                        playerSubscribers.Add(subscriber);
                        subscriber.BindStageEvents(this);
                    }
            PublishStageState(State);
        }

        private void HandleStageStateChanged(StageState state)
        {
            if (state != State) return;
            if (state == StageState.AfterStage) ReturnProjectiles();
            else ResumeTime();
            SetGameplayEnabled(!IsChoosingReward && Result != GameResult.Lost &&
                !(state == StageState.OnStage && IsSpawnFailed));
        }

        // Called by the player's start interaction, never automatically by Start().
        public bool TryStartStage()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || startingStage || IsRestarting ||
                State != StageState.BeforeStage || (Result != GameResult.None && Result != GameResult.Stopped)) return false;
            if (stageRunner == null || !stageRunner.isActiveAndEnabled || player == null || !player.isActiveAndEnabled || !player.IsAlive)
                return Invalid("An active stage runner and a living player are required.");
            if (stageRunner.IsRunning) return Invalid("The stage runner already has a running or pending wave.");
            if (stages == null || stages.Length == 0) return Invalid("At least one stage is required.");
            foreach (StageData stage in stages)
                if (stage == null || !stage.TryValidate(out _)) return Invalid("Every stage must contain valid wave data.");
            int revision = sessionRevision;
            bool started;
            startingStage = true;
            try
            {
                if (Result == GameResult.Stopped)
                {
                    CurrentStageIndex = 0;
                    SetResult(GameResult.None);
                    if (revision != sessionRevision || !isActiveAndEnabled) return false;
                }
                started = stageRunner.TryBeginStage(stages[CurrentStageIndex]);
            }
            finally { startingStage = false; }
            if (revision != sessionRevision || !isActiveAndEnabled) return false;
            LastError = stageRunner.LastError;
            if (!started) return false;
            SetStageState(StageState.OnStage);
            return revision == sessionRevision && Result == GameResult.None && State == StageState.OnStage;
        }

        private void LateUpdate()
        {
            if (Result != GameResult.None) return;
            // Death wins over both clearance and a queued reward completion.
            if (player == null || !player.IsAlive)
            {
                PendingRewardId = 0;
                rewardCompleted = false;
                SuspendGameplay();
                if (stageRunner != null) stageRunner.AbortStage();
                ReturnProjectiles();
                SetResult(GameResult.Lost);
                return;
            }
            if (stageRunner == null || !stageRunner.isActiveAndEnabled)
            { StopGame(); return; }
            if (!player.isActiveAndEnabled) { StopGame(); return; }
            if (State == StageState.OnStage)
            {
                if (IsSpawnFailed)
                {
                    LastError = stageRunner.LastError;
                    SuspendGameplay();
                }
                else if (stageRunner.IsComplete) BeginAfterStage();
                else if (!stageRunner.IsRunning) StopGame();
            }
            else if (State == StageState.AfterStage && rewardCompleted && rewardCompletedFrame != Time.frameCount)
                FinishReward();
        }

        private void BeginAfterStage()
        {
            int revision = sessionRevision;
            SetStageState(StageState.AfterStage);
            if (revision != sessionRevision || !isActiveAndEnabled) return;
            if (!player.IsAlive)
            {
                SuspendGameplay();
                SetResult(GameResult.Lost);
                return;
            }
            if (revision == sessionRevision && !IsChoosingReward) TryOpenReward();
        }

        public bool TryOpenReward()
        {
            if (!isActiveAndEnabled || Result != GameResult.None || State != StageState.AfterStage ||
                IsChoosingReward || rewardCompleted || player == null || !player.IsAlive) return false;
            int revision = sessionRevision;
            long requestId = ++rewardSequence;
            PendingRewardId = requestId;
            SetGameplayEnabled(false);
            if (revision != sessionRevision || !isActiveAndEnabled || PendingRewardId != requestId) return false;
            OnRewardRequested?.Invoke(requestId);
            return revision == sessionRevision && PendingRewardId == requestId;
        }

        public bool CompleteReward(long requestId)
        {
            if (!isActiveAndEnabled || Result != GameResult.None || State != StageState.AfterStage ||
                rewardCompleted || requestId == 0 || requestId != PendingRewardId || player == null || !player.IsAlive) return false;
            rewardCompleted = true;
            rewardCompletedFrame = Time.frameCount;
            return true;
        }

        // Presenter teardown releases input without granting a reward.
        public bool CancelRewardSelection(long requestId)
        {
            if (!isActiveAndEnabled || Result != GameResult.None || State != StageState.AfterStage ||
                rewardCompleted || requestId == 0 || requestId != PendingRewardId) return false;
            PendingRewardId = 0;
            SetGameplayEnabled(true);
            return true;
        }

        private void FinishReward()
        {
            PendingRewardId = 0;
            rewardCompleted = false;
            if (CurrentStageIndex == stages.Length - 1) SetResult(GameResult.Won);
            else CurrentStageIndex++;
            SetStageState(StageState.BeforeStage);
        }

        public bool TryRetrySpawn()
        {
            if (!isActiveAndEnabled || Result != GameResult.None || !IsSpawnFailed || startingStage ||
                player == null || !player.isActiveAndEnabled || !player.IsAlive || !stageRunner.isActiveAndEnabled) return false;
            int revision = sessionRevision;
            bool started;
            startingStage = true;
            try { started = stageRunner.TryRetrySpawn(); }
            finally { startingStage = false; }
            if (revision != sessionRevision || !isActiveAndEnabled) return false;
            LastError = stageRunner.LastError;
            if (!started) return false;
            if (State == StageState.OnStage) ResumeGameplay(); // Retry does not change the stage phase.
            else SetStageState(StageState.OnStage);
            return revision == sessionRevision && Result == GameResult.None && State == StageState.OnStage;
        }

        private void SuspendGameplay()
        {
            if (!suspended)
            {
                previousTimeScale = Time.timeScale;
                suspended = true;
                Time.timeScale = 0f;
            }
            SetGameplayEnabled(false);
        }

        private void ResumeGameplay()
        {
            ResumeTime();
            SetGameplayEnabled(true);
        }

        private void SetGameplayEnabled(bool enabled)
        {
            enabled = enabled && player != null && player.IsAlive;
            if (player != null) player.GetComponent<Combatant>()?.SetGameplayBlocked(!enabled);
            if (IsGameplayEnabled == enabled) return;
            IsGameplayEnabled = enabled;
            int revision = sessionRevision;
            Delegate[] listeners = OnGameplayEnabledChanged?.GetInvocationList();
            if (listeners == null) return;
            foreach (Action<bool> listener in listeners)
            {
                if (IsGameplayEnabled != enabled || revision != sessionRevision) break;
                listener(enabled);
            }
        }

        private void ResumeTime()
        {
            if (suspended) { Time.timeScale = previousTimeScale; suspended = false; }
        }

        private void ReturnProjectiles()
        {
            if (projectilePools == null) return;
            foreach (ProjectilePool pool in projectilePools) if (pool != null) pool.ReturnAll();
        }

        // Reload the saved gameplay scene so a new run also resets resources and runtime upgrades.
        public bool TryRestartGame()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsRestarting || Result != GameResult.Lost) return false;
            string scenePath = gameObject.scene.path;
            if (string.IsNullOrEmpty(scenePath) || !Application.CanStreamedLevelBeLoaded(scenePath))
                return Invalid("The gameplay scene must be saved and enabled in Build Settings to restart.");

            IsRestarting = true;
            sessionRevision++;
            PendingRewardId = 0;
            rewardCompleted = false;
            float resumeScale = suspended ? previousTimeScale : Time.timeScale;
            if (resumeScale <= 0f || float.IsNaN(resumeScale) || float.IsInfinity(resumeScale)) resumeScale = 1f;
            suspended = false;
            Time.timeScale = resumeScale;
            try
            {
                if (SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single) == null)
                    throw new InvalidOperationException("Unity could not begin loading the gameplay scene.");
                LastError = null;
                return true;
            }
            catch (Exception exception)
            {
                IsRestarting = false;
                SuspendGameplay();
                return Invalid("Could not restart the game: " + exception.Message);
            }
        }

        public void StopGame()
        {
            if (stoppingGame) return;
            stoppingGame = true;
            try
            {
                sessionRevision++;
                PendingRewardId = 0;
                rewardCompleted = false;
                SetGameplayEnabled(false);
                if (stageRunner != null) stageRunner.AbortStage();
                ReturnProjectiles();
                ResumeGameplay();
                SetResult(GameResult.Stopped);
                SetStageState(StageState.BeforeStage);
            }
            finally { stoppingGame = false; }
        }

        private void SetStageState(StageState state)
        {
            if (State == state) return;
            if (state == StageState.AfterStage) StageClearSequence++;
            State = state;
            PublishStageState(state);
        }

        private void PublishStageState(StageState state)
        {
            int revision = sessionRevision;
            Delegate[] listeners = OnStageStateChanged?.GetInvocationList();
            if (listeners == null) return;
            foreach (Action<StageState> listener in listeners)
            {
                // A listener can stop the run; do not deliver the superseded phase afterwards.
                if (State != state || revision != sessionRevision) break;
                listener(state);
            }
        }

        private void SetResult(GameResult result)
        {
            if (Result == result) return;
            Result = result;
            OnResultChanged?.Invoke(result);
        }

        private bool Invalid(string error) { LastError = error; return false; }
        private void OnDisable()
        {
            if (Application.isPlaying) StopGame();
            foreach (IPlayerStageSubscriber subscriber in playerSubscribers) subscriber.UnbindStageEvents(this);
            playerSubscribers.Clear();
            OnStageStateChanged -= HandleStageStateChanged;
        }
    }
}
