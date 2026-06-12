using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Mirror;
using UNetConn = Unity.Networking.Transport.NetworkConnection;

public class RelayTransport : Transport
{
    // ─── Tuning ──────────────────────────────────────────────
    // Reliable channel is delivery-guaranteed by Unity Transport's
    // ReliableSequencedPipelineStage, but that stage has a bounded
    // in-flight window. If we exceed it and just DROP the message,
    // Mirror desyncs permanently (stuck rockets, frozen ammo/health UI),
    // because Mirror never retransmits "reliable" traffic itself.
    //
    // Two-part defence:
    //   1) Enlarge the window (max 64) + fragmentation capacity.
    //   2) Never drop a reliable message — queue it and retry next tick
    //      (see _serverReliableQueues / _clientReliableQueue).
    private const int ReliableWindowSize = 64;       // UTP max is 64
    private const int FragmentationCapacity = 16384;  // matches GetMaxPacketSize(Reliable)
    private const int MaxReliableBacklog = 4096;      // safety cap per connection

    private NetworkDriver _driver;
    private NetworkPipeline _reliablePipeline;
    private NetworkPipeline _unreliablePipeline;

    private RelayServerData _relayServerData;
    private bool _isConfigured;

    // Server
    private bool _serverActive;
    private readonly Dictionary<int, UNetConn> _idToConn = new();
    private readonly Dictionary<UNetConn, int> _connToId = new();
    private readonly Dictionary<int, Queue<byte[]>> _serverReliableQueues = new();
    private int _nextId = 1;
    private readonly List<int> _pendingDisconnects = new();

    // Client
    private UNetConn _clientConn;
    private bool _clientConnected;
    private bool _clientDisconnecting;
    private readonly Queue<byte[]> _clientReliableQueue = new();

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
        return channelId == Channels.Reliable ? FragmentationCapacity : 1200;
    }

    private NetworkSettings BuildSettings()
    {
        var settings = new NetworkSettings();
        settings.WithRelayParameters(ref _relayServerData);
        settings.WithReliableStageParameters(windowSize: ReliableWindowSize);
        settings.WithFragmentationStageParameters(payloadCapacity: FragmentationCapacity);
        return settings;
    }

    private void CreatePipelines()
    {
        _reliablePipeline = _driver.CreatePipeline(
            typeof(FragmentationPipelineStage),
            typeof(ReliableSequencedPipelineStage));
        _unreliablePipeline = NetworkPipeline.Null;
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

        _driver = NetworkDriver.Create(BuildSettings());
        CreatePipelines();

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
        _serverReliableQueues.Clear();
        _nextId = 1;
        Debug.Log("[RelayTransport] Server started via relay");
    }

    public override bool ServerActive() => _serverActive;
    public override Uri ServerUri() => null;

    public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId)
    {
        if (!_idToConn.TryGetValue(connectionId, out var conn)) return;

        if (channelId == Channels.Reliable)
        {
            if (!_serverReliableQueues.TryGetValue(connectionId, out var queue))
            {
                queue = new Queue<byte[]>();
                _serverReliableQueues[connectionId] = queue;
            }
            EnqueueOrSendReliable(queue, conn, segment);
        }
        else
        {
            // Unreliable: best-effort, safe to drop on failure.
            RawSend(conn, segment.Array, segment.Offset, segment.Count, reliable: false);
        }
    }

    public override void ServerDisconnect(int connectionId)
    {
        if (!_idToConn.TryGetValue(connectionId, out var conn)) return;
        conn.Disconnect(_driver);
        _connToId.Remove(conn);
        _idToConn.Remove(connectionId);
        _serverReliableQueues.Remove(connectionId);
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
        _serverReliableQueues.Clear();

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

        _clientReliableQueue.Clear();

        _driver = NetworkDriver.Create(BuildSettings());
        CreatePipelines();

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

        if (channelId == Channels.Reliable)
            EnqueueOrSendReliable(_clientReliableQueue, _clientConn, segment);
        else
            RawSend(_clientConn, segment.Array, segment.Offset, segment.Count, reliable: false);
    }

    public override void ClientDisconnect()
    {
        if (_clientDisconnecting) return;
        _clientDisconnecting = true;

        _clientReliableQueue.Clear();

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

    /// <summary>
    /// Reliable send with back-pressure. To preserve ordering, once a
    /// connection has a backlog every subsequent reliable message goes
    /// through the queue (FIFO). A message is only considered "sent" once
    /// the transport accepts it — otherwise it stays queued and is retried.
    /// </summary>
    private void EnqueueOrSendReliable(Queue<byte[]> queue, UNetConn conn, ArraySegment<byte> segment)
    {
        if (queue.Count == 0 &&
            RawSend(conn, segment.Array, segment.Offset, segment.Count, reliable: true))
            return;

        if (queue.Count >= MaxReliableBacklog)
        {
            Debug.LogError("[RelayTransport] Reliable backlog overflow — connection is stalled, dropping.");
            return;
        }

        var copy = new byte[segment.Count];
        Buffer.BlockCopy(segment.Array, segment.Offset, copy, 0, segment.Count);
        queue.Enqueue(copy);
    }

    private void FlushReliableQueue(Queue<byte[]> queue, UNetConn conn)
    {
        while (queue.Count > 0)
        {
            byte[] data = queue.Peek();
            if (!RawSend(conn, data, 0, data.Length, reliable: true))
                break; // window still full — try again next tick
            queue.Dequeue();
        }
    }

    /// <summary>Returns true only if the transport accepted the bytes for sending.</summary>
    private bool RawSend(UNetConn conn, byte[] array, int offset, int count, bool reliable)
    {
        if (!_driver.IsCreated || !conn.IsCreated || array == null) return false;

        var pipeline = reliable ? _reliablePipeline : _unreliablePipeline;
        int beginStatus = _driver.BeginSend(pipeline, conn, out var writer);
        if (beginStatus != (int)Unity.Networking.Transport.Error.StatusCode.Success)
            return false; // e.g. NetworkSendQueueFull — caller decides whether to queue

        var nativeData = new NativeArray<byte>(count, Allocator.Temp);
        NativeArray<byte>.Copy(array, offset, nativeData, 0, count);
        writer.WriteBytes(nativeData);
        nativeData.Dispose();

        int endStatus = _driver.EndSend(writer);
        // EndSend runs the pipeline; a negative result means the reliable
        // window rejected it. The message was NOT committed, so it is safe
        // to retry without duplicating.
        return endStatus >= 0;
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
        {
            ProcessServerEvents();
            FlushServerQueues();
        }
        else if (_clientConn.IsCreated)
        {
            ProcessClientEvents();
            if (_clientConnected)
                FlushReliableQueue(_clientReliableQueue, _clientConn);
        }
    }

    private void FlushServerQueues()
    {
        foreach (var kvp in _serverReliableQueues)
        {
            if (kvp.Value.Count == 0) continue;
            if (_idToConn.TryGetValue(kvp.Key, out var conn))
                FlushReliableQueue(kvp.Value, conn);
        }
    }

    private void ProcessServerEvents()
    {
        UNetConn incoming;
        while ((incoming = _driver.Accept()) != default)
        {
            int mirrorId = _nextId++;
            _idToConn[mirrorId] = incoming;
            _connToId[incoming] = mirrorId;
            _serverReliableQueues[mirrorId] = new Queue<byte[]>();
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
            _serverReliableQueues.Remove(id);
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
                    _clientReliableQueue.Clear();
                    OnClientDisconnected?.Invoke();
                    Debug.Log("[RelayTransport] Client disconnected from relay");
                    return;
            }
        }
    }

    // Reusable receive buffer — Mirror copies the segment into its own pooled
    // storage synchronously inside the callback, so we can safely hand out a
    // view into this buffer and overwrite it on the next packet. Avoids a fresh
    // managed byte[] per packet (the old ToArray()), which spiked GC during
    // combat when packet rate climbs.
    private byte[] _recvBuffer = new byte[2048];

    private void ReadAndDeliver(DataStreamReader reader, Action<ArraySegment<byte>> callback)
    {
        int len = reader.Length;
        if (_recvBuffer.Length < len)
            _recvBuffer = new byte[Mathf.NextPowerOfTwo(len)];

        var nativeData = new NativeArray<byte>(len, Allocator.Temp);
        reader.ReadBytes(nativeData);
        NativeArray<byte>.Copy(nativeData, 0, _recvBuffer, 0, len);
        nativeData.Dispose();

        callback(new ArraySegment<byte>(_recvBuffer, 0, len));
    }
}
