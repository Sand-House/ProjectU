using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(110)]
    public abstract class EnemyAction : ActionBase
    {
        private EnemyMotor motor;
        private EnemyCombat combat;
        private EnemyAI brain;
        private Combatant target;
        protected EnemyMotor Motor => motor != null ? motor : (motor = GetComponent<EnemyMotor>());
        protected EnemyCombat Combat => combat != null ? combat : (combat = GetComponent<EnemyCombat>());
        protected EnemyAI Brain => brain != null ? brain : (brain = GetComponent<EnemyAI>());
        protected Combatant Target => target;
        protected override bool IsGrounded => Motor != null && Motor.IsGrounded;
        protected override bool CanRequest => (Combat == null || Combat.isActiveAndEnabled) &&
            (Brain == null || (Brain.isActiveAndEnabled && Brain.State != EnemyState.Dead));

        public bool CanStartFor(Combatant candidate) => CanStartWithContext(ActionContext.Targeting(candidate));
        public bool TryStartFor(Combatant candidate) => TryStart(ActionContext.Targeting(candidate));
        public virtual bool IsInPosition(Combatant candidate) => IsValidTarget(candidate);
        protected bool IsValidTarget(Combatant candidate)
            => candidate != null && candidate.Faction == Faction.Player && Owner != null && Owner.CanTarget(candidate);
        protected override bool CheckRequirements(in ActionContext request) => IsValidTarget(request.Target);
        protected override void OnAccepted(in ActionContext request) => target = request.Target;

        protected virtual void LateUpdate()
        {
            if (IsActive && (!CanRequest || !IsValidTarget(target)))
            {
                CancelAndClear(ActionCancelReason.TargetLost);
                return;
            }
            ProcessAction();
        }
    }
}
