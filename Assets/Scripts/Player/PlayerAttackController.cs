using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    [RequireComponent(typeof(PlayerInputReader))]
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
        private readonly RaycastHit[] aimHits = new RaycastHit[32];
        private readonly Dictionary<Attack, Action> executionHandlers = new Dictionary<Attack, Action>();

        public event Action<AttackSlot> AttackExecuted;
        public IReadOnlyList<PlayerAttackBinding> AttackBindings => attackBindings;

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
                Action handler = () => AttackExecuted?.Invoke(binding.Slot);
                executionHandlers.Add(binding.Attack, handler);
                binding.Attack.OnExecuted += handler;
            }
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
            if (motor != null && !motor.CanPerformActions)
            {
                foreach (PlayerAttackBinding binding in attackBindings)
                    if (binding.Attack != null) binding.Attack.CancelAttack();
                return;
            }
            if (Time.deltaTime <= 0f) return;
            Vector3 point = GetAimPoint();
            foreach (PlayerAttackBinding binding in attackBindings)
            {
                Attack attack = binding.Attack;
                if (attack == null || attack.Data == null) continue;
                Prepare(attack, point);
                ButtonInputState state = ReadInput(binding.Slot);
                if (state.Pressed || (attack.Data.RepeatWhileHeld && state.Held))
                    attack.TryAttack(binding.Origin, point);
            }
        }

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
