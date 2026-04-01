using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;
using System;

/// <summary>
/// Networked weapon controller.
/// Subscribes to HealthController.OnDeath / OnRespawn — no polling.
/// Uses New Input System.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class WeaponController : NetworkBehaviour
{
    [Header("Weapon")]
    [SerializeField] private WeaponSettings weaponSettings;

    [Header("References")]
    [SerializeField] private GameObject rocketPrefab;
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private CombatSettings combatSettings;
    [SerializeField] private VFXReferences vfxReferences;

    [Header("Aiming")]
    [SerializeField] private bool aimFromCamera = true;
    [SerializeField] private float aimRaycastDistance = 500f;
    [SerializeField] private LayerMask aimLayers = ~0;

    // ── Synced State ────────────────────────────────────────

    [SyncVar(hook = nameof(OnAmmoChangedHook))]
    private int _currentAmmo;

    [SyncVar] private bool _isReloading;
    [SyncVar] private float _reloadProgress;

    // ── Local State ─────────────────────────────────────────

    private float _fireCooldownTimer;
    private float _reloadTimer;
    private Rigidbody _carRigidbody;
    private bool _isDead;

    [SyncVar] private bool _isFrozen;

    /// <summary>Server: freeze or unfreeze this weapon's input processing.</summary>
    [Server] public void SetFrozen(bool frozen) => _isFrozen = frozen;

    private InputAction _fireAction;
    private InputAction _reloadAction;

    // Used when InputBindingManager is available (overrides _fireAction / _reloadAction)
    private bool _useBindingManager;

    // ── Events ──────────────────────────────────────────────

    public event Action<int, int> OnAmmoChanged;
    public event Action<bool, float> OnReloadStateChanged;
    public event Action OnShotFired;

    // ── Public ──────────────────────────────────────────────

    public int CurrentAmmo => _currentAmmo;
    public int MaxAmmo => weaponSettings != null ? weaponSettings.maxAmmo : 0;
    public bool IsReloading => _isReloading;
    public float ReloadProgress => _reloadProgress;

    // ── Lifecycle ───────────────────────────────────────────

    private void Awake()
    {
        _carRigidbody = GetComponent<Rigidbody>();

        _fireAction = new InputAction("Fire", InputActionType.Button);
        _fireAction.AddBinding("<Mouse>/leftButton");
        _reloadAction = new InputAction("Reload", InputActionType.Button);
        _reloadAction.AddBinding("<Keyboard>/r");

        var health = GetComponent<HealthController>();
        if (health != null)
        {
            health.OnDeath += (killerNetId) => { _isDead = true; };
            health.OnRespawn += () => { _isDead = false; };
        }
    }

    public override void OnStartServer()
    {
        _currentAmmo = weaponSettings != null ? weaponSettings.maxAmmo : 0;
        if (MatchManager.singleton != null && MatchManager.singleton.State != MatchState.InProgress)
            _isFrozen = true;
    }

    public override void OnStartLocalPlayer()
    {
        _useBindingManager = InputBindingManager.singleton != null;
        if (!_useBindingManager)
        {
            _fireAction.Enable();
            _reloadAction.Enable();
        }

        if (muzzlePoint == null)
        {
            SetupMuzzlePoint(new Vector3(0f, 1.5f, 2.5f), Vector3.forward);
            Debug.LogWarning("[WeaponController] No MuzzlePoint assigned — created default.");
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

        if (_isDead)
        {
            if (_isReloading)
            {
                _isReloading = false;
                _reloadProgress = 0f;
                RpcReloadProgress(false, 0f);
            }
            return;
        }

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
                    { _isReloading = false; _reloadProgress = 0f; }
                    else
                    { _reloadTimer = weaponSettings.reloadTime; }
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

    // ── Local Input ─────────────────────────────────────────

    private void LocalPlayerUpdate()
    {
        if (weaponSettings == null || _isDead || _isFrozen) return;

        bool firePressed   = _useBindingManager
            ? InputBindingManager.singleton.WasPressedThisFrame(InputBindingManager.GameAction.WeaponFire)
            : _fireAction.WasPerformedThisFrame();

        bool reloadPressed = _useBindingManager
            ? InputBindingManager.singleton.WasPressedThisFrame(InputBindingManager.GameAction.WeaponReload)
            : _reloadAction.WasPerformedThisFrame();

        if (firePressed && _currentAmmo > 0 && !_isReloading)
            CmdFire(GetAimDirection());

        if (reloadPressed && _currentAmmo < weaponSettings.maxAmmo && !_isReloading)
            CmdReload();
    }

    private Vector3 GetAimDirection()
    {
        if (!aimFromCamera || Camera.main == null)
            return muzzlePoint != null ? muzzlePoint.forward : transform.forward;

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 target = Physics.Raycast(ray, out RaycastHit hit, aimRaycastDistance, aimLayers)
            ? hit.point : ray.GetPoint(aimRaycastDistance);

        Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position;
        return (target - origin).normalized;
    }

    // ── Commands ────────────────────────────────────────────

    [Command]
    private void CmdFire(Vector3 aimDirection)
    {
        if (weaponSettings == null || _currentAmmo <= 0 || _isReloading) return;
        if (_fireCooldownTimer > 0f || _isDead) return;

        _currentAmmo--;
        _fireCooldownTimer = weaponSettings.fireCooldown;

        if (_currentAmmo <= 0) StartReload();

        SpawnRocket(aimDirection);
        RpcOnShotFired();
    }

    [Command]
    private void CmdReload()
    {
        if (weaponSettings == null || _isDead) return;
        if (_currentAmmo >= weaponSettings.maxAmmo || _isReloading) return;
        StartReload();
    }

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
        if (rocketPrefab == null) return;

        Vector3 spawnPos = muzzlePoint != null
            ? muzzlePoint.position
            : transform.position + transform.forward * 3f + transform.up * 1.5f;

        GameObject rocketObj = Instantiate(rocketPrefab, spawnPos, Quaternion.LookRotation(aimDirection));
        NetworkServer.Spawn(rocketObj);

        Rocket rocket = rocketObj.GetComponent<Rocket>();
        if (rocket != null)
        {
            Vector3 vel = _carRigidbody != null ? _carRigidbody.linearVelocity : Vector3.zero;
            rocket.Initialize(weaponSettings, aimDirection, vel, netIdentity.netId);
        }

    }

    // ── RPCs ────────────────────────────────────────────────

    [ClientRpc]
    private void RpcOnShotFired()
    {
        if (vfxReferences != null && weaponSettings != null && muzzlePoint != null)
            vfxReferences.SpawnEffect(weaponSettings.muzzleFlashVFXKey, muzzlePoint.position, muzzlePoint.rotation);
        if (isLocalPlayer) OnShotFired?.Invoke();
    }

    [ClientRpc]
    private void RpcReloadProgress(bool reloading, float progress)
    {
        OnReloadStateChanged?.Invoke(reloading, progress);
    }

    private void OnAmmoChangedHook(int oldAmmo, int newAmmo)
    {
        if (weaponSettings != null) OnAmmoChanged?.Invoke(newAmmo, weaponSettings.maxAmmo);
    }

    public void SetupMuzzlePoint(Vector3 localPos, Vector3 localFwd)
    {
        if (muzzlePoint != null) return;
        var go = new GameObject("MuzzlePoint");
        go.transform.SetParent(transform);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.LookRotation(localFwd);
        muzzlePoint = go.transform;
    }
}
