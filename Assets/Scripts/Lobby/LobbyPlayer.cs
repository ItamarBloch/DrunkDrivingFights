using UnityEngine;
using Mirror;

public class LobbyPlayer : NetworkRoomPlayer
{
    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "Player";

    [SyncVar(hook = nameof(OnColorChanged))]
    public Color playerColor = Color.white;

    [SyncVar(hook = nameof(OnOwnerChanged))]
    public bool isRoomOwner = false;

    [SyncVar]
    public int playerSlot = -1;

    // Room info synced from server to all clients
    [SyncVar]
    public string syncedRoomName = "";

    [SyncVar]
    public string syncedMapName = "";

    [SyncVar]
    public int syncedMaxPlayers = 6;

    [SyncVar]
    public string syncedRoomCode = "";

    public static readonly Color[] AvailableColors = new Color[]
    {
        new Color(0.9f, 0.2f, 0.2f),  // Red
        new Color(0.2f, 0.5f, 0.9f),  // Blue
        new Color(0.2f, 0.9f, 0.3f),  // Green
        new Color(0.9f, 0.9f, 0.2f),  // Yellow
        new Color(0.9f, 0.5f, 0.1f),  // Orange
        new Color(0.7f, 0.2f, 0.9f),  // Purple
    };

    public static event System.Action OnAnyPlayerDataChanged;

    public override void OnStartClient()
    {
        base.OnStartClient();

        // CRITICAL: Disable Mirror's built-in room player GUI
        showRoomGUI = false;

        if (isOwned)
        {
            string name = PlayerPrefs.GetString("PlayerName", $"Player_{Random.Range(100, 999)}");
            CmdSetName(name);

            int colorIndex = (int)(netId % (uint)AvailableColors.Length);
            CmdSetColor(AvailableColors[colorIndex]);
        }

        NotifyUpdate();
    }

    // CRITICAL: Override OnGUI to prevent Mirror's default "Ready" button
    public override void OnGUI() { }

    [Command]
    public void CmdSetName(string newName)
    {
        playerName = newName.Length > 20 ? newName.Substring(0, 20) : newName;
    }

    [Command]
    public void CmdSetColor(Color newColor)
    {
        playerColor = newColor;
    }

    /// <summary>
    /// Called by UI button. NOT a Command � it calls CmdChangeReadyState 
    /// which IS Mirror's built-in Command for changing ready state.
    /// Must be called on the client (isOwned).
    /// </summary>
    public void ToggleReady()
    {
        if (!isOwned) return;
        if (isRoomOwner) return;

        // CmdChangeReadyState is Mirror's built-in Command on NetworkRoomPlayer
        CmdChangeReadyState(!readyToBegin);
        Debug.Log($"[Lobby] {playerName} toggling ready -> {!readyToBegin}");
    }

    public override void ReadyStateChanged(bool oldReady, bool newReady)
    {
        Debug.Log($"[Lobby] {playerName} ready state: {oldReady} -> {newReady}");
        NotifyUpdate();
    }

    private void OnNameChanged(string oldVal, string newVal) => NotifyUpdate();
    private void OnColorChanged(Color oldVal, Color newVal) => NotifyUpdate();
    private void OnOwnerChanged(bool oldVal, bool newVal) => NotifyUpdate();

    private void NotifyUpdate()
    {
        OnAnyPlayerDataChanged?.Invoke();
    }

    public static LobbyPlayer LocalPlayer
    {
        get
        {
            if (NetworkClient.localPlayer == null) return null;
            return NetworkClient.localPlayer.GetComponent<LobbyPlayer>();
        }
    }
}
