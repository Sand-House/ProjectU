using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerStat))]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerAttackController : MonoBehaviour
    {
        [SerializeField] private PlayerAttackBinding[] attackBindings = Array.Empty<PlayerAttackBinding>();
        [SerializeField] private Camera aimCamera;
        [SerializeField, Min(1f), Tooltip("Maximum camera-center aim-point search distance. Does not limit attack range.")]
        private float aimDistance = 100f;
        private PlayerInputReader input;
        private PlayerStat stat;
        private PlayerMotor motor;
        private int currentAmmo;
        private float reloadWorkRemaining;
        private readonly RaycastHit[] aimHits = new RaycastHit[32];
        private readonly Dictionary<Attack, Action> executionHandlers = new Dictionary<Attack, Action>();
        private readonly Dictionary<Attack, Action> startHandlers = new Dictionary<Attack, Action>();

        public event Action<AttackSlot> AttackExecuted;
        public event Action OnReloadStarted;
        public event Action OnReloadCompleted;
        public IReadOnlyList<PlayerAttackBinding> AttackBindings => attackBindings;
        public int CurrentAmmo => Mathf.Min(currentAmmo, MaxAmmo);
        public int MaxAmmo => stat.MaxMagazine;
        public bool IsReloading { get; private set; }
        public float ReloadRemaining => IsReloading ? reloadWorkRemaining / stat.ReloadSpeedMultiplier : 0f;
        public bool CanReload => isActiveAndEnabled && Time.timeScale > 0f && Time.deltaTime > 0f &&
            !IsReloading && CurrentAmmo < MaxAmmo && CanUseWeapon() && !IsReloadBlocked();

        public PlayerAttackBinding GetBinding(AttackSlot slot)
        {
            foreach (PlayerAttackBinding binding in attackBindings)
                if (binding != null && binding.Slot == slot) return binding;
            return null;
        }

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            stat = GetComponent<PlayerStat>();
            motor = GetComponent<PlayerMotor>();
            currentAmmo = MaxAmmo;
        }

        private void OnEnable()
        {
            if (!ValidateBindings())
            {
                enabled = false;
                return;
            }
            foreach (PlayerAttackBinding binding in attackBindings)
            {
                Action handler = () => OnAttackExecuted(binding.Slot);
                executionHandlers.Add(binding.Attack, handler);
                binding.Attack.OnExecuted += handler;
                Action started = () => OnAttackStarted(binding);
                startHandlers.Add(binding.Attack, started);
                binding.Attack.OnStarted += started;
            }
            if (motor != null) motor.OnDashExecuted += OnDashExecuted;
        }

        private bool ValidateBindings()
        {
            if (aimCamera == null)
            {
                Debug.LogError("PlayerAttackController requires an aim camera.", this);
                return false;
            }
            var slots = new HashSet<AttackSlot>();
            var attacks = new HashSet<Attack>();
            foreach (PlayerAttackBinding binding in attackBindings)
            {
                if (binding == null || !Enum.IsDefined(typeof(AttackSlot), binding.Slot) ||
                    binding.Attack == null || binding.Origin == null || binding.Attack.gameObject != gameObject ||
                    !slots.Add(binding.Slot) || !attacks.Add(binding.Attack))
                {
                    Debug.LogError("PlayerAttackController bindings require a unique slot, a unique Attack on this player and an origin.", this);
                    return false;
                }
            }
            return true;
        }

        private void LateUpdate()
        {
            currentAmmo = CurrentAmmo;
            if (!CanUseWeapon())
            {
                CancelReload();
                foreach (PlayerAttackBinding binding in attackBindings)
                    if (binding.Attack != null) binding.Attack.CancelAttack();
                return;
            }
            if (Time.timeScale <= 0f || Time.deltaTime <= 0f) return;
            bool wasReloading = IsReloading;
            Vector3 point = GetAimPoint();
            foreach (PlayerAttackBinding binding in attackBindings)
            {
                Attack attack = binding.Attack;
                if (attack == null || attack.Data == null) continue;
                Prepare(attack, point);
                // Secondary fire and skills get to reserve their action before reload is checked.
                if (binding.Slot == AttackSlot.PrimaryFire ||
                    (IsReloading && binding.Slot == AttackSlot.SecondaryFire)) continue;
                if (WantsAttack(binding))
                    attack.TryAttack(binding.Origin, point);
            }

            if (IsReloading)
            {
                if (IsReloadBlocked()) CancelReload();
                else
                {
                    // Measure work at base speed so live upgrades also match the animation speed.
                    reloadWorkRemaining = Mathf.Max(0f, reloadWorkRemaining - Time.deltaTime * stat.ReloadSpeedMultiplier);
                    if (reloadWorkRemaining <= 0f)
                    {
                        currentAmmo = MaxAmmo;
                        IsReloading = false;
                        OnReloadCompleted?.Invoke();
                    }
                }
            }

            // This is deliberately level-triggered: an empty magazine retries after every interruption.
            if (input.Reload.Pressed || CurrentAmmo == 0) TryReload();
            // Let Aim evaluate the restored pose before the first post-reload shot.
            if (wasReloading || IsReloading || CurrentAmmo == 0) return;
            PlayerAttackBinding primary = GetBinding(AttackSlot.PrimaryFire);
            if (primary != null && primary.Attack.Data != null && WantsAttack(primary))
                primary.Attack.TryAttack(primary.Origin, point);
        }

        public bool TryReload()
        {
            if (!CanReload) return false;
            reloadWorkRemaining = stat.ReloadDuration;
            IsReloading = true;
            OnReloadStarted?.Invoke();
            return true;
        }

        public bool CancelReload()
        {
            if (!IsReloading) return false;
            IsReloading = false;
            reloadWorkRemaining = 0f;
            return true;
        }

        private bool CanUseWeapon()
        {
            // An attack's own lock must not cancel its pending execution.
            if (motor != null && motor.IsDashActionLocked) return false;
            Attack primary = GetBinding(AttackSlot.PrimaryFire)?.Attack;
            // Other attack slots can still be used by a character with no primary binding.
            if (primary != null) return primary.Owner != null && primary.Owner.IsAlive;
            Combatant owner = GetComponent<Combatant>();
            return owner == null || owner.IsAlive;
        }

        private bool IsReloadBlocked()
        {
            Attack primary = GetBinding(AttackSlot.PrimaryFire)?.Attack;
            return primary == null || primary.Owner.IsPerformingAttack;
        }

        private bool WantsAttack(PlayerAttackBinding binding)
        {
            ButtonInputState state = ReadInput(binding.Slot);
            return state.Pressed || (binding.Attack.Data.RepeatWhileHeld && state.Held);
        }

        private void OnAttackStarted(PlayerAttackBinding binding)
        {
            if (binding.Slot == AttackSlot.PrimaryFire || binding.Slot == AttackSlot.SecondaryFire)
            {
                if (IsReloading || (binding.Slot == AttackSlot.PrimaryFire && CurrentAmmo == 0))
                {
                    binding.Attack.CancelAttack();
                    return;
                }
            }
            if (binding.Slot == AttackSlot.PrimaryFire) return;
            if (binding.Slot != AttackSlot.SecondaryFire) CancelReload();
        }

        private void OnAttackExecuted(AttackSlot slot)
        {
            if (slot == AttackSlot.PrimaryFire) currentAmmo = Mathf.Max(0, CurrentAmmo - 1);
            AttackExecuted?.Invoke(slot);
        }

        private void OnDashExecuted() => CancelReload();

        private ButtonInputState ReadInput(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.PrimaryFire: return input.PrimaryFire;
                case AttackSlot.SecondaryFire: return input.SecondaryFire;
                case AttackSlot.Skill1: return input.Skill1;
                case AttackSlot.Skill2: return input.Skill2;
                case AttackSlot.Ultimate: return input.Ultimate;
                default: return default;
            }
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
            int mask = 0;
            foreach (PlayerAttackBinding binding in attackBindings)
                if (binding != null && binding.Attack != null && binding.Attack.Data != null)
                    mask |= binding.Attack.Data.HitMask;
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

        private void OnDisable()
        {
            CancelReload();
            if (motor != null) motor.OnDashExecuted -= OnDashExecuted;
            foreach (KeyValuePair<Attack, Action> subscription in startHandlers)
                if (subscription.Key != null) subscription.Key.OnStarted -= subscription.Value;
            startHandlers.Clear();
            foreach (KeyValuePair<Attack, Action> subscription in executionHandlers)
            {
                if (subscription.Key == null) continue;
                subscription.Key.OnExecuted -= subscription.Value;
                subscription.Key.CancelAttack();
            }
            executionHandlers.Clear();
        }
    }
}
