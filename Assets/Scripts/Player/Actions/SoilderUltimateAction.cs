using UnityEngine;

namespace ProjectU
{
    public sealed class SoilderUltimateAction : PlayerAction
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Ultimate;

        protected override bool CheckRequirements(in ActionContext request) => true;
        protected override void OnAccepted(in ActionContext request) => Combat?.CancelReload();
        protected override ActionResult ExecuteCore(in ActionContext request)
        {
            Debug.Log("[Player Action] Ultimate (Q): Executed", this);
            return ActionResult.Completed;
        }
    }
}

