using UnityEngine;
using Mirror;

/// <summary>
/// Networked rocket projectile. Spawned by WeaponController on the server.
///
/// Speed model (solves the "car outruns its own rocket" problem):
///   1. On spawn, rocket inherits the car's Rigidbody velocity
///   2. Adds rocketSpeed on top in the aim direction
///   3. Total is capped at rocketMaxSpeed
///   4. Over time, rocket lerps toward rocketCruiseSpeed
///   This means a rocket fired from a 200kph car launches FAST (200kph + rocket speed)
///   then gradually settles to cruise speed. Feels punchy, never hits the shooter.
///
/// Prefab structure:
///   Rocket              ← Rigidbody + NetworkIdentity + Rocket + SphereCollider
///     └── Visual        ← Mesh (capsule placeholder) or your rocket model
///     └── TrailAnchor   ← Empty — trail VFX parents here
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkIdentity))]
public class Rocket : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private CombatSettings combatSettings;
    [SerializeField] private VFXReferences vfxReferences;
    [SerializeField] private Transform trailAnchor;

    // ── Synced State ────────────────────────────────────────

    [SyncVar] private uint _ownerNetId;

    // ── Local State ─────────────────────────────────────────

    private WeaponSettings _weapon;
    private Rigidbody _rb;
    private bool _hasExploded;
    private float _lifetimeTimer;
    private float _currentSpeed;
    private Vector3 _flyDirection;
    private GameObject _trailInstance;

    // ── Setup ───────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    /// <summary>
    /// Called by WeaponController on the SERVER right after spawn.
    /// </summary>
    [Server]
    public void Initialize(
        WeaponSettings weapon,
        Vector3 aimDirection,
        Vector3 shooterVelocity,
        uint ownerNetId)
    {
        _weapon = weapon;
        _ownerNetId = ownerNetId;
        _lifetimeTimer = weapon.rocketLifetime;

        // ── Velocity inheritance ──
        // Start with the shooter's velocity so the rocket is never slower than the car
        Vector3 launchVelocity;

        if (weapon.inheritShooterVelocity)
        {
            // Project shooter velocity onto aim direction (forward component)
            // plus add the rocket's own speed on top
            float forwardComponent = Mathf.Max(0f, Vector3.Dot(shooterVelocity, aimDirection.normalized));
            launchVelocity = aimDirection.normalized * (forwardComponent + weapon.rocketSpeed);
        }
        else
        {
            launchVelocity = aimDirection.normalized * weapon.rocketSpeed;
        }

        // Cap at max speed
        if (launchVelocity.magnitude > weapon.rocketMaxSpeed)
            launchVelocity = launchVelocity.normalized * weapon.rocketMaxSpeed;

        _rb.linearVelocity = launchVelocity;
        _currentSpeed = launchVelocity.magnitude;
        _flyDirection = aimDirection.normalized;

        // Orient rocket to face travel direction
        if (aimDirection != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(aimDirection);

        // Tell clients to set up visuals
        RpcSetupVisuals(weapon.rocketTrailVFXKey);
    }

    // ── Physics (Server) ────────────────────────────────────

    private void FixedUpdate()
    {
        if (!isServer || _hasExploded || _weapon == null) return;

        // ── Gravity ──
        if (_weapon.rocketGravityScale > 0f)
            _rb.AddForce(Physics.gravity * _weapon.rocketGravityScale, ForceMode.Acceleration);

        // ── Cruise speed transition ──
        // Gradually bring speed toward cruise speed so rocket normalizes over time
        if (_weapon.rocketCruiseSpeed > 0f && _weapon.cruiseTransitionRate > 0f)
        {
            _currentSpeed = Mathf.MoveTowards(
                _currentSpeed,
                _weapon.rocketCruiseSpeed,
                _weapon.cruiseTransitionRate * Time.fixedDeltaTime
            );

            // Maintain direction but adjust magnitude
            Vector3 dir = _rb.linearVelocity.normalized;
            if (dir.sqrMagnitude > 0.01f)
            {
                // Preserve the actual direction (including gravity arc) but set speed
                _rb.linearVelocity = dir * _currentSpeed;
            }
        }

        // ── Orient to velocity ──
        if (_rb.linearVelocity.sqrMagnitude > 0.1f)
            transform.rotation = Quaternion.LookRotation(_rb.linearVelocity.normalized);

        // ── Lifetime ──
        _lifetimeTimer -= Time.fixedDeltaTime;
        if (_lifetimeTimer <= 0f)
            Explode();
    }

    // ── Collision ───────────────────────────────────────────

    [Server]
    private void OnCollisionEnter(Collision collision)
    {
        if (_hasExploded) return;

        // Don't collide with the shooter
        var netId = collision.gameObject.GetComponentInParent<NetworkIdentity>();
        if (netId != null && netId.netId == _ownerNetId) return;

        Explode();
    }

    // ── Explosion ───────────────────────────────────────────

    [Server]
    private void Explode()
    {
        if (_hasExploded) return;
        _hasExploded = true;

        Vector3 pos = transform.position;

        if (_weapon != null && combatSettings != null)
        {
            ExplosionHelper.ProcessExplosion(pos, _weapon, combatSettings, _ownerNetId);
        }

        string vfxKey = _weapon != null ? _weapon.explosionVFXKey : "";
        RpcSpawnExplosionVFX(pos, vfxKey);

        NetworkServer.Destroy(gameObject);
    }

    // ── Client Visuals ──────────────────────────────────────

    [ClientRpc]
    private void RpcSetupVisuals(string trailKey)
    {
        if (vfxReferences == null || string.IsNullOrEmpty(trailKey)) return;

        Transform anchor = trailAnchor != null ? trailAnchor : transform;
        var entry = vfxReferences.GetEffect(trailKey);
        if (entry != null)
            _trailInstance = Instantiate(entry.Value.prefab, anchor.position, anchor.rotation, anchor);
    }

    [ClientRpc]
    private void RpcSpawnExplosionVFX(Vector3 position, string vfxKey)
    {
        if (vfxReferences != null && !string.IsNullOrEmpty(vfxKey))
            vfxReferences.SpawnEffect(vfxKey, position, Quaternion.identity);
    }

    private void OnDestroy()
    {
        if (_trailInstance != null && _trailInstance.transform.parent != transform)
            Destroy(_trailInstance, 2f);
    }
}
