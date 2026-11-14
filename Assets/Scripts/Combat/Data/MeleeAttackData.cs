using UnityEngine;

namespace ProjectU
{
    public enum MeleeShape { Circle, Sector }

    [CreateAssetMenu(menuName = "ProjectU/Combat/Melee Attack")]
    public sealed class MeleeAttackData : AttackDataBase
    {
        [SerializeField, Min(0.01f)] private float range = 2f;
        [SerializeField] private MeleeShape shape = MeleeShape.Sector;
        [SerializeField, Range(0f, 360f)] private float sectorAngle = 90f;
        [SerializeField, Min(0.01f)] private float height = 2f;
        [SerializeField] private Vector3 originOffset = new Vector3(0f, 1f, 0f);
        [SerializeField, Tooltip("Solid world layers that always block melee damage. Units and triggers are excluded.")]
        private LayerMask obstructionMask = ~0;

        public float Range => FiniteAtLeast(range, 0.01f);
        public MeleeShape Shape => shape;
        public float SectorAngle => Mathf.Min(360f, FiniteAtLeast(sectorAngle, 0f));
        public float Height => FiniteAtLeast(height, 0.01f);
        public Vector3 OriginOffset => IsFinite(originOffset) ? originOffset : Vector3.zero;
        public LayerMask ObstructionMask => obstructionMask;

        protected override void OnValidate()
        {
            base.OnValidate();
            range = Range;
            sectorAngle = SectorAngle;
            height = Height;
            originOffset = OriginOffset;
        }
    }
}
