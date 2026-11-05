using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    [Serializable]
    public sealed class EnemySpawnGroup
    {
        [SerializeField] private EnemySpawnRequest[] enemies = Array.Empty<EnemySpawnRequest>();
        public IReadOnlyList<EnemySpawnRequest> Enemies => enemies;

        public EnemySpawnGroup(params EnemySpawnRequest[] enemies)
        {
            this.enemies = enemies == null ? null : (EnemySpawnRequest[])enemies.Clone();
        }

        public bool TryValidate(out string error, out int count)
        {
            error = null; count = 0;
            if (enemies == null || enemies.Length == 0) { error = "A spawn group must contain enemies."; return false; }
            var types = new HashSet<EnemyType>();
            foreach (EnemySpawnRequest request in enemies)
            {
                if (!Enum.IsDefined(typeof(EnemyType), request.Type) || !types.Add(request.Type))
                { error = "Each enemy type must be defined and unique within its group."; return false; }
                if (request.Count <= 0 || request.Count > int.MaxValue - count)
                { error = "Enemy counts must be positive and fit in an integer."; return false; }
                count += request.Count;
            }
            return true;
        }

        internal EnemySpawnGroup Copy() => new EnemySpawnGroup(enemies);
    }
}
