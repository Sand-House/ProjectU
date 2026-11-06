using UnityEngine;

namespace ProjectU.Editor
{
    public static class GameFlowValidationUtility
    {
        // Existing component validations own their scene's timing and spawned fixtures.
        // Change only the unsaved validation scene; component tests own their fixtures.
        public static void DisableAutomaticGameFlow()
        {
            foreach (StageFlowUI ui in Object.FindObjectsByType<StageFlowUI>(FindObjectsSortMode.None))
                ui.enabled = false;
            foreach (GameManager game in Object.FindObjectsByType<GameManager>(FindObjectsSortMode.None))
                game.enabled = false;
        }
    }
}
