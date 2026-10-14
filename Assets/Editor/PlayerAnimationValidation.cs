using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class PlayerAnimationValidation
    {
        private const string Running = "ProjectU.PlayerAnimationValidation";

        static PlayerAnimationValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Player Animation Validation Driver").AddComponent<PlayerAnimationValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player Animation")]
        public static void Run()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
                AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    "Assets/Characters/Soldier/Animator/Soldier.controller");
                Check(controller != null && controller.layers.Length == 2, "Two Animator layers.");
                AvatarMask mask = controller.layers[1].avatarMask;
                Check(mask != null && mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body) &&
                    !mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root) &&
                    !mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg) &&
                    !mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg), "Upper-body mask excludes root/legs.");
                Check(controller.animationClips.Distinct().Count() == 10, "All ten imported clips are used.");
                foreach (Transform transform in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0,
                        "Missing script: " + transform.name);
                SessionState.SetBool(Running, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        public static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        public static void Finish(int code)
        {
            SessionState.SetBool(Running, false);
            Time.timeScale = 1f;
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
            else
                EditorApplication.ExitPlaymode();
        }
    }

    // Synthetic input is injected after Reader.Update so batch focus/cursor behavior
    // cannot hide the movement, animation or same-frame firing checks.
    [DefaultExecutionOrder(-150)]
    public sealed class PlayerAnimationValidationDriver : MonoBehaviour
    {
        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public int shots;
            public int aimedFrames;
            public float maximumMuzzleAngle;
            public float maximumProjectileAngle;
            public float maximumLeftGripDistance;
            public float maximumRightGripDistance;
            public bool jumpUp;
            public bool jumpLoop;
            public bool jumpDown;
            public bool firing;
            public bool reload;
            public bool reloadInterrupted;
            public bool obstructedSpawn;
            public bool pauseStable;
            public bool nearWall;
            public bool cameraFlick;
            public bool reloadPauseResumed;
            public List<string> screenshots = new List<string>();
        }

        private readonly Report report = new Report();
        private PlayerInputReader input;
        private PlayerMotor motor;
        private PlayerCameraController cameraController;
        private PlayerAnimationController animationController;
        private PlayerAimController aim;
        private PlayerProjectileLauncher launcher;
        private ProjectilePool pool;
        private Animator animator;
        private Transform leftGrip;
        private Transform weapon;
        private int phase;
        private float phaseStarted;
        private bool shotRequested;
        private bool jumpRequested;
        private bool reloadRequested;
        private bool captured;
        private int pauseFrames;
        private float pausedTime;
        private Quaternion pausedSpine;
        private GameObject obstruction;
        private Camera observer;
        private double wallStarted;
        private bool finished;
        private bool positionedNearWall;
        private float pausedReloadTime;

        private void Start()
        {
            try
            {
                input = UnityEngine.Object.FindFirstObjectByType<PlayerInputReader>();
                motor = input.GetComponent<PlayerMotor>();
                cameraController = UnityEngine.Object.FindFirstObjectByType<PlayerCameraController>();
                animationController = input.GetComponent<PlayerAnimationController>();
                aim = input.GetComponent<PlayerAimController>();
                launcher = input.GetComponent<PlayerProjectileLauncher>();
                pool = UnityEngine.Object.FindFirstObjectByType<ProjectilePool>();
                animator = input.GetComponentInChildren<Animator>();
                leftGrip = input.transform.Find("Soldier Visual/Prototype Rifle/Left Grip");
                weapon = leftGrip.parent;
                Check(animationController != null && aim != null && animationController.enabled && aim.enabled && launcher.Muzzle != null,
                    "Configured runtime components and muzzle.");
                Check(animator.isHuman && !animator.applyRootMotion && animator.runtimeAnimatorController != null,
                    "Humanoid Animator with code-driven root motion.");
                Check(input.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 2, "Visible X Bot meshes.");
                Check(!input.GetComponent<MeshRenderer>().enabled, "Prototype capsule hidden.");
                Check(input.transform.Find("Soldier Visual").GetComponentsInChildren<Collider>().Length == 0,
                    "Visuals must not add self-colliders.");
                var controller = new SerializedObject(input.GetComponent<PlayerController>());
                controller.FindProperty("logInput").boolValue = false;
                controller.ApplyModifiedPropertiesWithoutUndo();
                launcher.PrimaryFired += ValidateShot;
                report.unityVersion = Application.unityVersion;
                phaseStarted = Time.time;
                wallStarted = EditorApplication.timeSinceStartup;
                gameObject.AddComponent<PlayerAnimationValidationObserver>().Driver = this;
                Directory.CreateDirectory("Logs/PlayerAnimationScreenshots");
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    observer = new GameObject("Validation Observer").AddComponent<Camera>();
                    observer.enabled = false;
                    observer.fieldOfView = 45f;
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void Update()
        {
            if (finished || input == null)
                return;
            try
            {
                Check(EditorApplication.timeSinceStartup - wallStarted < 60d, "Validation timeout.");
                SetInput("Move", Vector2.zero);
                SetInput("Look", Vector2.zero);
                SetInput("Jump", default(ButtonInputState));
                SetInput("Reload", default(ButtonInputState));
                SetInput("PrimaryFire", default(ButtonInputState));
                SetInput("SecondaryFire", default(ButtonInputState));
                float elapsed = Time.time - phaseStarted;
                float yaw = 0f;
                float pitch = 0f;
                if (phase >= 1 && phase <= 6)
                {
                    Vector2[] moves = { Vector2.up, Vector2.down, Vector2.left, Vector2.right,
                        new Vector2(1f, 1f).normalized, new Vector2(-1f, 1f).normalized };
                    SetInput("Move", moves[phase - 1]);
                    if (phase == 6) yaw = 135f;
                }
                if (phase >= 7 && phase <= 18)
                {
                    int angle = phase - 7;
                    yaw = (angle / 3) * 90f;
                    pitch = new[] { -40f, 0f, 70f }[angle % 3];
                    if (elapsed > 0.10f && !shotRequested)
                    {
                        SetInput("PrimaryFire", Pressed());
                        shotRequested = true;
                    }
                }
                if (phase == 19)
                {
                    SetInput("Move", new Vector2(0.4f, 0.8f));
                    pitch = -25f;
                    if (elapsed > 0.05f && !jumpRequested)
                    {
                        SetInput("Jump", Pressed());
                        jumpRequested = true;
                    }
                    if (elapsed > 0.22f && !shotRequested)
                    {
                        SetInput("PrimaryFire", Pressed());
                        shotRequested = true;
                    }
                }
                if (phase == 20)
                {
                    SetInput("Move", Vector2.right);
                    if (!reloadRequested)
                    {
                        SetInput("Reload", Pressed());
                        reloadRequested = true;
                    }
                    if (elapsed > 0.40f && !shotRequested)
                    {
                        Check(animationController.IsReloading, "Reload visual is active before interrupting.");
                        SetInput("PrimaryFire", Pressed());
                        shotRequested = true;
                    }
                }
                if (phase == 21 && pauseFrames == 0)
                {
                    pausedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    pausedSpine = animator.GetBoneTransform(HumanBodyBones.Spine).rotation;
                    Time.timeScale = 0f;
                }
                if (phase == 22)
                {
                    if (obstruction == null)
                    {
                        obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        obstruction.name = "Validation Muzzle Obstruction";
                        obstruction.transform.localScale = Vector3.one * 0.08f;
                        Physics.IgnoreCollision(obstruction.GetComponent<Collider>(), input.GetComponent<CharacterController>());
                    }
                    // Offset camera-center ray misses this narrow chest-to-muzzle blocker.
                    obstruction.transform.position = Vector3.Lerp(
                        animator.GetBoneTransform(HumanBodyBones.Chest).position, launcher.Muzzle.position, 0.6f);
                    Physics.SyncTransforms();
                    if (elapsed > 0.10f && !shotRequested)
                    {
                        SetInput("PrimaryFire", Pressed());
                        shotRequested = true;
                    }
                }
                if (phase == 23)
                {
                    if (!positionedNearWall)
                    {
                        Destroy(obstruction);
                        obstruction = null;
                        CharacterController character = input.GetComponent<CharacterController>();
                        character.enabled = false;
                        input.transform.position = new Vector3(0f, 1.05f, 18.99f);
                        character.enabled = true;
                        Physics.SyncTransforms();
                        positionedNearWall = true;
                    }
                    if (elapsed > 0.10f && !shotRequested)
                    {
                        SetInput("PrimaryFire", Pressed());
                        shotRequested = true;
                    }
                }
                if (phase == 24)
                {
                    yaw = 125f;
                    pitch = -35f;
                    SetInput("Move", Vector2.left);
                    if (!shotRequested)
                    {
                        SetInput("PrimaryFire", Pressed());
                        shotRequested = true;
                    }
                }
                if (phase == 25)
                {
                    if (!reloadRequested)
                    {
                        SetInput("Reload", Pressed());
                        reloadRequested = true;
                    }
                    if (elapsed > 0.20f && pauseFrames == 0)
                    {
                        Check(StateName(1) == "Reloading" && animationController.IsReloading, "Reload started before pausing.");
                        Time.timeScale = 0f;
                        pauseFrames = 1;
                    }
                }
                SetPrivate(cameraController, "<Yaw>k__BackingField", yaw);
                SetPrivate(cameraController, "pitch", pitch);
            }
            catch (Exception exception) { Fail(exception); }
        }

        public void ObserveLateFrame()
        {
            if (finished) return;
            try { CheckFrame(); }
            catch (Exception exception) { Fail(exception); }
        }

        private void CheckFrame()
        {
            float elapsed = Time.time - phaseStarted;
            string baseState = StateName(0);
            string upperState = StateName(1);
            report.jumpUp |= phase == 19 && baseState == "Jump Up";
            report.jumpLoop |= phase == 19 && baseState == "Jump Loop";
            report.jumpDown |= phase == 19 && baseState == "Jump Down";
            report.firing |= upperState == "Firing Rifle";
            report.reload |= upperState == "Reloading";
            if (phase == 25 && pauseFrames > 0)
            {
                pauseFrames++;
                if (pauseFrames == 2)
                    pausedReloadTime = animator.GetCurrentAnimatorStateInfo(1).normalizedTime;
                if (pauseFrames >= 7)
                {
                    Check(Mathf.Abs(animator.GetCurrentAnimatorStateInfo(1).normalizedTime - pausedReloadTime) < 0.0001f,
                        "Reload animation stays at the paused time.");
                    Time.timeScale = 1f;
                    NextPhase();
                }
                return;
            }
            if (phase == 26)
            {
                Check(animationController.IsReloading && upperState == "Reloading", "Reload state resumes after pause.");
                Check(animator.GetCurrentAnimatorStateInfo(1).normalizedTime >= pausedReloadTime - 0.0001f,
                    "Reload resumes from the saved time instead of restarting.");
                report.reloadPauseResumed = true;
            }
            if (phase == 20 && elapsed > 0.46f && elapsed < 0.60f)
                Check(upperState == "Firing Rifle", "Actual fire animation interrupts the reload state.");
            if (phase >= 7 && !animationController.IsReloading && phase != 21)
            {
                float angle = Vector3.Angle(launcher.Muzzle.forward, aim.AimPoint - launcher.Muzzle.position);
                report.maximumMuzzleAngle = Mathf.Max(report.maximumMuzzleAngle, angle);
                report.aimedFrames++;
                Check(angle < 0.1f, "Muzzle alignment at phase " + phase + ": " + angle);
                float gripDistance = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position, leftGrip.position);
                report.maximumLeftGripDistance = Mathf.Max(report.maximumLeftGripDistance, gripDistance);
                if (gripDistance >= 0.03f && observer != null)
                    Capture("failed-grip-" + phase);
                Check(gripDistance < 0.03f, "Left-hand grip at phase " + phase + ": " + gripDistance);
                float rightDistance = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.RightHand).position, weapon.position);
                report.maximumRightGripDistance = Mathf.Max(report.maximumRightGripDistance, rightDistance);
                Check(rightDistance < 0.03f, "Right-hand grip at phase " + phase + ": " + rightDistance);
            }
            if (phase >= 1 && phase <= 6 && elapsed > 0.32f)
            {
                Vector3 velocity = input.transform.InverseTransformDirection(motor.HorizontalVelocity);
                Check(velocity.magnitude > 2f, "Actual movement.");
                Check(Mathf.Abs(animator.GetFloat("MoveX") - velocity.x / motor.MoveSpeed) < 0.12f &&
                    Mathf.Abs(animator.GetFloat("MoveZ") - velocity.z / motor.MoveSpeed) < 0.12f,
                    "Animation follows camera-relative movement at phase " + phase +
                    ": velocity=" + velocity + ", params=" + animator.GetFloat("MoveX") + "," + animator.GetFloat("MoveZ") + ", elapsed=" + elapsed);
            }
            if (phase == 21)
            {
                pauseFrames++;
                if (pauseFrames == 1)
                {
                    pausedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    pausedSpine = animator.GetBoneTransform(HumanBodyBones.Spine).rotation;
                }
                if (pauseFrames > 1)
                    Check(Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime - pausedTime) < 0.0001f &&
                        Quaternion.Angle(animator.GetBoneTransform(HumanBodyBones.Spine).rotation, pausedSpine) < 0.1f,
                        "Pause preserves animation and procedural pose: time delta=" +
                        (animator.GetCurrentAnimatorStateInfo(0).normalizedTime - pausedTime) + ", angle=" +
                        Quaternion.Angle(animator.GetBoneTransform(HumanBodyBones.Spine).rotation, pausedSpine));
                if (pauseFrames >= 6)
                {
                    report.pauseStable = true;
                    Time.timeScale = 1f;
                    NextPhase();
                }
                return;
            }
            if (observer != null && !captured &&
                ((phase == 8 && elapsed > 0.15f) || (phase == 7 && elapsed > 0.15f) ||
                 (phase == 9 && elapsed > 0.15f) || (phase == 19 && elapsed > 0.30f) ||
                 (phase == 20 && elapsed > 0.22f) || (phase == 23 && elapsed > 0.15f)))
            {
                Capture("phase-" + phase + "-" + baseState.Replace(" ", "-"));
                captured = true;
            }
            float duration = phase == 0 ? 0.20f : phase <= 6 ? 0.40f : phase == 19 ? 1.1f : phase == 20 ? 0.75f : 0.30f;
            if (elapsed >= duration)
            {
                if (phase == 26)
                    Complete();
                else
                    NextPhase();
            }
        }

        private void ValidateShot()
        {
            try
            {
                ProjectileBullet bullet = pool.GetComponentsInChildren<ProjectileBullet>()
                    .Where(item => item.IsActive).OrderBy(item => Vector3.Distance(item.transform.position, launcher.Muzzle.position)).First();
                Vector3 expected = aim.AimPoint - bullet.transform.position;
                float angle = Vector3.Angle(bullet.GetComponent<Rigidbody>().linearVelocity, expected);
                report.maximumProjectileAngle = Mathf.Max(report.maximumProjectileAngle, angle);
                Check(angle < 0.1f, "Projectile direction uses the same-frame aim point: " + angle);
                report.shots++;
                if (phase == 20)
                {
                    Check(!animationController.IsReloading, "Shot interrupts visual reload.");
                    report.reloadInterrupted = true;
                }
                if (phase == 22)
                {
                    Check(Vector3.Distance(bullet.transform.position, launcher.Muzzle.position) > 0.05f,
                        "Obstruction moves spawn to the player side.");
                    Check(!obstruction.GetComponent<Collider>().bounds.Contains(bullet.transform.position),
                        "Obstructed projectile starts outside the blocker.");
                    report.obstructedSpawn = true;
                }
                if (phase == 23)
                {
                    Check(launcher.Muzzle.position.z < 19.5f && bullet.transform.position.z < 19.5f,
                        "Near-wall muzzle and projectile remain on the player side.");
                    Check(Vector3.Distance(launcher.Muzzle.position, aim.AimPoint) >= 0.07f,
                        "Near-wall barrel retracts from the aim point.");
                    report.nearWall = true;
                }
                if (phase == 24)
                {
                    Check(Mathf.Abs(Mathf.DeltaAngle(cameraController.Yaw, 125f)) < 0.01f,
                        "Camera flick reached the requested yaw in the firing frame.");
                    report.cameraFlick = true;
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void NextPhase()
        {
            phase++;
            phaseStarted = Time.time;
            shotRequested = jumpRequested = reloadRequested = captured = false;
            pauseFrames = 0;
            pool.ReturnAll();
        }

        private string StateName(int layer)
        {
            AnimatorStateInfo state = animator.IsInTransition(layer)
                ? animator.GetNextAnimatorStateInfo(layer) : animator.GetCurrentAnimatorStateInfo(layer);
            foreach (string name in new[] { "Locomotion", "Jump Up", "Jump Loop", "Jump Down", "Rifle Aim", "Firing Rifle", "Reloading" })
                if (state.shortNameHash == Animator.StringToHash(name)) return name;
            return "Unknown";
        }

        private void Capture(string name)
        {
            Vector3 offset = phase == 23 ? new Vector3(-3f, 1f, -4f) : new Vector3(3f, 1f, 4f);
            observer.transform.position = input.transform.position + input.transform.rotation * offset;
            observer.transform.LookAt(input.transform.position + Vector3.up * 0.05f);
            Render(observer, "Logs/PlayerAnimationScreenshots/" + name + "-observer.png");
            Render(cameraController.GetComponent<Camera>(), "Logs/PlayerAnimationScreenshots/" + name + "-game.png");
        }

        private void Render(Camera camera, string path)
        {
            var texture = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                report.screenshots.Add(path);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                texture.Release();
                Destroy(texture);
                Destroy(pixels);
            }
        }

        private void Complete()
        {
            Check(report.shots == 17, "Expected 12 angle shots, jump, reload interrupt, obstruction, near wall and camera flick shots.");
            Check(report.jumpUp && report.jumpLoop && report.jumpDown && report.firing && report.reload &&
                report.reloadInterrupted && report.obstructedSpawn && report.pauseStable && report.nearWall && report.cameraFlick &&
                report.reloadPauseResumed,
                "All runtime scenarios passed.");
            File.WriteAllText("Logs/PlayerAnimationValidationReport.json", JsonUtility.ToJson(report, true));
            finished = true;
            Debug.Log("PLAYER ANIMATION VALIDATION PASSED: " + JsonUtility.ToJson(report));
            PlayerAnimationValidation.Finish(0);
        }

        private void Fail(Exception exception)
        {
            if (finished) return;
            finished = true;
            Debug.LogException(exception);
            PlayerAnimationValidation.Finish(1);
        }

        private void SetInput(string name, object value) => typeof(PlayerInputReader).GetProperty(name).SetValue(input, value);
        private static void SetPrivate(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static ButtonInputState Pressed() => (ButtonInputState)typeof(ButtonInputState)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(bool), typeof(bool), typeof(bool) }, null).Invoke(new object[] { true, true, false });
        private static void Check(bool condition, string message) => PlayerAnimationValidation.Check(condition, message);
    }

    [DefaultExecutionOrder(200)]
    public sealed class PlayerAnimationValidationObserver : MonoBehaviour
    {
        public PlayerAnimationValidationDriver Driver { get; set; }
        private void LateUpdate() => Driver?.ObserveLateFrame();
    }
}
