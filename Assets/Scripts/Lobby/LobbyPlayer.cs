using UnityEngine;
using Mirror;

/// <summary>
/// Represents a player in the lobby. Extends NetworkRoomPlayer to add
/// custom synced data: name, color, owner status.
/// 
/// Attach this to a "LobbyPlayer" prefab (empty GameObject + NetworkIdentity + this script).
/// Assign that prefab as RoomPlayerPrefab on GameNetworkRoomManager.
/// </summary>
public class LobbyPlayer : NetworkRoomPlayer
{
    // ─── Synced Player Data ───────────────────────────────────────
    
    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "Player";

    [SyncVar(hook = nameof(OnColorChanged))]
    public Color playerColor = Color.white;

    [SyncVar(hook = nameof(OnOwnerChanged))]
    public bool isRoomOwner = false;

    [SyncVar]
    public int playerSlot = -1;

    // ─── Colors available for selection ───────────────────────────
    public static readonly Color[] AvailableColors = new Color[]
    {
        new Color(0.9f, 0.2f, 0.2f),  // Red
        new Color(0.2f, 0.5f, 0.9f),  // Blue
        new Color(0.2f, 0.9f, 0.3f),  // Green
        new Color(0.9f, 0.9f, 0.2f),  // Yellow
        new Color(0.9f, 0.5f, 0.1f),  // Orange
        new Color(0.7f, 0.2f, 0.9f),  // Purple
    };

    // ─── Events ───────────────────────────────────────────────────
    public static event System.Action OnAnyPlayerDataChanged;

    // ─── Lifecycle ────────────────────────────────────────────────

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Local player sets their name on join
        if (isOwned)
        {
            string name = PlayerPrefs.GetString("PlayerName", $"Player_{Random.Range(100, 999)}");
            CmdSetName(name);

            // Pick a color based on our index
            int colorIndex = (int)(netId % (uint)AvailableColors.Length);
            CmdSetColor(AvailableColors[colorIndex]);
        }

        NotifyUpdate();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
    }

    // ─── Commands (client → server) ──────────────────────────────

    [Command]
    public void CmdSetName(string newName)
    {
        // Sanitize: clamp length, strip tags
        playerName = newName.Length > 20 ? newName.Substring(0, 20) : newName;
    }

    [Command]
    public void CmdSetColor(Color newColor)
    {
        playerColor = newColor;
    }

    [Command]
    public void CmdToggleReady()
    {
        // Owner doesn't need to ready up — they control start
        if (isRoomOwner) return;

        CmdChangeReadyState(!readyToBegin);
    }

    // ─── Ready State ──────────────────────────────────────────────

    /// <summary>
    /// Override to notify UI when ready state changes.
    /// </summary>
    public override void ReadyStateChanged(bool oldReady, bool newReady)
    {
        NotifyUpdate();
    }

    // ─── SyncVar Hooks ────────────────────────────────────────────

    private void OnNameChanged(string oldVal, string newVal) => NotifyUpdate();
    private void OnColorChanged(Color oldVal, Color newVal) => NotifyUpdate();
    private void OnOwnerChanged(bool oldVal, bool newVal) => NotifyUpdate();

    private void NotifyUpdate()
    {
        OnAnyPlayerDataChanged?.Invoke();
    }

    // ─── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Get the local LobbyPlayer instance.
    /// </summary>
    public static LobbyPlayer LocalPlayer
    {
        get
        {
            if (NetworkClient.localPlayer == null) return null;
            return NetworkClient.localPlayer.GetComponent<LobbyPlayer>();
        }
    }
}
