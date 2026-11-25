using UnityEngine;

namespace ProjectU
{
    [DefaultExecutionOrder(-60), DisallowMultipleComponent, RequireComponent(typeof(ButtonTrigger))]
    public sealed class GameStarter : MonoBehaviour
    {
        [SerializeField] private GameManager game;
        private ButtonTrigger button;

        private bool CanStart => game != null && game.isActiveAndEnabled && game.IsGameplayEnabled &&
            game.State == StageState.BeforeStage && (game.Result == GameResult.None || game.Result == GameResult.Stopped) &&
            !game.IsRestarting && !game.IsSpawnFailed;

        private void Awake() => button = GetComponent<ButtonTrigger>();

        private void OnEnable() => RefreshButton();

        // Run before the player reads button availability, including after a failed spawn or a disabled manager.
        private void Update() => RefreshButton();

        private void OnDisable() => button.Interactable = false;

        public void StartGame()
        {
            if (!isActiveAndEnabled || !CanStart) return;
            game.TryStartStage();
            RefreshButton();
        }

        private void RefreshButton() => button.Interactable = isActiveAndEnabled && CanStart;
    }
}
