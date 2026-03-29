using UnityEngine;

/// <summary>
/// Engine exhaust smoke. Purely visual — runs on ALL clients for every car.
/// No smoke when stopped. Emission scales with speed.
/// Uses Local simulation space so smoke is always visible near the exhaust
/// regardless of how fast the car moves.
///
/// Add to the Car prefab root.
/// Assign any smoke/steam prefab from the Unity Particle Pack to SmokePrefab.
/// </summary>
[RequireComponent(typeof(CarController))]
[RequireComponent(typeof(HealthController))]
public class CarEngineSmoke : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("Drag any smoke or steam prefab from the Unity Particle Pack here.")]
    [SerializeField] private GameObject smokePrefab;

    [Header("Exhaust Position")]
    [Tooltip("Where on the car the exhaust sits. Rear-center is typical. " +
             "Use the orange gizmo in the Scene view to position it.")]
    [SerializeField] private Vector3 exhaustLocalPosition = new Vector3(0f, 0.4f, -1.8f);

    [Header("Emission")]
    [Tooltip("Car must be moving faster than this (km/h) for smoke to appear.")]
    [SerializeField] private float minSpeedKmh = 8f;

    [Tooltip("Particles/sec at minimum speed.")]
    [SerializeField] private float minEmissionRate = 10f;

    [Tooltip("Particles/sec at full speed.")]
    [SerializeField] private float maxEmissionRate = 50f;

    // ── Internals ────────────────────────────────────────────

    private CarController    _car;
    private HealthController _health;
    private ParticleSystem   _smoke;
    private bool             _isDead;

    // ── Lifecycle ────────────────────────────────────────────

    private void Awake()
    {
        _car    = GetComponent<CarController>();
        _health = GetComponent<HealthController>();
    }

    private void Start()
    {
        if (smokePrefab == null)
        {
            Debug.LogWarning("[CarEngineSmoke] No smoke prefab assigned.");
            return;
        }

        var anchor = new GameObject("ExhaustAnchor");
        anchor.transform.SetParent(transform);
        anchor.transform.localPosition = exhaustLocalPosition;
        anchor.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var instance = Instantiate(smokePrefab, anchor.transform.position,
                                   anchor.transform.rotation, anchor.transform);

        _smoke = instance.GetComponent<ParticleSystem>()
              ?? instance.GetComponentInChildren<ParticleSystem>();

        if (_smoke != null)
        {
            // Do NOT override simulation space — use whatever the prefab is configured with.
            // Adjust the particle prefab directly in Unity to get the look you want.
            var emission = _smoke.emission;
            emission.rateOverTime = 0f;
        }

        _health.OnDeath   += OnDeath;
        _health.OnRespawn += OnRespawn;
    }

    private void OnDestroy()
    {
        if (_health == null) return;
        _health.OnDeath   -= OnDeath;
        _health.OnRespawn -= OnRespawn;
    }

    // ── Per-frame ─────────────────────────────────────────────

    private void Update()
    {
        if (_smoke == null) return;

        var emission = _smoke.emission;

        if (_isDead)
        {
            emission.rateOverTime = 0f;
            return;
        }

        float speed = Mathf.Abs(_car.SyncedSpeedKmh);

        // No smoke when stopped or barely moving
        if (speed < minSpeedKmh)
        {
            emission.rateOverTime = 0f;
            return;
        }

        float t = Mathf.Clamp01((speed - minSpeedKmh) /
                                 (_car.Settings.maxForwardSpeed - minSpeedKmh));
        emission.rateOverTime = Mathf.Lerp(minEmissionRate, maxEmissionRate, t);
    }

    // ── Events ───────────────────────────────────────────────

    private void OnDeath(uint _)  => _isDead = true;
    private void OnRespawn()      => _isDead = false;

    // ── Gizmos ───────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Vector3 worldPos   = transform.TransformPoint(exhaustLocalPosition);
        Vector3 exhaustDir = transform.TransformDirection(Vector3.back);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
        Gizmos.DrawSphere(worldPos, 0.08f);
        Gizmos.DrawLine(worldPos, worldPos + exhaustDir * 0.4f);
        Gizmos.DrawSphere(worldPos + exhaustDir * 0.4f, 0.04f);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawLine(transform.position, worldPos);
    }
}
