using UnityEngine;
using Mirror;

/// <summary>
/// Rocket projectile. This is NOT a networked object — it is never spawned with
/// NetworkServer.Spawn and needs no NetworkIdentity / NetworkTransform.
///
/// Visuals and hit-detection are split (see WeaponController):
///   • Every peer simulates its own purely-visual rocket locally (RocketMode.Visual)
///     so there is zero network delay and it collides against the LOCAL world.
///   • The server runs one extra invisible rocket (RocketMode.ServerHit) that does the
///     authoritative damage and nothing else.
///
/// Hit detection is a SWEPT SphereCast every FixedUpdate (not a trigger): a fast, small
/// projectile can't tunnel through a wall, the cast naturally ignores anything it starts
/// overlapping (the shooter's car at the muzzle), and because the rocket has no active
/// physics collider it can never collide with another rocket or shove a car.
///
/// Prefab needs: Rigidbody, a SphereCollider (used only to read the sweep radius — it is
/// disabled at runtime), renderers, Rocket (this).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Rocket : MonoBehaviour
{
    public enum RocketMode { Visual, ServerHit }

    [Header("References")]
    [SerializeField] private CombatSettings combatSettings;
    [SerializeField] private VFXReferences vfxReferences;
    [SerializeField] private Transform trailAnchor;

    // Shared scratch buffer for the sweep (single-threaded, reused every cast).
    private static readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

    private RocketMode _mode;
    private WeaponSettings _weapon;
    private uint _ownerNetId;
    private Rigidbody _rb;
    private float _sweepRadius = 0.3f;
    private bool _done;
    private float _lifetimeTimer;
    private float _currentSpeed;
    private GameObject _trailInstance;
    private AudioSource _whooshSource;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        // No active collider, so no continuous-detection needed (avoids a PhysX warning).
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        // Read the sweep radius from the collider, then disable ALL colliders: the rocket
        // does its own raycast hit-detection and must never physically touch anything
        // (no rocket-vs-rocket, no self-hit, no car knock-up).
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            if (c is SphereCollider sc)
                _sweepRadius = sc.radius * MaxAbs(c.transform.lossyScale);
            c.enabled = false;
        }
    }

    private static float MaxAbs(Vector3 v)
        => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>
    /// Configure and fire. <paramref name="mode"/> decides whether this instance is a
    /// purely-visual rocket (on every client) or the invisible server hit-detector.
    /// </summary>
    public void Launch(RocketMode mode, WeaponSettings weapon, Vector3 aimDirection,
        Vector3 shooterVelocity, uint ownerNetId)
    {
        _mode = mode;
        _weapon = weapon;
        _ownerNetId = ownerNetId;
        _lifetimeTimer = weapon.rocketLifetime;

        Vector3 dir = aimDirection.normalized;
        Vector3 launchVelocity;
        if (weapon.inheritShooterVelocity)
        {
            float fwd = Mathf.Max(0f, Vector3.Dot(shooterVelocity, dir));
            launchVelocity = dir * (fwd + weapon.rocketSpeed);
        }
        else
        {
            launchVelocity = dir * weapon.rocketSpeed;
        }

        if (launchVelocity.magnitude > weapon.rocketMaxSpeed)
            launchVelocity = launchVelocity.normalized * weapon.rocketMaxSpeed;

        _rb.linearVelocity = launchVelocity;
        _currentSpeed = launchVelocity.magnitude;

        if (dir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(dir);

        if (mode == RocketMode.Visual)
            SetupVisuals();
        else
            HideRenderers(); // server hit-detector is invisible
    }

    private void FixedUpdate()
    {
        if (_done || _weapon == null) return;

        if (_weapon.rocketGravityScale > 0f)
            _rb.AddForce(Physics.gravity * _weapon.rocketGravityScale, ForceMode.Acceleration);

        if (_weapon.rocketCruiseSpeed > 0f && _weapon.cruiseTransitionRate > 0f)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _weapon.rocketCruiseSpeed,
                _weapon.cruiseTransitionRate * Time.fixedDeltaTime);
            Vector3 d = _rb.linearVelocity.normalized;
            if (d.sqrMagnitude > 0.01f)
                _rb.linearVelocity = d * _currentSpeed;
        }

        if (_rb.linearVelocity.sqrMagnitude > 0.1f)
            transform.rotation = Quaternion.LookRotation(_rb.linearVelocity.normalized);

        // Sweep the movement we're about to make this step — can't tunnel through walls.
        if (SweepForHit(out Vector3 hitPoint))
        {
            Detonate(hitPoint);
            return;
        }

        _lifetimeTimer -= Time.fixedDeltaTime;
        if (_lifetimeTimer <= 0f) Detonate(transform.position);
    }

    /// <summary>
    /// SphereCast along this step's movement. Returns the nearest hit that isn't the
    /// shooter's own car. Triggers are ignored (so other rockets / kill-zones don't
    /// count), and colliders we already overlap at the origin are skipped by PhysX.
    /// </summary>
    private bool SweepForHit(out Vector3 hitPoint)
    {
        hitPoint = default;

        Vector3 vel = _rb.linearVelocity;
        float speed = vel.magnitude;
        if (speed < 0.001f) return false;

        Vector3 dir = vel / speed;
        float dist = speed * Time.fixedDeltaTime;

        int count = Physics.SphereCastNonAlloc(transform.position, _sweepRadius, dir,
            _hitBuffer, dist, ~0, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = _hitBuffer[i];
            if (h.collider == null) continue;

            // Never detonate on the car that fired us.
            var nid = h.collider.GetComponentInParent<NetworkIdentity>();
            if (nid != null && nid.netId == _ownerNetId) continue;

            if (h.distance < nearest)
            {
                nearest = h.distance;
                hitPoint = h.point;
                found = true;
            }
        }
        return found;
    }

    private void Detonate(Vector3 pos)
    {
        if (_done) return;
        _done = true;

        if (_mode == RocketMode.ServerHit)
        {
            // Authoritative damage only — no visuals, no sound.
            if (_weapon != null && combatSettings != null)
                ExplosionHelper.ProcessExplosion(pos, _weapon, combatSettings, _ownerNetId);
            Destroy(gameObject);
            return;
        }

        // Visual: play explosion VFX + sound locally, drop the trail/whoosh, then vanish.
        if (_trailInstance != null) { Destroy(_trailInstance); _trailInstance = null; }
        if (_whooshSource != null)
        {
            _whooshSource.Stop();
            Destroy(_whooshSource.gameObject);
            _whooshSource = null;
        }

        string vfxKey = _weapon != null ? _weapon.explosionVFXKey : "";
        if (vfxReferences != null && !string.IsNullOrEmpty(vfxKey))
            vfxReferences.SpawnEffect(vfxKey, pos, Quaternion.identity);
        SoundManager.Instance?.PlayAtPoint("explosion", pos);

        Destroy(gameObject);
    }

    private void SetupVisuals()
    {
        if (vfxReferences != null && _weapon != null && !string.IsNullOrEmpty(_weapon.rocketTrailVFXKey))
        {
            Transform anchor = trailAnchor != null ? trailAnchor : transform;
            var entry = vfxReferences.GetEffect(_weapon.rocketTrailVFXKey);
            if (entry != null)
                _trailInstance = Instantiate(entry.Value.prefab, anchor.position, anchor.rotation, anchor);
        }

        // Whoosh loop — follows the rocket as it moves.
        _whooshSource = SoundManager.Instance?.PlayAttached("rocket_whoosh", transform, loop: true);
    }

    private void HideRenderers()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.enabled = false;
    }

    private void OnDestroy()
    {
        if (_trailInstance != null && _trailInstance.transform.parent != transform)
            Destroy(_trailInstance, 2f);

        if (_whooshSource != null)
        {
            _whooshSource.Stop();
            Destroy(_whooshSource.gameObject);
        }
    }
}
