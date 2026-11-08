using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace Repulsor
{
    public enum NetRole { None, Host, Client }

    /// <summary>
    /// Two-player LAN session over plain UDP. The host listens on <see cref="NetProtocol.Port"/>; the client
    /// works out the host's address from the room code and says hello. After that both sides send their player
    /// state ~20 times a second and events as they happen. Polled from the main thread: call <see cref="Poll"/>
    /// every frame. Lost packets are fine for pose and status, which are resent constantly.
    /// </summary>
    public sealed class NetSession : IDisposable
    {
        public const float SendInterval = 0.05f;
        public const float TimeoutSeconds = 5f;
        const float HelloInterval = 0.5f;

        UdpClient socket;
        IPEndPoint peer;
        float sinceHeard;
        float helloTimer;
        float sendTimer;

        public NetRole Role { get; private set; }
        public int RoomCode { get; private set; }
        public bool Connected => peer != null && sinceHeard < TimeoutSeconds;
        public byte Slot { get; private set; }
        public string Status { get; private set; } = "";
        public IPEndPoint Peer => peer;
        public int LocalPort => socket != null ? ((IPEndPoint)socket.Client.LocalEndPoint).Port : 0;

        public event Action<PlayerSnapshot> PlayerReceived;
        public event Action<LobbyState> LobbyReceived;
        public event Action<int, byte> StartReceived;
        public event Action<ShotEvent> ShotReceived;
        public event Action PeerJoined;
        public event Action PeerLeft;

        /// <summary>Starts hosting. Returns false if the port is taken or there's no network.</summary>
        public bool Host(int port = NetProtocol.Port)
        {
            Close();
            try
            {
                socket = new UdpClient(port);
                socket.Client.Blocking = false;
            }
            catch (SocketException e)
            {
                Status = "Couldn't open the network port: " + e.SocketErrorCode;
                return false;
            }
            Role = NetRole.Host;
            Slot = 0;
            IPAddress local = LocalAddress();
            RoomCode = local != null ? NetProtocol.RoomCode(local.GetAddressBytes()[3]) : -1;
            Status = local != null ? $"Hosting on {local}" : "No Wi-Fi address found";
            return local != null;
        }

        /// <summary>Joins the host the code points to on this network. The hello repeats until it answers.</summary>
        public bool Join(int roomCode, int port = NetProtocol.Port, IPAddress overrideHost = null)
        {
            Close();
            IPAddress host = overrideHost;
            if (host == null)
            {
                if (!NetProtocol.TryParseRoomCode(roomCode, out byte last))
                {
                    Status = "That code isn't right";
                    return false;
                }
                IPAddress local = LocalAddress();
                if (local == null)
                {
                    Status = "No Wi-Fi address found";
                    return false;
                }
                byte[] bytes = local.GetAddressBytes();
                bytes[3] = last;
                host = new IPAddress(bytes);
            }
            socket = new UdpClient(0);
            socket.Client.Blocking = false;
            Role = NetRole.Client;
            RoomCode = roomCode;
            peer = new IPEndPoint(host, port);
            sinceHeard = TimeoutSeconds;
            helloTimer = 0f;
            Status = $"Looking for {host}...";
            return true;
        }

        public void Poll(float dt)
        {
            if (socket == null)
                return;
            sinceHeard += dt;
            bool wasConnected = peer != null && sinceHeard - dt < TimeoutSeconds;

            if (Role == NetRole.Client && !Connected)
            {
                helloTimer -= dt;
                if (helloTimer <= 0f)
                {
                    helloTimer = HelloInterval;
                    Send(NetProtocol.Hello(SystemInfo.deviceName));
                }
            }

            for (int i = 0; i < 64; i++)
            {
                if (socket == null || socket.Available <= 0)
                    break;
                IPEndPoint from = null;
                byte[] data;
                try
                {
                    data = socket.Receive(ref from);
                }
                catch (SocketException)
                {
                    break;
                }
                Handle(data, from);
            }

            if (wasConnected && !Connected)
            {
                Status = "Lost the other player";
                PeerLeft?.Invoke();
            }
        }

        void Handle(byte[] data, IPEndPoint from)
        {
            if (!NetProtocol.TryRead(data, data.Length, out NetMessage type, out BinaryReader reader))
                return;
            using (reader)
            {
                switch (type)
                {
                    case NetMessage.Hello when Role == NetRole.Host:
                        bool fresh = peer == null || !peer.Equals(from) || !Connected;
                        peer = from;
                        sinceHeard = 0f;
                        Send(NetProtocol.Welcome(1, RoomCode));
                        if (fresh)
                        {
                            Status = $"{NetProtocol.ReadHello(reader)} joined";
                            PeerJoined?.Invoke();
                        }
                        break;
                    case NetMessage.Welcome when Role == NetRole.Client:
                        NetProtocol.ReadWelcome(reader, out byte slot, out _);
                        bool first = !Connected;
                        Slot = slot;
                        sinceHeard = 0f;
                        if (first)
                        {
                            Status = "Joined";
                            PeerJoined?.Invoke();
                        }
                        break;
                    case NetMessage.Player:
                        if (!FromPeer(from))
                            return;
                        PlayerReceived?.Invoke(NetProtocol.ReadPlayer(reader));
                        break;
                    case NetMessage.Lobby:
                        if (!FromPeer(from))
                            return;
                        LobbyReceived?.Invoke(NetProtocol.ReadLobby(reader));
                        break;
                    case NetMessage.Start:
                        if (!FromPeer(from))
                            return;
                        NetProtocol.ReadStart(reader, out int seed, out byte character);
                        StartReceived?.Invoke(seed, character);
                        break;
                    case NetMessage.Shot:
                        if (!FromPeer(from))
                            return;
                        ShotReceived?.Invoke(NetProtocol.ReadShot(reader));
                        break;
                    case NetMessage.Bye:
                        if (!FromPeer(from))
                            return;
                        sinceHeard = TimeoutSeconds;
                        Status = "The other player left";
                        PeerLeft?.Invoke();
                        break;
                }
            }
        }

        bool FromPeer(IPEndPoint from)
        {
            if (peer == null || !peer.Address.Equals(from.Address))
                return false;
            if (Role == NetRole.Client && peer.Port != from.Port)
                return false;
            sinceHeard = 0f;
            return true;
        }

        /// <summary>Sends the player state at most every <see cref="SendInterval"/>.</summary>
        public void SendPlayer(in PlayerSnapshot snapshot, float dt)
        {
            sendTimer -= dt;
            if (sendTimer > 0f || !Connected)
                return;
            sendTimer = SendInterval;
            Send(NetProtocol.Player(snapshot));
        }

        public void SendLobby(in LobbyState state)
        {
            if (Connected)
                Send(NetProtocol.Lobby(state));
        }

        public void SendStart(int seed, byte character)
        {
            if (Connected)
                Send(NetProtocol.Start(seed, character));
        }

        public void SendShot(in ShotEvent shot)
        {
            if (Connected)
                Send(NetProtocol.Shot(shot));
        }

        void Send(byte[] packet)
        {
            if (socket == null || peer == null)
                return;
            try
            {
                socket.Send(packet, packet.Length, peer);
            }
            catch (SocketException)
            {
                // The network can drop out; the timeout reports it.
            }
        }

        public void Close()
        {
            if (socket != null)
            {
                if (Connected)
                    Send(NetProtocol.Bye());
                socket.Close();
            }
            socket = null;
            peer = null;
            Role = NetRole.None;
            sinceHeard = TimeoutSeconds;
        }

        public void Dispose() => Close();

        /// <summary>This device's IPv4 address on the local network, found without sending anything.</summary>
        public static IPAddress LocalAddress()
        {
            try
            {
                using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                probe.Connect("10.255.255.255", 1);
                if (probe.LocalEndPoint is IPEndPoint end && !IPAddress.IsLoopback(end.Address)
                    && !end.Address.Equals(IPAddress.Any))
                    return end.Address;
            }
            catch (SocketException)
            {
            }

            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                        return address;
            }
            catch (SocketException)
            {
            }
            return null;
        }
    }
}
