using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using System.Collections.Generic;
using System.Linq;

public class GameNetworkRoomManager : NetworkRoomManager
{
    public static new GameNetworkRoomManager singleton { get; private set; }

    [Header("Room Configuration")]
    [SerializeField] private int defaultMaxPlayers = 6;

    [Header("Map Registry")]
    [Tooltip("Assign the MapRegistry ScriptableObject asset here")]
    [SerializeField] private MapRegistry mapRegistry;

    [HideInInspector] public string roomName = "New Room";
    [HideInInspector] public string selectedMap = "";
    [HideInInspector] public string roomCode = "";
    [HideInInspector] public string roomPassword = "";

    private GameRoomDiscovery discovery;
    private PasswordAuthenticator passwordAuthenticator;

    // Events for UI
    public event System.Action OnLobbyPlayersUpdated;
    public event System.Action<string> OnRoomCreated;
    public event System.Action OnGameStarting;
    public event System.Action<string> OnJoinFailed;

    // State
    public bool IsOwner => NetworkServer.active && NetworkClient.isConnected;
    public int CurrentPlayerCount => roomSlots.Count(s => s != null);
    public bool AllPlayersReady
    {
        get
        {
            foreach (var slot in roomSlots)
            {
                if (slot == null) continue;
                var lp = slot.GetComponent<LobbyPlayer>();
                if (lp != null && lp.isRoomOwner) continue;
                if (!slot.readyToBegin) return false;
            }
            return true;
        }
    }

    /// <summary> The map registry — single source of truth for all maps. </summary>
    public MapRegistry MapRegistry => mapRegistry;

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

        // Transport — required before base.Awake
        if (Transport.active == null)
        {
            var kcp = GetComponent<kcp2k.KcpTransport>();
            if (kcp == null)
                kcp = gameObject.AddComponent<kcp2k.KcpTransport>();
            Transport.active = kcp;
            transport = kcp;
        }

        // Discovery — set up early so it's ready for all callbacks
        discovery = GetComponent<GameRoomDiscovery>();
        if (discovery == null)
            discovery = gameObject.AddComponent<GameRoomDiscovery>();

        // Password authenticator — enforces passwords server-side
        passwordAuthenticator = GetComponent<PasswordAuthenticator>();
        if (passwordAuthenticator == null)
            passwordAuthenticator = gameObject.AddComponent<PasswordAuthenticator>();
        authenticator = passwordAuthenticator;

        base.Awake();
    }

    public override void Start()
    {
        base.Start();
        maxConnections = defaultMaxPlayers;
        showRoomGUI = false;

        // Validate map registry
        if (mapRegistry == null)
        {
            Debug.LogError("[Lobby] MapRegistry not assigned! Right-click in Project → Create → Game → Map Registry, then assign it.");
            return;
        }

        if (mapRegistry.Count == 0)
        {
            Debug.LogError("[Lobby] MapRegistry is empty! Add at least one map.");
            return;
        }

        // Set default map to first in registry
        selectedMap = mapRegistry.GetMap(0).sceneName;

        // Validate all scenes exist in Build Settings (editor only)
        mapRegistry.ValidateMaps();

        Debug.Log($"[Lobby] Ready — {mapRegistry.Count} map(s) available");
    }

    // Suppress Mirror's built-in GUI completely
    public override void OnGUI() { }

    // ─── Room Actions ─────────────────────────────────────────────

    /// <summary>
    /// Create a room using a map index from the MapRegistry.
    /// </summary>
    public void CreateRoom(string name, int maxPlayers, int mapIndex, string password = "")
    {
        MapData map = mapRegistry.GetMap(mapIndex);
        if (map == null)
        {
            Debug.LogError($"[Lobby] Invalid map index: {mapIndex}");
            return;
        }

        roomName = name;
        maxConnections = Mathf.Clamp(maxPlayers, 2, map.maxPlayers);
        selectedMap = map.displayName;
        GameplayScene = map.sceneName;
        roomPassword = password;
        roomCode = GenerateRoomCode();

        // Host's own client connection also goes through auth
        if (passwordAuthenticator != null)
            passwordAuthenticator.clientPasswordHash = string.IsNullOrEmpty(password) ? 0 : password.GetHashCode();

        Debug.Log($"[Lobby] Creating room '{roomName}' | Map: {map.displayName} | Code: {roomCode} | Max: {maxConnections} | Password: {(string.IsNullOrEmpty(password) ? "none" : "set")}");

        try
        {
            StartHost();
            OnRoomCreated?.Invoke(roomName);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            Debug.LogWarning($"[Lobby] Cannot host — port already in use: {e.Message}");
            OnJoinFailed?.Invoke("Port already in use. A game is already running on this machine.");
        }
    }

    private string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 6; i++)
            sb.Append(chars[Random.Range(0, chars.Length)]);
        return sb.ToString();
    }

    private static int HashPassword(string pwd) =>
        string.IsNullOrEmpty(pwd) ? 0 : pwd.GetHashCode();

    public void QuickMatch()
    {
        Debug.Log("[Lobby] Quick match — searching...");

        discovery.FindRooms((rooms) =>
        {
            var available = rooms.Where(r => r.currentPlayers < r.maxPlayers && !r.hasPassword).ToList();

            if (available.Count > 0)
            {
                var room = available[0];
                Debug.Log($"[Lobby] Quick match — joining '{room.roomName}'");
                JoinRoom(room.address, room.port);
            }
            else
            {
                // Try joining localhost first (same machine testing)
                // If that fails, then try creating a new room
                Debug.Log("[Lobby] Quick match — no rooms found, trying localhost...");
                JoinRoom("localhost", 7777);
            }
        });
    }

    public void JoinRoom(string address, int port = 7777, string password = "")
    {
        // Server enforces the password via PasswordAuthenticator — set the hash to send
        if (passwordAuthenticator != null)
            passwordAuthenticator.clientPasswordHash = string.IsNullOrEmpty(password) ? 0 : password.GetHashCode();

        networkAddress = address;
        if (Transport.active is kcp2k.KcpTransport kcpTransport)
            kcpTransport.port = (ushort)port;

        Debug.Log($"[Lobby] Joining room at {address}:{port}");
        StartClient();
    }

    /// <summary>Called by PasswordAuthenticator when the server rejects a connection.</summary>
    public void NotifyJoinFailed(string reason)
    {
        OnJoinFailed?.Invoke(reason);
    }

    /// <summary>Host kicks a player by their network identity netId.</summary>
    public void KickPlayer(uint targetNetId)
    {
        if (!NetworkServer.active) return;
        if (NetworkServer.spawned.TryGetValue(targetNetId, out var identity))
        {
            Debug.Log($"[Lobby] Kicking player netId={targetNetId}");
            identity.connectionToClient?.Disconnect();
        }
    }

    public void LeaveRoom()
    {
        if (IsOwner)
        {
            discovery.StopAdvertising();
            StopHost();
        }
        else
        {
            StopClient();
        }
    }

    /// <summary>
    /// Returns all players to the lobby while keeping the room alive.
    ///
    /// Host:   ServerChangeScene(RoomScene) — all connected clients follow automatically.
    ///         The room is preserved; a new match can be started immediately.
    ///
    /// Client: StopClient() + load lobby — they disconnect and land in the lobby UI.
    ///         If the host changes scene first, the client is taken there automatically
    ///         (and this code never runs for them).
    /// </summary>
    public void ReturnToLobby()
    {
        if (IsOwner)
        {
            discovery.StopAdvertising();
            ServerChangeScene(RoomScene);
        }
        else
        {
            StopClient();
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
        }
    }

    public void StartGame()
    {
        if (!IsOwner) return;
        if (!AllPlayersReady) { Debug.LogWarning("[Lobby] Not all players ready!"); return; }
        if (CurrentPlayerCount < 1) return;

        OnGameStarting?.Invoke();
        discovery.StopAdvertising();
        Debug.Log($"[Lobby] Starting game — {CurrentPlayerCount} players");
        ServerChangeScene(GameplayScene);
    }

    // ─── Server Callbacks ─────────────────────────────────────────

    public override void OnRoomStartServer()
    {
        base.OnRoomStartServer();
        Debug.Log("[Lobby] Server started — beginning advertisement");
        bool hasPassword = !string.IsNullOrEmpty(roomPassword);
        discovery?.AdvertiseRoom(roomName, maxConnections, selectedMap,
            hasPassword, HashPassword(roomPassword), roomCode);
    }

    public override void OnRoomServerConnect(NetworkConnectionToClient conn)
    {
        base.OnRoomServerConnect(conn);
        Debug.Log($"[Lobby] Player connected: {conn.connectionId}");
        Invoke(nameof(FirePlayersUpdated), 0.3f);
    }

    public override void OnRoomServerDisconnect(NetworkConnectionToClient conn)
    {
        base.OnRoomServerDisconnect(conn);
        Debug.Log($"[Lobby] Player disconnected: {conn.connectionId}");

        // Notify MatchManager so a mid-match disconnect counts as a death.
        if (MatchManager.singleton != null && conn.identity != null)
            MatchManager.singleton.HandlePlayerDisconnected(conn.identity.netId);

        Invoke(nameof(FirePlayersUpdated), 0.1f);
    }

    private void FirePlayersUpdated()
    {
        Debug.Log($"[Lobby] Players: {CurrentPlayerCount}/{maxConnections}");
        OnLobbyPlayersUpdated?.Invoke();
    }

    public override GameObject OnRoomServerCreateRoomPlayer(NetworkConnectionToClient conn)
    {
        // Instantiate ourselves — base can return null in some Mirror versions
        GameObject obj = Instantiate(roomPlayerPrefab.gameObject, Vector3.zero, Quaternion.identity);
        if (obj == null)
        {
            Debug.LogError("[Lobby] Failed to instantiate room player!");
            return null;
        }

        if (obj.TryGetComponent<LobbyPlayer>(out var lp))
        {
            lp.isRoomOwner = (conn.connectionId == 0);
            // Sync room info to all clients via the player object
            lp.syncedRoomName = roomName;
            lp.syncedMapName = selectedMap;
            lp.syncedMaxPlayers = maxConnections;
            lp.syncedRoomCode = roomCode;

            // Host is always considered ready via AllPlayersReady check
            // (we skip owner in the ready check instead of setting the SyncVar)

            Debug.Log($"[Lobby] Room player created | conn: {conn.connectionId} | owner: {lp.isRoomOwner}");
        }

        return obj;
    }

    public override GameObject OnRoomServerCreateGamePlayer(
        NetworkConnectionToClient conn, GameObject roomPlayer)
    {
        LobbyPlayer lobby = roomPlayer != null ? roomPlayer.GetComponent<LobbyPlayer>() : null;

        int index = lobby != null ? roomSlots.ToList().IndexOf(lobby) : conn.connectionId;
        if (index < 0) index = conn.connectionId;

        var (spawnPos, spawnRot) = SpawnPoint.Get(index, maxConnections);

        GameObject car = Instantiate(playerPrefab.gameObject, spawnPos, spawnRot);

        if (car.TryGetComponent<PlayerInfo>(out var info) && lobby != null)
        {
            info.playerName = lobby.playerName;
            info.playerColor = lobby.playerColor;
        }

        return car;
    }

    public override void OnRoomServerSceneChanged(string sceneName)
    {
        base.OnRoomServerSceneChanged(sceneName);
        // Randomise spawn order once per match, before any player is spawned.
        if (sceneName != RoomScene)
            SpawnPoint.ShuffleForMatch();
    }

    // Don't auto-start — owner clicks Start
    public override void OnRoomServerPlayersReady()
    {
        Debug.Log("[Lobby] All players ready — waiting for owner to start");
        OnLobbyPlayersUpdated?.Invoke();
    }

    public override void OnRoomClientSceneChanged()
    {
        base.OnRoomClientSceneChanged();
        OnLobbyPlayersUpdated?.Invoke();
    }

    public override void OnRoomClientConnect()
    {
        base.OnRoomClientConnect();
        Debug.Log("[Lobby] Connected to room");
    }

    public override void OnRoomClientDisconnect()
    {
        base.OnRoomClientDisconnect();
        Debug.Log("[Lobby] Disconnected from room");

        // If we're in the gameplay scene when the server drops (e.g. host quit mid-match),
        // the client would be stuck with a dead scene and no camera.
        // Load the lobby so the player lands somewhere sane.
        // RoomScene may be a full path (e.g. "Assets/Scenes/LobbyScene.unity") while
        // GetActiveScene().name is always just the bare name — strip path/extension to compare.
        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string roomSceneName = System.IO.Path.GetFileNameWithoutExtension(RoomScene);
        if (activeScene != roomSceneName)
        {
            Debug.Log("[Lobby] Server disconnected mid-match — returning to lobby");
            UnityEngine.SceneManagement.SceneManager.LoadScene(roomSceneName);
        }
    }

    public override void OnClientError(TransportError error, string reason)
    {
        base.OnClientError(error, reason);
        OnJoinFailed?.Invoke($"Connection failed: {reason}");
    }

    // ─── Helpers ──────────────────────────────────────────────────

    public List<LobbyPlayer> GetLobbyPlayers()
    {
        return roomSlots
            .Where(s => s != null)
            .Select(s => s.GetComponent<LobbyPlayer>())
            .Where(lp => lp != null)
            .ToList();
    }

    public void RefreshRoomList(System.Action<List<DiscoveredRoom>> callback)
    {
        discovery.FindRooms(callback);
    }
}
