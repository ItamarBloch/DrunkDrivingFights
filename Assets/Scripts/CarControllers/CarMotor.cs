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
    private WheelCollider[] _driveWheels;
    private WheelCollider[] _allWheels;

    // ── Public State ────────────────────────────────────────

    /// <summary>Current speed in km/h (positive = forward).</summary>
    public float SpeedKmh { get; private set; }

    /// <summary>0 = stopped, 1 = at max forward speed.</summary>
    public float SpeedRatio { get; private set; }

    /// <summary>True when all drive wheels touch the ground.</summary>
    public bool IsGrounded { get; private set; }

    // ── Initialization ──────────────────────────────────────

    public void Initialize(CarSettings settings, Rigidbody rb,
                           WheelCollider[] driveWheels, WheelCollider[] allWheels)
    {
        _settings    = settings;
        _rb          = rb;
        _driveWheels = driveWheels;
        _allWheels   = allWheels;
    }

    // ── Per-Physics-Tick (called by CarController) ──────────

    public void Tick(CarInputData input)
    {
        UpdateSpeedMetrics();
        UpdateGroundedState();

        if (input.Brake)
        {
            ApplyBrake();
        }
        else if (Mathf.Abs(input.Throttle) > 0.01f)
        {
            ApplyMotorTorque(input.Throttle);
            ReleaseBrake();
        }
        else
        {
            ApplyPassiveDeceleration();
        }

        ApplyDownforce();
    }

    // ── Speed ───────────────────────────────────────────────

    private void UpdateSpeedMetrics()
    {
        float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
        SpeedKmh   = forwardSpeed * 3.6f;
        SpeedRatio  = Mathf.Clamp01(Mathf.Abs(SpeedKmh) / _settings.maxForwardSpeed);
    }

    private void UpdateGroundedState()
    {
        bool grounded = true;
        for (int i = 0; i < _driveWheels.Length; i++)
        {
            if (!_driveWheels[i].isGrounded)
            {
                grounded = false;
                break;
            }
        }
        IsGrounded = grounded;
    }

    // ── Motor Torque ────────────────────────────────────────

    private void ApplyMotorTorque(float throttle)
    {
        if (!CanAccelerate(throttle))
        {
            ClearMotorTorque();
            return;
        }

        float curveMod      = _settings.torqueCurve.Evaluate(SpeedRatio);
        float torquePerWheel = (_settings.motorTorque * throttle * curveMod) / _driveWheels.Length;

        for (int i = 0; i < _driveWheels.Length; i++)
        {
            _driveWheels[i].motorTorque = torquePerWheel;
        }
    }

    private bool CanAccelerate(float throttle)
    {
        if (throttle > 0f && SpeedKmh >= _settings.maxForwardSpeed)
            return false;
        if (throttle < 0f && SpeedKmh <= -_settings.maxReverseSpeed)
            return false;
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

    // ── Downforce ───────────────────────────────────────────

    private void ApplyDownforce()
    {
        float force = _settings.downforceCoefficient * _rb.linearVelocity.magnitude;
        _rb.AddForce(-transform.up * force, ForceMode.Force);
    }
}
