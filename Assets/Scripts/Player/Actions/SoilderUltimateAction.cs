using UnityEngine;

namespace ProjectU
{
    public sealed class SoilderUltimateAction : PlayerAction
    {
        public override PlayerActionSlot Slot => PlayerActionSlot.Ultimate;

        protected override bool CheckRequirements(in PlayerActionContext request) => true;
        protected override void OnAccepted(in PlayerActionContext request) => Combat?.CancelReload();
        protected override PlayerActionResult ExecuteCore(in PlayerActionContext request)
        {
            Debug.Log("[Player Action] Ultimate (Q): Executed", this);
            return PlayerActionResult.Completed;
        }
    }
}

