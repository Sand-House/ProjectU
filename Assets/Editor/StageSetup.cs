using System;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectU.Editor
{
    public static class StageSetup
    {
        public const string StagePath = "Assets/Data/Stages/TempStage.asset";
        [MenuItem("ProjectU/Setup/Prepare Stage Arena")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            GameObject floor = GameObject.Find("Arena Floor (40 x 40)") ?? GameObject.Find("Arena Floor (50 x 50)");
            if (floor == null) throw new InvalidOperationException("Arena floor is missing.");
            floor.name = "Arena Floor (50 x 50)";
            floor.transform.localScale = new Vector3(5f, 1f, 5f);
            SetWall("North Wall", new Vector3(0f, 2f, 25f), new Vector3(51f, 4f, 1f));
            SetWall("South Wall", new Vector3(0f, 2f, -25f), new Vector3(51f, 4f, 1f));
            SetWall("East Wall", new Vector3(25f, 2f, 0f), new Vector3(1f, 4f, 49f));
            SetWall("West Wall", new Vector3(-25f, 2f, 0f), new Vector3(1f, 4f, 49f));

            foreach (EnemyAI enemy in UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(enemy.gameObject);
            GameObject oldGroup = GameObject.Find("Temp Enemies");
            if (oldGroup != null) UnityEngine.Object.DestroyImmediate(oldGroup);

            GameObject group = GameObject.Find("Enemy Spawners");
            if (group == null) group = new GameObject("Enemy Spawners");
            string[] names = { "EnemySpawner NW", "EnemySpawner NE", "EnemySpawner SW", "EnemySpawner SE" };
            Vector3[] positions = { new Vector3(-16f, 0f, 16f), new Vector3(16f, 0f, 16f),
                new Vector3(-16f, 0f, -16f), new Vector3(16f, 0f, -16f) };
            for (int index = 0; index < positions.Length; index++)
            {
                Transform child = group.transform.Find(names[index]);
                GameObject point = child != null ? child.gameObject : new GameObject(names[index]);
                point.transform.SetParent(group.transform);
                point.transform.position = positions[index];
                point.transform.rotation = Quaternion.LookRotation(-positions[index]);
                point.transform.localScale = Vector3.one;
                EnemySpawner spawner = point.GetComponent<EnemySpawner>();
                if (spawner == null) spawner = point.AddComponent<EnemySpawner>();
            }

            NavMeshSurface surface = GameObject.Find("Enemy Navigation").GetComponent<NavMeshSurface>();
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("NavMesh bake failed.");
            NavMeshData existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(TempEnemySetup.NavPath);
            if (existing == null) AssetDatabase.CreateAsset(surface.navMeshData, TempEnemySetup.NavPath);
            else
            {
                surface.RemoveData();
                EditorUtility.CopySerialized(surface.navMeshData, existing);
                UnityEngine.Object.DestroyImmediate(surface.navMeshData);
                surface.navMeshData = existing;
                surface.AddData();
                EditorUtility.SetDirty(existing);
            }
            EditorUtility.SetDirty(surface);
            ConfigureStageSystem();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("STAGE SETUP PASSED: 50x50 arena, rebuilt NavMesh, zero enemies and four configured spawners.");
        }

        [MenuItem("ProjectU/Setup/Connect Stage System")]
        public static void ConnectStageSystem()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            ConfigureStageSystem();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("STAGE SYSTEM SETUP PASSED: shared manager, four spawners and TempStage data configured; no stage execution.");
        }

        private static void ConfigureStageSystem()
        {
            Directory.CreateDirectory("Assets/Data/Stages");
            AssetDatabase.Refresh();
            StageData data = AssetDatabase.LoadAssetAtPath<StageData>(StagePath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<StageData>();
                var stageSettings = new SerializedObject(data);
                SerializedProperty waves = stageSettings.FindProperty("waves"); waves.arraySize = 3;
                int[][] counts = { new[] { 2, 2 }, new[] { 2, 2, 2 }, new[] { 2, 2, 2, 2 } };
                for (int wave = 0; wave < counts.Length; wave++)
                {
                    SerializedProperty value = waves.GetArrayElementAtIndex(wave);
                    value.FindPropertyRelative("nextWaveRemainingEnemyThreshold").intValue = wave < 2 ? 1 : 0;
                    SerializedProperty groups = value.FindPropertyRelative("spawnGroups"); groups.arraySize = counts[wave].Length;
                    for (int group = 0; group < groups.arraySize; group++)
                    {
                        SerializedProperty enemies = groups.GetArrayElementAtIndex(group).FindPropertyRelative("enemies"); enemies.arraySize = 1;
                        enemies.GetArrayElementAtIndex(0).FindPropertyRelative("type").intValue = (int)EnemyType.TempEnemy;
                        enemies.GetArrayElementAtIndex(0).FindPropertyRelative("count").intValue = counts[wave][group];
                    }
                }
                stageSettings.ApplyModifiedPropertiesWithoutUndo();
                if (!data.TryValidate(out string error)) throw new InvalidOperationException(error);
                AssetDatabase.CreateAsset(data, StagePath);
            }
            EnemySpawner[] spawners = UnityEngine.Object.FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None).OrderBy(s => s.name).ToArray();
            if (spawners.Length != 4) throw new InvalidOperationException("Expected four stage spawners.");
            GameObject root = GameObject.Find("Enemy Spawn Manager");
            if (root == null) root = new GameObject("Enemy Spawn Manager");
            EnemySpawnManager manager = root.GetComponent<EnemySpawnManager>();
            if (manager == null) manager = root.AddComponent<EnemySpawnManager>();
            var settings = new SerializedObject(manager);
            SerializedProperty entries = settings.FindProperty("entries");
            if (entries.arraySize == 0)
            {
                entries.arraySize = 1;
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("type").intValue = (int)EnemyType.TempEnemy;
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("prefab").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(TempEnemySetup.PrefabPath).GetComponent<EnemyAI>();
            }
            SerializedProperty points = settings.FindProperty("spawners"); points.arraySize = spawners.Length;
            for (int index = 0; index < spawners.Length; index++) points.GetArrayElementAtIndex(index).objectReferenceValue = spawners[index];
            settings.FindProperty("player").objectReferenceValue = GameObject.FindGameObjectWithTag("Player").GetComponent<Combatant>();
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetWall(string name, Vector3 position, Vector3 scale)
        {
            Transform wall = GameObject.Find(name).transform;
            wall.position = position;
            wall.localScale = scale;
        }
    }
}
