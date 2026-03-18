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

    [Tooltip("Direct forward force (m/s²) added to Rigidbody so the car can overcome drag and reach maxForwardSpeed. Applied constantly until max speed, then cuts off.")]
    public float directDriveForce = 40f;

    [Header("Braking")]
    [Tooltip("Brake torque applied when the player actively brakes (Nm).")]
    public float brakeTorque = 3000f;

    [Tooltip("Passive drag torque when no input is given (Nm). Simulates engine braking.")]
    public float decelerationTorque = 500f;

    [Header("Steering — Ackerman Geometry")]
    [Tooltip("Distance between front and rear axles (metres). Measure your car prefab.")]
    public float wheelBase = 2.55f;

    [Tooltip("Distance between the two rear wheels (metres). Measure your car prefab.")]
    public float rearTrack = 1.5f;

    [Tooltip("Base turning radius at zero speed (metres). Lower = tighter turns / more angle.")]
    public float turnRadius = 5f;

    [Tooltip("How quickly the wheels reach the target steer angle (degrees/sec).")]
    public float steerSpeed = 250f;

    [Tooltip("How quickly the wheels return to center when no input (degrees/sec).")]
    public float steerReturnSpeed = 250f;

    [Header("Agility")]
    [Tooltip("Deceleration (m/s²) applied directly when the player reverses drive direction. Creates fast arcade-style direction changes.")]
    public float counterThrustForce = 60f;

    [Tooltip("Direct acceleration boost (m/s²) applied at low speeds for a punchy launch feel.")]
    public float launchBoostForce = 15f;

    [Tooltip("Speed (km/h) below which the launch boost is active.")]
    public float launchBoostMaxSpeed = 40f;

    [Header("Grip & Feel")]
    [Tooltip("How much lateral (sideways) velocity is kept each physics step when driving normally. " +
             "Acts like tire grip. 0.92 = 92% kept per step → ~1.5% remains after 1 second. " +
             "Lower = more grip / snappier direction changes. Try 0.88–0.95.")]
    [Range(0.5f, 1f)]
    public float normalLateralDamping = 0.92f;

    [Tooltip("Same as normalLateralDamping but during drift. Higher = more slide kept. Try 0.96–0.99.")]
    [Range(0.5f, 1f)]
    public float driftLateralDamping = 0.98f;

    [Tooltip("How fast yaw (rotation) velocity decays when you release steer or counter-steer. " +
             "This fixes the 'stuck at angle' feel. 0 = no damping, 5 = snappy. Try 2–5.")]
    public float yawDampingStrength = 3f;

    [Tooltip("Maximum yaw rate (rad/s) allowed during drift. Prevents spinout. ~1.5–2.5 rad/s.")]
    public float maxDriftYawRate = 2f;

    [Header("Downforce")]
    [Tooltip("Additional downward force per unit of speed.")]
    public float downforceCoefficient = 2.5f;

    [Header("Drift")]
    [Tooltip("Minimum speed (km/h) required to enter drift mode.")]
    public float driftMinSpeed = 25f;

    [Tooltip("Friction multiplier. Used as: driftFactor * driftFriction = friction target. " +
             "driftFactor is derived from rear-wheel sideways slip (self-regulating). " +
             "Higher = more grip during drift. Reference value: 2.0")]
    public float driftFriction = 2f;

    [Tooltip("Brake torque applied to rear wheels during drift to initiate oversteer (Nm). " +
             "Needs to be high enough to lock the rear wheels. Try 8000–15000.")]
    public float driftBrakeTorque = 10000f;

    [Tooltip("Steering angle multiplier while drifting.")]
    public float driftSteerMultiplier = 1.5f;

    // ── Helpers ──────────────────────────────────────────────

    public float MaxForwardSpeedUnits => maxForwardSpeed / 3.6f;
    public float MaxReverseSpeedUnits => maxReverseSpeed / 3.6f;
}
