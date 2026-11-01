using System;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectU.Editor
{
    public static class TempEnemySetup
    {
        public const string PrefabPath = "Assets/Prefabs/Enemies/TempEnemy.prefab";
        public const string NavPath = "Assets/Scenes/Navigation/SampleSceneNavMesh.asset";

        [MenuItem("ProjectU/Setup/Create TempEnemy Demo")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            Directory.CreateDirectory("Assets/Prefabs/Enemies");
            Directory.CreateDirectory("Assets/Scenes/Navigation");
            AssetDatabase.Refresh();
            string materialPath = "Assets/Prefabs/Enemies/TempEnemy.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(0.8f, 0.16f, 0.08f));
                AssetDatabase.CreateAsset(material, materialPath);
            }

            GameObject root = new GameObject("TempEnemy");
            root.SetActive(false);
            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.up; capsule.height = 2f; capsule.radius = 0.45f;
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Capsule Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.up;
            visual.transform.localScale = new Vector3(0.9f, 1f, 0.9f);
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<Renderer>().sharedMaterial = material;
            Mortal mortal = root.AddComponent<Mortal>();
            Combatant owner = root.AddComponent<Combatant>();
            owner.Faction = Faction.Enemy;
            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.45f; agent.height = 2f; agent.baseOffset = 0f;
            agent.speed = 3.5f; agent.acceleration = 16f; agent.angularSpeed = 540f;
            agent.stoppingDistance = 1.6f; agent.updateRotation = false;
            root.AddComponent<EnemyMotor>();
            root.AddComponent<EnemySensor>();
            MeleeAttack attack = root.AddComponent<MeleeAttack>();
            SetReference(attack, "data", AssetDatabase.LoadAssetAtPath<MeleeAttackData>("Assets/Data/Attacks/StrikerMeleeAttack.asset"));
            SetReference(attack, "origin", root.transform);
            EnemyAttackAction action = root.AddComponent<EnemyAttackAction>();
            var actionSettings = new SerializedObject(action);
            actionSettings.FindProperty("attack").objectReferenceValue = attack;
            actionSettings.FindProperty("windupTime").floatValue = 0.5f;
            actionSettings.FindProperty("recoveryTime").floatValue = 0.8f;
            actionSettings.FindProperty("cooldown").floatValue = 1.2f;
            actionSettings.FindProperty("requiresGrounded").boolValue = true;
            actionSettings.FindProperty("canMove").boolValue = false;
            actionSettings.ApplyModifiedPropertiesWithoutUndo();
            EnemyCombat combat = root.AddComponent<EnemyCombat>();
            var combatSettings = new SerializedObject(combat);
            SerializedProperty actions = combatSettings.FindProperty("actions");
            actions.arraySize = 1; actions.GetArrayElementAtIndex(0).objectReferenceValue = action;
            combatSettings.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<EnemyAI>();
            root.SetActive(true);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            GameObject navigation = GameObject.Find("Enemy Navigation");
            if (navigation == null) navigation = new GameObject("Enemy Navigation");
            NavMeshSurface surface = navigation.GetComponent<NavMeshSurface>();
            if (surface == null) surface = navigation.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1; // Static Default geometry; the Ignore Raycast player is excluded.
            surface.ignoreNavMeshAgent = true;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("NavMesh bake failed.");
            NavMeshData existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);
            if (existing == null) AssetDatabase.CreateAsset(surface.navMeshData, NavPath);
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

            GameObject group = GameObject.Find("Temp Enemies");
            if (group != null) UnityEngine.Object.DestroyImmediate(group);
            group = new GameObject("Temp Enemies");
            Combatant player = GameObject.FindGameObjectWithTag("Player").GetComponent<Combatant>();
            Vector3[] positions = { new Vector3(-8f, 0f, 8f), new Vector3(0f, 0f, 11f), new Vector3(8f, 0f, 8f),
                new Vector3(-12f, 0f, -8f), new Vector3(12f, 0f, -8f) };
            for (int index = 0; index < positions.Length; index++)
            {
                if (!NavMesh.SamplePosition(positions[index], out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    throw new InvalidOperationException("Enemy spawn is outside the baked NavMesh.");
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = "TempEnemy " + (index + 1);
                instance.transform.SetParent(group.transform);
                instance.transform.position = hit.position;
                Vector3 direction = Vector3.ProjectOnPlane(player.transform.position - hit.position, Vector3.up);
                instance.transform.rotation = Quaternion.LookRotation(direction);
                SetReference(instance.GetComponent<EnemySensor>(), "player", player);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<EnemySensor>());
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("TEMP ENEMY SETUP PASSED: prefab, NavMesh and five scene instances saved.");
        }

        private static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("ProjectU/Validation/Capture Enemy Scene Preview")]
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Preview requires a graphics device.");
            Directory.CreateDirectory("Logs/Enemy_20261101");
            GameObject observerObject = new GameObject("Temporary Enemy Preview Camera");
            try
            {
                Camera observer = observerObject.AddComponent<Camera>();
                observer.CopyFrom(Camera.main);
                observer.fieldOfView = 55f;
                observer.transform.position = new Vector3(29f, 34f, -33f);
                observer.transform.LookAt(Vector3.zero);
                Render(observer, "Logs/Enemy_20261101/EnemyScene.Overview.png");
                EnemyAI enemy = UnityEngine.Object.FindFirstObjectByType<EnemyAI>();
                observer.transform.position = enemy.transform.position + new Vector3(3f, 2.5f, -4f);
                observer.transform.LookAt(enemy.transform.position + Vector3.up);
                Render(observer, "Logs/Enemy_20261101/TempEnemy.Closeup.png");
                Debug.Log("ENEMY PREVIEW PASSED: overview and capsule closeup rendered without saving the scene.");
            }
            finally { UnityEngine.Object.DestroyImmediate(observerObject); }
        }

        private static void Render(Camera camera, string path)
        {
            var texture = new RenderTexture(1600, 1000, 24);
            var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
