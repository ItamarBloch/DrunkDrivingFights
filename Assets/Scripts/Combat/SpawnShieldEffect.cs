using UnityEngine;
using Mirror;

/// <summary>
/// Visual spawn-protection indicator — blinks the car's renderers while invulnerable.
/// Works on ALL clients (HealthController._isInvulnerable is a SyncVar).
///
/// Setup: Add to Car prefab alongside HealthController. No Inspector wiring needed.
///
/// Uses renderer.enabled toggling — works with ANY shader/material regardless of
/// transparency support. Classic arcade-style blink.
/// </summary>
public class SpawnShieldEffect : NetworkBehaviour
{
    [Header("Blink Settings")]
    [Tooltip("Complete on/off cycles per second.")]
    [SerializeField] private float blinkFrequency = 6f;

    [Tooltip("Fraction of each cycle the car is VISIBLE (0.5 = equal on/off, 0.7 = mostly visible).")]
    [Range(0.1f, 0.9f)]
    [SerializeField] private float visibleFraction = 0.65f;

    // ── Internal ────────────────────────────────────────────

    private Renderer[]       _renderers;
    private HealthController _health;
    private bool             _blinking;
    private float            _timer;

    // ── Lifecycle ───────────────────────────────────────────

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        _health    = GetComponent<HealthController>();
    }

    public override void OnStartClient()
    {
        if (_health == null) return;

        _health.OnInvulnerabilityBegan += StartBlink;
        _health.OnInvulnerabilityEnded += StopBlink;
        _health.OnDeath                += _ => StopBlink();

        // Late-join / host: SyncVar already true before we subscribed — start now.
        if (_health.IsInvulnerable)
            StartBlink();
    }

    private void OnDestroy()
    {
        if (_health == null) return;
        _health.OnInvulnerabilityBegan -= StartBlink;
        _health.OnInvulnerabilityEnded -= StopBlink;
        _health.OnDeath                -= _ => StopBlink();
    }

    // ── Blink Control ───────────────────────────────────────

    private void StartBlink()
    {
        _blinking = true;
        _timer    = 0f;
    }

    private void StopBlink()
    {
        _blinking = false;
        SetRenderers(true); // always leave visible
    }

    // ── Update ──────────────────────────────────────────────

    private void Update()
    {
        if (!_blinking) return;

        _timer += Time.deltaTime;
        float cyclePos = (_timer * blinkFrequency) % 1f; // 0..1 within current cycle
        SetRenderers(cyclePos < visibleFraction);
    }

    // ── Helper ──────────────────────────────────────────────

    private void SetRenderers(bool visible)
    {
        foreach (var r in _renderers)
            r.enabled = visible;
    }
}
