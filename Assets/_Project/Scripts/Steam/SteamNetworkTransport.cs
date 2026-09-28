// Транспорт Netcode for GameObjects поверх Steam Networking Sockets (P2P через релеи Valve).
// Основан на SteamNetworkingSocketsTransport из Unity multiplayer-community-contributions (MIT,
// Copyright (c) 2021 Unity Technologies), переработан под проект.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace YourFinalOrder.Steam
{
    public class SteamNetworkTransport : NetworkTransport
    {
        class Connection
        {
            public CSteamID Id;
            public HSteamNetConnection Handle;
        }

        /// <summary>SteamID хоста, к которому подключается клиент.</summary>
        [NonSerialized] public ulong ConnectToSteamId;

        const int MaxMessagesPerPoll = 64;

        Callback<SteamNetConnectionStatusChangedCallback_t> statusCallback;
        HSteamListenSocket listenSocket = HSteamListenSocket.Invalid;
        Connection server;
        bool isServer;

        readonly Dictionary<ulong, Connection> connections = new();
        readonly Queue<SteamNetConnectionStatusChangedCallback_t> statusQueue = new();
        readonly Queue<(ulong id, byte[] data)> received = new();
        readonly IntPtr[] messageBuffer = new IntPtr[MaxMessagesPerPoll];

        public override ulong ServerClientId => 0;
        public override bool IsSupported => SteamBootstrap.Initialized;

        public override void Initialize(NetworkManager networkManager = null)
        {
            if (!SteamBootstrap.Initialized)
                Debug.LogError("SteamNetworkTransport: Steam не инициализирован");
        }

        public override bool StartServer()
        {
            if (!SteamBootstrap.Initialized) return false;
            isServer = true;
            EnsureCallback();
            SteamNetworkingUtils.InitRelayNetworkAccess();
            listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            return listenSocket != HSteamListenSocket.Invalid;
        }

        public override bool StartClient()
        {
            if (!SteamBootstrap.Initialized || ConnectToSteamId == 0) return false;
            isServer = false;
            EnsureCallback();
            SteamNetworkingUtils.InitRelayNetworkAccess();

            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(ConnectToSteamId));
            server = new Connection
            {
                Id = new CSteamID(ConnectToSteamId),
                Handle = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null),
            };
            connections[ConnectToSteamId] = server;
            return server.Handle != HSteamNetConnection.Invalid;
        }

        void EnsureCallback()
        {
            statusCallback ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(s => statusQueue.Enqueue(s));
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            clientId = 0;

            while (statusQueue.Count > 0)
            {
                var s = statusQueue.Dequeue();
                ulong remote = s.m_info.m_identityRemote.GetSteamID64();

                switch (s.m_info.m_eState)
                {
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                        if (isServer)
                        {
                            if (SteamNetworkingSockets.AcceptConnection(s.m_hConn) == EResult.k_EResultOK)
                                connections[remote] = new Connection { Id = new CSteamID(remote), Handle = s.m_hConn };
                            else
                                SteamNetworkingSockets.CloseConnection(s.m_hConn, 0, "Rejected", false);
                        }
                        break;

                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                        if (!connections.TryGetValue(remote, out var c))
                            connections[remote] = c = new Connection { Id = new CSteamID(remote) };
                        c.Handle = s.m_hConn;
                        clientId = isServer ? remote : ServerClientId;
                        return NetworkEvent.Connect;

                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                        SteamNetworkingSockets.CloseConnection(s.m_hConn, 0, "Closed", false);
                        connections.Remove(remote);
                        clientId = isServer ? remote : ServerClientId;
                        return NetworkEvent.Disconnect;
                }
            }

            if (received.Count == 0) ReceiveAll();
            if (received.Count > 0)
            {
                var (id, data) = received.Dequeue();
                clientId = isServer ? id : ServerClientId;
                payload = new ArraySegment<byte>(data);
                return NetworkEvent.Data;
            }

            return NetworkEvent.Nothing;
        }

        void ReceiveAll()
        {
            foreach (var c in connections.Values)
            {
                int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(c.Handle, messageBuffer, MaxMessagesPerPoll);
                for (int i = 0; i < count; i++)
                {
                    var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(messageBuffer[i]);
                    var data = new byte[msg.m_cbSize];
                    Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);
                    SteamNetworkingMessage_t.Release(messageBuffer[i]);
                    received.Enqueue((c.Id.m_SteamID, data));
                }
            }
        }

        public override void Send(ulong clientId, ArraySegment<byte> segment, NetworkDelivery delivery)
        {
            Connection c;
            if (!isServer) c = server;
            else if (!connections.TryGetValue(clientId, out c)) c = null;
            if (c == null) return;

            int flags = delivery switch
            {
                NetworkDelivery.Unreliable => Constants.k_nSteamNetworkingSend_Unreliable,
                NetworkDelivery.UnreliableSequenced => Constants.k_nSteamNetworkingSend_UnreliableNoNagle,
                NetworkDelivery.ReliableSequenced => Constants.k_nSteamNetworkingSend_ReliableNoNagle,
                _ => Constants.k_nSteamNetworkingSend_Reliable,
            };

            var handle = GCHandle.Alloc(segment.Array, GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject() + segment.Offset;
                var result = SteamNetworkingSockets.SendMessageToConnection(c.Handle, ptr, (uint)segment.Count, flags, out _);
                if (result != EResult.k_EResultOK && result != EResult.k_EResultLimitExceeded)
                    Debug.LogWarning($"SteamNetworkTransport: ошибка отправки {result}");
            }
            finally
            {
                handle.Free();
            }
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            var c = isServer ? (connections.TryGetValue(clientId, out var x) ? x : null) : server;
            if (c == null) return 0;
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lane = new SteamNetConnectionRealTimeLaneStatus_t();
            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(c.Handle, ref status, 0, ref lane) == EResult.k_EResultOK)
                return (ulong)Mathf.Max(0, status.m_nPing);
            return 0;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!connections.TryGetValue(clientId, out var c)) return;
            SteamNetworkingSockets.CloseConnection(c.Handle, 0, "Kicked", true);
            connections.Remove(clientId);
        }

        public override void DisconnectLocalClient()
        {
            if (server == null) return;
            SteamNetworkingSockets.CloseConnection(server.Handle, 0, "Disconnected", true);
            connections.Remove(server.Id.m_SteamID);
            server = null;
        }

        public override void Shutdown()
        {
            if (SteamBootstrap.Initialized)
            {
                foreach (var c in connections.Values)
                    SteamNetworkingSockets.CloseConnection(c.Handle, 0, "Shutdown", true);
                if (listenSocket != HSteamListenSocket.Invalid)
                    SteamNetworkingSockets.CloseListenSocket(listenSocket);
            }
            listenSocket = HSteamListenSocket.Invalid;
            connections.Clear();
            statusQueue.Clear();
            received.Clear();
            server = null;
            isServer = false;
            statusCallback?.Dispose();
            statusCallback = null;
        }
    }
}
