using System;
using UnityEngine;

namespace ProjectU
{
    [Serializable]
    public sealed class PlayerActionBinding
    {
        [SerializeField] private PlayerActionSlot slot;
        [SerializeField] private PlayerAction action;
        public PlayerActionSlot Slot => slot;
        public PlayerAction Action => action;
        public Attack Attack => (action as PlayerAttackAction)?.Attack;
        public Transform Origin => Attack != null ? Attack.Origin : null;
    }
}
