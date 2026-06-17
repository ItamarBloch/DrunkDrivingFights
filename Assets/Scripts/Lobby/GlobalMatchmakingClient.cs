using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Global matchmaking HTTP client.
///
/// When hosting: registers the session with a central REST server on start,
/// sends a heartbeat every <heartbeatInterval> seconds, and unregisters on stop.
///
/// When searching: fetches the list of active sessions and returns them as
/// DiscoveredRoom objects for GameNetworkRoomManager. This server is the single
/// source of truth for joinable rooms (LAN discovery was removed).
///
/// Architecture:
///   Host  → POST   /sessions          (register)
///   Host  → PATCH  /sessions/:id      (heartbeat + player count update)
///   Host  → DELETE /sessions/:id      (unregister)
///   Client→ GET    /sessions          (list all active sessions)
///
/// The server auto-detects the host's public IP from the HTTP request, so no
/// manual IP configuration is needed on the host side.  The host DOES need
/// port 7777 (or their configured game port) forwarded on their router for
/// remote clients to actually connect.
/// </summary>
public class GlobalMatchmakingClient : MonoBehaviour
{
    [Header("Matchmaking Server")]
    [Tooltip("Full base URL of your matchmaking server (e.g. https://my-server.railway.app). Leave empty to disable global matchmaking.")]
    [SerializeField] private string serverUrl = "";

    [Tooltip("Seconds between heartbeats while hosting. Server expires sessions after 60s.")]
    [SerializeField] private float heartbeatInterval = 20f;

    [Tooltip("HTTP request timeout in seconds.")]
    [SerializeField] private int requestTimeout = 8;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>True if a server URL has been configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(serverUrl);

    /// <summary>True while this client has an active registered session (i.e. we are hosting).</summary>
    public bool IsRegistered => !string.IsNullOrEmpty(_sessionId);

    // ── State ─────────────────────────────────────────────────────────────────

    private string _sessionId;
    private string _sessionToken;
    private Coroutine _heartbeatCoroutine;

    // ── Host: Register ────────────────────────────────────────────────────────

    /// <summary>
    /// Register a new hosted session with the global server.
    /// Call this after StartHost() succeeds.
    /// </summary>
    public void RegisterSession(string roomName, int maxPlayers, string mapName,
        bool hasPassword, string roomCode, int port, string relayJoinCode = null,
        Action<bool> callback = null)
    {
        if (!IsConfigured)
        {
            Debug.Log("[Global] Server URL not configured — global matchmaking disabled");
            callback?.Invoke(false);
            return;
        }

        StartCoroutine(DoRegisterSession(roomName, maxPlayers, mapName,
            hasPassword, roomCode, port, relayJoinCode, callback));
    }

    /// <summary>
    /// Unregister the session and stop the heartbeat.
    /// Call this when stopping the host (LeaveRoom / ReturnToLobby).
    /// </summary>
    public void UnregisterSession()
    {
        StopHeartbeat();

        if (!IsConfigured || !IsRegistered) return;

        // Fire-and-forget — we don't need to wait for the response
        StartCoroutine(DoUnregisterSession(_sessionId, _sessionToken));
        _sessionId = null;
        _sessionToken = null;
    }

    /// <summary>
    /// Push the current player count to the server immediately.
    /// Called automatically by the heartbeat; also call manually when a player joins/leaves.
    /// </summary>
    public void UpdatePlayerCount(int currentPlayers)
    {
        if (!IsConfigured || !IsRegistered) return;
        StartCoroutine(DoHeartbeat(_sessionId, _sessionToken, currentPlayers));
    }

    // ── Client: Discover ──────────────────────────────────────────────────────

    /// <summary>
    /// Fetch all active sessions from the global server.
    /// Results are converted to DiscoveredRoom for seamless merging with LAN rooms.
    /// On failure or if not configured, returns an empty list (does not throw).
    /// </summary>
    public void FetchSessions(Action<List<DiscoveredRoom>> callback)
    {
        if (!IsConfigured)
        {
            callback?.Invoke(new List<DiscoveredRoom>());
            return;
        }

        StartCoroutine(DoFetchSessions(callback));
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private void StopHeartbeat()
    {
        if (_heartbeatCoroutine != null)
        {
            StopCoroutine(_heartbeatCoroutine);
            _heartbeatCoroutine = null;
        }
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    private IEnumerator DoRegisterSession(string roomName, int maxPlayers, string mapName,
        bool hasPassword, string roomCode, int port, string relayJoinCode,
        Action<bool> callback)
    {
        var body = new SessionRegisterRequest
        {
            roomName = roomName,
            maxPlayers = maxPlayers,
            currentPlayers = 1,
            mapName = mapName,
            hasPassword = hasPassword,
            roomCode = roomCode,
            port = port,
            relayJoinCode = relayJoinCode ?? ""
        };

        string json = JsonUtility.ToJson(body);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest($"{serverUrl}/sessions", "POST");
        req.uploadHandler = new UploadHandlerRaw(bytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = requestTimeout;

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<SessionRegisterResponse>(req.downloadHandler.text);
            _sessionId = response.id;
            _sessionToken = response.token;

            Debug.Log($"[Global] Session registered (id={_sessionId})");
            _heartbeatCoroutine = StartCoroutine(HeartbeatLoop());
            callback?.Invoke(true);
        }
        else
        {
            Debug.LogWarning($"[Global] Failed to register session: {req.error}");
            callback?.Invoke(false);
        }
    }

    private IEnumerator DoUnregisterSession(string id, string token)
    {
        var body = new SessionTokenRequest { token = token };
        string json = JsonUtility.ToJson(body);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest($"{serverUrl}/sessions/{id}", "DELETE");
        req.uploadHandler = new UploadHandlerRaw(bytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = requestTimeout;

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log("[Global] Session unregistered");
        else
            Debug.LogWarning($"[Global] Failed to unregister session: {req.error}");
    }

    private IEnumerator DoHeartbeat(string id, string token, int currentPlayers)
    {
        var body = new SessionHeartbeatRequest { token = token, currentPlayers = currentPlayers };
        string json = JsonUtility.ToJson(body);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest($"{serverUrl}/sessions/{id}", "PATCH");
        req.uploadHandler = new UploadHandlerRaw(bytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = requestTimeout;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[Global] Heartbeat failed: {req.error}");
    }

    private IEnumerator HeartbeatLoop()
    {
        while (IsRegistered)
        {
            yield return new WaitForSeconds(heartbeatInterval);
            if (!IsRegistered) yield break;

            int currentPlayers = GameNetworkRoomManager.singleton != null
                ? GameNetworkRoomManager.singleton.CurrentPlayerCount
                : 1;

            yield return DoHeartbeat(_sessionId, _sessionToken, currentPlayers);
        }
    }

    private IEnumerator DoFetchSessions(Action<List<DiscoveredRoom>> callback)
    {
        using var req = UnityWebRequest.Get($"{serverUrl}/sessions");
        req.timeout = requestTimeout;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[Global] Failed to fetch sessions: {req.error}");
            callback?.Invoke(new List<DiscoveredRoom>());
            yield break;
        }

        // The server returns a JSON array. JsonUtility can't parse arrays at root level,
        // so we wrap it in an object before parsing.
        string wrapped = $"{{\"sessions\":{req.downloadHandler.text}}}";
        var wrapper = JsonUtility.FromJson<SessionListWrapper>(wrapped);

        var rooms = new List<DiscoveredRoom>();
        if (wrapper?.sessions != null)
        {
            foreach (var s in wrapper.sessions)
            {
                rooms.Add(new DiscoveredRoom
                {
                    roomName       = s.roomName,
                    address        = s.address,
                    port           = s.port,
                    currentPlayers = s.currentPlayers,
                    maxPlayers     = s.maxPlayers,
                    mapName        = s.mapName,
                    serverId       = s.serverId,
                    lastSeen       = DateTime.Now,
                    hasPassword    = s.hasPassword,
                    roomCode       = s.roomCode,
                    relayJoinCode  = s.relayJoinCode
                });
            }
        }

        Debug.Log($"[Global] Fetched {rooms.Count} global session(s)");
        callback?.Invoke(rooms);
    }
}

// ── Discovered room ───────────────────────────────────────────────────────────

/// <summary>
/// A joinable room returned by the global matchmaking server. This used to be
/// shared with LAN discovery; LAN has been removed, so the global client is now
/// the sole producer of these.
/// </summary>
[Serializable]
public class DiscoveredRoom
{
    public string roomName;
    public string address;
    public int port;
    public int currentPlayers;
    public int maxPlayers;
    public string mapName;
    public long serverId;
    public DateTime lastSeen;
    public bool hasPassword;
    public string roomCode;
    public string relayJoinCode;
}

// ── JSON DTOs (kept internal to this file) ────────────────────────────────────

[Serializable]
internal class SessionRegisterRequest
{
    public string roomName;
    public int    maxPlayers;
    public int    currentPlayers;
    public string mapName;
    public bool   hasPassword;
    public string roomCode;
    public int    port;
    public string relayJoinCode;
}

[Serializable]
internal class SessionRegisterResponse
{
    public string id;
    public string token;
}

[Serializable]
internal class SessionHeartbeatRequest
{
    public string token;
    public int    currentPlayers;
}

[Serializable]
internal class SessionTokenRequest
{
    public string token;
}

[Serializable]
internal class GlobalSessionData
{
    public string id;
    public string address;
    public string roomName;
    public int    maxPlayers;
    public int    currentPlayers;
    public string mapName;
    public bool   hasPassword;
    public string roomCode;
    public int    port;
    public long   serverId;
    public string relayJoinCode;
}

[Serializable]
internal class SessionListWrapper
{
    public List<GlobalSessionData> sessions;
}
