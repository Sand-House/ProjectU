namespace ProjectU
{
    public readonly struct ProjectileLaunchData
    {
        public DamageContext Damage { get; }
        public bool PiercesEnemies { get; }
        public float SizeMultiplier { get; }
        public float ExplosionRadiusMultiplier { get; }
        public bool IsValid => Damage.IsValid;

        public ProjectileLaunchData(in DamageContext damage, bool piercesEnemies, float sizeMultiplier = 1f, float explosionRadiusMultiplier = 1f)
        {
            Damage = damage;
            PiercesEnemies = piercesEnemies;
            SizeMultiplier = AttackDataBase.FiniteAtLeast(sizeMultiplier, 0.01f);
            ExplosionRadiusMultiplier = AttackDataBase.FiniteAtLeast(explosionRadiusMultiplier, 0.01f);
        }
    }
}
