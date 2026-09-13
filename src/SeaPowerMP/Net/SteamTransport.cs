using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SeaPowerMP.Core.Transport;
using Steamworks;

namespace SeaPowerMP.Net
{
    /// <summary>
    /// Steam P2P over the Steam relay network (no port forwarding). Connection-oriented
    /// SteamNetworkingSockets; the Steam lobby is only used to find and invite the host.
    /// </summary>
    internal sealed class SteamTransport : ITransport
    {
        private const int CloseReasonApp = 1000; // k_ESteamNetConnectionEnd_App_Min
        private const int MaxReasonChars = 127;  // Steam truncates the debug string at 128 bytes
        private const int ReliableFlags = Constants.k_nSteamNetworkingSend_Reliable | Constants.k_nSteamNetworkingSend_NoNagle;
        private const int UnreliableFlags = Constants.k_nSteamNetworkingSend_Unreliable | Constants.k_nSteamNetworkingSend_NoNagle;

        private readonly Callback<SteamNetConnectionStatusChangedCallback_t> _statusCallback;

        // Steam callbacks fire from SteamAPI.RunCallbacks (the game's SteamManager), not from Poll().
        // They are queued so that all ITransport events are raised inside Poll(), like the other transports.
        private readonly Queue<Action> _pendingEvents = new Queue<Action>();

        private readonly HashSet<uint> _owned = new HashSet<uint>();
        private readonly Dictionary<uint, HSteamNetConnection> _connected = new Dictionary<uint, HSteamNetConnection>();
        private readonly List<uint> _pollSnapshot = new List<uint>();
        private readonly IntPtr[] _messages = new IntPtr[64];
        private byte[] _receiveBuffer = new byte[4096];
        private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
        private bool _shutDown;

        private SteamTransport(bool isHost)
        {
            IsHost = isHost;
            _statusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
        }

        public bool IsHost { get; }

        public event Action<PeerId>? PeerConnected;
        public event Action<PeerId, string>? PeerDisconnected;
        public event Action<PeerId, ArraySegment<byte>>? DataReceived;

        public static SteamTransport Host()
        {
            RequireSteam();
            var transport = new SteamTransport(isHost: true);
            transport._listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            if (transport._listenSocket == HSteamListenSocket.Invalid)
            {
                transport.Shutdown("");
                throw new InvalidOperationException("Steam refused to open a P2P listen socket.");
            }
            return transport;
        }

        public static SteamTransport Connect(ulong hostSteamId)
        {
            RequireSteam();
            var transport = new SteamTransport(isHost: false);
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(hostSteamId);
            HSteamNetConnection connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
            if (connection == HSteamNetConnection.Invalid)
            {
                transport.Shutdown("");
                throw new InvalidOperationException("Steam refused to start a P2P connection.");
            }
            transport._owned.Add(connection.m_HSteamNetConnection);
            return transport;
        }

        public void Poll()
        {
            while (_pendingEvents.Count > 0)
                _pendingEvents.Dequeue()();
            if (_shutDown)
                return;

            _pollSnapshot.Clear();
            _pollSnapshot.AddRange(_connected.Keys);
            foreach (uint id in _pollSnapshot)
            {
                if (_connected.TryGetValue(id, out var connection))
                    Receive(id, connection);
            }
        }

        public unsafe void Send(PeerId peer, byte[] data, int length, Delivery delivery)
        {
            if (!_connected.TryGetValue((uint)peer.Value, out var connection))
                return;
            EResult result;
            fixed (byte* pointer = data)
            {
                result = SteamNetworkingSockets.SendMessageToConnection(
                    connection, (IntPtr)pointer, (uint)length,
                    delivery == Delivery.Reliable ? ReliableFlags : UnreliableFlags, out _);
            }
            if (result != EResult.k_EResultOK && delivery == Delivery.Reliable)
                Plugin.Log.LogWarning($"[Steam] Reliable send to {peer} failed: {result}");
        }

        public void Disconnect(PeerId peer, string reason)
        {
            uint id = (uint)peer.Value;
            if (!_owned.Remove(id))
                return;
            _connected.Remove(id);
            // Linger so queued reliable data (the session's Goodbye) still reaches the peer.
            SteamNetworkingSockets.CloseConnection(new HSteamNetConnection(id), CloseReasonApp, Truncate(reason), true);
            _pendingEvents.Enqueue(() => PeerDisconnected?.Invoke(peer, reason));
        }

        public void Shutdown(string reason)
        {
            if (_shutDown)
                return;
            _shutDown = true;
            foreach (uint id in _owned)
                SteamNetworkingSockets.CloseConnection(new HSteamNetConnection(id), CloseReasonApp, Truncate(reason), true);
            _owned.Clear();
            _connected.Clear();
            _pendingEvents.Clear();
            if (_listenSocket != HSteamListenSocket.Invalid)
                SteamNetworkingSockets.CloseListenSocket(_listenSocket);
            _listenSocket = HSteamListenSocket.Invalid;
            _statusCallback.Dispose();
        }

        public int GetRttMs(PeerId peer)
        {
            if (!_connected.TryGetValue((uint)peer.Value, out var connection))
                return 0;
            var status = default(SteamNetConnectionRealTimeStatus_t);
            var lanes = default(SteamNetConnectionRealTimeLaneStatus_t);
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes) == EResult.k_EResultOK
                ? status.m_nPing
                : 0;
        }

        private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
        {
            if (_shutDown)
                return;
            HSteamNetConnection connection = data.m_hConn;
            uint id = connection.m_HSteamNetConnection;

            switch (data.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    // Incoming connection on our listen socket. Every request is accepted here;
                    // the session handshake decides who may stay.
                    if (IsHost && data.m_info.m_hListenSocket == _listenSocket && !_owned.Contains(id))
                    {
                        if (SteamNetworkingSockets.AcceptConnection(connection) == EResult.k_EResultOK)
                            _owned.Add(id);
                        else
                            SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
                    }
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (_owned.Contains(id) && !_connected.ContainsKey(id))
                    {
                        _connected[id] = connection;
                        _pendingEvents.Enqueue(() => PeerConnected?.Invoke(new PeerId(id)));
                    }
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    if (_owned.Remove(id))
                    {
                        _connected.Remove(id);
                        string reason = DescribeClose(data);
                        SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
                        _pendingEvents.Enqueue(() => PeerDisconnected?.Invoke(new PeerId(id), reason));
                    }
                    break;
            }
        }

        private void Receive(uint id, HSteamNetConnection connection)
        {
            while (true)
            {
                int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection, _messages, _messages.Length);
                if (count <= 0)
                    return;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(_messages[i]);
                        int size = message.m_cbSize;
                        if (_receiveBuffer.Length < size)
                            _receiveBuffer = new byte[Math.Max(size, _receiveBuffer.Length * 2)];
                        Marshal.Copy(message.m_pData, _receiveBuffer, 0, size);
                        // Handlers may disconnect this peer mid-batch; the rest of the batch is still released.
                        if (_connected.ContainsKey(id))
                            DataReceived?.Invoke(new PeerId(id), new ArraySegment<byte>(_receiveBuffer, 0, size));
                    }
                    finally
                    {
                        SteamNetworkingMessage_t.Release(_messages[i]);
                    }
                }
                if (count < _messages.Length)
                    return;
            }
        }

        private string DescribeClose(SteamNetConnectionStatusChangedCallback_t data)
        {
            string debug = data.m_info.m_szEndDebug;
            bool wasConnected = data.m_eOldState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;
            if (data.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer)
                return string.IsNullOrEmpty(debug) ? "The other side closed the connection." : debug;
            if (!wasConnected && !IsHost)
                return "Could not reach the host over Steam" + (string.IsNullOrEmpty(debug) ? "." : " (" + debug + ").");
            return "Connection lost" + (string.IsNullOrEmpty(debug) ? "." : " (" + debug + ").");
        }

        private static string Truncate(string reason) =>
            reason.Length <= MaxReasonChars ? reason : reason.Substring(0, MaxReasonChars);

        private static void RequireSteam()
        {
            if (!global::SteamManager.Initialized)
                throw new InvalidOperationException("Steam is not available - start the game through Steam or use a direct IP connection.");
        }
    }
}
