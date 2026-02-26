using UnityEngine;

/// <summary>
/// Pure simulation for steering.
/// Computes speed-sensitive steering angles with smooth interpolation.
/// No networking, no input reading — just math and WheelCollider writes.
/// </summary>
public class CarSteering : MonoBehaviour
{
    // ── Dependencies (set via Initialize) ───────────────────

    private CarSettings     _settings;
    private WheelCollider[] _steerWheels;

    // ── State ───────────────────────────────────────────────

    private float _currentSteerAngle;

    /// <summary>Current applied steering angle (degrees, signed).</summary>
    public float CurrentSteerAngle => _currentSteerAngle;

    // ── Initialization ──────────────────────────────────────

    public void Initialize(CarSettings settings, WheelCollider[] steerWheels)
    {
        _settings    = settings;
        _steerWheels = steerWheels;
    }

    // ── Per-Physics-Tick (called by CarController) ──────────

    public void Tick(float steerInput, float speedRatio)
    {
        float targetAngle = CalculateTargetAngle(steerInput, speedRatio);
        _currentSteerAngle = SmoothSteerAngle(_currentSteerAngle, targetAngle, steerInput);
        ApplySteerAngle(_currentSteerAngle);
    }

    // ── Calculation ─────────────────────────────────────────

    private float CalculateTargetAngle(float steerInput, float speedRatio)
    {
        float maxAngle = Mathf.Lerp(
            _settings.maxSteerAngle,
            _settings.minSteerAngleAtMaxSpeed,
            speedRatio
        );
        return maxAngle * steerInput;
    }

    private float SmoothSteerAngle(float current, float target, float steerInput)
    {
        bool isReturning = Mathf.Abs(steerInput) < 0.05f;
        float speed = isReturning ? _settings.steerReturnSpeed : _settings.steerSpeed;
        return Mathf.MoveTowards(current, target, speed * Time.fixedDeltaTime);
    }

    private void ApplySteerAngle(float angle)
    {
        for (int i = 0; i < _steerWheels.Length; i++)
            _steerWheels[i].steerAngle = angle;
    }
}
