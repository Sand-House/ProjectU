using UnityEngine;

namespace ProjectU
{
    public sealed class PlayerMortal : Mortal, IPlayerStageSubscriber
    {
        private PlayerStageSubscription stageEvents;
        private bool protectionApplied;
        private bool previousInvincibility;
        public bool IsAlive => !isDead;
        private PlayerStageSubscription StageEvents => stageEvents ?? (stageEvents = new PlayerStageSubscription(
            this, HandleStageStateChanged, synchronize: ApplyStageProtection, released: ReleaseStageProtection));

        void IPlayerStageSubscriber.BindStageEvents(GameManager game) => StageEvents.Bind(game);
        void IPlayerStageSubscriber.UnbindStageEvents(GameManager game) => stageEvents?.Unbind(game);
        private void OnEnable() => stageEvents?.Enable();
        private void OnDisable() => stageEvents?.Disable();

        private void ApplyStageProtection(StageState state)
        {
            if (!protectionApplied) { previousInvincibility = IsInvincible; protectionApplied = true; }
            IsInvincible = state != StageState.OnStage;
        }

        private void ReleaseStageProtection()
        {
            if (protectionApplied) IsInvincible = previousInvincibility;
            protectionApplied = false;
        }

        private void HandleStageStateChanged(StageState state)
        {
            if (!StageEvents.IsCurrent(state)) return;
            ApplyStageProtection(state);
            if (state == StageState.AfterStage && IsAlive)
            {
                PlayerStat stat = GetComponent<PlayerStat>();
                if (stat != null) TryRestoreHealth(stat.MaxHealth);
            }
        }
    }
}
