using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
#endif

namespace ProjectU
{
    [Serializable]
    public sealed class EnemySpawnGroup
    {
        [SerializeField, SerializedDictionary("Enemy Type", "Count")]
        private SerializedDictionary<EnemyType, int> enemies = new SerializedDictionary<EnemyType, int>();
        public IReadOnlyDictionary<EnemyType, int> Enemies => enemies;

        public EnemySpawnGroup() { }

        public EnemySpawnGroup(IReadOnlyDictionary<EnemyType, int> enemies)
        {
            // The package's typed copy constructor also preserves Inspector conflict rows.
            this.enemies = enemies == null ? null : enemies is SerializedDictionary<EnemyType, int> serialized
                ? new SerializedDictionary<EnemyType, int>(serialized)
                : new SerializedDictionary<EnemyType, int>(enemies);
        }

        public bool TryValidate(out string error, out int count)
        {
            error = null; count = 0;
            if (enemies == null || enemies.Count == 0) { error = "A spawn group must contain enemies."; return false; }
#if UNITY_EDITOR
            if (!TryValidateSerializedRows(out error)) return false;
#endif
            foreach (KeyValuePair<EnemyType, int> entry in enemies)
            {
                if (!Enum.IsDefined(typeof(EnemyType), entry.Key))
                { error = "Each enemy type must be defined."; return false; }
                if (entry.Value <= 0 || entry.Value > int.MaxValue - count)
                { error = "Enemy counts must be positive and fit in an integer."; return false; }
                count += entry.Value;
            }
            return true;
        }

        internal EnemySpawnGroup Copy() => new EnemySpawnGroup(enemies);

#if UNITY_EDITOR
        // The package deliberately preserves duplicate Inspector rows, but hides duplicates in
        // Dictionary enumeration. Inspect its pinned-version backing data so spawning rejects them.
        // Keep this out of player code; a built Dictionary cannot contain duplicate keys.
        private static readonly FieldInfo serializedRowsField = typeof(SerializedDictionary<EnemyType, int>)
            .GetField("_serializedList", BindingFlags.Instance | BindingFlags.NonPublic);

        private bool TryValidateSerializedRows(out string error)
        {
            error = null;
            var rows = serializedRowsField?.GetValue(enemies) as List<SerializedKeyValuePair<EnemyType, int>>;
            if (rows == null)
            { error = "SerializedDictionary layout changed; update spawn-data validation for the installed package."; return false; }
            var keys = new HashSet<EnemyType>();
            foreach (SerializedKeyValuePair<EnemyType, int> row in rows)
            {
                if (!keys.Add(row.Key)) { error = "Duplicate enemy type in serialized spawn group: " + row.Key; return false; }
                if (!enemies.TryGetValue(row.Key, out int value) || value != row.Value)
                { error = "Serialized enemy counts and dictionary values are not synchronized."; return false; }
            }
            if (rows.Count != enemies.Count)
            { error = "Serialized enemy counts and dictionary values are not synchronized."; return false; }
            return true;
        }
#endif
    }
}
