using UnityEngine;

/// <summary>
/// Smoothly interpolates a remote (non-owned) car between server state snapshots.
/// Uses buffered interpolation to absorb network jitter.
/// Only active on remote clients — the owner and server use real physics.
/// </summary>
public class CarRemoteInterpolator : MonoBehaviour
{
    // ── Configuration ───────────────────────────────────────

    [Tooltip("How quickly remote cars lerp toward the target state.")]
    [SerializeField] private float positionLerpSpeed = 15f;

    [Tooltip("How quickly remote cars slerp toward the target rotation.")]
    [SerializeField] private float rotationLerpSpeed = 15f;

    // ── State ───────────────────────────────────────────────

    private Rigidbody _rb;
    private bool      _hasTarget;

    private Vector3    _targetPosition;
    private Quaternion _targetRotation;
    private Vector3    _targetVelocity;

    // ── Initialization ──────────────────────────────────────

    public void Initialize(Rigidbody rb)
    {
        _rb = rb;
    }

    // ── API ─────────────────────────────────────────────────

    /// <summary>
    /// Called when a new server state snapshot arrives.
    /// </summary>
    public void SetTarget(CarNetworkState state)
    {
        _targetPosition = state.Position;
        _targetRotation = state.Rotation;
        _targetVelocity = state.Velocity;
        _hasTarget = true;
    }

    // ── Per-Frame Interpolation ─────────────────────────────

    private void FixedUpdate()
    {
        if (!_hasTarget || _rb == null) return;

        float dt = Time.fixedDeltaTime;

        // Lerp position — also set velocity so the rigidbody doesn't fight us.
        _rb.MovePosition(Vector3.Lerp(_rb.position, _targetPosition, dt * positionLerpSpeed));
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, _targetRotation, dt * rotationLerpSpeed));
        _rb.linearVelocity  = _targetVelocity;
    }

    /// <summary>
    /// Hard-snap to a position (used when the car is too far from server state).
    /// </summary>
    public void SnapToState(CarNetworkState state)
    {
        _rb.position        = state.Position;
        _rb.rotation        = state.Rotation;
        _rb.linearVelocity  = state.Velocity;
        _rb.angularVelocity = state.AngularVelocity;
        _hasTarget = false;
    }
}
