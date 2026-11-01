namespace ProjectU
{
    public sealed class EnemyCombatState : EnemyStateBase
    {
        public EnemyCombatState(EnemyAI enemy) : base(enemy) { }
        public override EnemyState Kind => EnemyState.Combat;
        public override void Enter() => Enemy.Motor.Stop();
        public override void Tick()
        {
            if (!Enemy.HasTarget || Enemy.Combat.IsBusy) return;
            Enemy.Motor.Face(Enemy.Target.transform.position);
            if (!Enemy.Combat.IsInPosition(Enemy.Target)) Enemy.ChangeState(EnemyState.Chase);
            else Enemy.Combat.TryStartAvailable(Enemy.Target);
        }
    }
}
