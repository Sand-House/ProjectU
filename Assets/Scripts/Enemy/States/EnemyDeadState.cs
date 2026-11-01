using UnityEngine;

namespace ProjectU
{
    public sealed class EnemyDeadState : EnemyStateBase
    {
        public EnemyDeadState(EnemyAI enemy) : base(enemy) { }
        public override EnemyState Kind => EnemyState.Dead;
        public override void Enter()
        {
            Enemy.Combat.CancelAll(ActionCancelReason.Death);
            Enemy.Motor.Stop();
            foreach (Collider collider in Enemy.GetComponentsInChildren<Collider>()) collider.enabled = false;
        }
        public override void Tick() { }
    }
}
