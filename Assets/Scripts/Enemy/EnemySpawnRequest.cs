using System;
using UnityEngine;

namespace ProjectU
{
    [Serializable]
    public struct EnemySpawnRequest
    {
        [SerializeField] private EnemyType type;
        [SerializeField, Min(1)] private int count;

        public EnemyType Type => type;
        public int Count => count;

        public EnemySpawnRequest(EnemyType type, int count)
        {
            this.type = type;
            this.count = count;
        }
    }
}
