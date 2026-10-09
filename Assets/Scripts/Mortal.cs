using System;
using UnityEngine;

namespace ProjectU
{
    [DisallowMultipleComponent]
    public class Mortal : MonoBehaviour
    {
        [SerializeField, Min(1), Tooltip("Starting HP. Damage cannot reduce HP below zero.")]
        private int hp = 100;
        [SerializeField]
        private bool isInvincible;

        public int HP => hp;
        public bool isDead { get; private set; }
        public bool IsInvincible
        {
            get => isInvincible;
            set => isInvincible = value;
        }

        // Arguments are actual HP lost and the attacker (null for environmental damage).
        public event Action<int, GameObject> OnDamaged;
        public event Action<GameObject> OnDead;

        public virtual void TakeDamage(int damage, GameObject attacker)
        {
            if (isDead || IsInvincible || damage <= 0 || hp <= 0)
                return;

            int appliedDamage = Mathf.Min(hp, damage);
            hp -= appliedDamage;

            try
            {
                OnDamaged?.Invoke(appliedDamage, attacker);
            }
            finally
            {
                // A failing damage listener must not leave a unit alive at zero HP.
                if (hp == 0)
                    Dead(attacker);
            }
        }

        public virtual void Dead(GameObject attacker = null)
        {
            if (isDead)
                return;

            hp = 0;
            isDead = true;
            OnDead?.Invoke(attacker);
        }

        protected virtual void OnValidate()
        {
            if (!Application.isPlaying)
                hp = Mathf.Max(1, hp);
        }
    }
}
