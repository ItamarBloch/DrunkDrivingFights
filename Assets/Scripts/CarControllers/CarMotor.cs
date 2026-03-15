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
    private float _driftFactor = 1f;

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
    }

    // ── Drift ────────────────────────────────────────────────

    private void HandleDriftFriction(bool drifting, float steerInput)
    {
        float smoothTime = 0.08f;

        if (drifting)
        {
            // Read the current value directly from the wheel (avoids overshoot from accumulated velocity)
            float currentValue = _driveWheels[0].forwardFriction.asymptoteValue;
            float velocity     = 0f;
            float target       = _driftFactor * _settings.driftFriction;
            float smoothed     = Mathf.SmoothDamp(currentValue, target, ref velocity, smoothTime);

            // All wheels get the smoothed low-grip value
            SetAllWheelsFrictionValues(smoothed);

            // Front wheels keep high grip so steering stays responsive
            SetWheelsFrictionValues(_frontWheels, 1.1f);

            // Update driftFactor from actual rear wheel slip — self-regulating
            UpdateDriftFactor(steerInput);
        }
        else
        {
            // Outside drift: speed-proportional grip, set directly
            float value = ((Mathf.Abs(SpeedKmh) * _settings.driftFriction) / 300f) + 1f;
            SetAllWheelsFrictionValues(value);
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
                _driveWheels[i].brakeTorque = 200f;
        }
        else
        {
            // Release the drift brake; normal brake/decel handled elsewhere
            for (int i = 0; i < _driveWheels.Length; i++)
                _driveWheels[i].brakeTorque = 0f;
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

            // Prevent near-zero driftFactor from killing all grip and causing a sudden snap
            _driftFactor = Mathf.Max(0.5f, _driftFactor);
        }
    }

    // ── Friction Helpers ─────────────────────────────────────

    private void SetAllWheelsFrictionValues(float value)
    {
        SetWheelsFrictionValues(_allWheels, value);
    }

    private static void SetWheelsFrictionValues(WheelCollider[] wheels, float value)
    {
        for (int i = 0; i < wheels.Length; i++)
        {
            WheelFrictionCurve s = wheels[i].sidewaysFriction;
            WheelFrictionCurve f = wheels[i].forwardFriction;
            s.extremumValue = s.asymptoteValue = value;
            f.extremumValue = f.asymptoteValue = value;
            wheels[i].sidewaysFriction = s;
            wheels[i].forwardFriction  = f;
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

    // ── Downforce ───────────────────────────────────────────

    private void ApplyDownforce()
    {
        float force = _settings.downforceCoefficient * _rb.linearVelocity.magnitude;
        _rb.AddForce(-transform.up * force, ForceMode.Force);
    }
}
