using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class PlayerAnimationInheritanceValidation
    {
        private const string Running = "ProjectU.PlayerAnimationInheritanceValidation";

        static PlayerAnimationInheritanceValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Animation Inheritance Validation").AddComponent<PlayerAnimationInheritanceValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Player Animation Inheritance")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            GameFlowValidationUtility.DisableAutomaticGameFlow();
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        internal static void Finish(int code)
        {
            SessionState.SetBool(Running, false);
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }

    // A different character has no rifle, reload, grenade or upper-body-layer contract.
    public sealed class PlayerAnimationInheritanceProbe : PlayerAnimationBase
    {
        public bool AimAllowed { get; set; } = true;
        public int Updates { get; private set; }
        public int Resets { get; private set; }
        public readonly List<PlayerActionSlot> Attacks = new List<PlayerActionSlot>();
        public override bool CanApplyAimPose => AimAllowed;
        protected override void UpdateCharacterAnimation() => Updates++;
        protected override void HandleAttackExecuted(PlayerActionSlot slot) => Attacks.Add(slot);
        protected override void ResetCharacterAnimation() => Resets++;
    }

    [DefaultExecutionOrder(-150)]
    public sealed class PlayerAnimationInheritanceValidationDriver : MonoBehaviour
    {
        private readonly List<string> checks = new List<string>();
        private GameObject generic;
        private GameObject staging;
        private AnimatorController controller;

        private IEnumerator Start()
        {
            Time.captureDeltaTime = 1f / 60f;
            EnemyValidationIsolation.DisableDemoEnemies();
            IEnumerator validation = Validate();
            while (true)
            {
                bool more;
                object current = null;
                try { more = validation.MoveNext(); if (more) current = validation.Current; }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    PlayerAnimationInheritanceValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("PLAYER ANIMATION INHERITANCE VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            PlayerAnimationInheritanceValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            SoilderAnimation soldier = FindFirstObjectByType<SoilderAnimation>();
            PlayerAnimationBase common = soldier.GetComponent<PlayerAnimationBase>();
            var saved = new SerializedObject(soldier);
            Animator savedAnimator = (Animator)saved.FindProperty("animator").objectReferenceValue;
            Check(common == soldier && soldier.GetComponents<PlayerAnimationBase>().Length == 1,
                "saved character resolves its single Soldier component through the common base");
            Check(savedAnimator != null && savedAnimator.isHuman &&
                Mathf.Approximately(saved.FindProperty("movementDampTime").floatValue, 0.08f),
                "inherited serialized Animator and movement damping retain their saved values");
            Check(soldier.CanApplyAimPose, "idle Soldier allows procedural aim");
            int executionOrder = MonoImporter.GetExecutionOrder(MonoScript.FromMonoBehaviour(soldier));
            if (executionOrder == 0)
                executionOrder = typeof(SoilderAnimation).GetCustomAttribute<DefaultExecutionOrder>().order;
            Check(executionOrder == 50,
                "Soldier preserves its execution order before procedural aim");

            controller = new AnimatorController();
            controller.AddLayer("Locomotion");
            controller.layers[0].stateMachine.defaultState = controller.layers[0].stateMachine.AddState("Idle");
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            generic = new GameObject("Non-Soldier Animation Fixture");
            generic.SetActive(false);
            generic.transform.position = new Vector3(0f, 1f, -10f);
            Animator animator = generic.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            PlayerAnimationInheritanceProbe probe = generic.AddComponent<PlayerAnimationInheritanceProbe>();
            var serialized = new SerializedObject(probe);
            serialized.FindProperty("animator").objectReferenceValue = animator;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            generic.SetActive(true);
            yield return null;
            Check(probe.enabled && probe.Updates > 0 && !animator.isHuman && animator.layerCount == 1,
                "alternate character runs with a non-Humanoid one-layer movement-only controller");
            Check(!animator.applyRootMotion && animator.keepAnimatorStateOnDisable &&
                animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
                "alternate character receives the common code-driven Animator setup");
            PlayerMotor motor = generic.GetComponent<PlayerMotor>();
            for (int frame = 0; frame < 12; frame++)
            {
                motor.SetMoveCommand(Vector3.right, 0f);
                yield return null;
            }
            Check(animator.GetFloat("MoveX") > 0.5f && Mathf.Abs(animator.GetFloat("MoveZ")) < 0.01f,
                "alternate character receives normalized local movement without Soldier parameters");
            Check(animator.GetBool("Grounded") == motor.IsGrounded &&
                Mathf.Approximately(animator.GetFloat("VerticalSpeed"), motor.VerticalSpeed),
                "common animation follows actual grounding and vertical movement");
            PlayerCombat combat = generic.GetComponent<PlayerCombat>();
            NotifyAttack(combat, PlayerActionSlot.Skill1);
            Check(probe.Attacks.Count == 1 && probe.Attacks[0] == PlayerActionSlot.Skill1,
                "common attack dispatch leaves a skill's visual meaning to its subclass");
            probe.enabled = false;
            NotifyAttack(combat, PlayerActionSlot.PrimaryFire);
            Check(probe.Attacks.Count == 1, "disabled animation receives no combat callback");
            for (int cycle = 0; cycle < 3; cycle++) { probe.enabled = true; probe.enabled = false; }
            probe.enabled = true;
            NotifyAttack(combat, PlayerActionSlot.Ultimate);
            Check(probe.Attacks.Count == 2 && probe.Attacks[1] == PlayerActionSlot.Ultimate,
                "repeated reenable restores exactly one combat callback");
            common = probe;
            common.ResetForStage();
            Check(probe.Resets == 1, "stage reset reaches the actual character implementation through the base");

            int updates = probe.Updates;
            Time.timeScale = 0f;
            for (int frame = 0; frame < 3; frame++) yield return null;
            Check(!animator.enabled && probe.Updates == updates,
                "pause freezes both Animator evaluation and character animation updates");
            probe.enabled = false;
            Check(animator.enabled, "disabling paused animation releases its Animator suspension");
            probe.enabled = true;
            yield return null;
            Check(!animator.enabled, "reenabling during pause freezes the Animator again");
            Time.timeScale = 1f;
            yield return null;
            Check(animator.enabled && probe.Updates > updates, "resume restores common and character animation updates");

            staging = new GameObject("Inactive Character Fixture Parent");
            staging.SetActive(false);
            GameObject alternate = Instantiate(soldier.gameObject, staging.transform);
            alternate.GetComponent<PlayerInputReader>().enabled = false;
            alternate.GetComponent<PlayerController>().enabled = false;
            alternate.GetComponent<PlayerHUD>().enabled = false;
            foreach (PlayerActionBase action in alternate.GetComponents<PlayerActionBase>()) action.enabled = false;
            DestroyImmediate(alternate.GetComponent<SoilderAnimation>());
            PlayerAnimationInheritanceProbe alternateAnimation = alternate.AddComponent<PlayerAnimationInheritanceProbe>();
            Animator alternateAnimator = alternate.GetComponentInChildren<Animator>(true);
            serialized = new SerializedObject(alternateAnimation);
            serialized.FindProperty("animator").objectReferenceValue = alternateAnimator;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            alternate.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            staging.SetActive(true);
            yield return null;
            PlayerAim aim = alternate.GetComponent<PlayerAim>();
            Check(aim.enabled && alternate.GetComponent<SoilderAnimation>() == null &&
                ReferenceEquals(typeof(PlayerAim).GetField("animationController", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(aim), alternateAnimation),
                "procedural aim binds an alternate subclass without adding a Soldier component");
            aim.enabled = false;
            MethodInfo lateAim = typeof(PlayerAim).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Transform spine = alternateAnimator.GetBoneTransform(HumanBodyBones.Spine);
            Transform chest = alternateAnimator.GetBoneTransform(HumanBodyBones.Chest);
            alternateAnimator.Update(0f);
            Quaternion spineBefore = spine.rotation, chestBefore = chest.rotation;
            alternateAnimation.AimAllowed = false;
            lateAim.Invoke(aim, null);
            Check(Quaternion.Angle(spineBefore, spine.rotation) < 0.01f && Quaternion.Angle(chestBefore, chest.rotation) < 0.01f,
                "alternate character can suppress procedural body aim through the common contract");
            alternateAnimation.AimAllowed = true;
            lateAim.Invoke(aim, null);
            Check(Quaternion.Angle(spineBefore, spine.rotation) > 0.1f || Quaternion.Angle(chestBefore, chest.rotation) > 0.1f,
                "alternate character can restore procedural body aim through the same contract");

            soldier.ResetForStage();
            for (int frame = 0; frame < 6; frame++) yield return null;
            NotifyAttack(soldier.GetComponent<PlayerCombat>(), PlayerActionSlot.SecondaryFire);
            for (int frame = 0; frame < 6; frame++) yield return null;
            Check(savedAnimator.GetCurrentAnimatorStateInfo(1).IsName("Upper Body.Rifle Aim"),
                "ADS secondary slot is not interpreted as a rifle shot");
        }

        private static void NotifyAttack(PlayerCombat combat, PlayerActionSlot slot)
            => typeof(PlayerCombat).GetMethod("NotifyAttackExecuted", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(combat, new object[] { slot });

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }

        private void OnDestroy()
        {
            if (generic != null) Destroy(generic);
            if (staging != null) Destroy(staging);
            if (controller != null) Destroy(controller);
        }
    }
}
