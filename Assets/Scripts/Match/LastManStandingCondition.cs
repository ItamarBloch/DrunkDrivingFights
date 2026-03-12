using System.Collections.Generic;

/// <summary>
/// Win condition: the last alive player wins.
/// If all players die simultaneously, the result is a draw.
/// </summary>
public class LastManStandingCondition : IWinCondition
{
    public WinResult Evaluate(IReadOnlyList<PlayerMatchData> players)
    {
        if (players.Count == 0)
            return WinResult.Ongoing;

        var alive = new List<PlayerMatchData>();
        foreach (var p in players)
            if (p.IsAlive) alive.Add(p);

        if (alive.Count == 1)
            return WinResult.Winner(alive[0].NetId, "Last player standing");

        if (alive.Count == 0)
            return WinResult.Draw("All players eliminated");

        return WinResult.Ongoing;
    }
}
