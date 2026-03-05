using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

/// <summary>
/// Combat HUD for the local player. Attach to the Car prefab root.
///
/// The Canvas lives INSIDE the Car prefab as a child.
/// Screen Space Overlay renders fullscreen regardless of parent position.
/// This lets you drag UI elements into Inspector slots within the same prefab.
///
/// ─── WHAT TO BUILD IN THE CAR PREFAB ───
///
/// Open Car.prefab in Prefab Edit Mode, then:
///
/// Car (root)
///   ├── ... (existing children)
///   └── CombatHUD_Canvas          ← Right-click Car → UI → Canvas
///         │                          Render Mode: Screen Space - Overlay
///         │                          CanvasScaler: Scale With Screen Size, 1920×1080
///         │
///         ├── HealthBar_BG        ← UI → Image. Dark gray (#262626). Anchor bottom-left.
///         │     │                    Size ~300×25. This is the background "frame".
///         │     │
///         │     ├── HealthBar_Fill ← UI → Image. Green. THIS is what shrinks.
///         │     │                    Image Type = Filled, Fill Method = Horizontal.
///         │     │                    Stretch it to fill the BG (anchor all corners).
///         │     │                    → Drag this into the "Health Bar Fill" slot.
///         │     │
///         │     └── HealthText    ← UI → Text - TextMeshPro. White, centered, size ~14.
///         │                          Stretch to fill BG. Shows "85%".
///         │                          → Drag this into the "Health Text" slot.
///         │
///         ├── AmmoText            ← UI → Text - TextMeshPro. White, size ~28.
///         │                          Anchor bottom-right. Shows "3 / 3".
///         │                          → Drag this into the "Ammo Text" slot.
///         │
///         ├── ReloadBar_Group     ← Create Empty. Anchor bottom-right, below ammo.
///         │     │                    The script hides/shows this whole object.
///         │     │                    → Drag this into the "Reload Group" slot.
///         │     │
///         │     └── ReloadBar_BG  ← UI → Image. Dark gray. Size ~200×12.
///         │           │
///         │           └── ReloadBar_Fill ← UI → Image. Blue. Filled, Horizontal.
///         │                                 Stretch to fill BG.
///         │                                 → Drag into "Reload Bar Fill" slot.
///         │
///         └── DamageFlash         ← UI → Image. Red (#CC0000), alpha = 0.
///                                    Stretch to fill entire canvas (anchor all corners).
///                                    Raycast Target = OFF.
///                                    → Drag into "Damage Flash Image" slot.
///
/// Then on the Car root → CombatHUD component → drag the Canvas object
/// into the "Hud Canvas" slot.
///
/// Save the prefab.
/// </summary>
public class CombatHUD : NetworkBehaviour
{
    // ── UI References (drag from INSIDE the Car prefab) ─────

    [Header("Canvas")]
    [Tooltip("The Canvas child inside this prefab. Disabled for remote players.")]
    [SerializeField] private GameObject hudCanvas;

    [Header("Health Bar")]
    [Tooltip("The INNER image that shrinks/grows (Image Type = Filled, Horizontal).")]
    [SerializeField] private Image healthBarFill;

    [Tooltip("TextMeshPro showing health percentage like '85%'. Optional.")]
    [SerializeField] private TMP_Text healthText;

    [Header("Ammo")]
    [Tooltip("TextMeshPro showing ammo like '3 / 3'.")]
    [SerializeField] private TMP_Text ammoText;

    [Header("Reload Bar")]
    [Tooltip("Parent object that gets hidden/shown. Hide = not reloading.")]
    [SerializeField] private GameObject reloadGroup;

    [Tooltip("The INNER fill image (Filled, Horizontal).")]
    [SerializeField] private Image reloadBarFill;

    [Header("Damage Flash")]
    [Tooltip("Fullscreen image that flashes red when hit. Optional.")]
    [SerializeField] private Image damageFlashImage;

    // ── Tuning ──────────────────────────────────────────────

    [Header("Tuning")]
    [SerializeField] private float healthLerpSpeed = 5f;
    [SerializeField] private Color healthFullColor = new Color(0.2f, 0.9f, 0.3f, 1f);
    [SerializeField] private Color healthLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    [SerializeField] private float damageFlashDuration = 0.3f;

    // ── Internal ────────────────────────────────────────────

    private HealthController _health;
    private WeaponController _weapon;
    private float _displayedHealth = 1f;
    private float _flashAlpha;
    private Color _flashBaseColor;

    // ── Lifecycle ───────────────────────────────────────────

    private void Awake()
    {
        _health = GetComponent<HealthController>();
        _weapon = GetComponent<WeaponController>();

        if (damageFlashImage != null)
            _flashBaseColor = damageFlashImage.color;

        // Start hidden — OnStartLocalPlayer enables for the right player
        if (hudCanvas != null)
            hudCanvas.SetActive(false);
    }

    public override void OnStartLocalPlayer()
    {
        if (hudCanvas != null)
            hudCanvas.SetActive(true);

        if (reloadGroup != null)
            reloadGroup.SetActive(false);

        if (damageFlashImage != null)
            damageFlashImage.gameObject.SetActive(false);

        if (_health != null)
        {
            _health.OnHealthChanged += OnHealthChanged;
            _health.OnRespawn += OnRespawn;
        }

        if (_weapon != null)
        {
            _weapon.OnReloadStateChanged += OnReloadStateChanged;
        }
    }

    public override void OnStartClient()
    {
        if (!isLocalPlayer && hudCanvas != null)
            hudCanvas.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_health != null)
        {
            _health.OnHealthChanged -= OnHealthChanged;
            _health.OnRespawn -= OnRespawn;
        }

        if (_weapon != null)
        {
            _weapon.OnReloadStateChanged -= OnReloadStateChanged;
        }
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        UpdateHealthBar();
        UpdateAmmoText();
        UpdateReloadBar();
        UpdateDamageFlash();
    }

    // ── Event Handlers ──────────────────────────────────────

    private void OnHealthChanged(float current, float max, float damage, Vector3 source)
    {
        if (damage > 0f && damageFlashImage != null)
        {
            _flashAlpha = _flashBaseColor.a > 0f ? _flashBaseColor.a : 0.4f;
            damageFlashImage.gameObject.SetActive(true);
        }
    }

    private void OnRespawn()
    {
        _displayedHealth = 1f;
    }

    private void OnReloadStateChanged(bool reloading, float progress)
    {
        if (reloadGroup != null)
            reloadGroup.SetActive(reloading);
    }

    // ── UI Updates ──────────────────────────────────────────

    private void UpdateHealthBar()
    {
        if (_health == null) return;

        float target = _health.HealthRatio;
        _displayedHealth = Mathf.Lerp(_displayedHealth, target, Time.deltaTime * healthLerpSpeed);

        if (healthBarFill != null)
        {
            healthBarFill.fillAmount = _displayedHealth;
            healthBarFill.color = Color.Lerp(healthLowColor, healthFullColor, _displayedHealth);
        }

        if (healthText != null)
        {
            int percent = Mathf.CeilToInt(_health.HealthRatio * 100f);
            healthText.text = $"{percent}%";
        }
    }

    private void UpdateAmmoText()
    {
        if (_weapon == null || ammoText == null) return;

        ammoText.text = $"{_weapon.CurrentAmmo} / {_weapon.MaxAmmo}";

        Color c = ammoText.color;
        c.a = _weapon.CurrentAmmo <= 0 ? 0.4f : 1f;
        ammoText.color = c;
    }

    private void UpdateReloadBar()
    {
        if (_weapon == null || reloadBarFill == null) return;

        if (_weapon.IsReloading)
            reloadBarFill.fillAmount = _weapon.ReloadProgress;
    }

    private void UpdateDamageFlash()
    {
        if (damageFlashImage == null) return;

        if (_flashAlpha > 0f)
        {
            _flashAlpha -= Time.deltaTime / damageFlashDuration;
            _flashAlpha = Mathf.Max(0f, _flashAlpha);

            Color c = _flashBaseColor;
            c.a = _flashAlpha;
            damageFlashImage.color = c;

            if (_flashAlpha <= 0.01f)
                damageFlashImage.gameObject.SetActive(false);
        }
    }
}
