using System;
using UnityEngine;

namespace ProjectU
{
    internal interface IPlayerStageSubscriber
    {
        void BindStageEvents(GameManager game);
        void UnbindStageEvents(GameManager game);
    }

    // Subscription lifetime only. Each component supplies and owns its own handlers.
    internal sealed class PlayerStageSubscription
    {
        private readonly MonoBehaviour owner;
        private readonly Action<StageState> stageChanged;
        private readonly Action<bool> gameplayChanged;
        private readonly Action<StageState> synchronize;
        private readonly Action released;
        private GameManager game;
        private bool subscribed;

        internal PlayerStageSubscription(MonoBehaviour owner, Action<StageState> stageChanged = null,
            Action<bool> gameplayChanged = null, Action<StageState> synchronize = null, Action released = null)
        {
            this.owner = owner;
            this.stageChanged = stageChanged;
            this.gameplayChanged = gameplayChanged;
            this.synchronize = synchronize;
            this.released = released;
        }

        internal bool IsCurrent(StageState state) => subscribed && owner.isActiveAndEnabled &&
            game != null && game.isActiveAndEnabled && game.State == state;
        internal int StageClearSequence => game != null ? game.StageClearSequence : 0;

        internal void Bind(GameManager manager)
        {
            if (game != manager) { Disable(); game = manager; }
            Enable();
        }

        internal void Unbind(GameManager manager)
        {
            if (game != manager) return;
            Disable();
            game = null;
        }

        internal void Enable()
        {
            if (subscribed || !owner.isActiveAndEnabled || game == null || !game.isActiveAndEnabled) return;
            game.OnStageStateChanged += stageChanged;
            game.OnGameplayEnabledChanged += gameplayChanged;
            subscribed = true;
            synchronize?.Invoke(game.State);
            gameplayChanged?.Invoke(game.IsGameplayEnabled);
        }

        internal void Disable()
        {
            if (!subscribed) return;
            if (game != null)
            {
                game.OnStageStateChanged -= stageChanged;
                game.OnGameplayEnabledChanged -= gameplayChanged;
            }
            subscribed = false;
            released?.Invoke();
        }
    }
}
