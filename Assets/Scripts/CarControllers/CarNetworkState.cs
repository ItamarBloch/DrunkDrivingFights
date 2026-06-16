using Mirror;
using UnityEngine;

/// <summary>
/// Compact physics state snapshot synced from server → all clients.
/// Contains the minimum needed to interpolate/correct a remote car's visuals.
/// This struct travels: Server → [unreliable RPC] → Clients.
///
/// Only Position + Rotation + Velocity + Timestamp + LastProcessedInputSeq are
/// carried. Earlier versions also sent SpeedKmh, SteerAngle and AngularVelocity —
/// all three were dead weight (never consumed on clients; speed has its own
/// throttled SyncVar, steer/wheels are computed locally). Rotation is bit-packed
/// (smallest-three) to 4 bytes instead of 16. Net wire size ≈ 40 B.
///
/// LastProcessedInputSeq is the owner-prediction reconciliation key: it tells the
/// owning client "this pose is the result of YOUR input #N". The client then
/// compares against the pose IT predicted after the same input #N — same inputs,
/// so the input-in-flight lead cancels exactly and only genuine physics divergence
/// is corrected. (The old approach keyed on Timestamp, which secretly compared
/// different input sets and left a persistent phantom error → felt like input lag.)
/// </summary>
public struct CarNetworkState
{
    public Vector3    Position;
    public Quaternion Rotation;
    public Vector3    Velocity;
    public double     Timestamp;

    /// <summary>Sequence of the last owner input whose physics result this pose reflects.</summary>
    public uint       LastProcessedInputSeq;

    public static CarNetworkState Empty => new CarNetworkState
    {
        Position              = Vector3.zero,
        Rotation              = Quaternion.identity,
        Velocity              = Vector3.zero,
        Timestamp             = 0.0,
        LastProcessedInputSeq = 0
    };
}

/// <summary>
/// Mirror custom serializer for CarNetworkState. Rotation is compressed via Mirror's
/// smallest-three quaternion packing (16 B → 4 B).
/// </summary>
public static class CarNetworkStateSerializer
{
    public static void WriteCarNetworkState(this NetworkWriter writer, CarNetworkState value)
    {
        writer.WriteVector3(value.Position);
        writer.WriteUInt(Compression.CompressQuaternion(value.Rotation));
        writer.WriteVector3(value.Velocity);
        writer.WriteDouble(value.Timestamp);
        writer.WriteUInt(value.LastProcessedInputSeq);
    }

    public static CarNetworkState ReadCarNetworkState(this NetworkReader reader)
    {
        return new CarNetworkState
        {
            Position              = reader.ReadVector3(),
            Rotation              = Compression.DecompressQuaternion(reader.ReadUInt()),
            Velocity              = reader.ReadVector3(),
            Timestamp             = reader.ReadDouble(),
            LastProcessedInputSeq = reader.ReadUInt()
        };
    }
}
