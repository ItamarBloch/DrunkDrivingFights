using UnityEngine;

/// <summary>
/// Pure simulation for steering using Ackerman geometry.
/// Inner wheel turns sharper than outer; max angle naturally decreases with speed.
/// No networking, no input reading — just math and WheelCollider writes.
/// </summary>
public class CarSteering : MonoBehaviour
{
    // ── Dependencies (set via Initialize) ───────────────────

    private CarSettings     _settings;
    private WheelCollider[] _steerWheels; // [0] = FL, [1] = FR

    // ── State ───────────────────────────────────────────────

    private float _currentSteerAngle; // representative outer-wheel angle, used for network state

    /// <summary>Current outer-wheel steer angle (degrees, signed). Used for network state.</summary>
    public float CurrentSteerAngle => _currentSteerAngle;

    // ── Initialization ──────────────────────────────────────

    public void Initialize(CarSettings settings, WheelCollider[] steerWheels)
    {
        _settings    = settings;
        _steerWheels = steerWheels;
    }

    // ── Per-Physics-Tick (called by CarController) ──────────

    /// <param name="steerInput">-1 to 1, raw or drift-boosted.</param>
    /// <param name="speedKmh">Current forward speed in km/h for speed-sensitive angle.</param>
    public void Tick(float steerInput, float speedKmh)
    {
        float targetAngle = ComputeOuterAngle(steerInput, speedKmh);
        _currentSteerAngle = SmoothSteerAngle(_currentSteerAngle, targetAngle, steerInput);
        ApplyAckermanAngles(_currentSteerAngle, speedKmh);
    }

    // ── Ackerman Geometry ────────────────────────────────────

    /// <summary>
    /// Outer wheel target angle using Ackerman formula + speed-sensitive radius.
    /// radius = turnRadius + speedKmh/20 → as speed rises, radius grows → angle shrinks.
    /// </summary>
    private float ComputeOuterAngle(float steerInput, float speedKmh)
    {
        float radius = _settings.turnRadius + speedKmh / 20f;
        float half   = _settings.rearTrack / 2f;
        return Mathf.Rad2Deg * Mathf.Atan(_settings.wheelBase / (radius + half)) * steerInput;
    }

    /// <summary>
    /// Applies Ackerman-differentiated angles to both front wheels.
    /// Given the smoothed outer angle, scales the inner wheel proportionally.
    /// FL = wheel[0], FR = wheel[1].
    /// </summary>
    private void ApplyAckermanAngles(float outerAngle, float speedKmh)
    {
        if (Mathf.Abs(outerAngle) < 0.001f)
        {
            _steerWheels[0].steerAngle = 0f;
            _steerWheels[1].steerAngle = 0f;
            return;
        }

        float radius   = _settings.turnRadius + speedKmh / 20f;
        float half     = _settings.rearTrack / 2f;
        float refOuter = Mathf.Rad2Deg * Mathf.Atan(_settings.wheelBase / (radius + half));
        float refInner = Mathf.Rad2Deg * Mathf.Atan(_settings.wheelBase / (radius - half));

        // Scale inner angle proportionally: innerAngle / outerAngle = refInner / refOuter
        float innerAngle = (refOuter > 0.001f)
            ? Mathf.Abs(outerAngle) * (refInner / refOuter)
            : Mathf.Abs(outerAngle);

        if (outerAngle > 0f) // turning right: FL is outer (smaller), FR is inner (larger)
        {
            _steerWheels[0].steerAngle =  Mathf.Abs(outerAngle);
            _steerWheels[1].steerAngle =  innerAngle;
        }
        else                 // turning left:  FR is outer (smaller), FL is inner (larger)
        {
            _steerWheels[0].steerAngle = -innerAngle;
            _steerWheels[1].steerAngle = -Mathf.Abs(outerAngle);
        }
    }

    // ── Smoothing ────────────────────────────────────────────

    private float SmoothSteerAngle(float current, float target, float steerInput)
    {
        bool  isReturning = Mathf.Abs(steerInput) < 0.05f;
        float speed       = isReturning ? _settings.steerReturnSpeed : _settings.steerSpeed;
        return Mathf.MoveTowards(current, target, speed * Time.fixedDeltaTime);
    }
}
