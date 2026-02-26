using Mirror;

/// <summary>
/// Immutable snapshot of player input for one physics tick.
/// Implements Mirror custom serialization for efficient network transport.
/// This struct travels: Client → [Command] → Server.
/// </summary>
public struct CarInputData
{
    /// <summary>Throttle/reverse axis. Range: -1 (full reverse) to +1 (full throttle).</summary>
    public float Throttle;

    /// <summary>Steering axis. Range: -1 (full left) to +1 (full right).</summary>
    public float Steer;

    /// <summary>True while the player is holding the brake button.</summary>
    public bool Brake;

    public static CarInputData Empty => new CarInputData
    {
        Throttle = 0f,
        Steer    = 0f,
        Brake    = false
    };
}

/// <summary>
/// Mirror custom serializer for CarInputData.
/// Keeps bandwidth minimal — 9 bytes per input snapshot.
/// </summary>
public static class CarInputDataSerializer
{
    public static void WriteCarInputData(this NetworkWriter writer, CarInputData value)
    {
        writer.WriteFloat(value.Throttle);
        writer.WriteFloat(value.Steer);
        writer.WriteBool(value.Brake);
    }

    public static CarInputData ReadCarInputData(this NetworkReader reader)
    {
        return new CarInputData
        {
            Throttle = reader.ReadFloat(),
            Steer    = reader.ReadFloat(),
            Brake    = reader.ReadBool()
        };
    }
}
