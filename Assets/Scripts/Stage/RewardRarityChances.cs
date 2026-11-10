using System;
using UnityEngine;

namespace ProjectU
{
    public enum RewardRarity { Common, Rare, Epic, Unique }

    [Serializable]
    public sealed class RewardRarityChances
    {
        [Range(0, 100), SerializeField] private int common = 50;
        [Range(0, 100), SerializeField] private int rare = 30;
        [Range(0, 100), SerializeField] private int epic = 15;
        [Range(0, 100), SerializeField] private int unique = 5;

        public int Common => common;
        public int Rare => rare;
        public int Epic => epic;
        public int Unique => unique;
        public int Total => common + rare + epic + unique;

        // Also guard data edited outside the regular Inspector (Debug mode or YAML).
        public void Validate()
        {
            common = Mathf.Clamp(common, 0, 100);
            rare = Mathf.Clamp(rare, 0, 100 - common);
            epic = Mathf.Clamp(epic, 0, 100 - common - rare);
            unique = Mathf.Clamp(unique, 0, 100 - common - rare - epic);
            if (Total != 0) return;
            common = 50; rare = 30; epic = 15; unique = 5;
        }

        public RewardRarity Roll()
        {
            Validate();
            int roll = UnityEngine.Random.Range(0, Total);
            if (roll < common) return RewardRarity.Common;
            if (roll < common + rare) return RewardRarity.Rare;
            if (roll < common + rare + epic) return RewardRarity.Epic;
            return RewardRarity.Unique;
        }
    }
}
