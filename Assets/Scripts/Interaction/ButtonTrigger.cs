using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ProjectU
{
    public enum ButtonTriggerInputMode { Press, Hold }

    // A range and an event. Visual meshes and the event's gameplay effect are optional dependencies.
    [DisallowMultipleComponent]
    public sealed class ButtonTrigger : MonoBehaviour
    {
        [SerializeField, Tooltip("Trigger collider defining the usable volume. A mesh is not required.")]
        private Collider triggerRange;
        [SerializeField] private ButtonTriggerInputMode inputMode;
        [SerializeField, Min(0f), Tooltip("Continuous game seconds to hold Interact. Zero activates on the initial press.")]
        private float holdDuration = 1f;
        [SerializeField] private string promptText = "상호작용";
        [SerializeField] private bool interactable = true;
        [SerializeField] private UnityEvent onTriggered = new UnityEvent();

        private static readonly List<ButtonTrigger> activeButtons = new List<ButtonTrigger>();
        internal uint AvailabilityVersion { get; private set; }

        public Collider TriggerRange => triggerRange;
        public ButtonTriggerInputMode InputMode => inputMode;
        public float HoldDuration => AttackDataBase.FiniteAtLeast(holdDuration, 0f);
        public string PromptText => promptText;
        public UnityEvent OnTriggered => onTriggered;
        public bool Interactable
        {
            get => interactable;
            set { if (interactable == value) return; interactable = value; AvailabilityVersion++; }
        }
        public bool IsAvailable => isActiveAndEnabled && interactable && triggerRange != null &&
            triggerRange.enabled && triggerRange.gameObject.activeInHierarchy && triggerRange.isTrigger;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearRegistry() => activeButtons.Clear();

        private void Awake()
        {
            if (triggerRange == null) triggerRange = GetComponent<Collider>();
            if (triggerRange == null || !triggerRange.isTrigger)
                Debug.LogError("ButtonTrigger requires a Collider with Is Trigger enabled in Trigger Range.", this);
        }

        private void OnEnable()
        {
            if (!activeButtons.Contains(this)) activeButtons.Add(this);
        }

        private void OnDisable() { AvailabilityVersion++; activeButtons.Remove(this); }

        public bool Contains(Vector3 position) => IsAvailable &&
            (triggerRange.ClosestPoint(position) - position).sqrMagnitude <= 0.000001f;

        internal static ButtonTrigger FindNearest(Vector3 position)
        {
            ButtonTrigger nearest = null;
            float bestDistance = float.PositiveInfinity;
            foreach (ButtonTrigger button in activeButtons)
            {
                if (button == null || !button.Contains(position)) continue;
                float distance = (button.triggerRange.bounds.center - position).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                nearest = button;
            }
            return nearest;
        }

        internal void Activate(PlayerInteraction player)
        {
            if (player != null && player.CanInteract && player.CurrentButton == this && Contains(player.InteractionPosition))
                onTriggered.Invoke();
        }

        private void Reset()
        {
            triggerRange = GetComponent<Collider>();
            if (triggerRange == null)
            {
                var sphere = gameObject.AddComponent<SphereCollider>();
                sphere.radius = 2f;
                triggerRange = sphere;
            }
            triggerRange.isTrigger = true;
        }

        private void OnValidate() => holdDuration = HoldDuration;
    }
}
