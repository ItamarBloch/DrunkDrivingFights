using UnityEngine;

/// <summary>
/// Pure physics simulation for car drivetrain.
/// Applies motor torque, braking, passive deceleration, speed clamping, and downforce.
///
/// This class has ZERO networking or input code — it receives a CarInputData
/// and applies forces. This means it runs identically on:
///   • Server (authoritative simulation)
///   • Owning client (client-side prediction)
/// </summary>
public class CarMotor : MonoBehaviour
{
    // ── Dependencies (set via Initialize) ───────────────────

    private CarSettings     _settings;
    private Rigidbody       _rb;
    private WheelCollider[] _frontWheels;
    private WheelCollider[] _driveWheels;
    private WheelCollider[] _allWheels;

    // ── Public State ────────────────────────────────────────

    /// <summary>Current speed in km/h (positive = forward).</summary>
    public float SpeedKmh { get; private set; }

    /// <summary>0 = stopped, 1 = at max forward speed.</summary>
    public float SpeedRatio { get; private set; }

    /// <summary>True when all drive wheels touch the ground.</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>True while the car is actively drifting.</summary>
    public bool IsDrifting { get; private set; }

    // ── Private State ───────────────────────────────────────

    private float _localForwardSpeed;
    private float _driftFactor           = 1f;
    private float _driftFrictionVelocity = 0f;

    // ── Initialization ──────────────────────────────────────

    public void Initialize(CarSettings settings, Rigidbody rb,
                           WheelCollider[] frontWheels,
                           WheelCollider[] driveWheels,
                           WheelCollider[] allWheels)
    {
        _settings    = settings;
        _rb          = rb;
        _frontWheels = frontWheels;
        _driveWheels = driveWheels;
        _allWheels   = allWheels;
    }

    // ── Per-Physics-Tick (called by CarController) ──────────

    public void Tick(CarInputData input)
    {
        UpdateSpeedMetrics();
        UpdateGroundedState();

        bool wantsDrift = input.Brake
                          && IsGrounded
                          && Mathf.Abs(SpeedKmh) >= _settings.driftMinSpeed
                          && Mathf.Abs(input.Steer) > 0.1f;

        IsDrifting = wantsDrift;

        HandleDriftFriction(wantsDrift, input.Steer);
        HandleDriftBrake(wantsDrift);

        if (!wantsDrift && !input.Brake && Mathf.Abs(input.Throttle) > 0.01f)
        {
            if (IsCounterDriving(input.Throttle))
                ApplyCounterThrust();
            else
                ApplyLaunchBoost(input.Throttle);

            ApplyMotorTorque(input.Throttle);
            ApplyDirectDriveForce(input.Throttle);
            ReleaseBrake();
        }
        else if (!wantsDrift && input.Brake)
        {
            ApplyBrake();
        }
        else if (!wantsDrift && Mathf.Abs(input.Throttle) <= 0.01f)
        {
            ApplyPassiveDeceleration();
        }
        else if (wantsDrift && Mathf.Abs(input.Throttle) > 0.01f)
        {
            ApplyMotorTorque(input.Throttle);
        }
        else if (wantsDrift)
        {
            ClearMotorTorque();
        }

        ApplyDownforce();
        ApplyLateralDamping(wantsDrift);
        ApplyYawDamping(input.Steer, wantsDrift);
        ApplyAntiRollBars();
    }

    // ── Drift ────────────────────────────────────────────────

    private void HandleDriftFriction(bool drifting, float steerInput)
    {
        // Smooth factor matches reference: .7f * Time.deltaTime → very fast convergence,
        // letting driftFactor (slip-based) drive the actual friction value each frame.
        float smoothTime = 0.7f * Time.fixedDeltaTime;

        if (drifting)
        {
            float currentValue = _driveWheels[0].sidewaysFriction.asymptoteValue;
            float target       = _driftFactor * _settings.driftFriction;
            float smoothed     = Mathf.SmoothDamp(currentValue, target, ref _driftFrictionVelocity, smoothTime);

            SetAllWheelsFrictionValues(smoothed);
            // Front wheels keep higher grip so steering stays responsive during drift
            SetWheelsFrictionValues(_frontWheels, 1.1f);

            UpdateDriftFactor(steerInput);
        }
        else
        {
            _driftFrictionVelocity = 0f;
            _driftFactor           = 1f;
            // Outside drift: speed-proportional grip
            float value = ((Mathf.Abs(SpeedKmh) * _settings.driftFriction) / 300f) + 1f;
            SetAllWheelsFrictionValues(value);
        }
    }

    /// <summary>
    /// Reads actual rear-wheel sideways slip and updates _driftFactor.
    /// More slip → higher factor → friction target rises → self-stabilising slide.
    /// </summary>
    private void UpdateDriftFactor(float steerInput)
    {
        for (int i = 0; i < _driveWheels.Length; i++)
        {
            if (!_driveWheels[i].GetGroundHit(out WheelHit hit)) continue;

            if (hit.sidewaysSlip < 0f)
                _driftFactor = (1f + (-steerInput)) * Mathf.Abs(hit.sidewaysSlip);
            else if (hit.sidewaysSlip > 0f)
                _driftFactor = (1f + steerInput)   * Mathf.Abs(hit.sidewaysSlip);

            _driftFactor = Mathf.Max(0.5f, _driftFactor);
        }
    }

    private void HandleDriftBrake(bool drifting)
    {
        if (drifting)
        {
            // Rear brake only — front stays free so the car can steer into the slide
            for (int i = 0; i < _allWheels.Length; i++)
                _allWheels[i].brakeTorque = 0f;
            for (int i = 0; i < _driveWheels.Length; i++)
                _driveWheels[i].brakeTorque = _settings.driftBrakeTorque;
        }
        else
        {
            // Release the drift brake; normal brake/decel handled elsewhere
            for (int i = 0; i < _driveWheels.Length; i++)
                _driveWheels[i].brakeTorque = 0f;
        }
    }

    // ── Friction Helpers ─────────────────────────────────────

    private void SetAllWheelsFrictionValues(float value)
    {
        SetWheelsFrictionValues(_allWheels, value);
    }

    private static void SetWheelsFrictionValues(WheelCollider[] wheels, float value)
    {
        // Only modify sidewaysFriction — forwardFriction controls acceleration/braking traction
        // and should not be changed. Mixing them causes broken drift + lost motor grip.
        for (int i = 0; i < wheels.Length; i++)
        {
            WheelFrictionCurve s = wheels[i].sidewaysFriction;
            s.extremumValue = s.asymptoteValue = value;
            wheels[i].sidewaysFriction = s;
        }
    }

    // ── Speed ───────────────────────────────────────────────

    private void UpdateSpeedMetrics()
    {
        _localForwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
        SpeedKmh  = _localForwardSpeed * 3.6f;
        SpeedRatio = Mathf.Clamp01(Mathf.Abs(SpeedKmh) / _settings.maxForwardSpeed);
    }

    private void UpdateGroundedState()
    {
        bool grounded = true;
        for (int i = 0; i < _driveWheels.Length; i++)
        {
            if (!_driveWheels[i].isGrounded) { grounded = false; break; }
        }
        IsGrounded = grounded;
    }

    // ── Motor Torque ────────────────────────────────────────

    private void ApplyMotorTorque(float throttle)
    {
        if (!CanAccelerate(throttle)) { ClearMotorTorque(); return; }

        float curveMod       = _settings.torqueCurve.Evaluate(SpeedRatio);
        float torquePerWheel = (_settings.motorTorque * throttle * curveMod) / _driveWheels.Length;

        for (int i = 0; i < _driveWheels.Length; i++)
            _driveWheels[i].motorTorque = torquePerWheel;
    }

    private bool CanAccelerate(float throttle)
    {
        if (throttle > 0f && SpeedKmh >= _settings.maxForwardSpeed)  return false;
        if (throttle < 0f && SpeedKmh <= -_settings.maxReverseSpeed) return false;
        return true;
    }

    private void ClearMotorTorque()
    {
        for (int i = 0; i < _driveWheels.Length; i++)
            _driveWheels[i].motorTorque = 0f;
    }

    // ── Braking ─────────────────────────────────────────────

    private void ApplyBrake()
    {
        ClearMotorTorque();
        for (int i = 0; i < _allWheels.Length; i++)
            _allWheels[i].brakeTorque = _settings.brakeTorque;
    }

    private void ReleaseBrake()
    {
        for (int i = 0; i < _allWheels.Length; i++)
            _allWheels[i].brakeTorque = 0f;
    }

    private void ApplyPassiveDeceleration()
    {
        ClearMotorTorque();
        for (int i = 0; i < _allWheels.Length; i++)
            _allWheels[i].brakeTorque = _settings.decelerationTorque;
    }

    // ── Counter-Thrust & Launch Boost ───────────────────────

    private bool IsCounterDriving(float throttle)
    {
        const float threshold = 1f;
        if (throttle > 0.1f  && _localForwardSpeed < -threshold) return true;
        if (throttle < -0.1f && _localForwardSpeed >  threshold) return true;
        return false;
    }

    private void ApplyCounterThrust()
    {
        Vector3 opposing = -_rb.linearVelocity.normalized * _settings.counterThrustForce;
        _rb.AddForce(opposing, ForceMode.Acceleration);
    }

    private void ApplyLaunchBoost(float throttle)
    {
        if (!IsGrounded || Mathf.Abs(SpeedKmh) >= _settings.launchBoostMaxSpeed) return;
        Vector3 dir = throttle > 0f ? transform.forward : -transform.forward;
        _rb.AddForce(dir * _settings.launchBoostForce, ForceMode.Acceleration);
    }

    /// <summary>
    /// Direct Rigidbody forward push that fills the gap between wheel torque and max speed.
    /// WheelCollider torque alone reaches terminal velocity well below maxForwardSpeed due to
    /// rolling friction and drag. This force scales to zero as we approach max speed.
    /// </summary>
    private void ApplyDirectDriveForce(float throttle)
    {
        if (!IsGrounded) return;
        // Apply constant push until the speed cap — don't taper, just cut off at max.
        // CanAccelerate() in ApplyMotorTorque already guards the ceiling.
        if (throttle > 0f && SpeedKmh >= _settings.maxForwardSpeed)  return;
        if (throttle < 0f && SpeedKmh <= -_settings.maxReverseSpeed) return;
        _rb.AddForce(transform.forward * (throttle * _settings.directDriveForce), ForceMode.Acceleration);
    }

    // ── Lateral Damping & Yaw Control ───────────────────────

    /// <summary>
    /// GTA/arcade technique: manually dampen sideways velocity every tick.
    /// WheelCollider physics alone can't produce tight arcade-style grip.
    /// factor = 0.92 means 92% of lateral speed is kept → decays to ~1.5% in 1 second.
    /// During drift, factor is higher (less removal) to allow the slide.
    /// </summary>
    private void ApplyLateralDamping(bool isDrifting)
    {
        if (!IsGrounded) return;
        float lateralSpeed = Vector3.Dot(_rb.linearVelocity, transform.right);
        float factor       = isDrifting ? _settings.driftLateralDamping : _settings.normalLateralDamping;
        _rb.linearVelocity -= transform.right * lateralSpeed * (1f - factor);
    }

    /// <summary>
    /// Fixes the "stuck at angle" problem.
    /// When you counter-steer or release the stick, the car's angular velocity
    /// doesn't decay fast enough via physics alone (default angularDrag = 0.05).
    /// This directly lerps yaw toward 0 when not actively turning.
    /// During drift: clamps yaw to maxDriftYawRate to prevent spinout.
    /// </summary>
    private void ApplyYawDamping(float steerInput, bool isDrifting)
    {
        float yaw = Vector3.Dot(_rb.angularVelocity, transform.up);

        if (isDrifting)
        {
            // Cap yaw rate during drift — prevents uncontrolled spinout
            float clampedYaw = Mathf.Clamp(yaw, -_settings.maxDriftYawRate, _settings.maxDriftYawRate);
            _rb.angularVelocity += transform.up * (clampedYaw - yaw);
        }
        else
        {
            // Damp yaw when releasing steer or actively counter-steering
            bool counterSteering = steerInput * yaw < -0.01f;
            bool noInput         = Mathf.Abs(steerInput) < 0.05f;

            if (counterSteering || noInput)
            {
                float newYaw = Mathf.Lerp(yaw, 0f, _settings.yawDampingStrength * Time.fixedDeltaTime);
                _rb.angularVelocity += transform.up * (newYaw - yaw);
            }
        }
    }

    // ── Anti-Roll Bar ────────────────────────────────────────

    /// <summary>
    /// Simulates a physical anti-roll bar (stabilizer bar) on both axles.
    /// Measures the difference in suspension travel between left and right wheels.
    /// - Turning on flat ground: one side compresses more → big difference → corrective force.
    /// - Both wheels hitting a bump together: travel is equal → difference = 0 → springs work freely.
    /// - Single-wheel bump: some corrective force, but the spring still absorbs most of the impact.
    /// </summary>
    private void ApplyAntiRollBars()
    {
        if (_settings.antiRollStiffness <= 0f) return;
        ApplyAntiRollBar(_frontWheels[0], _frontWheels[1]);
        ApplyAntiRollBar(_driveWheels[0], _driveWheels[1]);
    }

    private void ApplyAntiRollBar(WheelCollider left, WheelCollider right)
    {
        float travelL = GetSuspensionTravel(left);
        float travelR = GetSuspensionTravel(right);

        float antiRollForce = (travelL - travelR) * _settings.antiRollStiffness;

        if (left.isGrounded)
            _rb.AddForceAtPosition(left.transform.up * -antiRollForce, left.transform.position);
        if (right.isGrounded)
            _rb.AddForceAtPosition(right.transform.up *  antiRollForce, right.transform.position);
    }

    /// <summary>
    /// Returns normalised suspension travel: 0 = fully compressed, 1 = fully extended.
    /// Returns 1 when the wheel is in the air (treat as fully extended).
    /// </summary>
    private static float GetSuspensionTravel(WheelCollider wheel)
    {
        if (!wheel.GetGroundHit(out WheelHit hit)) return 1f;
        return (-wheel.transform.InverseTransformPoint(hit.point).y - wheel.radius)
               / wheel.suspensionDistance;
    }

    // ── Downforce ───────────────────────────────────────────

    private void ApplyDownforce()
    {
        float force = _settings.downforceCoefficient * _rb.linearVelocity.magnitude;
        _rb.AddForce(-transform.up * force, ForceMode.Force);
    }
}
