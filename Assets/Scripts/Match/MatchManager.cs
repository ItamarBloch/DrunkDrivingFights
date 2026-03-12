using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Central match flow controller. Place on an empty GameObject (with NetworkIdentity)
/// in your gameplay scene.
///
/// State machine:
///   WaitingToStart → Countdown (players frozen) → InProgress → Ended (players frozen)
///
/// Win conditions are pluggable via IWinCondition — add new modes by implementing
/// the interface and registering it in CreateWinCondition().
///
/// UI listens to the static events:
///   OnCountdownBegan(float duration)
///   OnMatchBegan()
///   OnMatchEnded(uint winnerNetId)   ← 0 means draw
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class MatchManager : NetworkBehaviour
{
    // ── Singleton ────────────────────────────────────────────

    public static MatchManager singleton { get; private set; }

    // ── Inspector ────────────────────────────────────────────

    [Header("Timing")]
    [Tooltip("Seconds after scene load before countdown starts (lets all players spawn).")]
    [SerializeField] private float matchStartDelay = 1.5f;

    [Tooltip("Seconds players are frozen before the match begins.")]
    [SerializeField] private float countdownDuration = 3f;

    [Header("Win Condition")]
    [Tooltip("Which rule decides the winner. Swap this to change the game mode.")]
    [SerializeField] private WinConditionType winConditionType = WinConditionType.LastManStanding;

    // ── Synced State ─────────────────────────────────────────

    [SyncVar] public MatchState State = MatchState.WaitingToStart;
    [SyncVar] public uint       WinnerNetId;

    /// <summary>NetworkTime timestamp when the countdown ends — used by late-joining clients.</summary>
    [SyncVar] public double CountdownEndTime;

    // ── Static Events (fired on ALL clients via RPC) ─────────

    /// <summary>Countdown started. float = total duration in seconds.</summary>
    public static event Action<float> OnCountdownBegan;

    /// <summary>Countdown finished — gameplay is live.</summary>
    public static event Action OnMatchBegan;

    /// <summary>Match is over. uint = winner netId (0 = draw).</summary>
    public static event Action<uint> OnMatchEnded;

    // ── Private ──────────────────────────────────────────────

    private IWinCondition _winCondition;
    private readonly Dictionary<uint, PlayerMatchData> _players = new();

    // ════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════

    private void Awake()
    {
        if (singleton != null && singleton != this) { Destroy(gameObject); return; }
        singleton = this;
    }

    private void OnDestroy()
    {
        if (singleton == this) singleton = null;
        PlayerDeathHandler.OnPlayerDied -= HandlePlayerDied;
    }

    public override void OnStartServer()
    {
        _winCondition = CreateWinCondition(winConditionType);
        FreezeAllPlayers(true);
        Invoke(nameof(BeginCountdown), matchStartDelay);
    }

    // ════════════════════════════════════════════════════════
    //  SERVER — Match Flow
    // ════════════════════════════════════════════════════════

    [Server]
    private void BeginCountdown()
    {
        RegisterAllPlayers();
        State            = MatchState.Countdown;
        CountdownEndTime = NetworkTime.time + countdownDuration;

        FreezeAllPlayers(true);
        RpcCountdownBegan(countdownDuration);

        StartCoroutine(CountdownCoroutine());
    }

    [Server]
    private IEnumerator CountdownCoroutine()
    {
        yield return new WaitForSeconds(countdownDuration);
        BeginMatch();
    }

    [Server]
    private void BeginMatch()
    {
        State = MatchState.InProgress;
        FreezeAllPlayers(false);
        PlayerDeathHandler.OnPlayerDied += HandlePlayerDied;
        RpcMatchBegan();
    }

    [Server]
    private void HandlePlayerDied(uint deadNetId, uint killerNetId)
    {
        if (State != MatchState.InProgress) return;
        if (!_players.ContainsKey(deadNetId)) return;

        var data = _players[deadNetId];
        data.IsAlive = false;
        _players[deadNetId] = data;

        WinResult result = _winCondition.Evaluate(GetPlayerList());
        if (result.IsMatchOver)
            EndMatch(result.WinnerNetId);
    }

    [Server]
    private void EndMatch(uint winnerNetId)
    {
        if (State == MatchState.Ended) return;   // guard against double-call

        State       = MatchState.Ended;
        WinnerNetId = winnerNetId;

        FreezeAllPlayers(true);
        PlayerDeathHandler.OnPlayerDied -= HandlePlayerDied;

        RpcMatchEnded(winnerNetId);

        Debug.Log($"[MatchManager] Match ended. Winner netId={winnerNetId} " +
                  $"({(winnerNetId == 0 ? "Draw" : PlayerInfo.GetName(winnerNetId))})");
    }

    // ── Called by GameNetworkRoomManager when a client disconnects mid-match ──

    [Server]
    public void HandlePlayerDisconnected(uint netId)
    {
        if (State != MatchState.InProgress) return;
        HandlePlayerDied(netId, 0);
    }

    // ════════════════════════════════════════════════════════
    //  SERVER — Helpers
    // ════════════════════════════════════════════════════════

    [Server]
    private void RegisterAllPlayers()
    {
        _players.Clear();
        foreach (var health in FindObjectsByType<HealthController>(FindObjectsSortMode.None))
        {
            var identity = health.GetComponent<NetworkIdentity>();
            if (identity == null) continue;

            uint netId = identity.netId;
            _players[netId] = new PlayerMatchData
            {
                NetId      = netId,
                IsAlive    = true,
                PlayerName = PlayerInfo.GetName(netId)
            };
        }
        Debug.Log($"[MatchManager] Registered {_players.Count} player(s).");
    }

    [Server]
    private void FreezeAllPlayers(bool frozen)
    {
        foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            car.SetFrozen(frozen);

        foreach (var weapon in FindObjectsByType<WeaponController>(FindObjectsSortMode.None))
            weapon.SetFrozen(frozen);
    }

    private IReadOnlyList<PlayerMatchData> GetPlayerList()
    {
        return new List<PlayerMatchData>(_players.Values);
    }

    // ════════════════════════════════════════════════════════
    //  RPCs — broadcast events to all clients
    // ════════════════════════════════════════════════════════

    [ClientRpc]
    private void RpcCountdownBegan(float duration)
        => OnCountdownBegan?.Invoke(duration);

    [ClientRpc]
    private void RpcMatchBegan()
        => OnMatchBegan?.Invoke();

    [ClientRpc]
    private void RpcMatchEnded(uint winnerNetId)
        => OnMatchEnded?.Invoke(winnerNetId);

    // ════════════════════════════════════════════════════════
    //  WIN CONDITION FACTORY
    //  Add new game modes here — no other file needs to change.
    // ════════════════════════════════════════════════════════

    private static IWinCondition CreateWinCondition(WinConditionType type)
    {
        return type switch
        {
            WinConditionType.LastManStanding => new LastManStandingCondition(),
            _                               => new LastManStandingCondition()
        };
    }
}

/// <summary>
/// All available win conditions.
/// Add an entry here + a case in MatchManager.CreateWinCondition() to add a new mode.
/// </summary>
public enum WinConditionType
{
    LastManStanding
    // TimedSurvival,
    // CaptureTheFlag,
    // TeamDeathmatch,
}
