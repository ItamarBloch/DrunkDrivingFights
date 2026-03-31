/// <summary>
/// Static bag set by MatchSettingsBuilder before the host calls StartGame().
/// Read by MatchManager.OnStartServer() to configure CombatSettings for the match.
/// </summary>
public static class MatchCreationData
{
    public static bool SelfDamageEnabled = true;

    /// <summary>One of the fixed options: 30, 60, 90, 120, 150.</summary>
    public static int RocketDamage = 60;

    public static readonly int[] RocketDamageOptions = { 30, 60, 90, 120, 150 };

    /// <summary>Baseline damage used to compute globalDamageMultiplier (RocketDamage / Baseline).</summary>
    public const int BaselineDamage = 60;
}
