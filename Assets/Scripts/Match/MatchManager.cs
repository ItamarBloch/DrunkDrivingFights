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

    [Header("Combat Settings")]
    [Tooltip("Assign the shared CombatSettings ScriptableObject. MatchManager will apply per-match overrides here.")]
    [SerializeField] private CombatSettings combatSettings;

    // ── Synced State ─────────────────────────────────────────

    [SyncVar] public MatchState State = MatchState.WaitingToStart;
    [SyncVar] public uint       WinnerNetId;

    /// <summary>NetworkTime timestamp when the countdown ends — used by late-joining clients.</summary>
    [SyncVar] public double CountdownEndTime;

    [SyncVar(hook = nameof(OnSelfDamageSync))]   private bool  _syncSelfDamage = true;
    [SyncVar(hook = nameof(OnRocketDamageSync))] private int   _syncRocketDamage = MatchCreationData.BaselineDamage;

    // ── Static Events (fired on ALL clients via RPC) ─────────

    /// <summary>Countdown started. float = total duration in seconds.</summary>
    public static event Action<float> OnCountdownBegan;

    /// <summary>Countdown finished — gameplay is live.</summary>
    public static event Action OnMatchBegan;

    /// <summary>Match is over. uint = winner netId (0 = draw).</summary>
    public static event Action<uint> OnMatchEnded;

    /// <summary>
    /// Fired on each client individually with that player's own match stats.
    /// Arrives shortly after OnMatchEnded (same connection, ordered).
    /// </summary>
    public static event Action<int, int, int> OnLocalStatsReady; // (kills, deaths, damageDealt)

    // ── Private ──────────────────────────────────────────────

    private IWinCondition _winCondition;
    private readonly Dictionary<uint, PlayerMatchData> _players = new();
    private readonly Dictionary<uint, int>   _kills       = new();
    private readonly Dictionary<uint, int>   _deaths      = new();
    private readonly Dictionary<uint, float> _damageDealt = new();

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
        // Apply match creation settings before anything else
        _syncSelfDamage   = MatchCreationData.SelfDamageEnabled;
        _syncRocketDamage = MatchCreationData.RocketDamage;
        ApplyCombatSettings();

        _winCondition = CreateWinCondition(winConditionType);
        FreezeAllPlayers(true);
        Invoke(nameof(BeginCountdown), matchStartDelay);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Clients apply whatever the server synced (initial state sync may not fire hooks)
        ApplyCombatSettings();
    }

    private void ApplyCombatSettings()
    {
        if (combatSettings == null) return;
        combatSettings.selfDamage = _syncSelfDamage;
        combatSettings.globalDamageMultiplier = _syncRocketDamage / (float)MatchCreationData.BaselineDamage;
    }

    private void OnSelfDamageSync(bool _, bool newVal)
    {
        _syncSelfDamage = newVal;
        ApplyCombatSettings();
    }

    private void OnRocketDamageSync(int _, int newVal)
    {
        _syncRocketDamage = newVal;
        ApplyCombatSettings();
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
        HealthController.OnDamageDealt  += HandleDamageDealt;
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

        // Track KDA
        _deaths[deadNetId] = _deaths.TryGetValue(deadNetId, out var d) ? d + 1 : 1;
        if (killerNetId != 0 && killerNetId != deadNetId)
            _kills[killerNetId] = _kills.TryGetValue(killerNetId, out var k) ? k + 1 : 1;

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
        HealthController.OnDamageDealt  -= HandleDamageDealt;

        RpcMatchEnded(winnerNetId);

        // Send each client their own match stats
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            uint netId = conn.identity.netId;
            int k   = _kills.TryGetValue(netId, out var kv) ? kv : 0;
            int d   = _deaths.TryGetValue(netId, out var dv) ? dv : 0;
            int dmg = Mathf.RoundToInt(_damageDealt.TryGetValue(netId, out var dmgv) ? dmgv : 0f);
            TargetSendStats(conn, k, d, dmg);
        }

        Debug.Log($"[MatchManager] Match ended. Winner netId={winnerNetId} " +
                  $"({(winnerNetId == 0 ? "Draw" : PlayerInfo.GetName(winnerNetId))})");
    }

    [Server]
    private void HandleDamageDealt(uint instigatorNetId, float damage)
    {
        if (State != MatchState.InProgress) return;
        _damageDealt[instigatorNetId] = (_damageDealt.TryGetValue(instigatorNetId, out var v) ? v : 0f) + damage;
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
        _kills.Clear();
        _deaths.Clear();
        _damageDealt.Clear();
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

    [TargetRpc]
    private void TargetSendStats(NetworkConnectionToClient target, int kills, int deaths, int damageDealt)
        => OnLocalStatsReady?.Invoke(kills, deaths, damageDealt);

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
