using UnityEngine;
using UnityEngine.UI;
using Mirror;

/// <summary>
/// Combat HUD for the local player. Attach to the Car prefab root.
///
/// YOU build the Canvas and UI elements in the Unity editor.
/// This script just reads from HealthController / WeaponController and
/// updates the UI elements you drag into the Inspector slots.
///
/// Only activates for isLocalPlayer — the Canvas is disabled for remote players.
///
/// ─── HOW TO SET UP THE UI ───
///
/// 1. Create a Canvas in the scene (GameObject → UI → Canvas)
///    - Render Mode: Screen Space - Overlay
///    - Add a CanvasScaler: Scale With Screen Size, 1920×1080, Match 0.5
///    - Name it "CombatHUD_Canvas"
///
/// 2. Inside the Canvas, create your UI elements:
///
///    HEALTH BAR (bottom-left):
///    - Create an Image → name it "HealthBar_BG" → color dark gray, anchor bottom-left
///    - Inside it, create another Image → name it "HealthBar_Fill"
///      → Image Type = Filled, Fill Method = Horizontal, color green
///    - Inside HealthBar_BG, create a Text (or TextMeshPro) → name it "HealthText"
///      → anchored to stretch across the bar, centered, white text
///
///    AMMO TEXT (bottom-right):
///    - Create a Text → name it "AmmoText" → anchor bottom-right, font size ~28
///
///    RELOAD BAR (bottom-right, below ammo):
///    - Create an Image → name it "ReloadBar_BG" → anchor bottom-right
///    - Inside it, create Image → "ReloadBar_Fill"
///      → Image Type = Filled, Fill Method = Horizontal, blue color
///    - Optionally a Text inside → "RELOADING"
///    - The whole ReloadBar_BG will be hidden/shown by this script
///
///    CROSSHAIR (center):
///    - Create a small Image or use 4 thin Images for a cross shape
///    - Or just one small dot/cross sprite → anchor to center
///    - Assign to crosshairGroup
///
///    DAMAGE FLASH (fullscreen):
///    - Create a fullscreen Image → stretch to fill entire canvas
///    - Color = red, alpha = 0 (invisible by default)
///    - Raycast Target = OFF
///
/// 3. On the Car prefab, add CombatHUD component and drag all the UI elements
///    into the Inspector slots.
///
/// 4. The Canvas should NOT be a child of the Car — keep it at scene root.
///    Assign it to the "hudCanvas" slot. The script enables/disables it
///    based on isLocalPlayer.
/// </summary>
public class CombatHUD : NetworkBehaviour
{
    // ── UI References (drag from your scene Canvas) ─────────

    [Header("Canvas")]
    [Tooltip("The root Canvas GameObject. Disabled for non-local players.")]
    [SerializeField] private GameObject hudCanvas;

    [Header("Health Bar")]
    [Tooltip("The fill Image (Image Type = Filled, Horizontal).")]
    [SerializeField] private Image healthBarFill;

    [Tooltip("Text showing '85 / 100'. Optional — leave empty if you don't want numbers.")]
    [SerializeField] private Text healthText;

    [Header("Ammo")]
    [Tooltip("Text showing '3 / 3'.")]
    [SerializeField] private Text ammoText;

    [Header("Reload Bar")]
    [Tooltip("Parent object of the reload bar. Hidden when not reloading.")]
    [SerializeField] private GameObject reloadGroup;

    [Tooltip("The fill Image for reload progress (Filled, Horizontal).")]
    [SerializeField] private Image reloadBarFill;

    [Header("Crosshair")]
    [Tooltip("Parent object containing your crosshair elements. Optional.")]
    [SerializeField] private GameObject crosshairGroup;

    [Header("Damage Flash")]
    [Tooltip("Fullscreen Image that flashes red when hit. Optional.")]
    [SerializeField] private Image damageFlashImage;

    // ── Tuning ──────────────────────────────────────────────

    [Header("Tuning")]
    [Tooltip("How fast the health bar lerps to the target value.")]
    [SerializeField] private float healthLerpSpeed = 5f;

    [Tooltip("Health bar color at full health.")]
    [SerializeField] private Color healthFullColor = new Color(0.2f, 0.9f, 0.3f, 1f);

    [Tooltip("Health bar color at low health.")]
    [SerializeField] private Color healthLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);

    [Tooltip("How long the damage flash takes to fade out.")]
    [SerializeField] private float damageFlashDuration = 0.3f;

    // ── Cached ──────────────────────────────────────────────

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
    }

    public override void OnStartLocalPlayer()
    {
        // Show HUD for local player
        if (hudCanvas != null)
            hudCanvas.SetActive(true);

        if (reloadGroup != null)
            reloadGroup.SetActive(false);

        // Subscribe to events
        if (_health != null)
        {
            _health.OnHealthChanged += OnHealthChanged;
            _health.OnRespawn += () => _displayedHealth = 1f;
        }

        if (_weapon != null)
        {
            _weapon.OnReloadStateChanged += OnReloadStateChanged;
        }
    }

    public override void OnStartClient()
    {
        // Hide HUD for non-local players
        if (!isLocalPlayer && hudCanvas != null)
            hudCanvas.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_health != null)
        {
            _health.OnHealthChanged -= OnHealthChanged;
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
        // Took damage → trigger flash
        if (damage > 0f && damageFlashImage != null)
        {
            _flashAlpha = _flashBaseColor.a > 0f ? _flashBaseColor.a : 0.4f;
        }
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
            healthText.text = $"{Mathf.CeilToInt(_health.CurrentHealth)} / {_health.MaxHealth}";
        }
    }

    private void UpdateAmmoText()
    {
        if (_weapon == null || ammoText == null) return;

        ammoText.text = $"{_weapon.CurrentAmmo} / {_weapon.MaxAmmo}";

        // Dim when empty
        Color c = ammoText.color;
        c.a = _weapon.CurrentAmmo <= 0 ? 0.4f : 1f;
        ammoText.color = c;
    }

    private void UpdateReloadBar()
    {
        if (_weapon == null || reloadBarFill == null) return;

        if (_weapon.IsReloading)
        {
            reloadBarFill.fillAmount = _weapon.ReloadProgress;
        }
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
            damageFlashImage.gameObject.SetActive(_flashAlpha > 0.01f);
        }
        else
        {
            damageFlashImage.gameObject.SetActive(false);
        }
    }
}
