using UnityEngine;
using UnityEngine.AI;

namespace ProjectU
{
    // Copy shared authoring data once, before the enemy AI and actions wake up.
    [DefaultExecutionOrder(-150), DisallowMultipleComponent]
    [RequireComponent(typeof(Mortal), typeof(NavMeshAgent))]
    public sealed class EnemyStat : MonoBehaviour
    {
        [SerializeField, Tooltip("Base stats copied on first activation. Current HP belongs to Mortal.")]
        private EnemyStatData data;

        public EnemyStatData Data => data;
        public bool IsInitialized { get; private set; }
        public int MaxHealth { get; private set; }
        public float MoveSpeed { get; private set; }
        public float AttackMultiplier { get; private set; }
        public float AttackSpeed { get; private set; }

        private void Awake()
        {
            if (data == null)
            {
                Debug.LogError("EnemyStat requires an EnemyStatData asset.", this);
                return;
            }

            MaxHealth = data.MaxHealth;
            MoveSpeed = data.MoveSpeed;
            AttackMultiplier = data.AttackMultiplier;
            AttackSpeed = data.AttackSpeed;
            if (!GetComponent<Mortal>().TryInitializeHealth(MaxHealth))
            {
                Debug.LogError("EnemyStat must initialize before the first damage or death.", this);
                return;
            }

            GetComponent<NavMeshAgent>().speed = MoveSpeed;
            IsInitialized = true;
        }
    }
}
