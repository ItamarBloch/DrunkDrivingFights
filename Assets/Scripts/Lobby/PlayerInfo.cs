using UnityEngine;
using Mirror;
using System.Collections.Generic;

/// <summary>
/// Carries player identity data from lobby into the game scene.
/// Attach to the Car prefab alongside CarController, NetworkIdentity, etc.
///
/// The GameNetworkRoomManager sets playerName and playerColor on spawn.
///
/// Static lookup so any script can get a name by netId:
///   string name = PlayerInfo.GetName(someNetId);
/// </summary>
public class PlayerInfo : NetworkBehaviour
{
    // ── Static Registry ─────────────────────────────────────

    private static readonly Dictionary<uint, PlayerInfo> _registry = new Dictionary<uint, PlayerInfo>();

    /// <summary>
    /// Get a player's display name by netId. Returns "Unknown" if not found.
    /// </summary>
    public static string GetName(uint netId)
    {
        if (_registry.TryGetValue(netId, out var info))
            return info.playerName;
        return "Unknown";
    }

    public static PlayerInfo GetPlayer(uint netId)
    {
        _registry.TryGetValue(netId, out var info);
        return info;
    }

    // ── Synced State ────────────────────────────────────────

    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "Player";

    [SyncVar(hook = nameof(OnColorChanged))]
    public Color playerColor = Color.white;

    // ── Events ──────────────────────────────────────────────

    public event System.Action<string> OnPlayerNameChanged;
    public event System.Action<Color> OnPlayerColorChanged;

    // ── Lifecycle ───────────────────────────────────────────

    public override void OnStartClient()
    {
        base.OnStartClient();
        _registry[netIdentity.netId] = this;
        ApplyColor(playerColor);
        ApplyNameplate(playerName);
    }

    private void OnDestroy()
    {
        if (netIdentity != null)
            _registry.Remove(netIdentity.netId);
    }

    // ── SyncVar Hooks ───────────────────────────────────────

    private void OnNameChanged(string oldName, string newName)
    {
        ApplyNameplate(newName);
        OnPlayerNameChanged?.Invoke(newName);
    }

    private void OnColorChanged(Color oldColor, Color newColor)
    {
        ApplyColor(newColor);
        OnPlayerColorChanged?.Invoke(newColor);
    }

    // ── Visuals ─────────────────────────────────────────────

    private void ApplyColor(Color color)
    {
        Transform body = transform.Find("Body");
        if (body != null)
        {
            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", color);
                mpb.SetColor("_Color", color);
                renderer.SetPropertyBlock(mpb);
            }
        }
    }

    private void ApplyNameplate(string name)
    {
        gameObject.name = $"Car_{name}";
    }
}
