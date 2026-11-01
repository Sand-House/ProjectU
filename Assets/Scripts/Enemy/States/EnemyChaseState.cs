namespace ProjectU
{
    public sealed class EnemyChaseState : EnemyStateBase
    {
        public EnemyChaseState(EnemyAI enemy) : base(enemy) { }
        public override EnemyState Kind => EnemyState.Chase;
        public override void Tick()
        {
            if (!Enemy.HasTarget) return;
            if (Enemy.Combat.IsInPosition(Enemy.Target))
            {
                Enemy.Motor.Stop();
                Enemy.Motor.Face(Enemy.Target.transform.position);
                if (Enemy.Combat.TryStartAvailable(Enemy.Target)) Enemy.ChangeState(EnemyState.Combat);
            }
            else Enemy.Motor.Chase(Enemy.Target.transform.position);
        }
    }
}
