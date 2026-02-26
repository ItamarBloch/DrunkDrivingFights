using UnityEngine;

/// <summary>
/// Synchronises visual wheel transforms to WheelCollider positions/rotations.
/// Runs on ALL clients (including remotes) since it's purely visual.
/// </summary>
public class CarWheelVisuals : MonoBehaviour
{
    private WheelCollider[] _colliders;
    private Transform[]     _visuals;

    public void Initialize(WheelCollider[] colliders, Transform[] visuals)
    {
        Debug.Assert(colliders.Length == visuals.Length,
            "CarWheelVisuals: collider and visual arrays must match in length.");

        _colliders = colliders;
        _visuals   = visuals;
    }

    private void Update()
    {
        if (_colliders == null) return;

        for (int i = 0; i < _colliders.Length; i++)
            SyncWheelVisual(_colliders[i], _visuals[i]);
    }

    private void SyncWheelVisual(WheelCollider collider, Transform visual)
    {
        collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
        visual.position = pos;
        visual.rotation = rot;
    }
}
