using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

/// <summary>
/// Combat HUD. Subscribes to HealthController + WeaponController events.
/// All UI elements are built in the Unity Editor (Canvas children on the Car prefab).
/// Drag the slots in the Inspector — no programmatic layout.
/// </summary>
public class CombatHUD : NetworkBehaviour
{
    [Header("Canvas")]
    [SerializeField] private GameObject hudCanvas;

    [Header("Health Bar")]
    [SerializeField] private Image    healthBarFill;
    [SerializeField] private TMP_Text healthText;

    [Header("Ammo")]
    [SerializeField] private TMP_Text ammoText;

    [Header("Reload Bar")]
    [SerializeField] private GameObject reloadGroup;
    [SerializeField] private Image      reloadBarFill;

    [Header("Damage Flash")]
    [SerializeField] private Image damageFlashImage;

    [Header("Tuning")]
    [SerializeField] private float healthLerpSpeed     = 5f;
    [SerializeField] private Color healthFullColor     = new Color(0.2f, 0.9f, 0.3f, 1f);
    [SerializeField] private Color healthLowColor      = new Color(0.9f, 0.2f, 0.2f, 1f);
    [SerializeField] private float damageFlashDuration = 0.3f;

    [Header("Reload Bar Colors")]
    [SerializeField] private Color reloadReadyColor    = new Color(0.2f, 0.9f, 0.3f, 1f);
    [SerializeField] private Color reloadingColor      = new Color(1.0f, 0.5f, 0.0f, 1f);

    private HealthController _health;
    private WeaponController _weapon;
    private float _displayedHealth = 1f;
    private float _flashAlpha;
    private Color _flashBaseColor;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        _health = GetComponent<HealthController>();
        _weapon = GetComponent<WeaponController>();
        if (damageFlashImage != null) _flashBaseColor = damageFlashImage.color;
        if (hudCanvas != null) hudCanvas.SetActive(false);
    }

    public override void OnStartLocalPlayer()
    {
        if (hudCanvas != null) hudCanvas.SetActive(true);

        // Reload bar is always visible — shows READY state when not reloading
        if (reloadGroup != null) reloadGroup.SetActive(true);
        if (reloadBarFill != null) reloadBarFill.fillAmount = 1f;

        if (damageFlashImage != null) damageFlashImage.gameObject.SetActive(false);

        if (_health != null)
        {
            _health.OnHealthChanged += OnHealthChanged;
            _health.OnRespawn += () => { _displayedHealth = 1f; };
        }
        // No longer need OnReloadStateChanged to show/hide — bar is always on
    }

    public override void OnStartClient()
    {
        if (!isLocalPlayer && hudCanvas != null) hudCanvas.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_health != null) _health.OnHealthChanged -= OnHealthChanged;
    }

    // ── Update ────────────────────────────────────────────────

    private void Update()
    {
        if (!isLocalPlayer) return;

        UpdateHealth();
        UpdateWeapon();
        UpdateDamageFlash();
    }

    private void UpdateHealth()
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
            healthText.text = $"{Mathf.CeilToInt(_health.HealthRatio * 100f)}%";
    }

    private void UpdateWeapon()
    {
        if (_weapon == null) return;

        // Ammo counter
        if (ammoText != null)
        {
            ammoText.text = $"{_weapon.CurrentAmmo} / {_weapon.MaxAmmo}";
            Color c = ammoText.color;
            c.a = _weapon.CurrentAmmo <= 0 ? 0.4f : 1f;
            ammoText.color = c;
        }

        // Reload bar — always visible, full when ready, animates when reloading
        if (reloadBarFill != null)
        {
            reloadBarFill.fillAmount = _weapon.IsReloading ? _weapon.ReloadProgress : 1f;

            // Green = ready, orange = reloading
            reloadBarFill.color = _weapon.IsReloading
                ? Color.Lerp(reloadingColor, reloadReadyColor, _weapon.ReloadProgress)
                : reloadReadyColor;
        }
    }

    private void UpdateDamageFlash()
    {
        if (damageFlashImage == null || _flashAlpha <= 0f) return;

        _flashAlpha -= Time.deltaTime / damageFlashDuration;
        _flashAlpha = Mathf.Max(0f, _flashAlpha);
        Color c = _flashBaseColor; c.a = _flashAlpha;
        damageFlashImage.color = c;
        if (_flashAlpha <= 0.01f) damageFlashImage.gameObject.SetActive(false);
    }

    private void OnHealthChanged(float current, float max, float damage, Vector3 source)
    {
        if (damage > 0f && damageFlashImage != null)
        {
            _flashAlpha = _flashBaseColor.a > 0f ? _flashBaseColor.a : 0.4f;
            damageFlashImage.gameObject.SetActive(true);
        }
    }
}
