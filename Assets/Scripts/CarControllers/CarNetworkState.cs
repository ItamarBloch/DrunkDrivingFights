using Mirror;
using UnityEngine;

/// <summary>
/// Compact physics state snapshot synced from server → all clients.
/// Contains everything needed to interpolate/correct a remote car's visuals.
/// This struct travels: Server → [SyncVar / RPC] → Clients.
/// </summary>
public struct CarNetworkState
{
    public Vector3    Position;
    public Quaternion Rotation;
    public Vector3    Velocity;
    public Vector3    AngularVelocity;
    public float      SteerAngle;
    public float      SpeedKmh;
    public double     Timestamp;

    public static CarNetworkState Empty => new CarNetworkState
    {
        Position        = Vector3.zero,
        Rotation        = Quaternion.identity,
        Velocity        = Vector3.zero,
        AngularVelocity = Vector3.zero,
        SteerAngle      = 0f,
        SpeedKmh        = 0f,
        Timestamp       = 0.0
    };
}

/// <summary>
/// Mirror custom serializer for CarNetworkState.
/// </summary>
public static class CarNetworkStateSerializer
{
    public static void WriteCarNetworkState(this NetworkWriter writer, CarNetworkState value)
    {
        writer.WriteVector3(value.Position);
        writer.WriteQuaternion(value.Rotation);
        writer.WriteVector3(value.Velocity);
        writer.WriteVector3(value.AngularVelocity);
        writer.WriteFloat(value.SteerAngle);
        writer.WriteFloat(value.SpeedKmh);
        writer.WriteDouble(value.Timestamp);
    }

    public static CarNetworkState ReadCarNetworkState(this NetworkReader reader)
    {
        return new CarNetworkState
        {
            Position        = reader.ReadVector3(),
            Rotation        = reader.ReadQuaternion(),
            Velocity        = reader.ReadVector3(),
            AngularVelocity = reader.ReadVector3(),
            SteerAngle      = reader.ReadFloat(),
            SpeedKmh        = reader.ReadFloat(),
            Timestamp       = reader.ReadDouble()
        };
    }
}
