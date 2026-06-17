using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay.Models;

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

    private PasswordAuthenticator passwordAuthenticator;
    private GlobalMatchmakingClient globalClient;
    private kcp2k.KcpTransport kcpTransport;
    private RelayTransport relayTransport;

    private string _pendingRelayJoinCode;

    // Events for UI
    public event Action OnLobbyPlayersUpdated;
    public event Action<string> OnRoomCreated;
    public event Action OnGameStarting;
    public event Action<string> OnJoinFailed;

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
        kcpTransport = GetComponent<kcp2k.KcpTransport>();
        if (kcpTransport == null)
            kcpTransport = gameObject.AddComponent<kcp2k.KcpTransport>();
        if (Transport.active == null)
        {
            Transport.active = kcpTransport;
            transport = kcpTransport;
        }

        relayTransport = GetComponent<RelayTransport>();
        if (relayTransport == null)
            relayTransport = gameObject.AddComponent<RelayTransport>();

        // Global matchmaking client — HTTP-based, works over internet
        globalClient = GetComponent<GlobalMatchmakingClient>();
        if (globalClient == null)
            globalClient = gameObject.AddComponent<GlobalMatchmakingClient>();

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

        selectedMap = mapRegistry.GetMap(0).sceneName;
        mapRegistry.ValidateMaps();

        Debug.Log($"[Lobby] Ready — {mapRegistry.Count} map(s) available | Global matchmaking: {(globalClient.IsConfigured ? "enabled" : "disabled (no server URL)")}");
    }

    public override void OnGUI() { }

    // ─── Room Actions ─────────────────────────────────────────────

    /// <summary>
    /// Create a room using a map index from the MapRegistry.
    /// </summary>
    public async void CreateRoom(string name, int maxPlayers, int mapIndex, string password = "")
    {
        if (NetworkServer.active)
        {
            globalClient.UnregisterSession();
            StopHost();
        }
        else if (NetworkClient.active)
        {
            StopClient();
        }
        RestoreKcpTransport();

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
        _pendingRelayJoinCode = null;

        if (passwordAuthenticator != null)
            passwordAuthenticator.clientPasswordHash = string.IsNullOrEmpty(password) ? 0 : password.GetHashCode();

        Debug.Log($"[Lobby] Creating room '{roomName}' | Map: {map.displayName} | Code: {roomCode} | Max: {maxConnections} | Password: {(string.IsNullOrEmpty(password) ? "none" : "set")}");

        if (globalClient != null && globalClient.IsConfigured)
        {
            try
            {
                await RelayManager.InitializeAsync();
                var (allocation, joinCode) = await RelayManager.AllocateRelayAsync(maxConnections);
                _pendingRelayJoinCode = joinCode;
                relayTransport.ConfigureAsHost(new RelayServerData(allocation, "dtls"));
                Transport.active = relayTransport;
                transport = relayTransport;
                Debug.Log($"[Lobby] Relay allocated — join code: {joinCode}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lobby] Relay failed, falling back to LAN-only: {e.Message}");
                _pendingRelayJoinCode = null;
                RestoreKcpTransport();
            }
        }

        try
        {
            StartHost();
            OnRoomCreated?.Invoke(roomName);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            Debug.LogWarning($"[Lobby] Cannot host — port already in use: {e.Message}");
            OnJoinFailed?.Invoke("Port already in use. A game is already running on this machine.");
            RestoreKcpTransport();
        }
    }

    private string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 6; i++)
            sb.Append(chars[UnityEngine.Random.Range(0, chars.Length)]);
        return sb.ToString();
    }

    public void JoinRoom(string address, int port = 7777, string password = "")
    {
        if (NetworkClient.active && !NetworkServer.active)
            StopClient();

        RestoreKcpTransport();

        if (passwordAuthenticator != null)
            passwordAuthenticator.clientPasswordHash = string.IsNullOrEmpty(password) ? 0 : password.GetHashCode();

        networkAddress = address;
        if (Transport.active is kcp2k.KcpTransport kcp)
            kcp.port = (ushort)port;

        Debug.Log($"[Lobby] Joining room at {address}:{port}");
        StartClient();
    }

    public async void JoinRoomViaRelay(string relayJoinCode, string password = "")
    {
        if (NetworkClient.active && !NetworkServer.active)
            StopClient();

        try
        {
            await RelayManager.InitializeAsync();
            var joinAllocation = await RelayManager.JoinRelayAsync(relayJoinCode);

            relayTransport.ConfigureAsClient(new RelayServerData(joinAllocation, "dtls"));
            Transport.active = relayTransport;
            transport = relayTransport;

            if (passwordAuthenticator != null)
                passwordAuthenticator.clientPasswordHash = string.IsNullOrEmpty(password) ? 0 : password.GetHashCode();

            networkAddress = "relay";
            Debug.Log($"[Lobby] Joining room via relay (code: {relayJoinCode})");
            StartClient();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] Relay join failed: {e.Message}");
            RestoreKcpTransport();
            OnJoinFailed?.Invoke("Failed to connect via relay. The room may have closed.");
        }
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
            globalClient.UnregisterSession();
            StopHost();
        }
        else
        {
            StopClient();
        }
        RestoreKcpTransport();
    }

    /// <summary>
    /// Returns all players to the lobby while keeping the room alive.
    /// Host: ServerChangeScene(RoomScene) — all connected clients follow.
    /// Client: StopClient() + load lobby scene.
    /// </summary>
    public void ReturnToLobby()
    {
        if (IsOwner)
        {
            globalClient.UnregisterSession();
            ServerChangeScene(RoomScene);
        }
        else
        {
            StopClient();
            RestoreKcpTransport();
            SceneManager.LoadScene("LobbyScene");
        }
    }

    public void StartGame()
    {
        if (!IsOwner) return;
        if (!AllPlayersReady) { Debug.LogWarning("[Lobby] Not all players ready!"); return; }
        if (CurrentPlayerCount < 1) return;

        OnGameStarting?.Invoke();
        globalClient.UnregisterSession();

        // We force-start from the owner button instead of going through Mirror's
        // CheckReadyToBegin() flow, so pendingPlayers is never cleared the way the
        // normal ready-up path would clear it. Stale entries (e.g. the host re-added
        // after returning to the lobby) get spawned too early in OnServerSceneChanged
        // — before the local client is ready — which corrupts the connection's player
        // object and stops OnStartLocalPlayer from firing on the second match.
        // Clear it here so everyone spawns via the normal post-scene-load ready path.
        pendingPlayers.Clear();

        Debug.Log($"[Lobby] Starting game — {CurrentPlayerCount} players");
        ServerChangeScene(GameplayScene);
    }

    // ─── Server Callbacks ─────────────────────────────────────────

    public override void OnRoomStartServer()
    {
        foreach (var slot in roomSlots)
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
        roomSlots.Clear();

        base.OnRoomStartServer();
        Debug.Log("[Lobby] Server started");

        // Register with the central matchmaking server (includes relay join code).
        RegisterRoomGlobally();
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

        if (MatchManager.singleton != null && conn.identity != null)
            MatchManager.singleton.HandlePlayerDisconnected(conn.identity.netId);

        // Push updated count to global server after slight delay so roomSlots updates first
        Invoke(nameof(PushPlayerCountToGlobal), 0.3f);
        Invoke(nameof(FirePlayersUpdated), 0.3f);
    }

    private void FirePlayersUpdated()
    {
        Debug.Log($"[Lobby] Players: {CurrentPlayerCount}/{maxConnections}");
        OnLobbyPlayersUpdated?.Invoke();
    }

    private void PushPlayerCountToGlobal()
    {
        globalClient?.UpdatePlayerCount(CurrentPlayerCount);
    }

    public override GameObject OnRoomServerCreateRoomPlayer(NetworkConnectionToClient conn)
    {
        GameObject obj = Instantiate(roomPlayerPrefab.gameObject, Vector3.zero, Quaternion.identity);
        if (obj == null)
        {
            Debug.LogError("[Lobby] Failed to instantiate room player!");
            return null;
        }

        if (obj.TryGetComponent<LobbyPlayer>(out var lp))
        {
            lp.isRoomOwner = (conn.connectionId == 0);
            lp.syncedRoomName = roomName;
            lp.syncedMapName = selectedMap;
            lp.syncedMaxPlayers = maxConnections;
            lp.syncedRoomCode = roomCode;

            Debug.Log($"[Lobby] Room player created | conn: {conn.connectionId} | owner: {lp.isRoomOwner}");
        }

        // After a small delay (so roomSlots is updated), push count to global
        Invoke(nameof(PushPlayerCountToGlobal), 0.5f);

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
        if (sceneName != RoomScene)
        {
            // Randomise spawn order once per match, before any player is spawned.
            SpawnPoint.ShuffleForMatch();
        }
        else
        {
            // Back in the lobby after a match (ReturnToLobby unregistered the room
            // before the scene change). Re-advertise so the still-open room shows up
            // in Browse Rooms again. Guarded against duplicates by IsRegistered.
            RegisterRoomGlobally();
        }
    }

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
        if (!NetworkServer.active)
        {
            foreach (var slot in roomSlots)
            {
                if (slot != null && slot.gameObject != null)
                    Destroy(slot.gameObject);
            }
            roomSlots.Clear();
        }

        base.OnRoomClientConnect();
        Debug.Log("[Lobby] Connected to room");
    }

    public override void OnRoomClientDisconnect()
    {
        base.OnRoomClientDisconnect();
        Debug.Log("[Lobby] Disconnected from room");

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

    // ─── Room List (Global matchmaking server) ────────────────────

    /// <summary>
    /// Fetches the list of joinable rooms from the global matchmaking server.
    /// (LAN discovery was removed — the server is the single source of truth, which
    /// matches how real remote clients see the game.)
    /// </summary>
    public void RefreshRoomList(Action<List<DiscoveredRoom>> callback)
    {
        if (globalClient == null || !globalClient.IsConfigured)
        {
            Debug.LogWarning("[Lobby] Global matchmaking not configured — no rooms to list");
            callback?.Invoke(new List<DiscoveredRoom>());
            return;
        }

        globalClient.FetchSessions(callback);
    }

    /// <summary>
    /// Registers the current room with the global matchmaking server so it appears in
    /// Browse Rooms. Safe to call multiple times — skips if already registered, so the
    /// initial host start and the return-to-lobby re-advertise never create duplicates.
    /// </summary>
    private void RegisterRoomGlobally()
    {
        if (globalClient == null || !globalClient.IsConfigured) return;
        if (globalClient.IsRegistered) return;

        bool hasPassword = !string.IsNullOrEmpty(roomPassword);
        int port = GetGamePort();

        globalClient.RegisterSession(
            roomName, maxConnections, selectedMap,
            hasPassword, roomCode, port, _pendingRelayJoinCode,
            success => Debug.Log(success
                ? "[Lobby] Globally registered"
                : "[Lobby] Global registration failed"));
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

    private int GetGamePort()
    {
        if (Transport.active is kcp2k.KcpTransport kcp)
            return kcp.port;
        return 7777;
    }

    private void RestoreKcpTransport()
    {
        if (kcpTransport == null) return;
        Transport.active = kcpTransport;
        transport = kcpTransport;
    }
}
