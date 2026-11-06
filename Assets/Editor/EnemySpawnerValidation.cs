using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class EnemySpawnerValidation
    {
        private const string Running = "ProjectU.EnemySpawnerValidation";
        static EnemySpawnerValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Enemy Spawner Validation").AddComponent<EnemySpawnerValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Enemy Spawners")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            GameFlowValidationUtility.DisableAutomaticGameFlow();
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        internal static void Finish(int result)
        {
            SessionState.SetBool(Running, false);
            Time.timeScale = 1f;
            if (Application.isBatchMode) EditorApplication.Exit(result);
            else EditorApplication.ExitPlaymode();
        }
    }

    public sealed class EnemySpawnerValidationDriver : MonoBehaviour
    {
        private readonly List<GameObject> fixtures = new List<GameObject>();
        private int checks;

        private IEnumerator Start()
        {
            IEnumerator test = Validate();
            while (true)
            {
                bool more;
                object value = null;
                try { more = test.MoveNext(); if (more) value = test.Current; }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Cleanup(); EnemySpawnerValidation.Finish(1); yield break;
                }
                if (!more) break;
                yield return value;
            }
            Debug.Log("ENEMY SPAWNER VALIDATION PASSED: " + checks + " checks.");
            Cleanup(); EnemySpawnerValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            EnemySpawner[] spawners = FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None);
            Check(spawners.Length == 4, "four saved spawners");
            Check(spawners.All(s => Mathf.Abs(s.transform.position.x) == 16f && Mathf.Abs(s.transform.position.z) == 16f && s.transform.position.y == 0f) &&
                spawners.Select(s => s.transform.position).Distinct().Count() == 4, "square placement at plus/minus 16");
            Check(GameObject.Find("Arena Floor (50 x 50)").transform.localScale == new Vector3(5f, 1f, 5f), "50 metre floor");
            Check(GameObject.Find("North Wall").transform.position.z == 25f && GameObject.Find("South Wall").transform.position.z == -25f &&
                GameObject.Find("East Wall").transform.position.x == 25f && GameObject.Find("West Wall").transform.position.x == -25f, "expanded boundary walls");
            foreach (Vector3 point in new[] { new Vector3(23f, 0f, 0f), new Vector3(-23f, 0f, 0f), new Vector3(0f, 0f, 23f), new Vector3(0f, 0f, -23f) })
                Check(NavMesh.SamplePosition(point, out NavMeshHit hit, 0.2f, NavMesh.AllAreas), "new outer floor has baked NavMesh " + point);
            Check(GameObject.Find("Temp Enemies") == null && FindObjectsByType<EnemyAI>(FindObjectsSortMode.None).Length == 0, "no saved demo enemies");
            for (int frame = 0; frame < 10; frame++) yield return null;
            Check(FindObjectsByType<EnemyAI>(FindObjectsSortMode.None).Length == 0, "no automatic spawning after startup");
            Time.timeScale = 0f; // Explicit requests may prepare a stage while gameplay is paused.
            EnemyAI prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TempEnemySetup.PrefabPath).GetComponent<EnemyAI>();
            EnemySpawnManager savedManager = FindFirstObjectByType<EnemySpawnManager>();
            Check(savedManager != null && savedManager.Entries.Count == 1 && savedManager.Entries[0].Type == EnemyType.TempEnemy &&
                savedManager.Entries[0].Prefab == prefab && savedManager.Spawners.Count == 4, "one saved manager owns prefab mappings and four spawners");
            foreach (EnemySpawner spawner in spawners)
            {
                Check(NewManager(new[] { spawner }, prefab).TrySpawn(EnemyType.TempEnemy, 5, out EnemyAI[] spawned) && spawned.Length == 5, "five units from " + spawner.name);
                Track(spawned);
                Check(spawned.All(e => e.Motor.IsGrounded && e.GetComponent<Mortal>().HP == 100 && e.GetComponent<EnemyStat>().IsInitialized), "spawned HP and navigation initialized");
                Check(spawned.All(e => new SerializedObject(e.Sensor).FindProperty("player").objectReferenceValue == GameObject.FindGameObjectWithTag("Player").GetComponent<Combatant>()), "player reference passed to sensors");
            }
            Check(NoOverlaps(), "four batches have no collider penetration");
            EnemySpawnManager first = NewManager(new[] { spawners[0] }, prefab);
            Check(first.TrySpawn(EnemyType.TempEnemy, 4, out EnemyAI[] repeated), "second batch in same frame"); Track(repeated);
            Check(NoOverlaps(), "repeat batch avoids existing units");
            EnemySpawnManager shared = NewSpawner(first.transform.position, prefab);
            Check(shared.TrySpawn(EnemyType.TempEnemy, 3, out EnemyAI[] sharedUnits), "another spawner can share the area"); Track(sharedUnits);
            Check(NoOverlaps(), "overlapping spawner areas avoid each other in same frame");

            int before = EnemyCount();
            Check(!first.TrySpawn(EnemyType.Gunner, 1, out EnemyAI[] missing) && missing.Length == 0 && EnemyCount() == before, "unregistered type is atomic failure");
            Check(!first.TrySpawn((EnemyType)999, 1, out _), "undefined enum rejected");
            Check(!first.TrySpawn(EnemyType.TempEnemy, 0, out _) && !first.TrySpawn(EnemyType.TempEnemy, -1, out _), "invalid counts rejected");
            Check(!first.TrySpawn(EnemyType.TempEnemy, int.MaxValue, out _), "oversized batch rejected without overflow");
            Check(!first.TrySpawn(null, out _) && !first.TrySpawn(new SerializedDictionary<EnemyType, int>(), out _), "null and empty batches rejected");
            Check(!first.TrySpawn(new SerializedDictionary<EnemyType, int> { { EnemyType.TempEnemy, 1 }, { EnemyType.Gunner, 1 } }, out _) && EnemyCount() == before,
                "mixed valid and invalid requests create nothing");
            first.enabled = false;
            Check(!first.TrySpawn(EnemyType.TempEnemy, 1, out _), "disabled component rejects spawning");
            first.enabled = true;
            first.gameObject.SetActive(false);
            Check(!first.TrySpawn(EnemyType.TempEnemy, 1, out _), "inactive object rejects spawning");
            first.gameObject.SetActive(true);
            Check(EnemyCount() == before, "reactivation does not spawn anything");

            EnemySpawnManager absentNav = NewSpawner(new Vector3(1000f, 0f, 1000f), prefab);
            Check(!absentNav.TrySpawn(EnemyType.TempEnemy, 1, out _) && EnemyCount() == before, "missing NavMesh fails without objects");
            EnemySpawnManager blocked = NewSpawner(new Vector3(10f, 0f, 0f), prefab);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(wall);
            wall.transform.position = blocked.transform.position + Vector3.up * 1.5f;
            wall.transform.localScale = new Vector3(10f, 3f, 10f);
            Check(!blocked.TrySpawn(EnemyType.TempEnemy, 1, out _) && EnemyCount() == before, "fully blocked area fails atomically");
            wall.SetActive(false);
            Check(blocked.TrySpawn(EnemyType.TempEnemy, 2, out EnemyAI[] afterClear), "space can be reused after obstruction clears"); Track(afterClear);

            EnemySpawnManager tiny = NewSpawner(new Vector3(-8f, 0f, 0f), prefab);
            SetFloat(tiny, "halfExtent", 0.6f);
            before = EnemyCount();
            Check(!tiny.TrySpawn(EnemyType.TempEnemy, 2, out EnemyAI[] noRoom) && noRoom.Length == 0 && EnemyCount() == before, "insufficient room rolls back whole plan");
            Check(tiny.TrySpawn(EnemyType.TempEnemy, 1, out EnemyAI[] one), "one unit fits tiny area"); Track(one);
            Check(!tiny.TrySpawn(EnemyType.TempEnemy, 1, out _), "existing unit blocks tiny area");

            EnemySpawnManager playerArea = NewSpawner(Vector3.zero, prefab);
            SetFloat(playerArea, "halfExtent", 0.6f);
            Check(!playerArea.TrySpawn(EnemyType.TempEnemy, 1, out _), "Ignore Raycast player collider blocks spawn");
            EnemySpawnManager boundary = NewSpawner(new Vector3(24.5f, 0f, 10f), prefab);
            Check(boundary.TrySpawn(EnemyType.TempEnemy, 3, out EnemyAI[] edge), "partial edge area finds interior space"); Track(edge);
            Check(edge.All(e => e.transform.position.x + e.GetComponent<CapsuleCollider>().radius < 24.5f), "edge spawns stay inside wall");

            // Different live templates are test-only; they never modify the saved TempEnemy prefab.
            EnemyAI large = Instantiate(prefab, new Vector3(-12f, 0f, -2f), Quaternion.identity); fixtures.Add(large.gameObject);
            large.name = "Large test template";
            large.GetComponent<CapsuleCollider>().radius = 1f;
            large.GetComponent<NavMeshAgent>().radius = 1f;
            EnemySpawnManager mixed = NewSpawner(new Vector3(0f, 0f, -12f), prefab, large);
            Check(mixed.TrySpawn(new SerializedDictionary<EnemyType, int> { { EnemyType.Striker, 2 }, { EnemyType.TempEnemy, 3 } }, out EnemyAI[] mixture) && mixture.Length == 5,
                "two types and quantities in one batch"); Track(mixture);
            Check(mixture.Take(3).All(e => e.GetComponent<CapsuleCollider>().radius == 0.45f) && mixture.Skip(3).All(e => e.GetComponent<CapsuleCollider>().radius == 1f),
                "enum order is stable regardless of dictionary insertion order and prefab sizes are preserved");
            Check(NoOverlaps(), "different sizes avoid units player and walls");
            before = EnemyCount();
            var conflict = new SerializedDictionary<EnemyType, int> { { EnemyType.TempEnemy, 1 } };
            conflict.AddConflictAllowed(EnemyType.TempEnemy, 2);
            Check(!mixed.TrySpawn(conflict, out EnemyAI[] duplicates) && duplicates.Length == 0,
                "duplicate types within one group are rejected");
            Check(EnemyCount() == before && NoOverlaps(), "duplicate type rejection creates nothing");
            var settings = new SerializedObject(mixed);
            settings.FindProperty("entries").GetArrayElementAtIndex(1).FindPropertyRelative("type").intValue = (int)EnemyType.TempEnemy;
            settings.ApplyModifiedPropertiesWithoutUndo();
            before = EnemyCount();
            Check(!mixed.TrySpawn(EnemyType.TempEnemy, 1, out _) && EnemyCount() == before, "ambiguous duplicate registration rejected");

            Cleanup();
            yield return null;
            yield return null;
            StageData savedData = AssetDatabase.LoadAssetAtPath<StageData>(StageSetup.StagePath);
            Check(savedData != null && savedData.TryValidate(out _) && savedData.Waves.Count == 3, "saved authoring-only stage data validates");
            Check(savedManager.CanSpawnStage(savedData, out _), "shared catalog resolves all saved stage waves");
            Check(EnemyCount() == 0, "data and manager do not execute a stage automatically");

            var permutations = new HashSet<string>();
            UnityEngine.Random.State previousRandom = UnityEngine.Random.state;
            try
            {
                for (int seed = 0; seed < 8; seed++)
                {
                    UnityEngine.Random.InitState(seed);
                    var wave = new WaveData(0, Group(1), Group(2), Group(3), Group(4));
                    int frame = Time.frameCount;
                    Check(savedManager.TrySpawnWave(wave, out EnemyAI[] batch) && batch.Length == 10, "four groups spawn as one wave " + seed);
                    Track(batch);
                    Check(Time.frameCount == frame && savedManager.LastUsedSpawners.Distinct().Count() == 4, "same frame and distinct group assignments " + seed);
                    int cursor = 0;
                    bool orderMatches = true;
                    for (int group = 0; group < 4; group++)
                    {
                        EnemySpawner point = savedManager.LastUsedSpawners[group];
                        for (int count = 0; count <= group; count++)
                        {
                            Vector3 delta = batch[cursor++].transform.position - point.transform.position;
                            orderMatches &= Mathf.Abs(delta.x) <= point.HalfExtent && Mathf.Abs(delta.z) <= point.HalfExtent;
                        }
                    }
                    Check(orderMatches && NoOverlaps(), "results preserve group order and actual geometry " + seed);
                    permutations.Add(string.Join(",", savedManager.LastUsedSpawners.Select(s => s.name)));
                    foreach (EnemyAI enemy in batch) { enemy.gameObject.SetActive(false); Destroy(enemy.gameObject); }
                }
            }
            finally { UnityEngine.Random.state = previousRandom; }
            Check(permutations.Count > 1, "fresh shuffles produce different direction assignments");
            yield return null;

            // Independent groups may reuse a type, but cannot reserve the same physical position.
            EnemySpawnManager overlapA = NewSpawner(new Vector3(-10f, 0f, 0f), prefab);
            EnemySpawnManager overlapB = NewSpawner(new Vector3(-10f, 0f, 0f), prefab);
            SetFloat(overlapA, "halfExtent", 0.6f); SetFloat(overlapB, "halfExtent", 0.6f);
            EnemySpawnManager overlap = NewManager(new[] { overlapA.Spawners[0], overlapB.Spawners[0] }, prefab);
            before = EnemyCount();
            Check(!overlap.TrySpawnWave(new WaveData(0, Group(1), Group(1)), out EnemyAI[] failed) && failed.Length == 0 && EnemyCount() == before,
                "cross-group reservations reject overlap without partial spawning");
            overlapB.Spawners[0].transform.position = new Vector3(-13f, 0f, 0f);
            Check(overlap.TrySpawnWave(new WaveData(0, Group(1), Group(1)), out EnemyAI[] separated) && separated.Length == 2,
                "same type in different groups is supported"); Track(separated);
            Check(NoOverlaps(), "separated groups have no actual penetration");
            foreach (EnemyAI enemy in separated) { enemy.gameObject.SetActive(false); Destroy(enemy.gameObject); }

            // One small and one large area: a greedy assignment must be reconsidered for the larger group.
            EnemySpawnManager small = NewSpawner(new Vector3(0f, 0f, 10f), prefab);
            EnemySpawnManager roomy = NewSpawner(new Vector3(10f, 0f, 10f), prefab);
            SetFloat(small, "halfExtent", 0.6f);
            EnemySpawnManager flexible = NewManager(new[] { small.Spawners[0], roomy.Spawners[0] }, prefab);
            Check(flexible.TrySpawnWave(new WaveData(0, Group(1), Group(4)), out EnemyAI[] reassigned) && reassigned.Length == 5 &&
                flexible.LastUsedSpawners[0] == small.Spawners[0] && flexible.LastUsedSpawners[1] == roomy.Spawners[0],
                "assignment finds a feasible pairing for different group capacities"); Track(reassigned);
            roomy.Spawners[0].enabled = false;
            before = EnemyCount();
            Check(!flexible.TrySpawnWave(new WaveData(0, Group(1), Group(1)), out _) && EnemyCount() == before,
                "too few active spawners rejects the entire wave");
            Check(!savedManager.TrySpawnWave(new WaveData(0, Group(1), Group(1), Group(1), Group(1), Group(1)), out _), "more than four groups rejected");
            Check(!savedManager.TrySpawnWave(null, out _), "missing wave rejected");
            Check(!savedManager.TrySpawnWave(new WaveData(0), out _), "empty wave rejected");
            Check(!new WaveData(2, Group(2)).TryValidate(false, out _), "nonfinal threshold cannot skip a wave immediately");
            Check(new WaveData(999, Group(2)).TryValidate(true, out _), "final wave threshold is unused authoring data");
        }

        private static EnemySpawnGroup Group(int count) => new EnemySpawnGroup(new SerializedDictionary<EnemyType, int> { { EnemyType.TempEnemy, count } });

        private EnemySpawnManager NewSpawner(Vector3 position, EnemyAI prefab, EnemyAI second = null)
        {
            GameObject root = new GameObject("Test Spawner"); fixtures.Add(root); root.transform.position = position;
            EnemySpawner spawner = root.AddComponent<EnemySpawner>();
            return NewManager(new[] { spawner }, prefab, second);
        }

        private EnemySpawnManager NewManager(EnemySpawner[] spawners, EnemyAI prefab, EnemyAI second = null)
        {
            GameObject root = new GameObject("Test Spawn Manager"); fixtures.Add(root); root.transform.position = spawners[0].transform.position;
            EnemySpawnManager manager = root.AddComponent<EnemySpawnManager>();
            var settings = new SerializedObject(manager);
            SerializedProperty points = settings.FindProperty("spawners"); points.arraySize = spawners.Length;
            for (int index = 0; index < spawners.Length; index++) points.GetArrayElementAtIndex(index).objectReferenceValue = spawners[index];
            settings.FindProperty("player").objectReferenceValue = GameObject.FindGameObjectWithTag("Player").GetComponent<Combatant>();
            SerializedProperty entries = settings.FindProperty("entries"); entries.arraySize = second == null ? 1 : 2;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("type").intValue = (int)EnemyType.TempEnemy;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("prefab").objectReferenceValue = prefab;
            if (second != null)
            {
                entries.GetArrayElementAtIndex(1).FindPropertyRelative("type").intValue = (int)EnemyType.Striker;
                entries.GetArrayElementAtIndex(1).FindPropertyRelative("prefab").objectReferenceValue = second;
            }
            settings.ApplyModifiedPropertiesWithoutUndo(); return manager;
        }

        private bool NoOverlaps()
        {
            Physics.SyncTransforms();
            Collider[] solids = FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.enabled && !c.isTrigger).ToArray();
            foreach (EnemyAI enemy in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            {
                CapsuleCollider capsule = enemy.GetComponent<CapsuleCollider>();
                foreach (Collider other in solids)
                {
                    if (other == capsule) continue;
                    if (Physics.ComputePenetration(capsule, capsule.transform.position, capsule.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > 0.025f) return false;
                }
            }
            return true;
        }

        private static void SetFloat(EnemySpawnManager manager, string field, float value)
        {
            var settings = new SerializedObject(manager.Spawners[0]); settings.FindProperty(field).floatValue = value; settings.ApplyModifiedPropertiesWithoutUndo();
        }
        private static int EnemyCount() => FindObjectsByType<EnemyAI>(FindObjectsSortMode.None).Length;
        private void Track(EnemyAI[] enemies) { foreach (EnemyAI enemy in enemies) fixtures.Add(enemy.gameObject); }
        private void Check(bool pass, string label)
        {
            if (!pass) throw new InvalidOperationException("SPAWNER CHECK FAILED: " + label);
            checks++; Debug.Log("SPAWNER CHECK PASSED: " + label);
        }
        private void Cleanup() { foreach (GameObject fixture in fixtures) if (fixture != null) Destroy(fixture); }
    }
}
