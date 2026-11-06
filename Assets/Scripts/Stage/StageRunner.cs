using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    // Combat actions finish at order 110. Evaluate progression once, after all those effects.
    [DefaultExecutionOrder(200), DisallowMultipleComponent]
    public sealed class StageRunner : MonoBehaviour
    {
        [SerializeField] private EnemySpawnManager spawnManager;
        private sealed class Subscription
        {
            public Mortal Health;
            public Action<GameObject> OnDead;
        }
        private readonly HashSet<EnemyAI> aliveEnemies = new HashSet<EnemyAI>();
        private readonly Dictionary<EnemyAI, Subscription> subscriptions = new Dictionary<EnemyAI, Subscription>();
        private readonly List<EnemyAI> removedEnemies = new List<EnemyAI>();
        private int pendingWaveIndex = -1;
        private int lastSpawnFrame = -1;
        private bool operationInProgress;
        private bool countChanged;

        public EnemySpawnManager SpawnManager => spawnManager;
        public StageData CurrentStage { get; private set; }
        public int CurrentWaveIndex { get; private set; } = -1;
        public int RemainingEnemyCount => aliveEnemies.Count;
        // The executor owns work, while GameManager owns the game phase.
        public bool IsRunning { get; private set; }
        public bool IsComplete { get; private set; }
        public bool HasPendingWave => pendingWaveIndex >= 0;
        public string LastError { get; private set; }
        public event Action<int> OnRemainingEnemyCountChanged;
        public event Action<int> OnWaveStarted;
        public event Action OnStageCleared;
        public event Action<string> OnSpawnFailed;

        public bool TryBeginStage(StageData data)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || operationInProgress ||
                IsRunning) return false;
            CurrentStage = data;
            CurrentWaveIndex = -1;
            pendingWaveIndex = 0;
            IsRunning = true;
            IsComplete = false;
            LastError = null;
            return TrySpawnPendingWave();
        }

        public bool TryRetrySpawn()
            => IsRunning && HasPendingWave && isActiveAndEnabled && !operationInProgress && TrySpawnPendingWave();

        private bool TrySpawnPendingWave()
        {
            operationInProgress = true;
            try
            {
                if (spawnManager == null) return SpawnFailed("Enemy spawn manager is missing.");
                if (!spawnManager.CanSpawnStage(CurrentStage, out string error)) return SpawnFailed(error);
                if (!spawnManager.TrySpawnWave(CurrentStage.Waves[pendingWaveIndex], out EnemyAI[] enemies))
                    return SpawnFailed(spawnManager.LastError);
                // Register the complete batch before listeners can observe or kill any of it.
                foreach (EnemyAI enemy in enemies)
                {
                    Mortal health = enemy.GetComponent<Mortal>();
                    Action<GameObject> onDead = attacker => RemoveEnemy(enemy);
                    aliveEnemies.Add(enemy);
                    subscriptions.Add(enemy, new Subscription { Health = health, OnDead = onDead });
                    health.OnDead += onDead;
                    enemy.OnRemoved += RemoveEnemy;
                }
                CurrentWaveIndex = pendingWaveIndex;
                pendingWaveIndex = -1;
                lastSpawnFrame = Time.frameCount;
                LastError = null;
                countChanged = true;
                OnWaveStarted?.Invoke(CurrentWaveIndex);
                return true;
            }
            finally { operationInProgress = false; }
        }

        private bool SpawnFailed(string error)
        {
            LastError = error;
            OnSpawnFailed?.Invoke(error);
            return false;
        }

        private void RemoveEnemy(EnemyAI enemy)
        {
            if (!aliveEnemies.Remove(enemy)) return;
            Unsubscribe(enemy);
            countChanged = true;
        }

        private void Unsubscribe(EnemyAI enemy)
        {
            if (!subscriptions.TryGetValue(enemy, out Subscription subscription)) return;
            if (subscription.Health != null) subscription.Health.OnDead -= subscription.OnDead;
            if (enemy != null) enemy.OnRemoved -= RemoveEnemy;
            subscriptions.Remove(enemy);
        }

        private void LateUpdate()
        {
            if (!IsRunning) return;
            // Also repair missed death notifications (e.g. an earlier listener threw).
            removedEnemies.Clear();
            foreach (EnemyAI enemy in aliveEnemies)
                if (enemy == null || subscriptions[enemy].Health == null || subscriptions[enemy].Health.isDead)
                    removedEnemies.Add(enemy);
            foreach (EnemyAI enemy in removedEnemies) RemoveEnemy(enemy);
            if (countChanged)
            {
                countChanged = false;
                OnRemainingEnemyCountChanged?.Invoke(RemainingEnemyCount);
            }
            if (!IsRunning || HasPendingWave || operationInProgress || lastSpawnFrame == Time.frameCount ||
                Time.timeScale <= 0f || Time.deltaTime <= 0f) return;
            if (CurrentWaveIndex == CurrentStage.Waves.Count - 1)
            {
                if (RemainingEnemyCount != 0) return;
                IsRunning = false;
                IsComplete = true;
                OnStageCleared?.Invoke();
            }
            else if (RemainingEnemyCount <= CurrentStage.Waves[CurrentWaveIndex].NextWaveRemainingEnemyThreshold)
            {
                pendingWaveIndex = CurrentWaveIndex + 1;
                TrySpawnPendingWave();
            }
        }

        public void AbortStage()
        {
            bool hadEnemies = aliveEnemies.Count > 0;
            IsRunning = false;
            IsComplete = false; // Teardown must never look like a successful clear.
            foreach (EnemyAI enemy in new List<EnemyAI>(aliveEnemies))
            {
                Unsubscribe(enemy);
                if (enemy != null)
                {
                    enemy.gameObject.SetActive(false);
                    Destroy(enemy.gameObject);
                }
            }
            aliveEnemies.Clear();
            subscriptions.Clear();
            countChanged = false;
            pendingWaveIndex = -1;
            LastError = null;
            if (hadEnemies) OnRemainingEnemyCountChanged?.Invoke(0);
        }

        private void OnDisable() => AbortStage();
    }
}
