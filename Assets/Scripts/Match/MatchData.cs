/// <summary>Match lifecycle stages.</summary>
public enum MatchState
{
    WaitingToStart,
    Countdown,
    InProgress,
    Ended
}

/// <summary>
/// Result returned by IWinCondition.Evaluate().
/// Ongoing means the match continues; IsMatchOver = true ends the match.
/// </summary>
public struct WinResult
{
    public bool   IsMatchOver;
    public uint   WinnerNetId;   // 0 = draw / no individual winner
    public string Reason;

    public static WinResult Ongoing
        => new WinResult { IsMatchOver = false };

    public static WinResult Winner(uint netId, string reason = "")
        => new WinResult { IsMatchOver = true, WinnerNetId = netId, Reason = reason };

    public static WinResult Draw(string reason = "")
        => new WinResult { IsMatchOver = true, WinnerNetId = 0, Reason = reason };
}

/// <summary>Snapshot of a single player used by win condition logic.</summary>
public struct PlayerMatchData
{
    public uint   NetId;
    public bool   IsAlive;
    public string PlayerName;
}
