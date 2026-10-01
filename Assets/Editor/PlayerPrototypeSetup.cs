using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectU.Editor
{
    public static class PlayerPrototypeSetup
    {
        // Explicitly invoked setup; never modifies a scene on import or Play.
        [MenuItem("Project U/Set Up Player Prototype")]
        public static void Create()
        {
            const string path = "Assets/Scenes/SampleScene.unity";
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var scene = EditorSceneManager.OpenScene(path);
            if (UnityEngine.Object.FindFirstObjectByType<PlayerMotor>() != null)
                throw new InvalidOperationException("Player prototype already exists. Setup does not overwrite it.");

            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            var floorMaterial = CreateMaterial("ArenaFloor", new Color(0.22f, 0.28f, 0.32f));
            var playerMaterial = CreateMaterial("PlayerCapsule", new Color(0.1f, 0.65f, 0.95f));
            var wallMaterial = CreateMaterial("ArenaWall", new Color(0.55f, 0.6f, 0.65f));

            var floor = Primitive("Arena Floor (40 x 40)", PrimitiveType.Plane,
                Vector3.zero, new Vector3(4f, 1f, 4f), floorMaterial);
            floor.isStatic = true;
            Primitive("North Wall", PrimitiveType.Cube, new Vector3(0f, 2f, 20f), new Vector3(41f, 4f, 1f), wallMaterial);
            Primitive("South Wall", PrimitiveType.Cube, new Vector3(0f, 2f, -20f), new Vector3(41f, 4f, 1f), wallMaterial);
            Primitive("East Wall", PrimitiveType.Cube, new Vector3(20f, 2f, 0f), new Vector3(1f, 4f, 39f), wallMaterial);
            Primitive("West Wall", PrimitiveType.Cube, new Vector3(-20f, 2f, 0f), new Vector3(1f, 4f, 39f), wallMaterial);

            var player = Primitive("Player", PrimitiveType.Capsule, new Vector3(0f, 1.05f, 0f), Vector3.one, playerMaterial);
            player.tag = "Player";
            player.layer = 2; // Ignore Raycast: exclude the player from camera obstruction casts.
            UnityEngine.Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            var controller = player.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.center = Vector3.zero;
            controller.skinWidth = 0.05f;
            controller.stepOffset = 0.3f;
            controller.minMoveDistance = 0f;
            var input = player.AddComponent<PlayerInputReader>();
            var playerController = player.AddComponent<PlayerController>();
            // PlayerController requires and creates PlayerMotor.

            // A small front marker makes the capsule's facing direction visible.
            var marker = Primitive("Facing Marker", PrimitiveType.Cube, Vector3.zero,
                new Vector3(0.25f, 0.2f, 0.15f), wallMaterial);
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<BoxCollider>());
            marker.transform.SetParent(player.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.4f, 0.48f);
            marker.layer = 2;

            var camera = Camera.main;
            if (camera == null)
                throw new InvalidOperationException("SampleScene requires a Main Camera.");
            camera.nearClipPlane = 0.1f;
            var follow = camera.gameObject.AddComponent<PlayerCameraController>();
            SetReference(follow, "target", player.transform);
            SetReference(follow, "input", input);
            SetReference(playerController, "playerCamera", follow);
            var rotation = Quaternion.Euler(15f, 0f, 0f);
            camera.transform.SetPositionAndRotation(player.transform.position + Vector3.up * 0.65f
                + rotation * new Vector3(0.65f, 0f, -4.5f), rotation);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("PLAYER_PROTOTYPE_SETUP_OK");
        }

        public static void Validate()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            var camera = UnityEngine.Object.FindFirstObjectByType<PlayerCameraController>();
            if (motor == null || camera == null || motor.GetComponent<CharacterController>() == null
                || motor.GetComponent<PlayerInputReader>() == null || motor.GetComponent<PlayerController>() == null)
                throw new InvalidOperationException("Missing player components.");
            if (new SerializedObject(motor.GetComponent<PlayerController>()).FindProperty("playerCamera").objectReferenceValue != camera
                || new SerializedObject(camera).FindProperty("target").objectReferenceValue != motor.transform
                || new SerializedObject(camera).FindProperty("input").objectReferenceValue != motor.GetComponent<PlayerInputReader>())
                throw new InvalidOperationException("Player references are not wired.");
            Physics.SyncTransforms();
            if (!Physics.Raycast(motor.transform.position, Vector3.down, out _, 2f, 1))
                throw new InvalidOperationException("No floor below the player.");
            foreach (var root in motor.gameObject.scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                        throw new InvalidOperationException("Missing script on " + transform.name);
            Debug.Log("PLAYER_PROTOTYPE_VALIDATION_OK");
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = $"Assets/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.position = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }

        private static void SetReference(UnityEngine.Object component, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(component);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
