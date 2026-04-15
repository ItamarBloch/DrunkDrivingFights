using UnityEngine;

/// <summary>
/// Skid marks on the ground during drifts and hard braking.
/// Uses TrailRenderers — one per wheel — that draw on the ground surface.
/// Also spawns tire smoke puffs at the contact point (optional second prefab).
///
/// Add to the Car prefab root. No wheel wiring needed — auto-detects all WheelColliders.
///
/// MATERIAL: Assign any unlit/transparent material from the Unity Particle Pack.
/// The dust or smoke material from the pack works perfectly on a TrailRenderer.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CarWheelVFX : MonoBehaviour
{
    [Header("Skid Marks (TrailRenderer)")]
    [Tooltip("Any unlit/transparent material from the Unity Particle Pack. " +
             "The dust or smoke material works great here.")]
    [SerializeField] private Material skidMaterial;

    [Tooltip("Width of the skid mark line.")]
    [SerializeField] private float trailWidth = 0.35f;

    [Tooltip("How many seconds the skid mark stays visible before fading out.")]
    [SerializeField] private float trailFadeTime = 6f;

    [Tooltip("Sideways slip above which skid marks appear. Try 0.3–0.6.")]
    [SerializeField] private float sidewaysSlipThreshold = 0.4f;

    [Tooltip("Forward slip below which braking skids appear (wheel lockup).")]
    [SerializeField] private float brakeSlipThreshold = -0.4f;

    [Header("Tire Smoke (optional)")]
    [Tooltip("Optional: drag a smoke/dust prefab for a puff effect during drifts. " +
             "Leave empty to skip.")]
    [SerializeField] private GameObject driftSmokePrefab;

    // ── Internals ────────────────────────────────────────────

    private WheelCollider[]  _wheels;
    private Transform[]      _trailMarkers;
    private TrailRenderer[]  _trails;
    private ParticleSystem[] _smoke;

    // ── Setup ────────────────────────────────────────────────

    private void Start()
    {
        _wheels       = GetComponentsInChildren<WheelCollider>();
        _trailMarkers = new Transform[_wheels.Length];
        _trails       = new TrailRenderer[_wheels.Length];
        _smoke        = new ParticleSystem[_wheels.Length];

        for (int i = 0; i < _wheels.Length; i++)
        {
            // Skid trail marker — parented to car but positioned at contact point each frame
            var markerGo = new GameObject($"SkidMarker_{i}");
            markerGo.transform.SetParent(transform);
            _trailMarkers[i] = markerGo.transform;
            _trails[i]       = BuildTrail(markerGo);

            // Optional tire smoke prefab
            if (driftSmokePrefab != null)
            {
                var instance = Instantiate(driftSmokePrefab,
                                           _wheels[i].transform.position,
                                           Quaternion.identity,
                                           _wheels[i].transform);
                var ps = instance.GetComponent<ParticleSystem>()
                      ?? instance.GetComponentInChildren<ParticleSystem>();
                if (ps != null)
                {
                    var main = ps.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    var em = ps.emission;
                    em.rateOverTime = 0f;
                }
                _smoke[i] = ps;
            }
        }
    }

    // ── Per-frame ─────────────────────────────────────────────

    private void LateUpdate()
    {
        for (int i = 0; i < _wheels.Length; i++)
        {
            bool hasHit = _wheels[i].GetGroundHit(out WheelHit hit);

            if (hasHit)
            {
                // Move the trail marker to the wheel contact point
                _trailMarkers[i].position = hit.point + Vector3.up * 0.02f;

                bool slipping = Mathf.Abs(hit.sidewaysSlip) > sidewaysSlipThreshold
                             || hit.forwardSlip              < brakeSlipThreshold;

                _trails[i].emitting = slipping;

                // Drive optional smoke
                if (_smoke[i] != null)
                {
                    _smoke[i].transform.position = hit.point + Vector3.up * 0.05f;
                    var em = _smoke[i].emission;
                    em.rateOverTime = slipping ? 20f : 0f;
                }
            }
            else
            {
                _trails[i].emitting = false;
                if (_smoke[i] != null)
                {
                    var em = _smoke[i].emission;
                    em.rateOverTime = 0f;
                }
            }
        }
    }

    // ── Trail Construction ────────────────────────────────────

    private TrailRenderer BuildTrail(GameObject go)
    {
        var tr = go.AddComponent<TrailRenderer>();

        tr.startWidth        = trailWidth;
        tr.endWidth          = trailWidth;
        tr.time              = trailFadeTime;
        tr.minVertexDistance = 0.05f;
        tr.autodestruct      = false;
        tr.emitting          = false;
        tr.receiveShadows    = false;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Fade from semi-opaque to transparent over the trail lifetime
        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.black, 0f),
                new GradientColorKey(Color.black, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0f,   1f)
            }
        );
        tr.colorGradient = gradient;

        if (skidMaterial != null)
            tr.material = skidMaterial;
        else
            Debug.LogWarning("[CarWheelVFX] No skid material assigned. " +
                             "Drag a dust/smoke material from the Particle Pack into the Skid Material slot.");

        return tr;
    }
}
