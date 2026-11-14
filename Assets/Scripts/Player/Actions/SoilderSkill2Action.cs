using UnityEngine;

namespace ProjectU
{
    public sealed class SoilderSkill2Action : PlayerActionBase
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Skill2;

        protected override bool CheckRequirements(in ActionContext request) => true;
        protected override void OnAccepted(in ActionContext request) => Combat?.CancelReload();
        protected override ActionResult ExecuteCore(in ActionContext request)
        {
            Debug.Log("[Player Action] Skill2 (F): Executed", this);
            return ActionResult.Completed;
        }
    }
}

