using UnityEngine;
using Mirror;
using System;

/// <summary>
/// Subscribes to HealthController.OnDeath / OnRespawn.
/// Shows/hides death UI. Fires static events for future GameModeController.
///
/// Death UI: build a panel inside CombatHUD_Canvas, start it INACTIVE,
/// drag into the deathPanel slot.
/// </summary>
public class PlayerDeathHandler : NetworkBehaviour
{
    [Header("Death UI (inside Car prefab's Canvas)")]
    [SerializeField] private GameObject deathPanel;
    [SerializeField] private TMPro.TMP_Text killedByText;

    // ── Static events for future GameModeController ─────────

    /// <summary>(deadPlayerNetId, killerNetId) — server only.</summary>
    public static event Action<uint, uint> OnPlayerDied;

    /// <summary>(respawnedPlayerNetId) — server only.</summary>
    public static event Action<uint> OnPlayerRespawned;

    // ── Lifecycle ───────────────────────────────────────────

    private void Awake()
    {
        var health = GetComponent<HealthController>();
        if (health != null)
        {
            health.OnDeath += HandleDeath;
            health.OnRespawn += HandleRespawn;
        }

        if (deathPanel != null)
            deathPanel.SetActive(false);
    }

    private void OnEnable()  => MatchManager.OnMatchEnded += HandleMatchEnded;
    private void OnDisable() => MatchManager.OnMatchEnded -= HandleMatchEnded;

    private void OnDestroy()
    {
        var health = GetComponent<HealthController>();
        if (health != null)
        {
            health.OnDeath -= HandleDeath;
            health.OnRespawn -= HandleRespawn;
        }
    }

    private void HandleDeath(uint killerNetId)
    {
        if (isServer)
            OnPlayerDied?.Invoke(netIdentity.netId, killerNetId);

        if (isLocalPlayer)
        {
            if (deathPanel != null)
                deathPanel.SetActive(true);

            if (killedByText != null)
            {
                string killerName = PlayerInfo.GetName(killerNetId);
                killedByText.text = $"Destroyed by {killerName}";
            }
        }
    }

    private void HandleRespawn()
    {
        if (isServer)
            OnPlayerRespawned?.Invoke(netIdentity.netId);

        if (isLocalPlayer)
        {
            if (deathPanel != null)
                deathPanel.SetActive(false);
        }
    }

    private void HandleMatchEnded(uint _)
    {
        if (isLocalPlayer && deathPanel != null)
            deathPanel.SetActive(false);
    }
}
