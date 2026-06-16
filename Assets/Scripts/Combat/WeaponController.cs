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
    // Ammo is the ONLY authoritative value we sync, and it changes only on
    // discrete events (fire / reload-complete) — never per frame. The reload
    // bar and ammo readout are driven entirely client-side (see _ui* fields);
    // the server is the sole authority on whether a shot is actually allowed.

    [SyncVar(hook = nameof(OnAmmoChangedHook))]
    private int _currentAmmo;

    // ── Server-Authoritative State (server-only, never synced per frame) ──

    private float _fireCooldownTimer;
    private bool  _serverReloading;
    private float _serverReloadTimer;
    private Rigidbody _carRigidbody;
    private CarMotor _carMotor;
    private bool _isDead;

    // ── Client UI State (local player only, optimistic — not networked) ──
    // We don't care if a client "fakes" its own HUD; the server re-validates
    // every shot in CmdFire, so a lying UI can never actually fire illegally.

    private int   _uiAmmo;
    private bool  _uiReloading;
    private float _uiReloadTimer;
    private float _uiReloadProgress;
    private float _uiCooldownTimer;

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

    public int CurrentAmmo => isLocalPlayer ? _uiAmmo : _currentAmmo;
    public int MaxAmmo => weaponSettings != null ? weaponSettings.maxAmmo : 0;
    public bool IsReloading => _uiReloading;
    public float ReloadProgress => _uiReloadProgress;

    // ── Lifecycle ───────────────────────────────────────────

    private void Awake()
    {
        _carRigidbody = GetComponent<Rigidbody>();
        _carMotor = GetComponent<CarMotor>();

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

        _uiAmmo = _currentAmmo;
        _uiReloading = false;
        _uiReloadProgress = 1f;
        OnAmmoChanged?.Invoke(_uiAmmo, MaxAmmo);
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
            _serverReloading   = false;
            _serverReloadTimer = 0f;
            return;
        }

        if (!_serverReloading || weaponSettings == null) return;

        // Authoritative reload timer. No RPCs, no per-frame SyncVars — _currentAmmo
        // only changes when a reload actually completes (a discrete event).
        _serverReloadTimer -= Time.deltaTime;
        if (_serverReloadTimer > 0f) return;

        if (weaponSettings.reloadPerRocket)
        {
            _currentAmmo++;
            if (_currentAmmo >= weaponSettings.maxAmmo) _serverReloading = false;
            else _serverReloadTimer = weaponSettings.reloadTime;
        }
        else
        {
            _currentAmmo = weaponSettings.maxAmmo;
            _serverReloading = false;
        }
    }

    // ── Local Input ─────────────────────────────────────────

    private void LocalPlayerUpdate()
    {
        if (weaponSettings == null) return;
        float dt = Time.deltaTime;

        // Dead → cancel any local reload bar and take no input.
        if (_isDead)
        {
            if (_uiReloading)
            {
                _uiReloading = false;
                _uiReloadProgress = 0f;
                OnReloadStateChanged?.Invoke(false, 0f);
            }
            return;
        }

        if (_uiCooldownTimer > 0f) _uiCooldownTimer -= dt;
        TickLocalReload(dt);

        if (_isFrozen) return;

        bool firePressed   = _useBindingManager
            ? InputBindingManager.singleton.WasPressedThisFrame(InputBindingManager.GameAction.WeaponFire)
            : _fireAction.WasPerformedThisFrame();

        bool reloadPressed = _useBindingManager
            ? InputBindingManager.singleton.WasPressedThisFrame(InputBindingManager.GameAction.WeaponReload)
            : _reloadAction.WasPerformedThisFrame();

        if (firePressed && _uiAmmo > 0 && !_uiReloading && _uiCooldownTimer <= 0f)
        {
            // Optimistic local feedback — server re-validates the shot in CmdFire.
            _uiAmmo--;
            _uiCooldownTimer = weaponSettings.fireCooldown;
            OnAmmoChanged?.Invoke(_uiAmmo, weaponSettings.maxAmmo);
            OnShotFired?.Invoke();
            if (_uiAmmo <= 0) StartLocalReload();
            // Send the CLIENT's muzzle position too: under client-predicted movement the
            // owner's car is ahead of the server's authoritative car by the input latency,
            // so spawning from the server muzzle would launch the rocket from where the
            // car USED to be. The server spawns from this reported origin (sanity-clamped).
            Vector3 muzzlePos = GetMuzzlePosition();
            Vector3 aimDir    = GetAimDirection();
            // Fire our own visual rocket this frame — zero network delay. The server spawns
            // an invisible hit-detector and tells the OTHER clients to spawn their visuals.
            SpawnVisualRocket(muzzlePos, aimDir, LocalCarVelocity(), netIdentity.netId);
            CmdFire(muzzlePos, aimDir);
        }

        if (reloadPressed && _uiAmmo < weaponSettings.maxAmmo && !_uiReloading)
        {
            StartLocalReload();
            CmdReload();
        }
    }

    /// <summary>Advances the purely-local reload bar (UI only — never networked).</summary>
    private void TickLocalReload(float dt)
    {
        if (!_uiReloading) return;

        _uiReloadTimer -= dt;
        _uiReloadProgress = Mathf.Clamp01(1f - _uiReloadTimer / weaponSettings.reloadTime);

        if (_uiReloadTimer > 0f)
        {
            OnReloadStateChanged?.Invoke(true, _uiReloadProgress);
            return;
        }

        if (weaponSettings.reloadPerRocket)
        {
            _uiAmmo = Mathf.Min(_uiAmmo + 1, weaponSettings.maxAmmo);
            if (_uiAmmo >= weaponSettings.maxAmmo)
            {
                _uiReloading = false;
                _uiReloadProgress = 1f;
            }
            else
            {
                _uiReloadTimer = weaponSettings.reloadTime;
            }
        }
        else
        {
            _uiAmmo = weaponSettings.maxAmmo;
            _uiReloading = false;
            _uiReloadProgress = 1f;
        }

        OnAmmoChanged?.Invoke(_uiAmmo, weaponSettings.maxAmmo);
        OnReloadStateChanged?.Invoke(_uiReloading, _uiReloadProgress);
    }

    private void StartLocalReload()
    {
        _uiReloading = true;
        _uiReloadTimer = weaponSettings.reloadTime;
        _uiReloadProgress = 0f;
        OnReloadStateChanged?.Invoke(true, 0f);
    }

    /// <summary>The muzzle world position as the CLIENT sees it (matches the local predicted car).</summary>
    private Vector3 GetMuzzlePosition()
    {
        return muzzlePoint != null
            ? muzzlePoint.position
            : transform.position + transform.forward * 3f + transform.up * 1.5f;
    }

    private Vector3 GetAimDirection()
    {
        if (!aimFromCamera || Camera.main == null)
            return GetStabilizedMuzzleForward();

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 target = Physics.Raycast(ray, out RaycastHit hit, aimRaycastDistance, aimLayers)
            ? hit.point : ray.GetPoint(aimRaycastDistance);

        Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position;
        return (target - origin).normalized;
    }

    /// <summary>
    /// The muzzle's forward, stabilized against suspension squat/dive. When grounded we
    /// project it onto the actual wheel-contact ground plane, so accel/brake body wobble
    /// cancels out while genuine terrain slope is kept. Airborne → the raw muzzle forward
    /// (true car attitude). See CarMotor.GroundNormal.
    /// </summary>
    private Vector3 GetStabilizedMuzzleForward()
    {
        Vector3 raw = muzzlePoint != null ? muzzlePoint.forward : transform.forward;

        // CarMotor is added at runtime by CarController, so resolve it lazily.
        if (_carMotor == null) _carMotor = GetComponent<CarMotor>();

        if (_carMotor != null && _carMotor.IsGrounded)
        {
            Vector3 flat = Vector3.ProjectOnPlane(raw, _carMotor.GroundNormal);
            if (flat.sqrMagnitude > 1e-4f) return flat.normalized;
        }

        return raw;
    }

    // ── Commands ────────────────────────────────────────────

    [Command]
    private void CmdFire(Vector3 clientMuzzlePosition, Vector3 aimDirection)
    {
        // Authoritative validation: the client's UI is optimistic and untrusted,
        // so we re-check ammo, reload state and cooldown here before spawning.
        if (weaponSettings == null || _currentAmmo <= 0 || _serverReloading) return;
        if (_fireCooldownTimer > 0f || _isDead) return;

        _currentAmmo--;
        _fireCooldownTimer = weaponSettings.fireCooldown;

        if (_currentAmmo <= 0) StartServerReload();

        Vector3 spawnPos = SanitizeMuzzlePosition(clientMuzzlePosition);
        Vector3 vel = LocalCarVelocity();

        // Authoritative invisible rocket: damage only, no visuals.
        SpawnServerHitRocket(spawnPos, aimDirection, vel);

        // Tell every OTHER client to spawn their own visual rocket (the shooter already
        // spawned theirs locally on fire, so they ignore this).
        RpcSpawnVisualRocket(spawnPos, aimDirection, vel, netIdentity.netId);

        RpcOnShotFired();
    }

    /// <summary>
    /// Trust the client's reported muzzle origin only within a generous radius of the
    /// server's car (covers the legit prediction-vs-server offset at full speed). A
    /// wildly out-of-range value (a spoofing client) falls back to the server muzzle.
    /// </summary>
    [Server]
    private Vector3 SanitizeMuzzlePosition(Vector3 clientMuzzlePosition)
    {
        Vector3 serverMuzzle = GetMuzzlePosition();
        const float maxOriginOffset = 15f; // metres; > worst-case input-latency * top speed
        return (clientMuzzlePosition - serverMuzzle).sqrMagnitude <= maxOriginOffset * maxOriginOffset
            ? clientMuzzlePosition
            : serverMuzzle;
    }

    [Command]
    private void CmdReload()
    {
        if (weaponSettings == null || _isDead) return;
        if (_currentAmmo >= weaponSettings.maxAmmo || _serverReloading) return;
        StartServerReload();
    }

    [Server]
    private void StartServerReload()
    {
        _serverReloading = true;
        _serverReloadTimer = weaponSettings.reloadTime;
    }

    /// <summary>The shooter's current car velocity (predicted on the owner, authoritative on the server).</summary>
    private Vector3 LocalCarVelocity()
        => _carRigidbody != null ? _carRigidbody.linearVelocity : Vector3.zero;

    /// <summary>Server: spawn the invisible authoritative rocket that deals the real damage.</summary>
    [Server]
    private void SpawnServerHitRocket(Vector3 spawnPos, Vector3 aimDirection, Vector3 shooterVelocity)
        => SpawnLocalRocket(Rocket.RocketMode.ServerHit, spawnPos, aimDirection, shooterVelocity, netIdentity.netId);

    /// <summary>
    /// Instantiate a non-networked rocket on THIS peer only. Visual rockets show the
    /// mesh/trail/whoosh and explode cosmetically; the server's hit rocket is invisible
    /// and only deals damage. See Rocket for the mode behaviour.
    /// </summary>
    private void SpawnVisualRocket(Vector3 spawnPos, Vector3 aimDirection, Vector3 shooterVelocity, uint ownerNetId)
        => SpawnLocalRocket(Rocket.RocketMode.Visual, spawnPos, aimDirection, shooterVelocity, ownerNetId);

    private void SpawnLocalRocket(Rocket.RocketMode mode, Vector3 spawnPos,
        Vector3 aimDirection, Vector3 shooterVelocity, uint ownerNetId)
    {
        if (rocketPrefab == null || weaponSettings == null) return;

        GameObject rocketObj = Instantiate(rocketPrefab, spawnPos, Quaternion.LookRotation(aimDirection));
        Rocket rocket = rocketObj.GetComponent<Rocket>();
        if (rocket == null) { Destroy(rocketObj); return; }

        rocket.Launch(mode, weaponSettings, aimDirection, shooterVelocity, ownerNetId);
    }

    // ── RPCs ────────────────────────────────────────────────

    [ClientRpc]
    private void RpcSpawnVisualRocket(Vector3 spawnPos, Vector3 aimDirection,
        Vector3 shooterVelocity, uint ownerNetId)
    {
        // The shooter already fired their own predicted visual rocket on the input frame.
        if (isLocalPlayer) return;
        SpawnVisualRocket(spawnPos, aimDirection, shooterVelocity, ownerNetId);
    }

    [ClientRpc]
    private void RpcOnShotFired()
    {
        if (vfxReferences != null && weaponSettings != null && muzzlePoint != null)
            vfxReferences.SpawnEffect(weaponSettings.muzzleFlashVFXKey, muzzlePoint.position, muzzlePoint.rotation);
        if (isLocalPlayer) OnShotFired?.Invoke();
    }

    private void OnAmmoChangedHook(int oldAmmo, int newAmmo)
    {
        // Reconcile the optimistic local UI to the server's authoritative ammo.
        // Fires only on discrete ammo events (fire / reload-complete), not per frame.
        if (isLocalPlayer)
        {
            _uiAmmo = newAmmo;
            if (newAmmo >= MaxAmmo)
            {
                _uiReloading = false;
                _uiReloadProgress = 1f;
                OnReloadStateChanged?.Invoke(false, 1f);
            }
        }
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
