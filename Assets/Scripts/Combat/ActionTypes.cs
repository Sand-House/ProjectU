using UnityEngine;

namespace ProjectU
{
    public enum ActionPhase { Ready, Windup, Active, Recovery }
    public enum ActionResult { Failed, Completed, Running }
    public enum ActionCancelReason { Explicit, Released, InputInterrupted, Disabled, Death, TargetLost }

    // One request's execution anchors. Input history and AI selection remain outside the action.
    public readonly struct ActionContext
    {
        public readonly Transform Origin;
        public readonly Vector3 Position, AimPoint, Direction;
        public readonly Combatant Target;
        public readonly bool HasOrigin, HasPosition, HasAimPoint, HasDirection;

        private ActionContext(Transform origin, Vector3 position, Vector3 aimPoint, Vector3 direction,
            bool hasOrigin, bool hasPosition, bool hasAimPoint, bool hasDirection, Combatant target = null)
        {
            Origin = origin; Position = position; AimPoint = aimPoint; Direction = direction; Target = target;
            HasOrigin = hasOrigin; HasPosition = hasPosition; HasAimPoint = hasAimPoint; HasDirection = hasDirection;
        }

        public static ActionContext Aim(Transform origin, Vector3 point)
            => new ActionContext(origin, default, point, default, true, false, true, false);
        public static ActionContext Aim(Vector3 position, Vector3 point)
            => new ActionContext(null, position, point, default, false, true, true, false);
        public static ActionContext Move(Vector3 direction)
            => new ActionContext(null, default, default, direction, false, false, false, true);
        public static ActionContext Targeting(Combatant target)
            => new ActionContext(null, default, default, default, false, false, false, false, target);
    }
}
