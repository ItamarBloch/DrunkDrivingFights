using UnityEngine;
using Mirror;

/// <summary>
/// Per-car audio — engine hum only.
/// Runs on ALL clients (local + remote).
///
/// Uses SoundManager.Instance for the engine clip (key: "engine_loop").
/// No separate SoundRegistry field needed — assign everything on SoundManager only.
///
/// Engine pitch and volume are driven each frame by CarController.SyncedSpeedKmh.
/// </summary>
[RequireComponent(typeof(CarController))]
[RequireComponent(typeof(HealthController))]
public class CarAudioController : NetworkBehaviour
{
    [Header("Engine Tuning")]
    [Tooltip("Car speed (km/h) that maps to maximum pitch/volume.")]
    [SerializeField] private float _maxSpeedKmh = 120f;
    [SerializeField] private float _engineIdlePitch   = 0.6f;
    [SerializeField] private float _engineMaxPitch    = 1.4f;
    [SerializeField] private float _engineIdleVolume  = 0.3f;
    [SerializeField] private float _engineMaxVolume   = 0.7f;
    [Tooltip("How quickly pitch and volume respond to speed changes.")]
    [SerializeField] private float _engineSmoothing   = 5f;
    [SerializeField] private float _engineMinDistance = 3f;
    [SerializeField] private float _engineMaxDistance = 40f;

    private CarController   _car;
    private HealthController _health;
    private AudioSource     _engineSource;

    // ── Lifecycle ────────────────────────────────────────────

    private void Awake()
    {
        _car    = GetComponent<CarController>();
        _health = GetComponent<HealthController>();
    }

    public override void OnStartClient()
    {
        _engineSource = CreateEngineSource();

        _health.OnDeath   += OnDeath;
        _health.OnRespawn += OnRespawn;
    }

    private void OnDestroy()
    {
        if (_health == null) return;
        _health.OnDeath   -= OnDeath;
        _health.OnRespawn -= OnRespawn;
    }

    // ── Per-frame engine update ───────────────────────────────

    private void Update()
    {
        if (_engineSource == null) return;

        float speedRatio  = Mathf.Clamp01(Mathf.Abs(_car.SyncedSpeedKmh) / _maxSpeedKmh);
        float targetPitch  = Mathf.Lerp(_engineIdlePitch,  _engineMaxPitch,   speedRatio);
        float targetVolume = Mathf.Lerp(_engineIdleVolume, _engineMaxVolume,  speedRatio);
        float t = Time.deltaTime * _engineSmoothing;

        _engineSource.pitch  = Mathf.Lerp(_engineSource.pitch,  targetPitch,  t);
        _engineSource.volume = Mathf.Lerp(_engineSource.volume, targetVolume, t);
    }

    // ── Event handlers ───────────────────────────────────────

    private void OnDeath(uint killerNetId)
    {
        if (_engineSource != null) _engineSource.volume = 0f;
    }

    private void OnRespawn()
    {
        if (_engineSource != null) _engineSource.volume = _engineIdleVolume;
    }

    // ── Engine source setup ──────────────────────────────────

    private AudioSource CreateEngineSource()
    {
        // Uses SoundManager — no separate registry reference needed on this component.
        // If SoundManager isn't in the scene or "engine_loop" has no clip, returns null silently.
        var src = SoundManager.Instance?.PlayAttached("engine_loop", transform, loop: true);
        if (src == null) return null;

        // Override spatial settings with the tuning fields above
        src.spatialBlend  = 1f;
        src.minDistance   = _engineMinDistance;
        src.maxDistance   = _engineMaxDistance;
        src.rolloffMode   = AudioRolloffMode.Linear;
        src.volume        = _engineIdleVolume;
        src.pitch         = _engineIdlePitch;
        return src;
    }
}
