using UnityEngine;
using Mirror;
using Mirror.Discovery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

// ─── Messages ─────────────────────────────────────────────────
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
    public int passwordHash;
    public string roomCode;
    public string relayJoinCode;
}

public struct RoomDiscoveryRequest : NetworkMessage
{
    // Empty — just a broadcast ping
}

public struct RoomDiscoveryResponse : NetworkMessage
{
    public string roomName;
    public int currentPlayers;
    public int maxPlayers;
    public string mapName;
    public long serverId;
    public int port;
    public bool hasPassword;
    public int passwordHash;
    public string roomCode;
}

// ─── Discovery Component ──────────────────────────────────────
public class GameRoomDiscovery : NetworkDiscoveryBase<RoomDiscoveryRequest, RoomDiscoveryResponse>
{
    private readonly Dictionary<long, DiscoveredRoom> discoveredRooms = new();
    private bool isAdvertising = false;

    // Room info (set by host)
    private string hostRoomName = "";
    private int hostMaxPlayers = 6;
    private string hostMapName = "";
    private bool hostHasPassword = false;
    private int hostPasswordHash = 0;
    private string hostRoomCode = "";

    // Search state
    private Action<List<DiscoveredRoom>> searchCallback;
    private float searchTimer = -1f;

    // ─── Host: Advertise ──────────────────────────────────────

    public void AdvertiseRoom(string roomName, int maxPlayers, string mapName,
        bool hasPassword = false, int passwordHash = 0, string roomCode = "")
    {
        hostRoomName = roomName;
        hostMaxPlayers = maxPlayers;
        hostMapName = mapName;
        hostHasPassword = hasPassword;
        hostPasswordHash = passwordHash;
        hostRoomCode = roomCode;

        AdvertiseServer();
        isAdvertising = true;

        Debug.Log($"[Discovery] Now advertising room '{roomName}' on LAN (port {secretHandshake})");
    }

    public void StopAdvertising()
    {
        if (isAdvertising)
        {
            StopDiscovery();
            isAdvertising = false;
            Debug.Log("[Discovery] Stopped advertising");
        }
    }

    // ─── Client: Find Rooms ───────────────────────────────────

    public void FindRooms(Action<List<DiscoveredRoom>> callback, float timeout = 3f)
    {
        // Stop any in-progress search before starting a new one
        if (searchTimer > 0)
        {
            StopDiscovery();
            searchTimer = -1f;
            searchCallback = null;
        }

        discoveredRooms.Clear();
        searchCallback = callback;
        searchTimer = timeout;

        StartDiscovery();

        Debug.Log("[Discovery] Started searching for rooms...");
    }

    private void Update()
    {
        if (searchTimer > 0)
        {
            searchTimer -= Time.deltaTime;
            if (searchTimer <= 0)
            {
                StopDiscovery();
                var rooms = discoveredRooms.Values.ToList();
                Debug.Log($"[Discovery] Search complete — found {rooms.Count} room(s)");
                searchCallback?.Invoke(rooms);
                searchCallback = null;
            }
        }
    }

    public List<DiscoveredRoom> GetDiscoveredRooms()
    {
        var staleKeys = discoveredRooms
            .Where(kvp => (DateTime.Now - kvp.Value.lastSeen).TotalSeconds > 10)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in staleKeys)
            discoveredRooms.Remove(key);

        return discoveredRooms.Values.ToList();
    }

    // ─── Mirror Overrides (Client Side) ───────────────────────

    /// <summary>
    /// CLIENT: Build the request to broadcast when searching.
    /// This is REQUIRED — without it, no search packets are sent.
    /// </summary>
    protected override RoomDiscoveryRequest GetRequest()
    {
        return new RoomDiscoveryRequest();
    }

    /// <summary>
    /// CLIENT: Process a response from a server.
    /// Called when a host responds to our search broadcast.
    /// </summary>
    protected override void ProcessResponse(
        RoomDiscoveryResponse response, IPEndPoint endpoint)
    {
        string address = endpoint.Address.ToString();

        var room = new DiscoveredRoom
        {
            roomName = response.roomName,
            address = address,
            port = response.port,
            currentPlayers = response.currentPlayers,
            maxPlayers = response.maxPlayers,
            mapName = response.mapName,
            serverId = response.serverId,
            lastSeen = DateTime.Now,
            hasPassword = response.hasPassword,
            passwordHash = response.passwordHash,
            roomCode = response.roomCode
        };

        discoveredRooms[response.serverId] = room;

        Debug.Log($"[Discovery] Found room: '{room.roomName}' at {address}:{room.port} " +
                  $"({room.currentPlayers}/{room.maxPlayers})");
    }

    // ─── Mirror Overrides (Server Side) ───────────────────────

    /// <summary>
    /// SERVER: Build the response to send back to searching clients.
    /// </summary>
    protected override RoomDiscoveryResponse ProcessRequest(
        RoomDiscoveryRequest request, IPEndPoint endpoint)
    {
        var manager = GameNetworkRoomManager.singleton;
        int currentPlayers = manager != null ? manager.CurrentPlayerCount : 0;

        Debug.Log($"[Discovery] Received search request from {endpoint.Address} — responding with room info");

        return new RoomDiscoveryResponse
        {
            roomName = hostRoomName,
            currentPlayers = currentPlayers,
            maxPlayers = hostMaxPlayers,
            mapName = hostMapName,
            serverId = ServerId,
            port = GetPort(),
            hasPassword = hostHasPassword,
            passwordHash = hostPasswordHash,
            roomCode = hostRoomCode
        };
    }

    // ─── Helpers ──────────────────────────────────────────────

    private int GetPort()
    {
        var transport = Transport.active;
        if (transport is kcp2k.KcpTransport kcp)
            return kcp.port;
        return 7777;
    }
}
