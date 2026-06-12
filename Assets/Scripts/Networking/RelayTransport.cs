using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Mirror;
using UNetConn = Unity.Networking.Transport.NetworkConnection;

public class RelayTransport : Transport
{
    private NetworkDriver _driver;
    private NetworkPipeline _reliablePipeline;
    private NetworkPipeline _unreliablePipeline;

    private RelayServerData _relayServerData;
    private bool _isConfigured;

    // Server
    private bool _serverActive;
    private readonly Dictionary<int, UNetConn> _idToConn = new();
    private readonly Dictionary<UNetConn, int> _connToId = new();
    private int _nextId = 1;
    private readonly List<int> _pendingDisconnects = new();

    // Client
    private UNetConn _clientConn;
    private bool _clientConnected;
    private bool _clientDisconnecting;

    public void ConfigureAsHost(RelayServerData data)
    {
        _relayServerData = data;
        _isConfigured = true;
    }

    public void ConfigureAsClient(RelayServerData data)
    {
        _relayServerData = data;
        _isConfigured = true;
    }

    public override bool Available() => _isConfigured;

    public override int GetMaxPacketSize(int channelId = Channels.Reliable)
    {
        return channelId == Channels.Reliable ? 16384 : 1200;
    }

    // ─── Server ──────────────────────────────────────────────

    public override void ServerStart()
    {
        if (!_isConfigured)
        {
            Debug.LogError("[RelayTransport] Not configured");
            return;
        }

        DisposeDriver();
        _serverActive = false;

        var settings = new NetworkSettings();
        settings.WithRelayParameters(ref _relayServerData);

        _driver = NetworkDriver.Create(settings);

        _reliablePipeline = _driver.CreatePipeline(
            typeof(FragmentationPipelineStage),
            typeof(ReliableSequencedPipelineStage));
        _unreliablePipeline = NetworkPipeline.Null;

        if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
        {
            Debug.LogError("[RelayTransport] Server bind failed");
            return;
        }
        if (_driver.Listen() != 0)
        {
            Debug.LogError("[RelayTransport] Server listen failed");
            return;
        }

        _serverActive = true;
        _idToConn.Clear();
        _connToId.Clear();
        _nextId = 1;
        Debug.Log("[RelayTransport] Server started via relay");
    }

    public override bool ServerActive() => _serverActive;
    public override Uri ServerUri() => null;

    public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId)
    {
        if (!_idToConn.TryGetValue(connectionId, out var conn)) return;
        Send(conn, segment, channelId);
    }

    public override void ServerDisconnect(int connectionId)
    {
        if (!_idToConn.TryGetValue(connectionId, out var conn)) return;
        conn.Disconnect(_driver);
        _connToId.Remove(conn);
        _idToConn.Remove(connectionId);
        OnServerDisconnected?.Invoke(connectionId);
    }

    public override string ServerGetClientAddress(int connectionId) => "relay";

    public override void ServerStop()
    {
        if (!_serverActive) return;
        _serverActive = false;

        foreach (var conn in _idToConn.Values)
        {
            if (conn.IsCreated)
                conn.Disconnect(_driver);
        }

        if (_driver.IsCreated)
            _driver.ScheduleUpdate().Complete();

        _idToConn.Clear();
        _connToId.Clear();

        DisposeDriver();
    }

    // ─── Client ──────────────────────────────────────────────

    public override bool ClientConnected() => _clientConnected;

    public override void ClientConnect(string address)
    {
        if (!_isConfigured)
        {
            Debug.LogError("[RelayTransport] Client relay not configured");
            OnClientError?.Invoke(TransportError.Unexpected, "Relay not configured");
            return;
        }

        if (!_serverActive)
            DisposeDriver();

        var settings = new NetworkSettings();
        settings.WithRelayParameters(ref _relayServerData);

        _driver = NetworkDriver.Create(settings);

        _reliablePipeline = _driver.CreatePipeline(
            typeof(FragmentationPipelineStage),
            typeof(ReliableSequencedPipelineStage));
        _unreliablePipeline = NetworkPipeline.Null;

        if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
        {
            Debug.LogError("[RelayTransport] Client bind failed");
            OnClientError?.Invoke(TransportError.Unexpected, "Bind failed");
            return;
        }

        _clientConn = _driver.Connect(_relayServerData.Endpoint);
        _clientConnected = false;
        Debug.Log("[RelayTransport] Client connecting via relay...");
    }

    public override void ClientConnect(Uri uri) => ClientConnect(uri?.ToString() ?? "");

    public override void ClientSend(ArraySegment<byte> segment, int channelId)
    {
        if (!_clientConnected) return;
        Send(_clientConn, segment, channelId);
    }

    public override void ClientDisconnect()
    {
        if (_clientDisconnecting) return;
        _clientDisconnecting = true;

        if (_clientConn.IsCreated && _driver.IsCreated)
        {
            _clientConn.Disconnect(_driver);
            _driver.ScheduleUpdate().Complete();
        }
        _clientConnected = false;

        OnClientDisconnected?.Invoke();

        if (!_serverActive)
            DisposeDriver();

        _clientDisconnecting = false;
    }

    // ─── Shared ──────────────────────────────────────────────

    public override void Shutdown()
    {
        ServerStop();
        ClientDisconnect();
        _isConfigured = false;
    }

    private void Send(UNetConn conn, ArraySegment<byte> segment, int channelId)
    {
        if (!_driver.IsCreated || !conn.IsCreated) return;

        var pipeline = channelId == Channels.Reliable ? _reliablePipeline : _unreliablePipeline;
        int status = _driver.BeginSend(pipeline, conn, out var writer);
        if (status != (int)Unity.Networking.Transport.Error.StatusCode.Success)
        {
            Debug.LogWarning($"[RelayTransport] BeginSend failed: {status}");
            return;
        }

        var nativeData = new NativeArray<byte>(segment.Count, Allocator.Temp);
        NativeArray<byte>.Copy(segment.Array, segment.Offset, nativeData, 0, segment.Count);
        writer.WriteBytes(nativeData);
        nativeData.Dispose();
        _driver.EndSend(writer);
    }

    private void DisposeDriver()
    {
        if (_driver.IsCreated)
            _driver.Dispose();
    }

    // ─── Update Loop ─────────────────────────────────────────

    private void LateUpdate()
    {
        if (!_driver.IsCreated) return;
        _driver.ScheduleUpdate().Complete();

        if (_serverActive)
            ProcessServerEvents();
        else if (_clientConn.IsCreated)
            ProcessClientEvents();
    }

    private void ProcessServerEvents()
    {
        UNetConn incoming;
        while ((incoming = _driver.Accept()) != default)
        {
            int mirrorId = _nextId++;
            _idToConn[mirrorId] = incoming;
            _connToId[incoming] = mirrorId;
            OnServerConnected?.Invoke(mirrorId);
            Debug.Log($"[RelayTransport] Client connected (mirrorId={mirrorId})");
        }

        _pendingDisconnects.Clear();
        foreach (var kvp in _idToConn)
        {
            NetworkEvent.Type evt;
            while ((evt = _driver.PopEventForConnection(kvp.Value, out var reader)) !=
                   NetworkEvent.Type.Empty)
            {
                switch (evt)
                {
                    case NetworkEvent.Type.Data:
                        ReadAndDeliver(reader, data =>
                            OnServerDataReceived?.Invoke(kvp.Key, data, Channels.Reliable));
                        break;

                    case NetworkEvent.Type.Disconnect:
                        _pendingDisconnects.Add(kvp.Key);
                        break;
                }
            }
        }

        foreach (int id in _pendingDisconnects)
        {
            if (_idToConn.TryGetValue(id, out var conn))
            {
                _connToId.Remove(conn);
                _idToConn.Remove(id);
            }
            OnServerDisconnected?.Invoke(id);
        }
    }

    private void ProcessClientEvents()
    {
        NetworkEvent.Type evt;
        while ((evt = _driver.PopEventForConnection(_clientConn, out var reader)) !=
               NetworkEvent.Type.Empty)
        {
            switch (evt)
            {
                case NetworkEvent.Type.Connect:
                    _clientConnected = true;
                    OnClientConnected?.Invoke();
                    Debug.Log("[RelayTransport] Client connected to relay host");
                    break;

                case NetworkEvent.Type.Data:
                    ReadAndDeliver(reader, data =>
                        OnClientDataReceived?.Invoke(data, Channels.Reliable));
                    break;

                case NetworkEvent.Type.Disconnect:
                    _clientConnected = false;
                    OnClientDisconnected?.Invoke();
                    Debug.Log("[RelayTransport] Client disconnected from relay");
                    return;
            }
        }
    }

    private static void ReadAndDeliver(DataStreamReader reader, Action<ArraySegment<byte>> callback)
    {
        var nativeData = new NativeArray<byte>(reader.Length, Allocator.Temp);
        reader.ReadBytes(nativeData);
        byte[] managed = nativeData.ToArray();
        nativeData.Dispose();
        callback(new ArraySegment<byte>(managed));
    }
}
