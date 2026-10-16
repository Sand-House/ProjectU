using System;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(PlayerInputReader))]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerProjectileLauncher : MonoBehaviour
    {
        [SerializeField] private HitscanAttack primaryAttack;
        [SerializeField] private ProjectileAttack secondaryAttack;
        [SerializeField] private ProjectileAttack grenadeAttack;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private Transform muzzle;
        [SerializeField, Min(1f)] private float aimDistance = 100f;
        private PlayerInputReader input;
        private PlayerStat stat;
        private readonly RaycastHit[] aimHits = new RaycastHit[32];

        public event Action PrimaryFired;
        public event Action SecondaryFired;
        public event Action GrenadeThrown;
        public event Action WeaponFired;
        public Transform Muzzle => muzzle;
        public HitscanAttack PrimaryAttack => primaryAttack;
        public ProjectileAttack SecondaryAttack => secondaryAttack;
        public ProjectileAttack GrenadeAttack => grenadeAttack;

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            stat = GetComponent<PlayerStat>();
            if (primaryAttack != null && secondaryAttack != null && grenadeAttack != null && aimCamera != null && muzzle != null) return;
            Debug.LogError("PlayerProjectileLauncher requires three attack components, an aim camera and a muzzle.", this);
            enabled = false;
        }

        private void OnEnable()
        {
            if (primaryAttack != null) primaryAttack.OnExecuted += OnPrimaryExecuted;
            if (secondaryAttack != null) secondaryAttack.OnExecuted += OnSecondaryExecuted;
            if (grenadeAttack != null) grenadeAttack.OnExecuted += OnGrenadeExecuted;
        }

        private void LateUpdate()
        {
            if (Time.deltaTime <= 0f) return;
            Vector3 point = GetAimPoint();
            Prepare(primaryAttack, point);
            Prepare(secondaryAttack, point);
            Prepare(grenadeAttack, point);
            // Keep short taps, and retry held fire when Attack's timing permits another shot.
            if (input.PrimaryFire.Pressed || input.PrimaryFire.Held) primaryAttack.TryAttack(muzzle, point);
            if (input.SecondaryFire.Pressed || input.SecondaryFire.Held) secondaryAttack.TryAttack(muzzle, point);
            if (input.Skill1.Pressed) grenadeAttack.TryAttack(muzzle, point);
        }

        private void Prepare(Attack attack, Vector3 point)
        {
            attack.SetAimPoint(point);
            attack.DamageMultiplier = stat != null ? stat.AttackMultiplier : 1f;
            attack.SpeedMultiplier = stat != null ? stat.AttackSpeed : 1f;
        }

        // Both the pose controller and projectiles converge on this camera-center target.
        public Vector3 GetAimPoint()
        {
            Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            Vector3 aimPoint = ray.GetPoint(aimDistance);
            float closest = aimDistance;
            int mask = (primaryAttack != null && primaryAttack.Data != null ? (int)primaryAttack.Data.HitMask : 0) |
                (secondaryAttack != null && secondaryAttack.Data != null ? (int)secondaryAttack.Data.HitMask : 0) |
                (grenadeAttack != null && grenadeAttack.Data != null ? (int)grenadeAttack.Data.HitMask : 0);
            int count = Physics.RaycastNonAlloc(ray, aimHits, aimDistance, mask, QueryTriggerInteraction.Collide);
            // A full buffer may omit the closest hit, so use the complete set in this rare case.
            RaycastHit[] hits = count == aimHits.Length
                ? Physics.RaycastAll(ray, aimDistance, mask, QueryTriggerInteraction.Collide)
                : aimHits;
            if (hits != aimHits)
                count = hits.Length;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Projectile>() != null) continue;
                if (hit.collider.isTrigger && !Hitbox.TryResolve(hit.collider, out _, out _)) continue;
                if (hit.distance >= closest) continue;
                closest = hit.distance;
                aimPoint = hit.point;
            }
            return aimPoint;
        }

        private void OnPrimaryExecuted()
        {
            WeaponFired?.Invoke();
            PrimaryFired?.Invoke();
        }

        private void OnSecondaryExecuted()
        {
            WeaponFired?.Invoke();
            SecondaryFired?.Invoke();
        }

        private void OnGrenadeExecuted() => GrenadeThrown?.Invoke();

        private void OnDisable()
        {
            if (primaryAttack != null) { primaryAttack.OnExecuted -= OnPrimaryExecuted; primaryAttack.CancelAttack(); }
            if (secondaryAttack != null) { secondaryAttack.OnExecuted -= OnSecondaryExecuted; secondaryAttack.CancelAttack(); }
            if (grenadeAttack != null) { grenadeAttack.OnExecuted -= OnGrenadeExecuted; grenadeAttack.CancelAttack(); }
        }
    }
}
