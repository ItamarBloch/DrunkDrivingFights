using Mirror;
using UnityEngine;

/// <summary>
/// Networked car controller using Mirror.
/// 
/// Subscribes to HealthController.OnDeath / OnRespawn events.
/// When dead → returns zero input, car coasts to a stop.
/// When alive → processes input normally.
///
/// Architecture:
///   • SERVER:  Authoritative — receives input from owning client, runs physics,
///              broadcasts state to all clients.
///   • OWNER:   Sends input via [Command], runs local physics prediction for
///              responsiveness, reconciles when server state diverges.
///   • REMOTE:  Receives server state, smoothly interpolates via CarRemoteInterpolator.
///
/// Expected hierarchy:
///   Car (Rigidbody + NetworkIdentity + CarController)
///     ├── Body
///     ├── Wheels
///     │     ├── WheelFL (WheelCollider)
///     │     ├── WheelFR (WheelCollider)
///     │     ├── WheelRL (WheelCollider)
///     │     └── WheelRR (WheelCollider)
///     └── WheelMeshes
///           ├── WheelVisualFL
///           ├── WheelVisualFR
///           ├── WheelVisualRL
///           └── WheelVisualRR
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkIdentity))]
public class CarController : NetworkBehaviour
{
    // ── Inspector References ────────────────────────────────

    [Header("Settings")]
    [SerializeField] private CarSettings settings;

    [Header("Wheel Colliders (FL, FR, RL, RR)")]
    [SerializeField] private WheelCollider[] frontWheels = new WheelCollider[2];
    [SerializeField] private WheelCollider[] rearWheels = new WheelCollider[2];

    [Header("Wheel Visuals (FL, FR, RL, RR)")]
    [SerializeField] private Transform[] wheelVisuals = new Transform[4];

    [Header("Physics")]
    [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.5f, 0f);

    [Header("Network Tuning")]
    [Tooltip("How many FixedUpdate ticks between server state broadcasts.")]
    [SerializeField] private int networkSendRate = 2;

    [Tooltip("If the owner's predicted position is this far from server state, snap correct.")]
    [SerializeField] private float correctionSnapThreshold = 3f;

    [Tooltip("Smooth correction speed when within snap threshold.")]
    [SerializeField] private float correctionLerpSpeed = 10f;

    // ── Subsystems ──────────────────────────────────────────

    private CarInputHandler _inputHandler;
    private CarMotor _motor;
    private CarSteering _steering;
    private CarWheelVisuals _wheelVisuals;
    private CarRemoteInterpolator _interpolator;
    private Rigidbody _rb;

    // ── Network State ───────────────────────────────────────

    private CarInputData _serverInput;
    private CarInputData _localInput;
    private int _tickCounter;

    // ── Death State (set via HealthController events) ───────

    private bool _isDead;

    // ── Public Accessors ────────────────────────────────────

    public CarMotor Motor => _motor;
    public CarSteering Steering => _steering;
    public CarSettings Settings => settings;
    public Rigidbody Body => _rb;

    [SyncVar] public float SyncedSpeedKmh;

    // ════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════

    private void Awake()
    {
        CacheAndCreateComponents();
        ConfigureRigidbody();
        InitializeSubsystems();
        SubscribeToHealthEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromHealthEvents();
    }

    public override void OnStartAuthority()
    {
        enabled = true;
    }

    public override void OnStartServer()
    {
        enabled = true;
    }

    private void Update()
    {
        if (!isOwned) return;
        _inputHandler.Poll();
        _localInput = _isDead ? new CarInputData() : _inputHandler.CurrentInput;
    }

    /// <summary>
    /// Physics tick — different behaviour per role.
    /// 
    /// Three possible roles:
    ///   HOST    (isServer + isOwned) → Use local input directly for authoritative sim.
    ///   SERVER  (isServer only)      → Use input received via CmdSendInput.
    ///   OWNER   (isOwned only)       → Predict locally + send input to server.
    ///   REMOTE  (neither)            → Do nothing, CarRemoteInterpolator handles it.
    /// </summary>
    private void FixedUpdate()
    {
        bool amHost = isServer && isOwned;
        bool amServer = isServer && !isOwned;
        bool amOwner = isOwned && !isServer;

        if (amHost)
        {
            HostTick();
        }
        else if (amServer)
        {
            ServerTick();
        }
        else if (amOwner)
        {
            OwnerPredictionTick();
        }
    }

    // ════════════════════════════════════════════════════════
    //  HEALTH EVENT SUBSCRIPTION
    // ════════════════════════════════════════════════════════

    private void SubscribeToHealthEvents()
    {
        var health = GetComponent<HealthController>();
        if (health != null)
        {
            health.OnDeath += HandleDeath;
            health.OnRespawn += HandleRespawn;
        }
    }

    private void UnsubscribeFromHealthEvents()
    {
        var health = GetComponent<HealthController>();
        if (health != null)
        {
            health.OnDeath -= HandleDeath;
            health.OnRespawn -= HandleRespawn;
        }
    }

    private void HandleDeath(uint killerNetId)
    {
        _isDead = true;

        // Server: stop the car immediately
        if (isServer && _rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    private void HandleRespawn()
    {
        _isDead = false;
    }

    // ════════════════════════════════════════════════════════
    //  HOST — Server + Owner on the same machine
    // ════════════════════════════════════════════════════════

    private void HostTick()
    {
        _motor.Tick(_localInput);
        _steering.Tick(_localInput.Steer, _motor.SpeedRatio);

        SyncedSpeedKmh = _motor.SpeedKmh;

        _tickCounter++;
        if (_tickCounter >= networkSendRate)
        {
            _tickCounter = 0;
            BroadcastState();
        }
    }

    // ════════════════════════════════════════════════════════
    //  SERVER — Dedicated server (not the owner)
    // ════════════════════════════════════════════════════════

    private void ServerTick()
    {
        _motor.Tick(_serverInput);
        _steering.Tick(_serverInput.Steer, _motor.SpeedRatio);

        SyncedSpeedKmh = _motor.SpeedKmh;

        _tickCounter++;
        if (_tickCounter >= networkSendRate)
        {
            _tickCounter = 0;
            BroadcastState();
        }
    }

    private void BroadcastState()
    {
        CarNetworkState state = CaptureCurrentState();
        RpcReceiveState(state);
    }

    private CarNetworkState CaptureCurrentState()
    {
        return new CarNetworkState
        {
            Position = _rb.position,
            Rotation = _rb.rotation,
            Velocity = _rb.linearVelocity,
            AngularVelocity = _rb.angularVelocity,
            SteerAngle = _steering.CurrentSteerAngle,
            SpeedKmh = _motor.SpeedKmh,
            Timestamp = NetworkTime.time
        };
    }

    // ════════════════════════════════════════════════════════
    //  OWNER CLIENT — Prediction & Reconciliation
    // ════════════════════════════════════════════════════════

    private void OwnerPredictionTick()
    {
        CmdSendInput(_localInput);

        _motor.Tick(_localInput);
        _steering.Tick(_localInput.Steer, _motor.SpeedRatio);
    }

    private void OwnerReconcile(CarNetworkState serverState)
    {
        float posError = Vector3.Distance(_rb.position, serverState.Position);

        if (posError > correctionSnapThreshold)
        {
            _rb.position = serverState.Position;
            _rb.rotation = serverState.Rotation;
            _rb.linearVelocity = serverState.Velocity;
            _rb.angularVelocity = serverState.AngularVelocity;
        }
        else if (posError > 0.05f)
        {
            _rb.position = Vector3.Lerp(
                _rb.position,
                serverState.Position,
                Time.fixedDeltaTime * correctionLerpSpeed
            );
            _rb.rotation = Quaternion.Slerp(
                _rb.rotation,
                serverState.Rotation,
                Time.fixedDeltaTime * correctionLerpSpeed
            );
        }
    }

    // ════════════════════════════════════════════════════════
    //  NETWORK MESSAGES
    // ════════════════════════════════════════════════════════

    [Command(channel = Channels.Unreliable)]
    private void CmdSendInput(CarInputData input)
    {
        _serverInput = input;
    }

    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcReceiveState(CarNetworkState state)
    {
        if (isServer) return;

        if (isOwned)
        {
            OwnerReconcile(state);
        }
        else
        {
            _interpolator.SetTarget(state);
        }
    }

    // ════════════════════════════════════════════════════════
    //  SETUP
    // ════════════════════════════════════════════════════════

    private void CacheAndCreateComponents()
    {
        _rb = GetComponent<Rigidbody>();
        _inputHandler = GetOrAddComponent<CarInputHandler>();
        _motor = GetOrAddComponent<CarMotor>();
        _steering = GetOrAddComponent<CarSteering>();
        _wheelVisuals = GetOrAddComponent<CarWheelVisuals>();
        _interpolator = GetOrAddComponent<CarRemoteInterpolator>();
    }

    private void ConfigureRigidbody()
    {
        _rb.centerOfMass = centerOfMassOffset;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void InitializeSubsystems()
    {
        WheelCollider[] allWheels = BuildAllWheelsArray();

        _motor.Initialize(settings, _rb, rearWheels, allWheels);
        _steering.Initialize(settings, frontWheels);
        _wheelVisuals.Initialize(allWheels, wheelVisuals);
        _interpolator.Initialize(_rb);
    }

    private WheelCollider[] BuildAllWheelsArray()
    {
        WheelCollider[] all = new WheelCollider[frontWheels.Length + rearWheels.Length];
        frontWheels.CopyTo(all, 0);
        rearWheels.CopyTo(all, frontWheels.Length);
        return all;
    }

    private T GetOrAddComponent<T>() where T : Component
    {
        T comp = GetComponent<T>();
        return comp != null ? comp : gameObject.AddComponent<T>();
    }
}
