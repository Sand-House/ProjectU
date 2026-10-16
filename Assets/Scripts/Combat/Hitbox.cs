using UnityEngine;

namespace ProjectU
{
    [DisallowMultipleComponent]
    public sealed class Hitbox : MonoBehaviour
    {
        [SerializeField] private Combatant combatant;
        [SerializeField] private bool isCritical;

        public Combatant Combatant => combatant != null ? combatant : GetComponentInParent<Combatant>();
        public bool IsCritical => isCritical;

        public static bool TryResolve(Collider collider, out Combatant target, out bool critical)
        {
            target = null;
            critical = false;
            if (collider == null) return false;
            Hitbox hitbox = collider.GetComponentInParent<Hitbox>();
            target = hitbox != null ? hitbox.Combatant : collider.GetComponentInParent<Combatant>();
            critical = hitbox != null && hitbox.IsCritical;
            return target != null;
        }
    }
}
