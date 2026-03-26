using UnityEngine;
using Mirror;
using System;

/// <summary>
/// Networked health system. THE EVENT DISPATCHER.
/// All other scripts subscribe to OnDeath / OnRespawn / OnInvulnerabilityBegan / OnInvulnerabilityEnded.
/// Does NOT auto-respawn — a future GameModeController calls ForceRespawn().
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class HealthController : NetworkBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Spawn / Respawn Protection")]
    [SerializeField] private float spawnInvulnerabilityTime = 3f;
    [SerializeField] private float respawnInvulnerabilityTime = 3f;

    // ── Networked State ─────────────────────────────────────

    [SyncVar(hook = nameof(OnHealthSyncHook))]
    private float _currentHealth;

    [SyncVar]
    private bool _isAlive = true;

    [SyncVar]
    private uint _lastDamagedByNetId;

    /// <summary>Synced so all clients can show the spawn-protection visual.</summary>
    [SyncVar(hook = nameof(OnInvulnSyncHook))]
    private bool _isInvulnerable;

    private float _invulnTimer;

    // ── EVENTS — other scripts subscribe to these ───────────

    /// <summary>(currentHealth, maxHealth, damageAmount, damageSourcePosition)</summary>
    public event Action<float, float, float, Vector3> OnHealthChanged;

    /// <summary>(killerNetId) — player just died.</summary>
    public event Action<uint> OnDeath;

    /// <summary>Player was respawned by a game mode.</summary>
    public event Action OnRespawn;

    /// <summary>Invulnerability just became active — fired on ALL clients.</summary>
    public event Action OnInvulnerabilityBegan;

    /// <summary>Invulnerability just expired — fired on ALL clients.</summary>
    public event Action OnInvulnerabilityEnded;

    // ── IDamageable ─────────────────────────────────────────

    public bool IsAlive => _isAlive;
    public float HealthRatio => maxHealth > 0f ? _currentHealth / maxHealth : 0f;
    public float CurrentHealth => _currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsInvulnerable => _isInvulnerable;
    public uint LastDamagedByNetId => _lastDamagedByNetId;

    // ── Lifecycle ───────────────────────────────────────────

    public override void OnStartServer()
    {
        _currentHealth = maxHealth;
        _isAlive = true;
        BeginInvulnerability(spawnInvulnerabilityTime);
    }

    public override void OnStartClient()
    {
        OnHealthChanged?.Invoke(_currentHealth, maxHealth, 0f, Vector3.zero);
    }

    private void Update()
    {
        if (!isServer) return;
        if (_invulnTimer > 0f)
        {
            _invulnTimer -= Time.deltaTime;
            if (_invulnTimer <= 0f)
            {
                _invulnTimer = 0f;
                _isInvulnerable = false; // SyncVar — hook fires on all clients
            }
        }
    }

    // ── Damage (Server Only) ────────────────────────────────

    [Server]
    public void TakeDamage(float damage, uint instigatorNetId, Vector3 damageSource)
    {
        if (!_isAlive || _isInvulnerable || damage <= 0f) return;

        _lastDamagedByNetId = instigatorNetId;
        _currentHealth = Mathf.Max(0f, _currentHealth - damage);
        RpcDamageReceived(damage, damageSource);

        if (_currentHealth <= 0f)
            Die();
    }

    [Server]
    public void Heal(float amount)
    {
        if (!_isAlive || amount <= 0f) return;
        _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
        RpcDamageReceived(-amount, Vector3.zero);
    }

    // ── Death (stays dead until ForceRespawn) ───────────────

    [Server]
    private void Die()
    {
        _isAlive = false;
        _isInvulnerable = false;
        _invulnTimer = 0f;
        RpcOnDeath(_lastDamagedByNetId);
    }

    /// <summary>
    /// Called by a future GameModeController. NOT automatic.
    /// Teleport the player BEFORE calling this.
    /// </summary>
    [Server]
    public void ForceRespawn()
    {
        _currentHealth = maxHealth;
        _isAlive = true;
        BeginInvulnerability(respawnInvulnerabilityTime);
        RpcOnRespawn();
    }

    // ── Invulnerability (Server Only) ───────────────────────

    [Server]
    private void BeginInvulnerability(float duration)
    {
        if (duration <= 0f) return;
        _invulnTimer = duration;
        _isInvulnerable = true; // SyncVar — hook fires on all clients
    }

    // ── SyncVar Hooks ────────────────────────────────────────

    private void OnHealthSyncHook(float oldVal, float newVal) { }

    private void OnInvulnSyncHook(bool oldVal, bool newVal)
    {
        if (newVal)
            OnInvulnerabilityBegan?.Invoke();
        else
            OnInvulnerabilityEnded?.Invoke();
    }

    // ── RPCs — these fire the events on ALL clients ─────────

    [ClientRpc]
    private void RpcDamageReceived(float damageAmount, Vector3 source)
    {
        OnHealthChanged?.Invoke(_currentHealth, maxHealth, damageAmount, source);
    }

    [ClientRpc]
    private void RpcOnDeath(uint killerNetId) => OnDeath?.Invoke(killerNetId);

    [ClientRpc]
    private void RpcOnRespawn() => OnRespawn?.Invoke();
}
