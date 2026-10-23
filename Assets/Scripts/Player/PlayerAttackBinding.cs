using System;
using UnityEngine;

namespace ProjectU
{
    public enum AttackSlot { PrimaryFire, SecondaryFire, Skill1, Skill2, Ultimate }

    // Prefab-local wiring. Shared attack settings remain in AttackData.
    [Serializable]
    public sealed class PlayerAttackBinding
    {
        [SerializeField] private AttackSlot slot;
        [SerializeField] private Attack attack;
        [SerializeField] private Transform origin;

        public AttackSlot Slot => slot;
        public Attack Attack => attack;
        public Transform Origin => origin;
    }
}
