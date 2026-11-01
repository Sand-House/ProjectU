namespace ProjectU
{
    public enum EnemyState { Idle, Chase, Combat, Dead }

    public abstract class EnemyStateBase
    {
        protected EnemyAI Enemy { get; }
        protected EnemyStateBase(EnemyAI enemy) => Enemy = enemy;
        public abstract EnemyState Kind { get; }
        public virtual void Enter() { }
        public abstract void Tick();
        public virtual void Exit() { }
    }
}
