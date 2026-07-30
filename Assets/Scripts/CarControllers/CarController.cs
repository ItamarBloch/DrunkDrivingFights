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
    [Tooltip("How many FixedUpdate ticks between server state broadcasts. 1 = every tick (~50Hz), " +
             "matching how Rocket League sends ~60Hz. Affordable now that the state packet is slim.")]
    [SerializeField] private int networkSendRate = 1;

    [Tooltip("Hard-snap only when divergence exceeds this (metres). Kept large so normal play never " +
             "snaps — only genuine teleports/respawns do. Everything smaller eases in smoothly.")]
    [SerializeField] private float correctionSnapThreshold = 8f;

    [Tooltip("How fast the owner bleeds off a measured prediction error (higher = faster, snappier).")]
    [SerializeField] private float correctionLerpSpeed = 10f;

    [Tooltip("Positional divergence below this (metres) is ignored — absorbs normal RTT/jitter noise without correcting.")]
    [SerializeField] private float positionDeadzone = 0.25f;

    [Tooltip("Rotational divergence below this (degrees) is ignored.")]
    [SerializeField] private float rotationDeadzoneDeg = 2f;

    [Tooltip("Seconds the owner stays client-AUTHORITATIVE after it was last 'unstable' — i.e. " +
             "touching a wall/obstacle OR airborne. In those states client and server physics " +
             "diverge fast; while authoritative the owner reports its own state and the server " +
             "follows it, so a mispredicted collision can't explode and air control isn't fought. " +
             "Stable grounded driving stays server-authoritative and is unaffected.")]
    [SerializeField] private float clientAuthGrace = 0.25f;

    // ── Subsystems ──────────────────────────────────────────

    private CarInputHandler _inputHandler;
    private CarMotor _motor;
    private CarSteering _steering;
    private CarAirControl _airControl;
    private CarWheelVisuals _wheelVisuals;
    private CarRemoteInterpolator _interpolator;
    private Rigidbody _rb;

    // ── Network State ───────────────────────────────────────

    private CarInputData _localInput;
    private int _tickCounter;

    // Server: the most recent non-stale input received from the owning client.
    // Applied immediately each tick (and naturally repeated if no newer input
    // arrived). We deliberately do NOT queue/buffer inputs: the owner predicts
    // locally and immediately, so the server has to stay in phase by applying the
    // newest input the moment it lands. A buffer added a phase delay that dragged
    // the owner's prediction backward and made steering feel unresponsive.
    private CarInputData _serverInput;
    private uint _lastInputSeq;
    private bool _hasReceivedInput;

    // Server: sequence of the input whose physics result the most recently CAPTURED
    // state reflects. Unity steps physics AFTER FixedUpdate, so the rigidbody pose we
    // read in BroadcastState reflects the input applied LAST tick — hence we stamp the
    // snapshot with the sequence recorded last tick (updated at the end of ServerTick).
    // This is the reconciliation key the owner client matches against (see RpcReceiveState).
    private uint _lastAppliedSeq;

    // Owner's outgoing input sequence counter — lets the server drop stale/reordered
    // inputs that the unreliable channel can deliver late.
    private uint _inputSequence;

    // Timestamp of the most recent state snapshot we applied. Used to reject
    // stale / out-of-order snapshots — the state RPC rides the unreliable
    // channel, which reorders packets over a real relay link.
    private double _lastStateTimestamp = double.MinValue;

    // Cap on how far we extrapolate a snapshot forward (seconds). Guards against
    // a bad clock or a huge hitch projecting the car miles away.
    private const float MaxExtrapolation = 0.5f;

    // ── Owner Prediction History ────────────────────────────
    // Ring buffer of the poses our local prediction passed through, each tagged with
    // the INPUT SEQUENCE that produced it. When a server snapshot arrives carrying
    // "this pose is the result of your input #N", we compare it to where WE predicted
    // we were after that SAME input #N. Because both sides reflect identical inputs,
    // the input-in-flight lead cancels EXACTLY and only genuine physics divergence is
    // corrected. (Keying on wall-clock time, as before, secretly compared different
    // input sets — the server hadn't received our latest input yet — leaving a
    // persistent phantom error that felt like ~RTT of input lag.)
    private struct PredictedPose
    {
        public uint       Sequence;
        public Vector3    Position;
        public Quaternion Rotation;
    }

    private const int HistoryCapacity = 64; // ~1.3s at 50Hz — covers any relay RTT
    private readonly PredictedPose[] _history = new PredictedPose[HistoryCapacity];
    private int _historyCount;
    private int _historyHead; // index of next write

    // Remaining error to bleed into the body over the next few ticks (set on
    // reconcile, decayed every OwnerPredictionTick). Smooth, not instantaneous.
    private Vector3    _posError;
    private Quaternion _rotError = Quaternion.identity;

    // ── Wall-Contact Client Authority ───────────────────────
    // The owner-client goes AUTHORITATIVE while its body is touching geometry (detected
    // locally via OnCollision — instant, no RTT). While authoritative it reports its own
    // pose and ignores server corrections; the server just holds+relays that pose. This
    // is what stops a mispredicted wall hit from being "corrected" into an explosion, and
    // because the server is holding OUR pose the hand-back is seamless. Body colliders are
    // frictionless and the ground rides on raycast WheelColliders (no collision events),
    // so any OnCollision is an obstacle hit, never the ground.

    // Owner-client: > 0 while authoritative (refreshed each contact tick, then counts down).
    private float _ownerAuthTimer;
    private bool OwnerAuthActive => _ownerAuthTimer > 0f;

    // Server copy: latest client-authoritative state + a grace fallback. Superseded the
    // instant a normal input arrives (see CmdSendInput), which is the real hand-back signal.
    private Vector3    _authPos, _authVel, _authAngVel;
    private Quaternion _authRot = Quaternion.identity;
    private uint       _authSeq;
    private float      _serverAuthTimer;
    private bool ServerAuthActive => _serverAuthTimer > 0f;

    // ── Death / Freeze State ────────────────────────────────

    private bool _isDead;

    [SyncVar] private bool _isFrozen;

    /// <summary>Server: freeze or unfreeze this car's input processing.</summary>
    [Server] public void SetFrozen(bool frozen) => _isFrozen = frozen;

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
        // Throttle this component's SyncVars (SyncedSpeedKmh, _isFrozen) to ~10Hz.
        // SyncedSpeedKmh was being dirtied every physics tick and synced at the full
        // 60Hz over the reliable channel — a constant per-frame trickle that adds to
        // relay congestion. 10Hz is plenty: the speedometer and engine audio both
        // lerp toward the value every frame, and _isFrozen changes only at match
        // start/end. The car's POSITION state is a separate unreliable RPC and is
        // unaffected by this.
        syncInterval = 0.1f;

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
        // Cars spawn AFTER MatchManager.OnStartServer, so FreezeAllPlayers misses them.
        // Start frozen ourselves if the match hasn't begun yet.
        if (MatchManager.singleton != null && MatchManager.singleton.State != MatchState.InProgress)
            _isFrozen = true;
    }

    private void Update()
    {
        if (!isOwned) return;
        _inputHandler.Poll();
        _localInput = (_isDead || _isFrozen) ? new CarInputData() : _inputHandler.CurrentInput;
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

    private static CarInputData ToAerialInput(CarInputData src) => new CarInputData
    {
        Throttle = src.AerialThrottle,
        Steer    = src.AerialSteer,
        Brake    = false
    };

    private void HostTick()
    {
        _motor.Tick(_localInput);
        _airControl.Tick(ToAerialInput(_localInput));
        float steer = _motor.IsDrifting ? _localInput.Steer * settings.driftSteerMultiplier : _localInput.Steer;
        _steering.Tick(steer, _motor.SpeedKmh);

        // Only re-dirty the SyncVar on a meaningful change so steady cruising /
        // idling sends nothing; combined with the 10Hz syncInterval this keeps the
        // speed sync off the per-frame reliable path.
        float speedNow = _motor.SpeedKmh;
        if (Mathf.Abs(SyncedSpeedKmh - speedNow) >= 1f) SyncedSpeedKmh = speedNow;

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
        if (ServerAuthActive)
        {
            _serverAuthTimer -= Time.fixedDeltaTime;

            // Client is authoritative (wall contact). Hold the body at the client-reported
            // pose so hit queries (rockets, kill zones) stay correct, and relay that exact
            // pose to remotes. Run NO local physics — the divergent server sim is precisely
            // what used to fight the client and explode. We broadcast the clean _auth values
            // (not the rigidbody) so any sub-tick suspension jitter never reaches remotes.
            _rb.position        = _authPos;
            _rb.rotation        = _authRot;
            _rb.linearVelocity  = _authVel;
            _rb.angularVelocity = _authAngVel;
            _lastAppliedSeq     = _authSeq;

            _tickCounter++;
            if (_tickCounter >= networkSendRate)
            {
                _tickCounter = 0;
                RpcReceiveState(new CarNetworkState
                {
                    Position              = _authPos,
                    Rotation              = _authRot,
                    Velocity              = _authVel,
                    Timestamp             = NetworkTime.time,
                    LastProcessedInputSeq = _authSeq
                });
            }
            return;
        }

        CarInputData input = _serverInput;

        _motor.Tick(input);
        _airControl.Tick(ToAerialInput(input));
        float steer = _motor.IsDrifting ? input.Steer * settings.driftSteerMultiplier : input.Steer;
        _steering.Tick(steer, _motor.SpeedKmh);

        // Only re-dirty the SyncVar on a meaningful change so steady cruising /
        // idling sends nothing; combined with the 10Hz syncInterval this keeps the
        // speed sync off the per-frame reliable path.
        float speedNow = _motor.SpeedKmh;
        if (Mathf.Abs(SyncedSpeedKmh - speedNow) >= 1f) SyncedSpeedKmh = speedNow;

        _tickCounter++;
        if (_tickCounter >= networkSendRate)
        {
            _tickCounter = 0;
            BroadcastState();
        }

        // Physics steps AFTER this method returns, so the pose we capture NEXT tick
        // will reflect the input we just applied here. Record its sequence now so the
        // next snapshot is stamped correctly for owner reconciliation.
        _lastAppliedSeq = input.Sequence;
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
            Position              = _rb.position,
            Rotation              = _rb.rotation,
            Velocity              = _rb.linearVelocity,
            Timestamp             = NetworkTime.time,
            LastProcessedInputSeq = _lastAppliedSeq
        };
    }

    // ════════════════════════════════════════════════════════
    //  OWNER CLIENT — Prediction & Reconciliation
    // ════════════════════════════════════════════════════════

    private void OwnerPredictionTick()
    {
        if (_ownerAuthTimer > 0f) _ownerAuthTimer -= Time.fixedDeltaTime;

        // Airborne is "unstable" too (air control diverges fast with nothing to anchor it),
        // so stay client-authoritative while off the ground. Uses last tick's airborne flag;
        // the grace window covers the landing settle before handing back to the server.
        if (_airControl.IsAirborne) RefreshClientAuth();

        // Record where prediction currently sits BEFORE this step integrates. In normal
        // mode we then bleed off any outstanding correction (ApplyOwnerCorrection also
        // shifts the freshly-recorded entry so history stays in the corrected frame).
        RecordPredictedPose();

        // Stamp one sequence per FixedUpdate (shared by input and auth-state messages so
        // the server's stale-packet guard works across the mode boundary).
        _localInput.Sequence = ++_inputSequence;

        if (OwnerAuthActive)
        {
            // Wall contact: WE are authoritative. Do NOT apply server corrections (that is
            // what shoved the body into the wall). Report our own pose so the server and all
            // remotes follow us — making the eventual hand-back seamless.
            CmdSendAuthState(_rb.position, _rb.rotation, _rb.linearVelocity, _rb.angularVelocity, _localInput.Sequence);
        }
        else
        {
            ApplyOwnerCorrection();
            CmdSendInput(_localInput);
        }

        _motor.Tick(_localInput);
        _airControl.Tick(ToAerialInput(_localInput));
        float steer = _motor.IsDrifting ? _localInput.Steer * settings.driftSteerMultiplier : _localInput.Steer;
        _steering.Tick(steer, _motor.SpeedKmh);

        // Local-only write: keeps the owner's speedometer/engine audio accurate
        // every frame regardless of how often the throttled server value arrives.
        // (On a non-host client this just overwrites the field locally; the server
        // remains the authority and re-syncs it at the throttled rate.)
        SyncedSpeedKmh = _motor.SpeedKmh;
    }

    /// <summary>
    /// Reconcile a server snapshot against our OWN prediction history, keyed by input
    /// sequence: we compare the snapshot to where we predicted we'd be after the SAME
    /// input the server says it last applied (serverState.LastProcessedInputSeq). Same
    /// inputs on both sides → the input-in-flight lead cancels exactly and only genuine
    /// physics divergence remains. Small divergence → ignored. Moderate → eased in
    /// smoothly. Large → hard snap (real desync / teleport).
    /// </summary>
    private void OwnerReconcile(CarNetworkState serverState)
    {
        if (!TryGetPredictedPoseForSeq(serverState.LastProcessedInputSeq, out Vector3 predPos, out Quaternion predRot))
        {
            // Sequence aged out of the ring (or no history yet) — seed directly.
            SnapToServer(serverState);
            return;
        }

        Vector3 posError = serverState.Position - predPos;
        float errMag = posError.magnitude;

        if (errMag > correctionSnapThreshold)
        {
            SnapToServer(serverState);
            return;
        }

        // Replace (not accumulate) the smoothing target: because history is kept
        // in the corrected frame, each snapshot already reflects what we've fixed.
        _posError = errMag > positionDeadzone ? posError : Vector3.zero;

        Quaternion rotError = serverState.Rotation * Quaternion.Inverse(predRot);
        rotError.ToAngleAxis(out float angleDeg, out _);
        if (angleDeg > 180f) angleDeg -= 360f;
        _rotError = Mathf.Abs(angleDeg) > rotationDeadzoneDeg ? rotError : Quaternion.identity;
    }

    /// <summary>
    /// Hard-snap the body to authoritative state; invalidates prediction history.
    /// Reserved for genuine teleports/respawns (error > correctionSnapThreshold) or
    /// seeding before any history exists — never normal play. Angular velocity is
    /// zeroed rather than forced from the snapshot: forcing a stale spin onto a
    /// wheeled body was what flipped/knocked the car up on a correction.
    /// </summary>
    private void SnapToServer(CarNetworkState serverState)
    {
        _rb.position        = serverState.Position;
        _rb.rotation        = serverState.Rotation;
        _rb.linearVelocity  = serverState.Velocity;
        _rb.angularVelocity = Vector3.zero;

        _posError     = Vector3.zero;
        _rotError     = Quaternion.identity;
        _historyCount = 0;
        _historyHead  = 0;
    }

    /// <summary>Bleed a fraction of the outstanding error into the body and keep history consistent.</summary>
    private void ApplyOwnerCorrection()
    {
        float t = Mathf.Clamp01(correctionLerpSpeed * Time.fixedDeltaTime);

        Vector3 posStep = Vector3.zero;
        if (_posError.sqrMagnitude > 1e-8f)
        {
            posStep = _posError * t;
            _rb.position += posStep;
            _posError -= posStep;
        }
        else
        {
            _posError = Vector3.zero;
        }

        Quaternion rotStep = Quaternion.Slerp(Quaternion.identity, _rotError, t);
        _rb.rotation = rotStep * _rb.rotation;
        _rotError = Quaternion.Inverse(rotStep) * _rotError;

        ShiftHistory(posStep, rotStep);
    }

    // ── Prediction History Buffer ───────────────────────────

    private void RecordPredictedPose()
    {
        // Called at the top of OwnerPredictionTick, BEFORE this tick's input is stamped
        // and applied. So the rigidbody pose we read here is the result of LAST tick's
        // input — whose sequence is the current _inputSequence value (not yet ++'d).
        // That matches how the server labels its captured pose (see _lastAppliedSeq).
        _history[_historyHead] = new PredictedPose
        {
            Sequence = _inputSequence,
            Position = _rb.position,
            Rotation = _rb.rotation
        };
        _historyHead = (_historyHead + 1) % HistoryCapacity;
        if (_historyCount < HistoryCapacity) _historyCount++;
    }

    private PredictedPose GetHistory(int logicalIndex)
    {
        int idx = (_historyHead - _historyCount + logicalIndex + HistoryCapacity * 2) % HistoryCapacity;
        return _history[idx];
    }

    /// <summary>
    /// Fetch the predicted pose we held right after applying owner input <paramref name="seq"/>.
    /// Sequences are contiguous (one per tick), so an exact match is the norm. Returns
    /// false when the sequence has already aged out of the ring (huge lag spike) or no
    /// history exists yet — the caller treats that as "reseed from the snapshot".
    /// </summary>
    private bool TryGetPredictedPoseForSeq(uint seq, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        if (_historyCount == 0) return false;

        for (int i = 0; i < _historyCount; i++)
        {
            PredictedPose p = GetHistory(i);
            if (p.Sequence == seq)
            {
                pos = p.Position;
                rot = p.Rotation;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Shift every stored pose by the correction we just applied to the body, so
    /// the history mirrors the corrected path. Without this, the next snapshot
    /// would re-measure error we already removed and over-correct (jitter).
    /// </summary>
    private void ShiftHistory(Vector3 posDelta, Quaternion rotDelta)
    {
        bool hasPos = posDelta.sqrMagnitude > 1e-10f;
        bool hasRot = Quaternion.Angle(rotDelta, Quaternion.identity) > 1e-4f;
        if (!hasPos && !hasRot) return;

        for (int i = 0; i < _historyCount; i++)
        {
            int idx = (_historyHead - _historyCount + i + HistoryCapacity * 2) % HistoryCapacity;
            if (hasPos) _history[idx].Position += posDelta;
            if (hasRot) _history[idx].Rotation = rotDelta * _history[idx].Rotation;
        }
    }

    // ════════════════════════════════════════════════════════
    //  CLIENT AUTHORITY (owner-client, while "unstable")
    // ════════════════════════════════════════════════════════
    // The owner goes authoritative whenever it is NOT stably grounded — touching a
    // wall/obstacle, or airborne — because that is exactly when client and server physics
    // diverge fast. Collisions are caught via OnCollision* (child body colliders forward
    // their callbacks here); the airborne case is refreshed each tick in OwnerPredictionTick.
    // Only the owner-client switches — the host is already authoritative locally, and the
    // server copy switches only when it receives CmdSendAuthState.

    private void OnCollisionEnter(Collision collision) => RefreshClientAuth();
    private void OnCollisionStay(Collision collision)  => RefreshClientAuth();

    /// <summary>Refresh the owner's client-authoritative window. Safe to call every tick.</summary>
    private void RefreshClientAuth()
    {
        if (!isOwned || isServer) return;   // owner-client only (never host or dedicated server)

        if (!OwnerAuthActive)
        {
            // Entering authority: drop any pending server correction so it can't yank us on the
            // way in. RpcReceiveState won't set a new one while we're authoritative.
            _posError = Vector3.zero;
            _rotError = Quaternion.identity;
        }
        _ownerAuthTimer = clientAuthGrace;
    }

    // ════════════════════════════════════════════════════════
    //  NETWORK MESSAGES
    // ════════════════════════════════════════════════════════

    [Command(channel = Channels.Unreliable)]
    private void CmdSendInput(CarInputData input)
    {
        // Reject stale/duplicate inputs the unreliable channel delivered out of order,
        // then apply immediately. _serverInput persists, so if no newer input arrives
        // the server simply reuses the last one next tick — no buffering, no delay.
        if (_hasReceivedInput && input.Sequence <= _lastInputSeq) return;
        _lastInputSeq = input.Sequence;
        _hasReceivedInput = true;
        _serverInput = input;

        // A normal input means the client has left wall-auth mode → resume simulating from
        // the pose we were holding (which is where the client actually is), so there is no
        // gap to snap. This is the primary hand-back trigger; _serverAuthTimer is only a
        // fallback in case the resuming input is lost.
        _serverAuthTimer = 0f;
    }

    /// <summary>
    /// Owner → server while the owner is wall-contact authoritative. The server adopts this
    /// pose as truth (holds + relays it) instead of running its own divergent simulation.
    /// </summary>
    [Command(channel = Channels.Unreliable)]
    private void CmdSendAuthState(Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel, uint seq)
    {
        // Same monotonic sequence as input, so a late/reordered auth packet can't overwrite
        // a newer one (and can't override a fresher normal input after hand-back).
        if (_hasReceivedInput && seq <= _lastInputSeq) return;
        _lastInputSeq     = seq;
        _hasReceivedInput = true;

        _authPos    = pos;
        _authRot    = rot;
        _authVel    = vel;
        _authAngVel = angVel;
        _authSeq    = seq;

        // Fallback only — the real hand-back is the next normal input (see CmdSendInput).
        // Kept longer than the owner's send window so we never resume mid-contact.
        _serverAuthTimer = clientAuthGrace + 0.25f;
    }

    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcReceiveState(CarNetworkState state)
    {
        if (isServer) return;

        // Reject stale / out-of-order snapshots. Without this an older snapshot
        // arriving late (common on the unreliable channel over relay) yanks the
        // car backward → the tremor.
        if (state.Timestamp <= _lastStateTimestamp) return;
        _lastStateTimestamp = state.Timestamp;

        if (isOwned)
        {
            // While wall-contact authoritative, ignore server corrections entirely — WE are
            // the source of truth and the server is only echoing our own pose back.
            if (OwnerAuthActive) return;

            // Owner: reconcile against same-time prediction history. No forward
            // extrapolation here — we don't want to project the snapshot to "now";
            // OwnerReconcile compares it to where WE were at its own timestamp.
            OwnerReconcile(state);
        }
        else
        {
            // Remote: project the (stale) snapshot forward to "now" using its own
            // velocity so the interpolator chases the present, not the past.
            float age = Mathf.Clamp((float)(NetworkTime.time - state.Timestamp), 0f, MaxExtrapolation);
            state.Position += state.Velocity * age;
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
        _airControl = GetOrAddComponent<CarAirControl>();
        _wheelVisuals = GetOrAddComponent<CarWheelVisuals>();
        _interpolator = GetOrAddComponent<CarRemoteInterpolator>();
    }

    private void ConfigureRigidbody()
    {
        _rb.centerOfMass = centerOfMassOffset;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Clamp depenetration velocity so colliding with concave surfaces (e.g. ramp underside)
        // doesn't apply infinite separation impulse and launch the car.
        _rb.maxDepenetrationVelocity = 2f;
        // Sweep-test the body collider against static geometry each frame so fast-moving
        // cars don't tunnel through thin surfaces (e.g. ramp underside) between frames.
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        ApplyBodyColliderFriction();
    }

    /// <summary>
    /// Makes every non-wheel collider on the car body frictionless so it slides
    /// over curb edges instead of snagging. WheelColliders handle all traction
    /// through their own friction curves and are left untouched.
    /// </summary>
    private void ApplyBodyColliderFriction()
    {
        var mat = new PhysicsMaterial("CarBody_Frictionless")
        {
            dynamicFriction = 0f,
            staticFriction  = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness      = 0f,
            bounceCombine   = PhysicsMaterialCombine.Minimum
        };

        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            if (col is WheelCollider) continue;
            col.material = mat;
        }
    }

    private void InitializeSubsystems()
    {
        WheelCollider[] allWheels = BuildAllWheelsArray();

        _motor.Initialize(settings, _rb, frontWheels, rearWheels, allWheels);
        _steering.Initialize(settings, frontWheels);
        _airControl.Initialize(settings, _rb, allWheels);
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
