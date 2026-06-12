using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct AuthRequestMessage : NetworkMessage
{
    public int passwordHash;
}

public struct AuthResponseMessage : NetworkMessage
{
    public bool success;
    public string reason;
}

/// <summary>
/// Server-side room password enforcement using Mirror's NetworkAuthenticator.
/// Mirrors official BasicAuthenticator pattern: send rejection message first,
/// then delay 1 second before ServerReject so KCP has time to flush the packet.
/// </summary>
public class PasswordAuthenticator : NetworkAuthenticator
{
    // Set by GameNetworkRoomManager before StartClient() or StartHost()
    [HideInInspector] public int clientPasswordHash;

    // Tracks connections mid-reject to avoid processing them twice
    private readonly HashSet<NetworkConnectionToClient> _pendingDisconnect = new();

    // Tracks whether we sent auth credentials and are waiting for a response
    private bool _pendingAuth;

    // ── Server ────────────────────────────────────────────────────────────────

    public override void OnStartServer()
    {
        NetworkServer.ReplaceHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
    }

    public override void OnStopServer()
    {
        NetworkServer.UnregisterHandler<AuthRequestMessage>();
    }

    public override void OnServerAuthenticate(NetworkConnectionToClient conn)
    {
        // Do nothing — wait for the client to send AuthRequestMessage
    }

    private void OnAuthRequestMessage(NetworkConnectionToClient conn, AuthRequestMessage msg)
    {
        // Ignore if we already started the reject flow for this connection
        if (_pendingDisconnect.Contains(conn)) return;

        var mgr = GameNetworkRoomManager.singleton;
        bool hasPassword = mgr != null && !string.IsNullOrEmpty(mgr.roomPassword);
        int expectedHash = hasPassword ? mgr.roomPassword.GetHashCode() : 0;

        if (!hasPassword || msg.passwordHash == expectedHash)
        {
            conn.Send(new AuthResponseMessage { success = true, reason = "" });
            ServerAccept(conn);
        }
        else
        {
            // Official Mirror pattern: send message first, THEN disconnect after a delay.
            // Calling ServerReject immediately after Send causes a KCP "RawSend invalid
            // connectionId" warning because KCP removes the connection before the packet flushes.
            _pendingDisconnect.Add(conn);
            conn.Send(new AuthResponseMessage { success = false, reason = "Wrong password." });
            conn.isAuthenticated = false;
            StartCoroutine(DelayedReject(conn));
        }
    }

    private IEnumerator DelayedReject(NetworkConnectionToClient conn)
    {
        // 1 second matches Mirror's own BasicAuthenticator — enough for KCP to flush
        yield return new WaitForSeconds(1f);
        ServerReject(conn);
        yield return null;
        _pendingDisconnect.Remove(conn);
    }

    // ── Client ────────────────────────────────────────────────────────────────

    public override void OnStartClient()
    {
        NetworkClient.ReplaceHandler<AuthResponseMessage>(OnAuthResponseMessage, false);
    }

    public override void OnStopClient()
    {
        NetworkClient.UnregisterHandler<AuthResponseMessage>();

        // Fallback: if the connection dropped before we received a response (e.g. network
        // error, or host restarted), fire a generic failure only if we had a password typed.
        if (_pendingAuth)
        {
            _pendingAuth = false;
            if (clientPasswordHash != 0)
                GameNetworkRoomManager.singleton?.NotifyJoinFailed("Wrong password.");
            // hash == 0 means QuickMatch or no-password join — don't fire, let coroutine continue
        }
    }

    public override void OnClientAuthenticate()
    {
        _pendingAuth = true;
        NetworkClient.Send(new AuthRequestMessage { passwordHash = clientPasswordHash });
    }

    private void OnAuthResponseMessage(AuthResponseMessage msg)
    {
        // Ignore stale messages — can arrive after the 1-second server delay
        // if the client already disconnected (e.g. user clicked Leave, coroutine
        // timed out, or a prior connection attempt's delayed rejection fires late).
        if (!_pendingAuth) return;

        _pendingAuth = false;

        if (msg.success)
        {
            ClientAccept();
        }
        else
        {
            // Explicit rejection — tell the UI before disconnecting
            GameNetworkRoomManager.singleton?.NotifyJoinFailed(msg.reason);
            ClientReject();
        }
    }
}
