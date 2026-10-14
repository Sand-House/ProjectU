using UnityEngine;

namespace ProjectU
{
    // Runs after the camera and Animator, before Launcher.LateUpdate emits a projectile.
    [DefaultExecutionOrder(75)]
    [RequireComponent(typeof(PlayerAnimationController), typeof(PlayerProjectileLauncher), typeof(PlayerInputReader))]
    public sealed class PlayerAimController : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Transform weapon;
        [SerializeField] private Transform leftGrip;
        [SerializeField] private Quaternion rightHandRotationOffset = Quaternion.identity;
        [SerializeField] private Quaternion leftHandRotationOffset = Quaternion.identity;
        [SerializeField, Range(0f, 1f)] private float spineWeight = 0.35f;
        [SerializeField, Range(0f, 90f)] private float maximumBodyAngle = 80f;

        private PlayerAnimationController animationController;
        private PlayerProjectileLauncher launcher;
        private PlayerInputReader input;
        private Transform spine;
        private Transform chest;
        private Transform neck;
        private Transform rightHand;
        private Transform rightUpperArm;
        private Transform rightForearm;
        private Transform leftUpperArm;
        private Transform leftForearm;
        private Transform leftHand;

        public Vector3 AimPoint { get; private set; }

        private void Awake()
        {
            animationController = GetComponent<PlayerAnimationController>();
            launcher = GetComponent<PlayerProjectileLauncher>();
            input = GetComponent<PlayerInputReader>();
            if (animator == null || !animator.isHuman || weapon == null || leftGrip == null || launcher.Muzzle == null)
            {
                FailConfiguration();
                return;
            }
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (spine == null || chest == null || neck == null || rightHand == null || rightUpperArm == null || rightForearm == null || leftUpperArm == null ||
                leftForearm == null || leftHand == null)
                FailConfiguration();
        }

        private void FailConfiguration()
        {
            Debug.LogError("PlayerAimController requires Humanoid bones, a weapon, a left grip and a muzzle.", this);
            enabled = false;
        }

        private void LateUpdate()
        {
            if (Time.deltaTime <= 0f)
                return;
            AimPoint = launcher.GetAimPoint();
            bool aim = !animationController.IsReloading || input.PrimaryFire.Pressed;
            if (aim)
            {
                Vector3 bodyDirection = AimPoint - chest.position;
                if (bodyDirection.sqrMagnitude > 0.0001f)
                {
                    // Humanoid clips can twist the torso independently of the player root.
                    // Use the evaluated shoulder/neck pose; bone-local +Z is not a reliable
                    // facing axis across imported rigs. Both axes are below Chest, so the
                    // distributed Spine/Chest turn rotates this frame as a whole.
                    Vector3 bodyRight = rightUpperArm.position - leftUpperArm.position;
                    Vector3 bodyUp = neck.position - chest.position;
                    Vector3 bodyForward = Vector3.Cross(bodyRight, bodyUp);
                    if (bodyForward.sqrMagnitude < 0.000001f)
                        bodyForward = transform.forward;
                    Quaternion turn = Quaternion.FromToRotation(bodyForward.normalized, bodyDirection.normalized);
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
                float minimumDistance = Vector3.Distance(weapon.position, launcher.Muzzle.position) + 0.08f;
                if (direction.magnitude < minimumDistance)
                    weapon.position -= direction.normalized * (minimumDistance - direction.magnitude);
            }
            else if (!aim)
                weapon.rotation = rightHand.rotation * Quaternion.Inverse(rightHandRotationOffset);
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
