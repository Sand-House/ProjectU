using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    [DisallowMultipleComponent]
    public sealed class EnemySpawnManager : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private EnemyType type;
            [SerializeField] private EnemyAI prefab;
            public EnemyType Type => type;
            public EnemyAI Prefab => prefab;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        [SerializeField] private EnemySpawner[] spawners = Array.Empty<EnemySpawner>();
        [SerializeField] private Combatant player;
        // Covers callbacks during Instantiate, including calls into another manager.
        private static bool transactionInProgress;
        private IReadOnlyList<EnemySpawner> lastUsedSpawners = Array.Empty<EnemySpawner>();
        public IReadOnlyList<Entry> Entries => entries;
        public IReadOnlyList<EnemySpawner> Spawners => spawners;
        public IReadOnlyList<EnemySpawner> LastUsedSpawners => lastUsedSpawners;
        public string LastError { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTransaction() => transactionInProgress = false;

        public bool TrySpawn(EnemyType type, int count, out EnemyAI[] spawned)
        {
            return TrySpawn(new[] { new EnemySpawnRequest(type, count) }, out spawned);
        }

        public bool TrySpawn(EnemySpawnRequest[] requests, out EnemyAI[] spawned)
            => TrySpawnWave(new WaveData(0, new EnemySpawnGroup(requests)), out spawned);

        public bool TrySpawnWave(WaveData wave, out EnemyAI[] spawned)
        {
            spawned = Array.Empty<EnemyAI>();
            LastError = null;
            if (!Application.isPlaying || !isActiveAndEnabled || transactionInProgress)
                return Fail("Spawning requires an active manager in Play mode with no active spawn transaction.");
            if (wave == null) return Fail("The wave is missing.");
            if (!wave.TryValidate(true, out string error)) return Fail(error);
            if (!TryBuildRegistry(out Dictionary<EnemyType, EnemyAI> registry, out error)) return Fail(error);
            if (!TryGetCandidates(out List<EnemySpawner> candidates, out error)) return Fail(error);
            if (candidates.Count < wave.SpawnGroups.Count) return Fail("Not enough active spawners for this wave.");
            if (!TryResolve(wave, registry, out EnemySpawner.PrefabRequest[][] requests, out error)) return Fail(error);

            transactionInProgress = true;
            var created = new List<EnemyAI>();
            try
            {
                // Fisher-Yates: one fresh permutation per wave request, no carried round-robin cursor.
                for (int index = candidates.Count - 1; index > 0; index--)
                {
                    int selected = UnityEngine.Random.Range(0, index + 1);
                    EnemySpawner temporary = candidates[index];
                    candidates[index] = candidates[selected]; candidates[selected] = temporary;
                }
                Physics.SyncTransforms();
                var plan = new List<EnemySpawner.Placement>();
                var assigned = new EnemySpawner[requests.Length];
                if (!TryAssign(0, requests, candidates, new bool[candidates.Count], assigned, plan))
                    return Fail("The entire wave could not fit on the available NavMesh without overlap.");

                // No yield between planning and commit. Reservations include every group in this wave.
                foreach (EnemySpawner.Placement placement in plan)
                {
                    EnemyAI enemy = EnemySpawner.Create(placement);
                    created.Add(enemy);
                    enemy.Sensor.SetPlayer(player);
                }
                foreach (EnemyAI enemy in created)
                    if (enemy == null || !enemy.isActiveAndEnabled || !enemy.GetComponent<EnemyStat>().IsInitialized ||
                        enemy.GetComponent<Mortal>().isDead || !enemy.Motor.IsGrounded)
                        throw new InvalidOperationException("An enemy failed to initialize on its reserved NavMesh position.");
                Physics.SyncTransforms();
                spawned = created.ToArray();
                lastUsedSpawners = Array.AsReadOnly(assigned);
                LastError = null;
                return true;
            }
            catch (Exception exception)
            {
                foreach (EnemyAI enemy in created)
                    if (enemy != null) { enemy.gameObject.SetActive(false); Destroy(enemy.gameObject); }
                Debug.LogException(exception, this);
                return Fail("Spawn initialization failed; created objects were disabled and scheduled for removal.");
            }
            finally { transactionInProgress = false; }
        }

        // Check every wave's registrations before beginning a stage, so a missing later type is caught early.
        public bool CanSpawnStage(StageData data, out string error)
        {
            error = null;
            if (data == null) { error = "Stage data is missing."; return false; }
            if (!data.TryValidate(out error) || !TryBuildRegistry(out Dictionary<EnemyType, EnemyAI> registry, out error) ||
                !TryGetCandidates(out List<EnemySpawner> candidates, out error)) return false;
            foreach (WaveData wave in data.Waves)
            {
                if (wave.SpawnGroups.Count > candidates.Count) { error = "Not enough active spawners for a stage wave."; return false; }
                if (!TryResolve(wave, registry, out _, out error)) return false;
            }
            return true;
        }

        private static bool TryAssign(int group, EnemySpawner.PrefabRequest[][] requests, List<EnemySpawner> candidates,
            bool[] used, EnemySpawner[] assigned, List<EnemySpawner.Placement> plan)
        {
            if (group == requests.Length) return true;
            for (int index = 0; index < candidates.Count; index++)
            {
                if (used[index]) continue;
                int start = plan.Count;
                if (!candidates[index].TryPlan(requests[group], plan)) continue;
                used[index] = true; assigned[group] = candidates[index];
                if (TryAssign(group + 1, requests, candidates, used, assigned, plan)) return true;
                used[index] = false;
                plan.RemoveRange(start, plan.Count - start);
            }
            return false;
        }

        private bool TryGetCandidates(out List<EnemySpawner> candidates, out string error)
        {
            candidates = new List<EnemySpawner>(); error = null;
            if (spawners == null || spawners.Length < 1 || spawners.Length > WaveData.MaxSpawnGroups)
            { error = "Register 1 to 4 scene spawners."; return false; }
            var unique = new HashSet<EnemySpawner>();
            foreach (EnemySpawner spawner in spawners)
            {
                if (spawner == null || !unique.Add(spawner)) { error = "Spawner references must be present and unique."; return false; }
                if (spawner.isActiveAndEnabled) candidates.Add(spawner);
            }
            return true;
        }

        private bool TryBuildRegistry(out Dictionary<EnemyType, EnemyAI> registry, out string error)
        {
            registry = new Dictionary<EnemyType, EnemyAI>(); error = null;
            if (entries == null || entries.Length == 0) { error = "Register at least one enemy prefab."; return false; }
            foreach (Entry entry in entries)
            {
                if (entry == null || !Enum.IsDefined(typeof(EnemyType), entry.Type) || registry.ContainsKey(entry.Type))
                { error = "Enemy registrations must have unique, defined types."; return false; }
                if (!EnemySpawner.TryGetShape(entry.Prefab, out _))
                { error = $"Invalid enemy prefab for {entry.Type}."; return false; }
                registry.Add(entry.Type, entry.Prefab);
            }
            return true;
        }

        private static bool TryResolve(WaveData wave, Dictionary<EnemyType, EnemyAI> registry,
            out EnemySpawner.PrefabRequest[][] requests, out string error)
        {
            error = null; requests = new EnemySpawner.PrefabRequest[wave.SpawnGroups.Count][];
            for (int group = 0; group < requests.Length; group++)
            {
                IReadOnlyList<EnemySpawnRequest> entries = wave.SpawnGroups[group].Enemies;
                requests[group] = new EnemySpawner.PrefabRequest[entries.Count];
                for (int index = 0; index < entries.Count; index++)
                {
                    if (!registry.TryGetValue(entries[index].Type, out EnemyAI prefab))
                    { error = $"No prefab is registered for {entries[index].Type}."; return false; }
                    requests[group][index] = new EnemySpawner.PrefabRequest(prefab, entries[index].Count);
                }
            }
            return true;
        }

        private bool Fail(string error) { LastError = error; return false; }
    }
}
