using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    [Serializable]
    public sealed class WaveData
    {
        public const int MaxSpawnGroups = 4;
        [SerializeField] private EnemySpawnGroup[] spawnGroups = Array.Empty<EnemySpawnGroup>();
        [SerializeField, Min(0), Tooltip("Start the next wave when total surviving stage enemies reach this count or less. Ignored for the final wave.")]
        private int nextWaveRemainingEnemyThreshold;
        public IReadOnlyList<EnemySpawnGroup> SpawnGroups => spawnGroups;
        public int NextWaveRemainingEnemyThreshold => nextWaveRemainingEnemyThreshold;

        public WaveData(int nextWaveRemainingEnemyThreshold, params EnemySpawnGroup[] spawnGroups)
        {
            this.nextWaveRemainingEnemyThreshold = nextWaveRemainingEnemyThreshold;
            if (spawnGroups == null) { this.spawnGroups = null; return; }
            this.spawnGroups = new EnemySpawnGroup[spawnGroups.Length];
            for (int index = 0; index < spawnGroups.Length; index++) this.spawnGroups[index] = spawnGroups[index]?.Copy();
        }

        public bool TryValidate(bool isLastWave, out string error)
        {
            error = null;
            if (spawnGroups == null || spawnGroups.Length < 1 || spawnGroups.Length > MaxSpawnGroups)
            { error = "A wave must contain 1 to 4 spawn groups."; return false; }
            int total = 0;
            foreach (EnemySpawnGroup group in spawnGroups)
            {
                if (group == null) { error = "A spawn group is missing."; return false; }
                if (!group.TryValidate(out error, out int count)) return false;
                if (count > int.MaxValue - total) { error = "Wave count exceeds the integer limit."; return false; }
                total += count;
            }
            if (!isLastWave && (nextWaveRemainingEnemyThreshold < 0 || nextWaveRemainingEnemyThreshold >= total))
            { error = "The next-wave threshold must be nonnegative and smaller than this wave's enemy count."; return false; }
            return true;
        }

    }
}
