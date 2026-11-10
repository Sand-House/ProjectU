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

        private Mortal health;
        private PlayerStat stat;
        private PlayerCombat combat;
        private bool viewReady;
        private int displayedHealth = -1;
        private int displayedMaxHealth = -1;
        private int displayedAmmo = -1;
        private int displayedMaxAmmo = -1;
        private Rect displayedSafeArea;
        private Vector2Int displayedScreenSize;

        public Canvas Canvas => canvas;

        private void Awake()
        {
            health = GetComponent<Mortal>();
            stat = GetComponent<PlayerStat>();
            combat = GetComponent<PlayerCombat>();
            viewReady = canvas != null && safeArea != null && healthWidget != null && ammoWidget != null &&
                crosshair != null && healthNumber != null && ammoNumber != null && prompt != null && healthFill != null;
            if (!viewReady)
            {
                Debug.LogError("PlayerHUD requires its scene Canvas and UI references.", this);
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
            RefreshPrompt();
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
                if (current != displayedAmmo || maximum != displayedMaxAmmo)
                {
                    displayedAmmo = current;
                    displayedMaxAmmo = maximum;
                    // Reserve ammo is unlimited; the denominator is the magazine capacity.
                    ammoNumber.text = $"{current}<size=28><color=#CBD4DF> / {maximum}</color></size>";
                }
            }
            crosshair.SetActive(hasHealth && !health.isDead && combat != null && combat.isActiveAndEnabled &&
                (game == null || (game.isActiveAndEnabled && game.IsGameplayEnabled)));
        }

        private void RefreshPrompt()
        {
            bool show = game != null && game.isActiveAndEnabled;
            prompt.transform.parent.gameObject.SetActive(show);
            if (!show) return;
            string message = game.Result == GameResult.Won ? "모든 스테이지 클리어" :
                game.Result == GameResult.Lost ? "플레이어 사망" :
                game.IsChoosingReward ? "보상을 선택하여 계속하세요" :
                game.IsSpawnFailed ? "적 생성 실패 · 설정 확인 후 재시도" :
                game.State == StageState.BeforeStage ? "[G] 상호작용 · 스테이지 시작" :
                game.State == StageState.AfterStage ? "[G] 보상 선택" : "적을 처치하여 다음 웨이브로 진행하세요";
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
