using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectU
{
    [Serializable]
    public sealed class RewardCardView
    {
        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private Text rarityLabel;

        public Button Button => button;
        public RewardRarity Rarity { get; private set; }
        public bool IsAssigned => button != null && background != null && rarityLabel != null;

        public void Show(RewardRarity rarity, Color color)
        {
            Rarity = rarity;
            background.color = color;
            rarityLabel.text = rarity.ToString();
            Color textColor = rarity == RewardRarity.Unique ? new Color(0.08f, 0.1f, 0.14f, 1f) : Color.white;
            foreach (Text label in button.GetComponentsInChildren<Text>(true)) label.color = textColor;
            button.interactable = true;
        }
    }
}
