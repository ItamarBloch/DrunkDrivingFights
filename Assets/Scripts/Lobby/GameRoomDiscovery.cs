using UnityEngine;
using Mirror;
using Mirror.Discovery;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Data about a discovered room. Sent over LAN broadcast.
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
}

/// <summary>
/// Discovery request — sent by clients searching for rooms.
/// </summary>
public struct RoomDiscoveryRequest : NetworkMessage
{
    // Empty is fine — we just need the broadcast trigger
}

/// <summary>
/// Room discovery response sent from host → searching clients.
/// </summary>
public struct RoomDiscoveryResponse : NetworkMessage
{
    public string roomName;
    public int currentPlayers;
    public int maxPlayers;
    public string mapName;
    public long serverId;
    public int port;
}

/// <summary>
/// Handles LAN-based room discovery: broadcasting room existence 
/// and finding available rooms.
/// 
/// For online play, you'd replace this with a web-based lobby service
/// (e.g., Steam Lobbies, PlayFab Matchmaking, or a custom REST API).
/// The GameNetworkRoomManager API stays the same either way.
/// </summary>
public class GameRoomDiscovery : NetworkDiscoveryBase<RoomDiscoveryRequest, RoomDiscoveryResponse>
{
    // ─── State ────────────────────────────────────────────────────

    private readonly Dictionary<long, DiscoveredRoom> discoveredRooms = new();
    private bool isAdvertising = false;

    // Room info (set by host)
    private string hostRoomName;
    private int hostMaxPlayers;
    private string hostMapName;

    // Search callback
    private Action<List<DiscoveredRoom>> searchCallback;
    private float searchTimeout = 2f;
    private float searchTimer = -1f;

    // ─── Advertising (Host Side) ──────────────────────────────────

    /// <summary>
    /// Start advertising this room on LAN.
    /// </summary>
    public void AdvertiseRoom(string roomName, int maxPlayers, string mapName)
    {
        hostRoomName = roomName;
        hostMaxPlayers = maxPlayers;
        hostMapName = mapName;
        isAdvertising = true;

        // Start the server broadcast
        StartDiscovery();

        Debug.Log($"[Discovery] Advertising room '{roomName}' on LAN");
    }

    /// <summary>
    /// Stop advertising.
    /// </summary>
    public void StopAdvertising()
    {
        if (isAdvertising)
        {
            StopDiscovery();
            isAdvertising = false;
            Debug.Log("[Discovery] Stopped advertising");
        }
    }

    // ─── Finding Rooms (Client Side) ──────────────────────────────

    /// <summary>
    /// Search for rooms on LAN. Callback fires after timeout with all found rooms.
    /// </summary>
    public void FindRooms(Action<List<DiscoveredRoom>> callback, float timeout = 2f)
    {
        discoveredRooms.Clear();
        searchCallback = callback;
        searchTimeout = timeout;
        searchTimer = timeout;

        // Start listening for broadcasts
        StartDiscovery();

        Debug.Log("[Discovery] Searching for rooms...");
    }

    private void Update()
    {
        if (searchTimer > 0)
        {
            searchTimer -= Time.deltaTime;
            if (searchTimer <= 0)
            {
                // Search complete — fire callback
                StopDiscovery();
                var rooms = discoveredRooms.Values.ToList();
                searchCallback?.Invoke(rooms);
                searchCallback = null;

                Debug.Log($"[Discovery] Search complete — found {rooms.Count} room(s)");
            }
        }
    }

    /// <summary>
    /// Get the current list of discovered rooms (useful for UI refresh).
    /// </summary>
    public List<DiscoveredRoom> GetDiscoveredRooms()
    {
        // Prune stale rooms (not seen in 10 seconds)
        var staleKeys = discoveredRooms
            .Where(kvp => (DateTime.Now - kvp.Value.lastSeen).TotalSeconds > 10)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in staleKeys)
            discoveredRooms.Remove(key);

        return discoveredRooms.Values.ToList();
    }

    // ─── Mirror Discovery Overrides ───────────────────────────────

    /// <summary>
    /// Server: Build the response to send to searching clients.
    /// </summary>
    protected override RoomDiscoveryResponse ProcessRequest(
        RoomDiscoveryRequest request, System.Net.IPEndPoint endpoint)
    {
        var manager = GameNetworkRoomManager.singleton;
        int currentPlayers = manager != null ? manager.CurrentPlayerCount : 0;

        return new RoomDiscoveryResponse
        {
            roomName = hostRoomName,
            currentPlayers = currentPlayers,
            maxPlayers = hostMaxPlayers,
            mapName = hostMapName,
            serverId = ServerId,
            port = GetPort()
        };
    }

    /// <summary>
    /// Client: Process a room advertisement received from a host.
    /// </summary>
    protected override void ProcessResponse(
        RoomDiscoveryResponse response, System.Net.IPEndPoint endpoint)
    {
        var room = new DiscoveredRoom
        {
            roomName = response.roomName,
            address = endpoint.Address.ToString(),
            port = response.port,
            currentPlayers = response.currentPlayers,
            maxPlayers = response.maxPlayers,
            mapName = response.mapName,
            serverId = response.serverId,
            lastSeen = DateTime.Now
        };

        discoveredRooms[response.serverId] = room;

        Debug.Log($"[Discovery] Found room: '{room.roomName}' at {room.address} " +
                  $"({room.currentPlayers}/{room.maxPlayers})");
    }

    // ─── Helpers ──────────────────────────────────────────────────

    private int GetPort()
    {
        var transport = Transport.active;
        if (transport is kcp2k.KcpTransport kcp)
            return kcp.port;
        return 7777;
    }
}
