using UnityEngine;
using Mirror;
using System;

/// <summary>
/// Networked health system. Attach to the Car prefab root.
///
/// Server-authoritative: only the server modifies health.
/// Health syncs to all clients via SyncVar.
///
/// Events fire on ALL clients so UI / effects can react:
///   OnHealthChanged  → CombatHUD listens to this
///   OnDeath          → death screen, kill feed, etc.
///   OnRespawn        → reset UI, re-enable controls, etc.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class HealthController : NetworkBehaviour, IDamageable
{
    // ── Settings ────────────────────────────────────────────

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 3f;

    [Tooltip("Brief invincibility after respawning (prevents spawn-kill).")]
    [SerializeField] private float respawnInvulnerabilityTime = 2f;

    // ── Networked State ─────────────────────────────────────

    [SyncVar(hook = nameof(OnHealthSyncHook))]
    private float _currentHealth;

    [SyncVar]
    private bool _isAlive = true;

    [SyncVar]
    private uint _lastDamagedByNetId;

    // ── Local State ─────────────────────────────────────────

    private float _invulnTimer;
    private float _respawnTimer;
    private bool _waitingToRespawn;

    // ── Events ──────────────────────────────────────────────

    /// <summary>
    /// (currentHealth, maxHealth, damageAmount, damageSourcePosition)
    /// damageAmount > 0 means damage, &lt; 0 means heal.
    /// </summary>
    public event Action<float, float, float, Vector3> OnHealthChanged;

    /// <summary>(killerNetId)</summary>
    public event Action<uint> OnDeath;

    public event Action OnRespawn;

    // ── IDamageable ─────────────────────────────────────────

    public bool IsAlive => _isAlive;
    public float HealthRatio => maxHealth > 0f ? _currentHealth / maxHealth : 0f;
    public float CurrentHealth => _currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsInvulnerable => _invulnTimer > 0f;

    // ── Lifecycle ───────────────────────────────────────────

    public override void OnStartServer()
    {
        _currentHealth = maxHealth;
        _isAlive = true;
    }

    public override void OnStartClient()
    {
        OnHealthChanged?.Invoke(_currentHealth, maxHealth, 0f, Vector3.zero);
    }

    private void Update()
    {
        if (!isServer) return;

        if (_invulnTimer > 0f)
            _invulnTimer -= Time.deltaTime;

        if (_waitingToRespawn)
        {
            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer <= 0f)
                PerformRespawn();
        }
    }

    // ── Damage (Server Only) ────────────────────────────────

    [Server]
    public void TakeDamage(float damage, uint instigatorNetId, Vector3 damageSource)
    {
        if (!_isAlive || _invulnTimer > 0f || damage <= 0f) return;

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

    // ── Death & Respawn ─────────────────────────────────────

    [Server]
    private void Die()
    {
        _isAlive = false;
        RpcOnDeath(_lastDamagedByNetId);

        if (respawnDelay > 0f)
        {
            _waitingToRespawn = true;
            _respawnTimer = respawnDelay;
        }
    }

    [Server]
    private void PerformRespawn()
    {
        _waitingToRespawn = false;
        _currentHealth = maxHealth;
        _isAlive = true;
        _invulnTimer = respawnInvulnerabilityTime;

        // TODO: teleport to spawn point when spawn system exists
        // transform.position = SpawnManager.GetSpawnPoint();

        RpcOnRespawn();
    }

    [Server]
    public void ForceRespawn() => PerformRespawn();

    // ── SyncVar Hook ────────────────────────────────────────

    private void OnHealthSyncHook(float oldVal, float newVal)
    {
        // Actual event firing is via RPC (carries more data)
    }

    // ── RPCs ────────────────────────────────────────────────

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
