using UnityEngine;

namespace ProjectU
{
    public sealed class SoilderSkill2Action : PlayerAction
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Skill2;

        protected override bool CheckRequirements(in PlayerActionContext request) => true;
        protected override void OnAccepted(in PlayerActionContext request) => Combat?.CancelReload();
        protected override PlayerActionResult ExecuteCore(in PlayerActionContext request)
        {
            Debug.Log("[Player Action] Skill2 (F): Executed", this);
            return PlayerActionResult.Completed;
        }
    }
}

