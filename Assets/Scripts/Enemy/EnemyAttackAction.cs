using UnityEngine;

namespace ProjectU
{
    // The same adapter accepts melee, hitscan and projectile executors.
    public class EnemyAttackAction : EnemyAction
    {
        [SerializeField] private Attack attack;
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;
        [SerializeField, Min(0.1f), Tooltip("Used by ranged attacks. Melee reads its actual attack volume.")]
        private float attackDistance = 10f;
        [SerializeField, Range(0f, 180f)] private float facingTolerance = 30f;
        [SerializeField] private LayerMask obstructionMask = ~0;
        private Vector3 committedDirection;
        private EnemyStat stat;
        public Attack Attack => attack;
        public override bool IsAttackAction => true;

        protected virtual void Awake()
        {
            stat = GetComponent<EnemyStat>();
            if (stat != null && stat.IsInitialized)
                SpeedMultiplier = stat.AttackSpeed;
        }

        public override bool IsInPosition(Combatant candidate)
        {
            if (!base.IsInPosition(candidate) || attack == null || attack.gameObject != gameObject ||
                !attack.IsConfiguredForExecution || attack.Origin == null) return false;
            if (attack is MeleeAttack melee)
                return melee.CanReach(candidate, attack.Origin.position, transform.forward);
            Vector3 delta = EnemySensor.TargetPoint(candidate) - attack.Origin.position;
            if (delta.sqrMagnitude > attackDistance * attackDistance) return false;
            if (Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(delta, Vector3.up)) > facingTolerance) return false;
            return !WorldObstruction.IsBlocked(attack.Origin.position, EnemySensor.TargetPoint(candidate), obstructionMask);
        }

        protected override bool CheckRequirements(in ActionContext request)
            => base.CheckRequirements(request) && IsInPosition(request.Target);
        // Once committed, a target may dodge. The actual Attack volume decides whether the strike lands.
        protected override bool CheckExecutionRequirements(in ActionContext request)
            => base.CheckRequirements(request) && attack != null && attack.Origin != null && attack.IsConfiguredForExecution;
        protected override void OnAccepted(in ActionContext request)
        {
            base.OnAccepted(request);
            committedDirection = transform.forward;
        }
        protected override ActionResult ExecuteCore(in ActionContext request)
        {
            Vector3 origin = attack.Origin.position;
            Vector3 aim = attack is MeleeAttack ? origin + committedDirection : EnemySensor.TargetPoint(request.Target);
            float multiplier = damageMultiplier * (stat != null && stat.IsInitialized ? stat.AttackMultiplier : 1f);
            return attack.TryExecute(this, origin, aim, multiplier) ? ActionResult.Completed : ActionResult.Failed;
        }
        protected override void OnValidate()
        {
            base.OnValidate();
            damageMultiplier = AttackData.FiniteAtLeast(damageMultiplier, 0f);
            attackDistance = AttackData.FiniteAtLeast(attackDistance, 0.1f);
            facingTolerance = Mathf.Clamp(AttackData.FiniteAtLeast(facingTolerance, 0f), 0f, 180f);
        }
    }
}
