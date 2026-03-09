using UnityEngine;
using Mirror;

/// <summary>
/// Networked rocket. Inherits car velocity on spawn, has cruise speed transition.
///
/// IMPORTANT — Rocket prefab needs these components:
///   - Rigidbody
///   - NetworkIdentity
///   - NetworkTransform (syncs position/rotation from server to clients)
///   - SphereCollider
///   - Rocket (this script)
///
/// The server drives the Rigidbody. Clients receive position via NetworkTransform.
/// On clients the Rigidbody is set to kinematic so it doesn't fight the sync.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkIdentity))]
public class Rocket : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private CombatSettings combatSettings;
    [SerializeField] private VFXReferences vfxReferences;
    [SerializeField] private Transform trailAnchor;

    [SyncVar] private uint _ownerNetId;

    private WeaponSettings _weapon;
    private Rigidbody _rb;
    private bool _hasExploded;
    private float _lifetimeTimer;
    private float _currentSpeed;
    private GameObject _trailInstance;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public override void OnStartClient()
    {
        // On clients (not the server), make rigidbody kinematic
        // so it doesn't fight with NetworkTransform syncing the position.
        if (!isServer)
        {
            _rb.isKinematic = true;
        }
    }

    [Server]
    public void Initialize(WeaponSettings weapon, Vector3 aimDirection,
        Vector3 shooterVelocity, uint ownerNetId)
    {
        _weapon = weapon;
        _ownerNetId = ownerNetId;
        _lifetimeTimer = weapon.rocketLifetime;

        Vector3 launchVelocity;
        if (weapon.inheritShooterVelocity)
        {
            float fwd = Mathf.Max(0f, Vector3.Dot(shooterVelocity, aimDirection.normalized));
            launchVelocity = aimDirection.normalized * (fwd + weapon.rocketSpeed);
        }
        else
        {
            launchVelocity = aimDirection.normalized * weapon.rocketSpeed;
        }

        if (launchVelocity.magnitude > weapon.rocketMaxSpeed)
            launchVelocity = launchVelocity.normalized * weapon.rocketMaxSpeed;

        _rb.linearVelocity = launchVelocity;
        _currentSpeed = launchVelocity.magnitude;

        if (aimDirection != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(aimDirection);

        RpcSetupVisuals(weapon.rocketTrailVFXKey);
    }

    private void FixedUpdate()
    {
        if (!isServer || _hasExploded || _weapon == null) return;

        if (_weapon.rocketGravityScale > 0f)
            _rb.AddForce(Physics.gravity * _weapon.rocketGravityScale, ForceMode.Acceleration);

        if (_weapon.rocketCruiseSpeed > 0f && _weapon.cruiseTransitionRate > 0f)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _weapon.rocketCruiseSpeed,
                _weapon.cruiseTransitionRate * Time.fixedDeltaTime);
            Vector3 dir = _rb.linearVelocity.normalized;
            if (dir.sqrMagnitude > 0.01f)
                _rb.linearVelocity = dir * _currentSpeed;
        }

        if (_rb.linearVelocity.sqrMagnitude > 0.1f)
            transform.rotation = Quaternion.LookRotation(_rb.linearVelocity.normalized);

        _lifetimeTimer -= Time.fixedDeltaTime;
        if (_lifetimeTimer <= 0f) Explode();
    }

    // Unity calls OnCollisionEnter on ALL clients — [Server] attribute
    // does NOT prevent it, it just logs a warning. Use a manual check.
    private void OnCollisionEnter(Collision collision)
    {
        if (!isServer) return;
        if (_hasExploded) return;

        var netId = collision.gameObject.GetComponentInParent<NetworkIdentity>();
        if (netId != null && netId.netId == _ownerNetId) return;

        Explode();
    }

    private void Explode()
    {
        if (!isServer) return;
        if (_hasExploded) return;
        _hasExploded = true;

        if (_weapon != null && combatSettings != null)
            ExplosionHelper.ProcessExplosion(transform.position, _weapon, combatSettings, _ownerNetId);

        string vfxKey = _weapon != null ? _weapon.explosionVFXKey : "";
        RpcSpawnExplosionVFX(transform.position, vfxKey);
        NetworkServer.Destroy(gameObject);
    }

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
