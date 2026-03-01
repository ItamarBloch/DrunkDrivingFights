using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Core lobby/room manager for the vehicular combat game.
/// Extends Mirror's NetworkRoomManager to handle:
/// - Room creation with settings (name, max players, map)
/// - Quick match (auto-join or create)
/// - Owner authority to start the game
/// - Smooth lobby → game scene transition
/// 
/// Setup:
/// 1. Create an empty GameObject in your Lobby scene, attach this script
/// 2. Assign RoomPlayerPrefab (LobbyPlayer prefab) and GamePlayerPrefab (Car prefab)
/// 3. Set RoomScene to your Lobby scene name and GameplayScene to your Game scene name
/// 4. Both scenes must be in Build Settings
/// </summary>
public class GameNetworkRoomManager : NetworkRoomManager
{
    // ─── Singleton ────────────────────────────────────────────────
    public static new GameNetworkRoomManager singleton { get; private set; }

    // ─── Room Settings (synced from host) ─────────────────────────
    [Header("Room Configuration")]
    [SerializeField] private int defaultMaxPlayers = 6;
    [SerializeField] private string[] availableMaps = { "Arena_Desert", "Arena_City", "Arena_Factory" };

    // Current room info (set by host before starting)
    [HideInInspector] public string roomName = "New Room";
    [HideInInspector] public string selectedMap = "Arena_Desert";

    // ─── Room Discovery ───────────────────────────────────────────
    // Rooms advertised on LAN — for online play you'd swap this
    // for a relay/matchmaker service
    private GameRoomDiscovery discovery;

    // ─── Events ───────────────────────────────────────────────────
    public event System.Action OnLobbyPlayersUpdated;
    public event System.Action<string> OnRoomCreated;
    public event System.Action OnGameStarting;
    public event System.Action<string> OnJoinFailed;

    // ─── State ────────────────────────────────────────────────────
    public bool IsOwner => NetworkServer.active && NetworkClient.isConnected; // host = owner
    public int CurrentPlayerCount => roomSlots.Count(s => s != null);
    public bool AllPlayersReady => roomSlots.All(s => s == null || s.readyToBegin);
    public string[] AvailableMaps => availableMaps;

    // ─── Lifecycle ────────────────────────────────────────────────

    public override void Awake()
    {
        if (singleton != null && singleton != this)
        {
            Destroy(gameObject);
            return;
        }
        singleton = this;
        DontDestroyOnLoad(gameObject);

        base.Awake();
    }

    public override void Start()
    {
        base.Start();

        maxConnections = defaultMaxPlayers;

        // Setup discovery component (add programmatically if not present)
        discovery = GetComponent<GameRoomDiscovery>();
        if (discovery == null)
            discovery = gameObject.AddComponent<GameRoomDiscovery>();
    }

    // ─── Room Creation ────────────────────────────────────────────

    /// <summary>
    /// Create a new room. The caller becomes the host/owner.
    /// </summary>
    public void CreateRoom(string name, int maxPlayers, string map)
    {
        roomName = name;
        maxConnections = Mathf.Clamp(maxPlayers, 2, 12);
        selectedMap = map;

        // Set the gameplay scene based on map selection
        // (In production you'd map names → scene names)
        GameplayScene = map;

        // Start hosting
        StartHost();

        // Start advertising this room on LAN
        discovery.AdvertiseRoom(roomName, maxConnections, selectedMap);

        OnRoomCreated?.Invoke(roomName);

        Debug.Log($"[Lobby] Room '{roomName}' created | Map: {map} | Max: {maxPlayers}");
    }

    /// <summary>
    /// Quick Match: find an available room and join, or create one.
    /// </summary>
    public void QuickMatch()
    {
        Debug.Log("[Lobby] Quick match — searching for rooms...");

        discovery.FindRooms((rooms) =>
        {
            // Filter to rooms that aren't full
            var available = rooms.Where(r => r.currentPlayers < r.maxPlayers).ToList();

            if (available.Count > 0)
            {
                // Join the first available room
                var room = available[0];
                JoinRoom(room.address, room.port);
                Debug.Log($"[Lobby] Quick match — joining '{room.roomName}'");
            }
            else
            {
                // No rooms found — create one with defaults
                string autoName = $"Game_{System.DateTime.Now:HHmmss}";
                CreateRoom(autoName, defaultMaxPlayers, availableMaps[0]);
                Debug.Log("[Lobby] Quick match — no rooms found, created new room");
            }
        });
    }

    /// <summary>
    /// Join a specific room by address.
    /// </summary>
    public void JoinRoom(string address, int port = 7777)
    {
        networkAddress = address;

        // If using KCP transport (Mirror default), set the port
        var transport = Transport.active;
        if (transport is kcp2k.KcpTransport kcpTransport)
            kcpTransport.port = (ushort)port;

        StartClient();
        Debug.Log($"[Lobby] Joining room at {address}:{port}");
    }

    /// <summary>
    /// Leave the current room. Host leaving destroys the room.
    /// </summary>
    public void LeaveRoom()
    {
        if (IsOwner)
        {
            discovery.StopAdvertising();
            StopHost();
            Debug.Log("[Lobby] Host left — room closed");
        }
        else
        {
            StopClient();
            Debug.Log("[Lobby] Left room");
        }
    }

    /// <summary>
    /// Owner starts the game — transitions everyone to the game scene.
    /// </summary>
    public void StartGame()
    {
        if (!IsOwner)
        {
            Debug.LogWarning("[Lobby] Only the room owner can start the game!");
            return;
        }

        if (!AllPlayersReady)
        {
            Debug.LogWarning("[Lobby] Not all players are ready!");
            return;
        }

        if (CurrentPlayerCount < 1)
        {
            Debug.LogWarning("[Lobby] Need at least 1 player to start!");
            return;
        }

        OnGameStarting?.Invoke();
        discovery.StopAdvertising();

        Debug.Log($"[Lobby] Starting game on map '{selectedMap}' with {CurrentPlayerCount} players");

        // This triggers Mirror's room → game scene transition
        // All room players get replaced with game players (car prefabs)
        ServerChangeScene(GameplayScene);
    }

    // ─── Mirror Room Callbacks ────────────────────────────────────

    /// <summary>
    /// Called on server when a new player connects to the room.
    /// </summary>
    public override void OnRoomServerConnect(NetworkConnectionToClient conn)
    {
        base.OnRoomServerConnect(conn);
        Debug.Log($"[Lobby] Player connected: {conn.connectionId}");
    }

    /// <summary>
    /// Called on server when a player disconnects from the room.
    /// </summary>
    public override void OnRoomServerDisconnect(NetworkConnectionToClient conn)
    {
        base.OnRoomServerDisconnect(conn);
        Debug.Log($"[Lobby] Player disconnected: {conn.connectionId}");

        // Notify UI
        OnLobbyPlayersUpdated?.Invoke();
    }

    /// <summary>
    /// Called on server when a room player is created.
    /// We use this to assign ownership info.
    /// </summary>
    public override GameObject OnRoomServerCreateRoomPlayer(NetworkConnectionToClient conn)
    {
        // Use base behavior to instantiate the RoomPlayerPrefab
        GameObject roomPlayerObj = base.OnRoomServerCreateRoomPlayer(conn);

        // Mark the first player (host) as the room owner
        if (roomPlayerObj.TryGetComponent<LobbyPlayer>(out var lobbyPlayer))
        {
            // Connection 0 is always the host
            lobbyPlayer.isRoomOwner = (conn.connectionId == 0);
        }

        return roomPlayerObj;
    }

    /// <summary>
    /// Called on server when a game player needs to be created for a room player.
    /// This is where room player → car player conversion happens.
    /// </summary>
    public override GameObject OnRoomServerCreateGamePlayer(
        NetworkConnectionToClient conn, GameObject roomPlayer)
    {
        // Get the lobby player's chosen data
        LobbyPlayer lobby = roomPlayer.GetComponent<LobbyPlayer>();

        // Calculate a spawn position (spread players out)
        int index = roomSlots.ToList().IndexOf(lobby);
        Vector3 spawnPos = GetSpawnPosition(index);
        Quaternion spawnRot = Quaternion.Euler(0f, index * 60f, 0f);

        // Instantiate the game player (car prefab)
        GameObject carPlayer = Instantiate(
            playerPrefab.gameObject, spawnPos, spawnRot);

        // Transfer lobby data to the car if needed
        // (e.g., player name, color, team)
        if (carPlayer.TryGetComponent<PlayerInfo>(out var playerInfo) && lobby != null)
        {
            playerInfo.playerName = lobby.playerName;
            playerInfo.playerColor = lobby.playerColor;
        }

        Debug.Log($"[Game] Spawned car for player '{lobby?.playerName}' at {spawnPos}");

        return carPlayer;
    }

    /// <summary>
    /// Calculate spawn positions in a circle around the arena center.
    /// </summary>
    private Vector3 GetSpawnPosition(int playerIndex)
    {
        float radius = 20f;
        float angle = playerIndex * (360f / Mathf.Max(maxConnections, 1)) * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Cos(angle) * radius,
            1f, // slightly above ground
            Mathf.Sin(angle) * radius
        );
    }

    /// <summary>
    /// Called on server when all players are ready. 
    /// We override to NOT auto-start — owner must manually press Start.
    /// </summary>
    public override void OnRoomServerPlayersReady()
    {
        // Do NOT call base — base auto-starts the game
        // We want the owner to manually start
        Debug.Log("[Lobby] All players are ready! Waiting for owner to start.");
        OnLobbyPlayersUpdated?.Invoke();
    }

    /// <summary>
    /// Called on every client when the room player list changes.
    /// </summary>
    public override void OnRoomClientSceneChanged()
    {
        base.OnRoomClientSceneChanged();
        OnLobbyPlayersUpdated?.Invoke();
    }

    // ─── Client Callbacks ─────────────────────────────────────────

    public override void OnRoomClientConnect()
    {
        base.OnRoomClientConnect();
        Debug.Log("[Lobby] Connected to room");
    }

    public override void OnRoomClientDisconnect()
    {
        base.OnRoomClientDisconnect();
        Debug.Log("[Lobby] Disconnected from room");
    }

    public override void OnClientError(TransportError error, string reason)
    {
        base.OnClientError(error, reason);
        OnJoinFailed?.Invoke($"Connection failed: {reason}");
        Debug.LogError($"[Lobby] Client error: {error} — {reason}");
    }

    // ─── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Get all current lobby players.
    /// </summary>
    public List<LobbyPlayer> GetLobbyPlayers()
    {
        return roomSlots
            .Where(s => s != null)
            .Select(s => s.GetComponent<LobbyPlayer>())
            .Where(lp => lp != null)
            .ToList();
    }

    /// <summary>
    /// Refresh available rooms (calls discovery).
    /// </summary>
    public void RefreshRoomList(System.Action<List<DiscoveredRoom>> callback)
    {
        discovery.FindRooms(callback);
    }
}
