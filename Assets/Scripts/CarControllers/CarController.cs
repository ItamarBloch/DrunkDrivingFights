using Mirror;
using UnityEngine;

/// <summary>
/// Networked car controller using Mirror.
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

    /// <summary>Latest input from the owning client (lives on server).</summary>
    private CarInputData _serverInput;

    /// <summary>Latest input polled locally (lives on owning client).</summary>
    private CarInputData _localInput;

    /// <summary>Tick counter for throttling network sends.</summary>
    private int _tickCounter;

    // ── Public Accessors ────────────────────────────────────

    public CarMotor Motor => _motor;
    public CarSteering Steering => _steering;
    public CarSettings Settings => settings;
    public Rigidbody Body => _rb;

    /// <summary>Current speed in km/h (available on all clients).</summary>
    [SyncVar] public float SyncedSpeedKmh;

    // ════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════

    private void Awake()
    {
        CacheAndCreateComponents();
        ConfigureRigidbody();
        InitializeSubsystems();
    }

    public override void OnStartAuthority()
    {
        // Only the owning player reads input.
        enabled = true;
    }

    public override void OnStartServer()
    {
        enabled = true;
    }

    /// <summary>
    /// Polled every frame on the owning client to capture input
    /// at screen refresh rate (smoother than FixedUpdate polling).
    /// </summary>
    private void Update()
    {
        if (!isOwned) return;
        _inputHandler.Poll();
        _localInput = _inputHandler.CurrentInput;
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
            // Host: we ARE the server and the owner. Use local input directly —
            // no need to send a Command to ourselves.
            HostTick();
        }
        else if (amServer)
        {
            // Dedicated server: use input received from the owning client.
            ServerTick();
        }
        else if (amOwner)
        {
            // Remote owner: predict locally for responsiveness,
            // send input to server for authoritative sim.
            OwnerPredictionTick();
        }
        // Remote clients: do nothing here. CarRemoteInterpolator handles visuals.
    }

    // ════════════════════════════════════════════════════════
    //  HOST — Server + Owner on the same machine
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Host is both server and owner. Uses local input directly
    /// for the authoritative simulation — no Command round-trip needed.
    /// </summary>
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

    /// <summary>
    /// Dedicated server: runs the authoritative physics simulation
    /// using input received from the owning client via CmdSendInput.
    /// </summary>
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

    /// <summary>
    /// Builds a state snapshot and sends it to all clients.
    /// </summary>
    private void BroadcastState()
    {
        CarNetworkState state = CaptureCurrentState();
        RpcReceiveState(state);
    }

    /// <summary>
    /// Captures the current rigidbody + steering state into a snapshot.
    /// </summary>
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

    /// <summary>
    /// The owning client runs physics locally for instant responsiveness.
    /// Also sends input to the server every tick.
    /// </summary>
    private void OwnerPredictionTick()
    {
        // Send our input to the server.
        CmdSendInput(_localInput);

        // Run the same simulation locally (prediction).
        _motor.Tick(_localInput);
        _steering.Tick(_localInput.Steer, _motor.SpeedRatio);
    }

    /// <summary>
    /// When the owner receives server state, check if our prediction diverged.
    /// If so, smoothly correct (or snap if way off).
    /// </summary>
    private void OwnerReconcile(CarNetworkState serverState)
    {
        float posError = Vector3.Distance(_rb.position, serverState.Position);

        if (posError > correctionSnapThreshold)
        {
            // Too far off — hard snap.
            _rb.position = serverState.Position;
            _rb.rotation = serverState.Rotation;
            _rb.linearVelocity = serverState.Velocity;
            _rb.angularVelocity = serverState.AngularVelocity;
        }
        else if (posError > 0.05f)
        {
            // Gently nudge toward server truth.
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
        // If within 0.05m — prediction is close enough, do nothing.
    }

    // ════════════════════════════════════════════════════════
    //  NETWORK MESSAGES
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// [Client → Server] Sends the owner's input to the server for
    /// authoritative simulation.
    /// </summary>
    [Command(channel = Channels.Unreliable)]
    private void CmdSendInput(CarInputData input)
    {
        _serverInput = input;
    }

    /// <summary>
    /// [Server → All Clients] Broadcasts the authoritative car state.
    /// Owner uses it for reconciliation, remotes use it for interpolation.
    /// </summary>
    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcReceiveState(CarNetworkState state)
    {
        if (isServer) return; // Server already has the truth.

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