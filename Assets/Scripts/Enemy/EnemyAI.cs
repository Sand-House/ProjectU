using System;
using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(80), DisallowMultipleComponent]
    [RequireComponent(typeof(Combatant), typeof(EnemySensor), typeof(EnemyMotor))]
    [RequireComponent(typeof(EnemyCombat))]
    public sealed class EnemyAI : MonoBehaviour
    {
        private Combatant owner;
        private EnemyStateBase[] states;
        private EnemyStateBase current;
        public EnemySensor Sensor { get; private set; }
        public EnemyMotor Motor { get; private set; }
        public EnemyCombat Combat { get; private set; }
        public Combatant Target { get; private set; }
        public EnemyState State => current != null ? current.Kind : EnemyState.Idle;
        public bool HasTarget => Target != null && Target.Faction == Faction.Player && owner.CanTarget(Target);
        public event Action<EnemyState> OnStateChanged;

        private void Awake()
        {
            owner = GetComponent<Combatant>();
            Sensor = GetComponent<EnemySensor>(); Motor = GetComponent<EnemyMotor>(); Combat = GetComponent<EnemyCombat>();
            states = new EnemyStateBase[] { new EnemyIdleState(this), new EnemyChaseState(this), new EnemyCombatState(this), new EnemyDeadState(this) };
            // Death must also dispose an enemy whose AI component was temporarily disabled.
            owner.Mortal.OnDead += HandleDeath;
        }
        private void OnEnable()
        {
            owner.Mortal.OnDamaged += HandleDamaged;
            if (owner.Mortal.isDead) { HandleDeath(null); return; }
            Target = null;
            ChangeState(EnemyState.Idle);
        }
        private void Update()
        {
            if (State == EnemyState.Dead || Time.timeScale <= 0f || Time.deltaTime <= 0f) return;
            if (State != EnemyState.Idle && !HasTarget)
            {
                Target = null;
                Combat.CancelAll(ActionCancelReason.TargetLost);
                ChangeState(EnemyState.Idle);
            }
            current?.Tick();
        }
        public bool AcquireTarget(Combatant candidate)
        {
            if (!isActiveAndEnabled || State == EnemyState.Dead || owner.Mortal.HP <= 0 || candidate == null ||
                candidate.Faction != Faction.Player || !owner.CanTarget(candidate)) return false;
            Target = candidate;
            if (State == EnemyState.Idle) ChangeState(EnemyState.Chase);
            return true;
        }
        internal void ChangeState(EnemyState next)
        {
            if (current != null && (current.Kind == next || current.Kind == EnemyState.Dead)) return;
            current?.Exit();
            current = states[(int)next];
            current.Enter();
            OnStateChanged?.Invoke(next);
        }
        private void HandleDamaged(int amount, GameObject attacker)
        {
            if (owner.Mortal.HP <= 0 || attacker == null) return;
            AcquireTarget(attacker.GetComponentInParent<Combatant>());
        }
        private void HandleDeath(GameObject attacker)
        {
            try { ChangeState(EnemyState.Dead); }
            finally { Destroy(gameObject); }
        }
        private void OnDisable()
        {
            if (owner != null) owner.Mortal.OnDamaged -= HandleDamaged;
            if (Combat != null) Combat.CancelAll(ActionCancelReason.Disabled);
            if (Motor != null) Motor.Stop();
        }
        private void OnDestroy()
        {
            if (owner != null) owner.Mortal.OnDead -= HandleDeath;
        }
    }
}
