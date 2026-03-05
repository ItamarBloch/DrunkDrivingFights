using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;
using System;

/// <summary>
/// Networked weapon controller. Attach to the Car prefab root.
///
/// Uses the NEW Input System (InputAction). No InputActionAsset needed —
/// actions are created in code with default bindings.
///
/// Flow: Local player clicks → CmdFire(aimDir) → server validates →
///       spawns Rocket with car's velocity inherited → syncs ammo to all clients.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class WeaponController : NetworkBehaviour
{
    // ── Settings ────────────────────────────────────────────

    [Header("Weapon")]
    [SerializeField] private WeaponSettings weaponSettings;

    [Header("References")]
    [Tooltip("The Rocket prefab. MUST be in NetworkManager → Registered Spawnable Prefabs.")]
    [SerializeField] private GameObject rocketPrefab;

    [Tooltip("Where rockets spawn from. +Z = fire direction.")]
    [SerializeField] private Transform muzzlePoint;

    [SerializeField] private CombatSettings combatSettings;
    [SerializeField] private VFXReferences vfxReferences;

    [Header("Aiming")]
    [Tooltip("If true, rockets aim toward screen center (camera crosshair). " +
             "If false, rockets fire straight from the muzzle's forward direction.")]
    [SerializeField] private bool aimFromCamera = true;

    [SerializeField] private float aimRaycastDistance = 500f;
    [SerializeField] private LayerMask aimLayers = ~0;

    // ── Synced State ────────────────────────────────────────

    [SyncVar(hook = nameof(OnAmmoChangedHook))]
    private int _currentAmmo;

    [SyncVar]
    private bool _isReloading;

    [SyncVar]
    private float _reloadProgress;

    // ── Local State ─────────────────────────────────────────

    private float _fireCooldownTimer;
    private float _reloadTimer;
    private Rigidbody _carRigidbody;
    private HealthController _health;

    // Input actions (new Input System)
    private InputAction _fireAction;
    private InputAction _reloadAction;

    // ── Events ──────────────────────────────────────────────

    /// <summary> (currentAmmo, maxAmmo) </summary>
    public event Action<int, int> OnAmmoChanged;

    /// <summary> (isReloading, reloadProgress 0-1) </summary>
    public event Action<bool, float> OnReloadStateChanged;

    /// <summary> Fires on local client when a shot is fired. </summary>
    public event Action OnShotFired;

    // ── Public Properties ───────────────────────────────────

    public int CurrentAmmo => _currentAmmo;
    public int MaxAmmo => weaponSettings != null ? weaponSettings.maxAmmo : 0;
    public bool IsReloading => _isReloading;
    public float ReloadProgress => _reloadProgress;

    // ── Lifecycle ───────────────────────────────────────────

    private void Awake()
    {
        _carRigidbody = GetComponent<Rigidbody>();
        _health = GetComponent<HealthController>();

        // Create input actions with default bindings
        _fireAction = new InputAction("Fire", InputActionType.Button);
        _fireAction.AddBinding("<Mouse>/leftButton");

        _reloadAction = new InputAction("Reload", InputActionType.Button);
        _reloadAction.AddBinding("<Keyboard>/r");
    }

    public override void OnStartServer()
    {
        _currentAmmo = weaponSettings != null ? weaponSettings.maxAmmo : 0;
    }

    public override void OnStartLocalPlayer()
    {
        _fireAction.Enable();
        _reloadAction.Enable();

        if (muzzlePoint == null)
        {
            SetupMuzzlePoint(new Vector3(0f, 1.5f, 2.5f), Vector3.forward);
            Debug.LogWarning("[WeaponController] No MuzzlePoint assigned — created default. " +
                             "Assign one in the Inspector for precise placement.");
        }

        OnAmmoChanged?.Invoke(_currentAmmo, MaxAmmo);
    }

    private void OnDisable()
    {
        _fireAction?.Disable();
        _reloadAction?.Disable();
    }

    private void OnDestroy()
    {
        _fireAction?.Dispose();
        _reloadAction?.Dispose();
    }

    private void Update()
    {
        if (isServer) ServerUpdate();
        if (isLocalPlayer) LocalPlayerUpdate();
    }

    // ── Server ──────────────────────────────────────────────

    [Server]
    private void ServerUpdate()
    {
        if (_fireCooldownTimer > 0f)
            _fireCooldownTimer -= Time.deltaTime;

        if (_isReloading && weaponSettings != null)
        {
            _reloadTimer -= Time.deltaTime;

            if (weaponSettings.reloadPerRocket)
            {
                _reloadProgress = 1f - (_reloadTimer / weaponSettings.reloadTime);
                if (_reloadTimer <= 0f)
                {
                    _currentAmmo++;
                    if (_currentAmmo >= weaponSettings.maxAmmo)
                    {
                        _isReloading = false;
                        _reloadProgress = 0f;
                    }
                    else
                    {
                        _reloadTimer = weaponSettings.reloadTime;
                    }
                }
            }
            else
            {
                _reloadProgress = 1f - (_reloadTimer / weaponSettings.reloadTime);
                if (_reloadTimer <= 0f)
                {
                    _currentAmmo = weaponSettings.maxAmmo;
                    _isReloading = false;
                    _reloadProgress = 0f;
                }
            }

            RpcReloadProgress(_isReloading, _reloadProgress);
        }
    }

    // ── Local Input (New Input System) ──────────────────────

    private void LocalPlayerUpdate()
    {
        if (weaponSettings == null) return;
        if (_health != null && !_health.IsAlive) return;

        if (_fireAction.WasPerformedThisFrame())
        {
            if (_currentAmmo > 0 && !_isReloading)
            {
                Vector3 aimDir = GetAimDirection();
                CmdFire(aimDir);
            }
        }

        if (_reloadAction.WasPerformedThisFrame())
        {
            if (_currentAmmo < weaponSettings.maxAmmo && !_isReloading)
                CmdReload();
        }
    }

    private Vector3 GetAimDirection()
    {
        if (!aimFromCamera || Camera.main == null)
            return muzzlePoint != null ? muzzlePoint.forward : transform.forward;

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 targetPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, aimRaycastDistance, aimLayers))
            targetPoint = hit.point;
        else
            targetPoint = ray.GetPoint(aimRaycastDistance);

        Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position;
        return (targetPoint - origin).normalized;
    }

    // ── Commands ────────────────────────────────────────────

    [Command]
    private void CmdFire(Vector3 aimDirection)
    {
        if (weaponSettings == null || _currentAmmo <= 0 || _isReloading) return;
        if (_fireCooldownTimer > 0f) return;
        if (_health != null && !_health.IsAlive) return;

        _currentAmmo--;
        _fireCooldownTimer = weaponSettings.fireCooldown;

        if (_currentAmmo <= 0)
            StartReload();

        SpawnRocket(aimDirection);
        RpcOnShotFired();
    }

    [Command]
    private void CmdReload()
    {
        if (weaponSettings == null) return;
        if (_currentAmmo >= weaponSettings.maxAmmo || _isReloading) return;
        StartReload();
    }

    // ── Server Internals ────────────────────────────────────

    [Server]
    private void StartReload()
    {
        _isReloading = true;
        _reloadProgress = 0f;
        _reloadTimer = weaponSettings.reloadTime;
    }

    [Server]
    private void SpawnRocket(Vector3 aimDirection)
    {
        if (rocketPrefab == null)
        {
            Debug.LogError("[WeaponController] Rocket prefab not assigned!", this);
            return;
        }

        Vector3 spawnPos;
        Quaternion spawnRot;

        if (muzzlePoint != null)
        {
            spawnPos = muzzlePoint.position;
            spawnRot = Quaternion.LookRotation(aimDirection);
        }
        else
        {
            spawnPos = transform.position + transform.forward * 3f + transform.up * 1.5f;
            spawnRot = Quaternion.LookRotation(aimDirection);
        }

        GameObject rocketObj = Instantiate(rocketPrefab, spawnPos, spawnRot);
        NetworkServer.Spawn(rocketObj);

        Rocket rocket = rocketObj.GetComponent<Rocket>();
        if (rocket != null)
        {
            Vector3 shooterVelocity = _carRigidbody != null ? _carRigidbody.linearVelocity : Vector3.zero;

            rocket.Initialize(
                weaponSettings,
                aimDirection,
                shooterVelocity,
                netIdentity.netId
            );
        }
    }

    // ── RPCs ────────────────────────────────────────────────

    [ClientRpc]
    private void RpcOnShotFired()
    {
        if (vfxReferences != null && weaponSettings != null && muzzlePoint != null)
            vfxReferences.SpawnEffect(weaponSettings.muzzleFlashVFXKey, muzzlePoint.position, muzzlePoint.rotation);

        if (isLocalPlayer)
            OnShotFired?.Invoke();
    }

    [ClientRpc]
    private void RpcReloadProgress(bool reloading, float progress)
    {
        OnReloadStateChanged?.Invoke(reloading, progress);
    }

    // ── SyncVar Hooks ───────────────────────────────────────

    private void OnAmmoChangedHook(int oldAmmo, int newAmmo)
    {
        if (weaponSettings != null)
            OnAmmoChanged?.Invoke(newAmmo, weaponSettings.maxAmmo);
    }

    // ── Public Helpers ──────────────────────────────────────

    public void SetupMuzzlePoint(Vector3 localPosition, Vector3 localForward)
    {
        if (muzzlePoint != null) return;
        var go = new GameObject("MuzzlePoint");
        go.transform.SetParent(transform);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.LookRotation(localForward);
        muzzlePoint = go.transform;
    }
}
