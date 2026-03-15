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
    public float motorTorque = 4000f;

    [Tooltip("Torque curve relative to speed ratio (0 = stopped, 1 = max speed).")]
    public AnimationCurve torqueCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    [Header("Braking")]
    [Tooltip("Brake torque applied when the player actively brakes (Nm).")]
    public float brakeTorque = 3000f;

    [Tooltip("Passive drag torque when no input is given (Nm). Simulates engine braking.")]
    public float decelerationTorque = 500f;

    [Header("Steering")]
    [Tooltip("Maximum steering angle at low speed (degrees).")]
    public float maxSteerAngle = 40f;

    [Tooltip("Minimum steering angle at max speed (degrees).")]
    public float minSteerAngleAtMaxSpeed = 15f;

    [Tooltip("How quickly the wheels reach the target steer angle (degrees/sec).")]
    public float steerSpeed = 150f;

    [Tooltip("How quickly the wheels return to center when no input (degrees/sec).")]
    public float steerReturnSpeed = 180f;

    [Header("Agility")]
    [Tooltip("Deceleration (m/s²) applied directly when the player reverses drive direction. Creates fast arcade-style direction changes.")]
    public float counterThrustForce = 60f;

    [Tooltip("Direct acceleration boost (m/s²) applied at low speeds for a punchy launch feel.")]
    public float launchBoostForce = 15f;

    [Tooltip("Speed (km/h) below which the launch boost is active.")]
    public float launchBoostMaxSpeed = 40f;

    [Header("Downforce")]
    [Tooltip("Additional downward force per unit of speed.")]
    public float downforceCoefficient = 2.5f;

    [Header("Drift")]
    [Tooltip("Minimum speed (km/h) required to enter drift mode.")]
    public float driftMinSpeed = 25f;

    [Tooltip("Friction multiplier during drift. Lower = more slide. Try 0.5–0.8.")]
    [Range(0.1f, 1f)]
    public float driftFriction = 0.5f;

    [Tooltip("Steering angle multiplier while drifting.")]
    public float driftSteerMultiplier = 1.5f;

    // ── Helpers ──────────────────────────────────────────────

    public float MaxForwardSpeedUnits => maxForwardSpeed / 3.6f;
    public float MaxReverseSpeedUnits => maxReverseSpeed / 3.6f;
}
