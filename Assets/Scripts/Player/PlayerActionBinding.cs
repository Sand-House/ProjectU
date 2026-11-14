using System;
using UnityEngine;

namespace ProjectU
{
    [Serializable]
    public sealed class PlayerActionBinding
    {
        [SerializeField] private PlayerActionSlot slot;
        [SerializeField] private PlayerActionBase action;
        public PlayerActionSlot Slot => slot;
        public PlayerActionBase Action => action;
        public AttackBase Attack => (action as PlayerAttackActionBase)?.Attack;
        public Transform Origin => Attack != null ? Attack.Origin : null;
    }
}
