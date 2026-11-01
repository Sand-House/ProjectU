using UnityEngine;

namespace ProjectU.Editor
{
    internal static class EnemyValidationIsolation
    {
        // Existing player fixtures control HP and input themselves; demo AI is tested by EnemyValidation.
        public static void DisableDemoEnemies()
        {
            foreach (EnemyAI enemy in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                enemy.gameObject.SetActive(false);
        }
    }
}
