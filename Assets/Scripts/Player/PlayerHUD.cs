using UnityEngine;
using UnityEngine.UI;

namespace ProjectU
{
    [DefaultExecutionOrder(400), DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerStat), typeof(PlayerCombat))]
    public sealed class PlayerHUD : MonoBehaviour
    {
        [SerializeField] private GameManager game;
        [Header("Scene UI")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform safeArea;
        [SerializeField] private GameObject healthWidget;
        [SerializeField] private GameObject ammoWidget;
        [SerializeField] private GameObject crosshair;
        [SerializeField] private Text healthNumber;
        [SerializeField] private Text ammoNumber;
        [SerializeField] private Text prompt;
        [SerializeField] private Image healthFill;
        [Header("Health State Colors")]
        [SerializeField] private Color healthColor = new Color(0.94f, 0.97f, 1f);
        [SerializeField] private Color lowHealthColor = new Color(1f, 0.28f, 0.24f);

        [Header("HUD Text")]
        [Tooltip("{0}: current magazine ammo, {1}: maximum magazine ammo. Supports rich text.")]
        [SerializeField] private string ammoFormat = "{0}<size=28><color=#CBD4DF> / {1}</color></size>";
        [Header("Prompt Text")]
        [SerializeField] private string victoryText = "모든 스테이지 클리어";
        [SerializeField] private string deathText = "플레이어 사망";
        [SerializeField] private string choosingRewardText = "보상을 선택하여 계속하세요";
        [SerializeField] private string spawnFailedText = "적 생성 실패 · 설정 확인 후 재시도";
        [SerializeField] private string beforeStageText = "중앙 장치로 이동하세요";
        [SerializeField] private string afterStageText = "[G] 보상 선택";
        [SerializeField] private string combatText = "적을 처치하여 다음 웨이브로 진행하세요";

        private const string MissingSceneUIMessage = "PlayerHUD requires its scene Canvas and UI references.";

        private Mortal health;
        private PlayerStat stat;
        private PlayerCombat combat;
        private PlayerInteraction interaction;
        private bool viewReady;
        private int displayedHealth = -1;
        private int displayedMaxHealth = -1;
        private int displayedAmmo = -1;
        private int displayedMaxAmmo = -1;
        private string displayedAmmoFormat;
        private Rect displayedSafeArea;
        private Vector2Int displayedScreenSize;

        public Canvas Canvas => canvas;

        private void Awake()
        {
            health = GetComponent<Mortal>();
            stat = GetComponent<PlayerStat>();
            combat = GetComponent<PlayerCombat>();
            interaction = GetComponent<PlayerInteraction>();
            viewReady = canvas != null && safeArea != null && healthWidget != null && ammoWidget != null &&
                crosshair != null && healthNumber != null && ammoNumber != null && prompt != null && healthFill != null;
            if (!viewReady)
            {
                Debug.LogError(MissingSceneUIMessage, this);
                enabled = false;
                return;
            }
            RefreshPresentation();
            canvas.gameObject.SetActive(isActiveAndEnabled);
        }

        private void LateUpdate() => RefreshPresentation();

        private void RefreshPresentation()
        {
            RefreshSafeArea();
            bool hasInteractionPrompt = interaction != null && interaction.CurrentButton != null;
            RefreshPrompt(hasInteractionPrompt);
            bool hasHealth = health != null && stat != null;
            healthWidget.SetActive(hasHealth);
            ammoWidget.SetActive(combat != null);
            if (hasHealth)
            {
                int current = Mathf.Max(0, health.HP);
                int maximum = Mathf.Max(1, stat.MaxHealth);
                if (current != displayedHealth || maximum != displayedMaxHealth)
                {
                    displayedHealth = current;
                    displayedMaxHealth = maximum;
                    healthNumber.text = current.ToString();
                    float ratio = Mathf.Clamp01((float)current / maximum);
                    healthFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
                    Color color = ratio <= 0.3f ? lowHealthColor : healthColor;
                    healthNumber.color = healthFill.color = color;
                }
            }
            if (combat != null)
            {
                int current = combat.CurrentAmmo;
                int maximum = combat.MaxAmmo;
                if (current != displayedAmmo || maximum != displayedMaxAmmo || ammoFormat != displayedAmmoFormat)
                {
                    displayedAmmo = current;
                    displayedMaxAmmo = maximum;
                    displayedAmmoFormat = ammoFormat;
                    // Reserve ammo is unlimited; the denominator is the magazine capacity.
                    ammoNumber.text = string.Format(ammoFormat, current, maximum);
                }
            }
            crosshair.SetActive(hasHealth && !health.isDead && combat != null && combat.isActiveAndEnabled && !hasInteractionPrompt &&
                (game == null || (game.isActiveAndEnabled && game.IsGameplayEnabled)));
        }

        private void RefreshPrompt(bool hasInteractionPrompt)
        {
            bool show = game != null && game.isActiveAndEnabled;
            if (show && hasInteractionPrompt && game.State == StageState.BeforeStage && !game.IsSpawnFailed &&
                (game.Result == GameResult.None || game.Result == GameResult.Stopped)) show = false;
            prompt.transform.parent.gameObject.SetActive(show);
            if (!show) return;
            string message = game.Result == GameResult.Won ? victoryText :
                game.Result == GameResult.Lost ? deathText :
                game.IsChoosingReward ? choosingRewardText :
                game.IsSpawnFailed ? spawnFailedText :
                game.State == StageState.BeforeStage ? beforeStageText :
                game.State == StageState.AfterStage ? afterStageText : combatText;
            if (prompt.text != message) prompt.text = message;
        }

        private void RefreshSafeArea()
        {
            Rect area = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size.x <= 0 || size.y <= 0 || (area == displayedSafeArea && size == displayedScreenSize)) return;
            displayedSafeArea = area;
            displayedScreenSize = size;
            safeArea.anchorMin = new Vector2(area.xMin / size.x, area.yMin / size.y);
            safeArea.anchorMax = new Vector2(area.xMax / size.x, area.yMax / size.y);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }

        private void OnEnable()
        {
            if (!viewReady) return;
            RefreshPresentation();
            canvas.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
        }
    }
}
