using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    [CreateAssetMenu(fileName = "StageData", menuName = "ProjectU/Stage Data")]
    public sealed class StageData : ScriptableObject
    {
        [SerializeField] private WaveData[] waves = Array.Empty<WaveData>();
        public IReadOnlyList<WaveData> Waves => waves;

        public bool TryValidate(out string error)
        {
            error = null;
            if (waves == null || waves.Length == 0) { error = "A stage must contain at least one wave."; return false; }
            for (int index = 0; index < waves.Length; index++)
            {
                if (waves[index] == null) { error = $"Wave {index + 1} is missing."; return false; }
                if (!waves[index].TryValidate(index == waves.Length - 1, out error))
                { error = $"Wave {index + 1}: {error}"; return false; }
            }
            return true;
        }

    }
}
