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

    /// <summary>Aerial pitch axis — arrow keys only. Range: -1 to +1.</summary>
    public float AerialThrottle;

    /// <summary>Aerial roll axis — arrow keys only. Range: -1 to +1.</summary>
    public float AerialSteer;

    public static CarInputData Empty => new CarInputData
    {
        Throttle       = 0f,
        Steer          = 0f,
        Brake          = false,
        AerialThrottle = 0f,
        AerialSteer    = 0f
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
        writer.WriteFloat(value.AerialThrottle);
        writer.WriteFloat(value.AerialSteer);
    }

    public static CarInputData ReadCarInputData(this NetworkReader reader)
    {
        return new CarInputData
        {
            Throttle       = reader.ReadFloat(),
            Steer          = reader.ReadFloat(),
            Brake          = reader.ReadBool(),
            AerialThrottle = reader.ReadFloat(),
            AerialSteer    = reader.ReadFloat()
        };
    }
}
