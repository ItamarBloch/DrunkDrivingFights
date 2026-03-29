using UnityEngine;

/// <summary>
/// Handles aerial car behaviour:
///   - Air control: W/S applies pitch torque, A/D applies roll torque while airborne.
///   - Auto-flip: snaps the car upright after being flipped past 90° for too long.
///
/// Torque is applied in car-local space so controls always feel intuitive regardless
/// of the car's current orientation in the air.
///
/// Called every physics tick by CarController (same code paths as CarMotor).
/// ZERO networking code — runs identically on server and owning client.
/// </summary>
public class CarAirControl : UnityEngine.MonoBehaviour
{
    // ── Dependencies ─────────────────────────────────────────

    private CarSettings   _settings;
    private Rigidbody     _rb;
    private WheelCollider[] _allWheels;

    // ── State ────────────────────────────────────────────────

    private float _flippedTimer;

    /// <summary>True when no wheel is touching the ground.</summary>
    public bool IsAirborne { get; private set; }

    // ── Initialization ───────────────────────────────────────

    public void Initialize(CarSettings settings, Rigidbody rb, WheelCollider[] allWheels)
    {
        _settings  = settings;
        _rb        = rb;
        _allWheels = allWheels;
    }

    // ── Per-Physics-Tick (called by CarController) ───────────

    public void Tick(CarInputData input)
    {
        UpdateAirborneState();

        if (IsAirborne)
        {
            ApplyAirControlTorque(input);
            ApplyAirAngularDamping();
        }

        // Auto-flip runs whether in the air or tipped on the ground.
        UpdateFlipTimer();
    }

    // ── Airborne Detection ───────────────────────────────────

    private void UpdateAirborneState()
    {
        for (int i = 0; i < _allWheels.Length; i++)
        {
            if (_allWheels[i].isGrounded) { IsAirborne = false; return; }
        }
        IsAirborne = true;
    }

    // ── Air Control ──────────────────────────────────────────

    /// <summary>
    /// Applies torque in car-local space:
    ///   W (throttle +1) → nose tilts DOWN  (transform.right axis, positive torque)
    ///   S (throttle -1) → nose tilts UP
    ///   A (steer   -1) → rolls LEFT        (transform.forward axis, positive torque)
    ///   D (steer   +1) → rolls RIGHT
    /// </summary>
    private void ApplyAirControlTorque(CarInputData input)
    {
        Vector3 pitch = transform.right   *  (input.Throttle * _settings.airPitchTorque);
        Vector3 roll  = transform.forward * (-input.Steer    * _settings.airRollTorque);
        _rb.AddTorque(pitch + roll, ForceMode.Acceleration);
    }

    /// <summary>
    /// Lightly damps angular velocity in the air to prevent runaway spinning.
    /// Multiplicative reduction so it never fully kills spin — just makes it controllable.
    /// </summary>
    private void ApplyAirAngularDamping()
    {
        _rb.angularVelocity *= 1f - (_settings.airAngularDamping * Time.fixedDeltaTime);
    }

    // ── Auto-Flip ────────────────────────────────────────────

    private void UpdateFlipTimer()
    {
        // dot(car.up, world.up) < threshold means the car is past 90° tilt
        bool isFlipped = Vector3.Dot(transform.up, Vector3.up) < _settings.flipDetectThreshold;

        if (isFlipped)
        {
            _flippedTimer += Time.fixedDeltaTime;
            if (_flippedTimer >= _settings.autoFlipDelay)
                ExecuteAutoFlip();
        }
        else
        {
            _flippedTimer = 0f;
        }
    }

    /// <summary>
    /// Snaps the car upright: preserves yaw and horizontal velocity,
    /// resets pitch/roll and angular velocity, and adds a small upward kick
    /// so the car doesn't immediately slam back into the same position.
    /// </summary>
    private void ExecuteAutoFlip()
    {
        Vector3 velocity = _rb.linearVelocity;
        float   yaw      = transform.eulerAngles.y;

        _rb.rotation        = Quaternion.Euler(0f, yaw, 0f);
        _rb.angularVelocity = Vector3.zero;

        // Preserve horizontal velocity; ensure a small upward pop to clear the ground.
        _rb.linearVelocity = new Vector3(velocity.x, Mathf.Max(velocity.y, 3f), velocity.z);

        _flippedTimer = 0f;
    }
}
