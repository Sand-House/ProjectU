namespace ProjectU
{
    public sealed class EnemyIdleState : EnemyStateBase
    {
        public EnemyIdleState(EnemyAI enemy) : base(enemy) { }
        public override EnemyState Kind => EnemyState.Idle;
        public override void Enter() => Enemy.Motor.Stop();
        public override void Tick()
        {
            if (Enemy.Sensor.TryDetect(out Combatant player)) Enemy.AcquireTarget(player);
        }
    }
}
