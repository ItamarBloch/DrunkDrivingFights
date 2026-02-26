using UnityEngine;

/// <summary>
/// ScriptableObject holding all tunable car parameters.
/// Create via: Assets > Create > Vehicle > Car Settings
/// Pure data — no networking, no MonoBehaviour.
/// </summary>
[CreateAssetMenu(fileName = "NewCarSettings", menuName = "Vehicle/Car Settings")]
public class CarSettings : ScriptableObject
{
    [Header("Speed")]
    [Tooltip("Maximum forward speed in km/h.")]
    public float maxForwardSpeed = 120f;

    [Tooltip("Maximum reverse speed in km/h.")]
    public float maxReverseSpeed = 40f;

    [Header("Engine")]
    [Tooltip("Peak motor torque applied to driven wheels (Nm).")]
    public float motorTorque = 1500f;

    [Tooltip("Torque curve relative to speed ratio (0 = stopped, 1 = max speed).")]
    public AnimationCurve torqueCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    [Header("Braking")]
    [Tooltip("Brake torque applied when the player actively brakes (Nm).")]
    public float brakeTorque = 3000f;

    [Tooltip("Passive drag torque when no input is given (Nm). Simulates engine braking.")]
    public float decelerationTorque = 500f;

    [Header("Steering")]
    [Tooltip("Maximum steering angle at low speed (degrees).")]
    public float maxSteerAngle = 35f;

    [Tooltip("Minimum steering angle at max speed (degrees).")]
    public float minSteerAngleAtMaxSpeed = 10f;

    [Tooltip("How quickly the wheels reach the target steer angle (degrees/sec).")]
    public float steerSpeed = 150f;

    [Tooltip("How quickly the wheels return to center when no input (degrees/sec).")]
    public float steerReturnSpeed = 250f;

    [Header("Downforce")]
    [Tooltip("Additional downward force per unit of speed.")]
    public float downforceCoefficient = 2.5f;

    // ── Helpers ──────────────────────────────────────────────

    public float MaxForwardSpeedUnits => maxForwardSpeed / 3.6f;
    public float MaxReverseSpeedUnits => maxReverseSpeed / 3.6f;
}
