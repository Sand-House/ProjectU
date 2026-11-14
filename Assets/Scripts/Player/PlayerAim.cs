using UnityEngine;

namespace ProjectU
{
    // Runs after camera and Animator, before Launcher requests attacks and Attack executes.
    [DefaultExecutionOrder(75)]
    [RequireComponent(typeof(PlayerCombat))]
    public sealed class PlayerAim : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Transform weapon;
        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform leftGrip;
        [SerializeField] private Quaternion rightHandRotationOffset = Quaternion.identity;
        [SerializeField] private Quaternion leftHandRotationOffset = Quaternion.identity;
        [SerializeField, Range(0f, 1f)] private float spineWeight = 0.35f;
        [SerializeField, Range(0f, 90f)] private float maximumBodyAngle = 80f;
        [Header("Aim Down Sights")]
        [SerializeField, Tooltip("Weapon grip position relative to the camera at full ADS. The existing iron sight sits above this grip.")]
        private Vector3 aimWeaponPosition = new Vector3(0f, -0.16f, 0.35f);

        private PlayerAnimationBase animationController;
        private PlayerCombat launcher;
        private PlayerCamera playerCamera;
        private Transform spine;
        private Transform chest;
        private Transform rightHand;
        private Transform rightUpperArm;
        private Transform rightForearm;
        private Transform leftUpperArm;
        private Transform leftForearm;
        private Transform leftHand;

        public Vector3 AimPoint { get; private set; }
        public Transform Muzzle => muzzle;

        private void Awake()
        {
            animationController = GetComponent<PlayerAnimationBase>();
            launcher = GetComponent<PlayerCombat>();
            playerCamera = launcher.AimCamera != null ? launcher.AimCamera.GetComponent<PlayerCamera>() : null;
            if (animationController == null || animator == null || !animator.isHuman ||
                weapon == null || leftGrip == null || muzzle == null)
            {
                FailConfiguration();
                return;
            }
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (spine == null || chest == null || rightHand == null || rightUpperArm == null || rightForearm == null || leftUpperArm == null ||
                leftForearm == null || leftHand == null)
                FailConfiguration();
        }

        private void FailConfiguration()
        {
            Debug.LogError("PlayerAim requires PlayerAnimationBase, Humanoid bones, a weapon, a left grip and a muzzle.", this);
            enabled = false;
        }

        private void LateUpdate()
        {
            if (Time.deltaTime <= 0f)
                return;
            AimPoint = launcher.GetAimPoint();
            bool aim = animationController.CanApplyAimPose;
            if (aim)
            {
                Vector3 bodyDirection = AimPoint - chest.position;
                if (bodyDirection.sqrMagnitude > 0.0001f)
                {
                    Quaternion turn = Quaternion.FromToRotation(transform.forward, bodyDirection.normalized);
                    turn = Quaternion.RotateTowards(Quaternion.identity, turn, maximumBodyAngle);
                    spine.rotation = Quaternion.Slerp(Quaternion.identity, turn, spineWeight) * spine.rotation;
                    chest.rotation = Quaternion.Slerp(Quaternion.identity, turn, 1f - spineWeight) * chest.rotation;
                }
            }

            // The weapon pivot is the right grip. Its muzzle lies on local +Z, so its
            // forward ray passes through AimPoint even with an offset shoulder camera.
            weapon.position = rightHand.position;
            Vector3 direction = AimPoint - weapon.position;
            if (aim && direction.sqrMagnitude > 0.0001f)
            {
                weapon.rotation = Quaternion.LookRotation(direction, transform.up);
                float minimumDistance = Vector3.Distance(weapon.position, muzzle.position) + 0.08f;
                if (direction.magnitude < minimumDistance)
                    weapon.position -= direction.normalized * (minimumDistance - direction.magnitude);
            }
            else if (!aim)
                weapon.rotation = rightHand.rotation * Quaternion.Inverse(rightHandRotationOffset);
            if (aim && playerCamera != null && playerCamera.AimBlend > 0f)
            {
                // The camera is placed first. Align the rifle to its sight line without
                // feeding an animated weapon transform back into camera positioning.
                Transform view = playerCamera.transform;
                Vector3 aimPosition = playerCamera.AimCameraPosition + view.rotation * aimWeaponPosition;
                weapon.position = Vector3.Lerp(weapon.position, aimPosition, playerCamera.AimBlend);
                weapon.rotation = Quaternion.Slerp(weapon.rotation, view.rotation, playerCamera.AimBlend);
            }
            if (aim)
            {
                SolveArm(rightUpperArm, rightForearm, rightHand, weapon.position,
                    weapon.rotation * rightHandRotationOffset, transform.right);
                SolveArm(leftUpperArm, leftForearm, leftHand, leftGrip.position,
                    leftGrip.rotation * leftHandRotationOffset, -transform.right);
            }
            else
                rightHand.rotation = weapon.rotation * rightHandRotationOffset;
        }

        private void OnValidate()
        {
            if (!AttackDataBase.IsFinite(aimWeaponPosition)) aimWeaponPosition = new Vector3(0f, -0.16f, 0.35f);
        }

        private void SolveArm(Transform upperArm, Transform forearm, Transform hand,
            Vector3 target, Quaternion handRotation, Vector3 fallbackBend)
        {
            Vector3 shoulder = upperArm.position;
            Vector3 elbow = forearm.position;
            float upperLength = Vector3.Distance(shoulder, elbow);
            float lowerLength = Vector3.Distance(elbow, hand.position);
            Vector3 offset = target - shoulder;
            if (offset.sqrMagnitude < 0.000001f || upperLength < 0.0001f || lowerLength < 0.0001f)
                return;
            Vector3 direction = offset.normalized;
            float distance = Mathf.Clamp(offset.magnitude,
                Mathf.Abs(upperLength - lowerLength) + 0.0001f, upperLength + lowerLength - 0.0001f);
            Vector3 bend = Vector3.ProjectOnPlane(elbow - shoulder, direction);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.ProjectOnPlane(fallbackBend, direction);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.ProjectOnPlane(transform.up, direction);
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 desiredElbow = shoulder + direction * along + bend.normalized * height;
            upperArm.rotation = Quaternion.FromToRotation(elbow - shoulder, desiredElbow - shoulder) * upperArm.rotation;
            Vector3 reachableTarget = shoulder + direction * distance;
            forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position,
                reachableTarget - forearm.position) * forearm.rotation;
            hand.rotation = handRotation;
        }
    }
}
