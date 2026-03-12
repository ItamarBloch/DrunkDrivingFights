using System.Collections.Generic;

/// <summary>
/// Plug-in win condition interface.
/// Implement this to define a new game mode's victory rule.
///
/// The MatchManager holds one IWinCondition and calls Evaluate() after every
/// player death. Return WinResult.Ongoing to keep the match running or
/// WinResult.Winner / WinResult.Draw to end it.
///
/// To add a new game mode win condition:
///   1. Create a new class implementing IWinCondition.
///   2. Add an entry to the WinConditionType enum in MatchData.cs.
///   3. Add a case to MatchManager.CreateWinCondition().
/// </summary>
public interface IWinCondition
{
    WinResult Evaluate(IReadOnlyList<PlayerMatchData> players);
}
