using UnityEngine;
using UnityEngine.UI;

namespace ProjectU
{
    // Attach to a dedicated Text directly under a screen-space Canvas; never creates runtime UI.
    [DefaultExecutionOrder(410), DisallowMultipleComponent, RequireComponent(typeof(Text))]
    public sealed class InteractionPrompt : MonoBehaviour
    {
        [SerializeField] private PlayerInteraction player;
        [SerializeField, Tooltip("{0}: button prompt text.")]
        private string pressFormat = "[G] {0}";
        [SerializeField, Tooltip("{0}: button prompt text.")]
        private string holdFormat = "[G] 길게 누르기 · {0}";
        [SerializeField, Tooltip("{0}: button prompt text, {1}: completed percentage from 0 to 100.")]
        private string progressFormat = "[G] {0} · {1:0}%";
        private Text label;

        private void Awake()
        {
            label = GetComponent<Text>();
            label.raycastTarget = false;
            label.enabled = false;
        }

        private void LateUpdate()
        {
            ButtonTrigger button = player != null ? player.CurrentButton : null;
            label.enabled = button != null;
            if (button == null) return;
            label.text = button.InputMode == ButtonTriggerInputMode.Press ? string.Format(pressFormat, button.PromptText) :
                player.IsHolding || player.HoldProgress >= 1f ? string.Format(progressFormat, button.PromptText, player.HoldProgress * 100f) :
                string.Format(holdFormat, button.PromptText);
        }

        private void OnDisable() { if (label != null) label.enabled = false; }

        private void Reset()
        {
            Text text = GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(600f, 80f);
        }
    }
}
