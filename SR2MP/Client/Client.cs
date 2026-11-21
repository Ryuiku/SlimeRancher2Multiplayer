using System.Buffers;
using System.Net;
using System.Net.Sockets;
using JetBrains.Annotations;
using SR2MP.Api;
using SR2MP.Client.Managers;
using SR2MP.Client.Models;
using SR2MP.Components.UI;
using SR2MP.Packets.Api;
using SR2MP.Packets.Internal;
using SR2MP.Packets.Player;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;
using SR2MP.Shared.Utils;

namespace SR2MP.Client;

/// <summary>
/// Provides the main client interface.
/// </summary>
public sealed class SR2MPClient
{
    private UdpClient? udpClient;
    private IPEndPoint? serverEndPoint;
    private Thread? receiveThread;
    private ReliabilityManager? reliabilityManager;

    private volatile bool isConnected;
    private volatile bool isConnecting;
    private volatile bool connectionAcknowledged;
    private Timer? connectionTimeoutTimer;
    private const int ConnectionTimeoutSeconds = 10;

    // The host sends a heartbeat every few seconds from a background timer, so even a host busy
    // loading a zone is heard from. Nothing at all for this long means the host's game closed,
    // hung or lost its connection: this game then leaves the world instead of waiting forever.
    private Timer? hostSilenceTimer;
    private long lastHeardFromHost;
    private const int HostSilenceSeconds = 30;
    internal const int LanConnectionTimeoutSeconds = 4;

    /// <summary>
    /// Where to connect next if the current attempt times out (the public address from a join code,
    /// after trying the host's LAN address first).
    /// </summary>
    internal IPEndPoint? FallbackEndPoint;

    // Goes up every time a connection is started or torn down. A receive thread, timer or queued
    // action that belongs to an older connection compares it and does nothing.
    private int connectionGeneration;

    private bool shownConnectionError;
    private bool subscribedToQuitting;

    private readonly ClientPacketManager packetManager;

    /// <summary>
    /// Gets a value indicating the connection status of the client.
    /// </summary>
    public bool IsConnected => isConnected;
    
    /// <summary>
    /// Gets a value indicating the connection status of the client.
    /// </summary>
    public bool IsConnecting => isConnecting;

    /// <summary>
    /// Gets a value indicating whether the client is currently fully in a world
    /// </summary>
    internal bool IsJoined => isConnected && connectionAcknowledged;

    /// <summary>
    /// Gets a value indicating the client's identifier.
    /// </summary>
    public string PlayerId { get; private set; } = string.Empty;

    /// <summary>
    /// Occurs when the client successfully connects to the server.
    /// </summary>
    /// <remarks>The parameter typically provides the connection details or the assigned client ID.</remarks>
    public event Action<string>? OnConnected;

    /// <summary>
    /// Occurs when the client is disconnected from the server.
    /// </summary>
    public event Action? OnDisconnected;

    /// <summary>
    /// Occurs when a new remote player joins the multiplayer session.
    /// </summary>
    /// <remarks>The parameter provides the unique identifier of the player who joined.</remarks>
    public event Action<string>? OnPlayerJoined;

    /// <summary>
    /// Occurs when a remote player leaves the multiplayer session.
    /// </summary>
    /// <remarks>The parameter provides the unique identifier of the player who left.</remarks>
    public event Action<string>? OnPlayerLeft;

    /// <summary>
    /// Occurs when a remote player's state (such as position, rotation, or animation) is updated.
    /// </summary>
    /// <remarks>The first parameter is the player's unique ID, and the second is their updated <see cref="RemotePlayer"/> data.</remarks>
    public event Action<string, RemotePlayer>? OnPlayerUpdate;

    internal SR2MPClient()
    {
        packetManager = new ClientPacketManager(this);

        PlayerManager.OnPlayerAdded += playerId => OnPlayerJoined?.Invoke(playerId);
        PlayerManager.OnPlayerRemoved += playerId => OnPlayerLeft?.Invoke(playerId);
        PlayerManager.OnPlayerUpdated += (playerId, player) => OnPlayerUpdate?.Invoke(playerId, player);
    }

    internal void Connect(string serverIp, int port, int timeoutSeconds = ConnectionTimeoutSeconds)
    {
        if (Main.Server.IsRunning)
        {
            SrLogger.LogWarning("You can not join a world while hosting a server.");
            return;
        }

        if (isConnected)
        {
            SrLogger.LogWarning("You are already connected to a Server!");
            return;
        }
        
        if (isConnecting)
        {
            SrLogger.LogWarning("You are already trying to connect to a Server!");
            return;
        }

        isConnecting = true;

        try
        {
            var generation = Interlocked.Increment(ref connectionGeneration);
            var parsedIp = IPAddress.Parse(serverIp);

            if (parsedIp.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (!Socket.OSSupportsIPv6)
                {
                    SrLogger.LogError("IPv6 is not supported on this machine! Please enable IPv6 or use an IPv4 address.");
                    throw new NotSupportedException("IPv6 is not available on this system");
                }

                udpClient = new UdpClient(AddressFamily.InterNetworkV6);
                SrLogger.LogMessage("Using IPv6 connection");
            }
            else
            {
                udpClient = new UdpClient(AddressFamily.InterNetwork);
                SrLogger.LogMessage("Using IPv4 connection");
            }

            PacketDeduplication.Clear();
            NetworkActorManager.InitialActorsLoaded = false;
            NetworkActorManager.KnownOwners.Clear();
            NetworkActorManager.ClearDeferredWorldChanges();
            // The world behind the actor ids is about to be replaced by the host's.
            NetworkActorManager.PendingRadiancy.Clear();
            NetworkDroneManager.CachedOwners.Clear();
            NetworkDroneManager.PendingOwners.Clear();
            NetworkDroneManager.ClearPrograms();
            NetworkGordoSlimeManager.Reset();
            NetworkTornadoManager.ResetClientState();

            serverEndPoint = new IPEndPoint(parsedIp, port);
            udpClient.Connect(serverEndPoint);

            udpClient.Client.ReceiveBufferSize = 512 * 1024;
            udpClient.Client.SendBufferSize = 512 * 1024;

            PlayerId = PlayerIdGenerator.GeneratePersistentPlayerId();

            reliabilityManager = new ReliabilityManager(SendRaw);
            reliabilityManager.Start();

            packetManager.RegisterHandlers(Main.Core);

            isConnected = true;
            MainThreadDispatcher.KeepRunningInBackground();
            connectionAcknowledged = false;

            // After every step that can fail, before any of the host's packets can be handled.
            NetworkGardenManager.ResetForNewConnection();

            var socket = udpClient;
            receiveThread = new Thread(new Action(() => ReceiveLoop(socket, generation)))
            {
                IsBackground = true
            };
            receiveThread.Start();

            connectionTimeoutTimer = new Timer(CheckConnectionTimeout, generation,
                TimeSpan.FromSeconds(timeoutSeconds), Timeout.InfiniteTimeSpan);

            Interlocked.Exchange(ref lastHeardFromHost, Environment.TickCount64);
            Interlocked.Exchange(ref hostSilenceTimer,
                new Timer(CheckHostSilence, generation, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)))?.Dispose();

            if (!subscribedToQuitting)
            {
                subscribedToQuitting = true;
                Application.quitting += new Action(Disconnect);
            }

            var connectPacket = new ConnectPacket
            {
                PlayerId = PlayerId,
                Username = Main.Username
            };

            SendPacket(connectPacket);

            SrLogger.LogMessage("Connecting to the Server...",
                $"Connecting to {serverIp}:{port} as {PlayerId}...");
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error connecting to the Server: {ex}");
            FallbackEndPoint = null;
            TearDownConnection();

            // The gardens were maybe set up for the session already: back to the offline game.
            try
            {
                NetworkGardenManager.OnDisconnected();
            }
            catch (Exception resetEx)
            {
                SrLogger.LogDebug($"Could not reset the gardens: {resetEx.Message}");
            }

            throw;
        }
    }

    private bool IsCurrentConnection(int generation) => generation == Volatile.Read(ref connectionGeneration);

    private void CheckConnectionTimeout(object? state)
    {
        // The attempt this timer belongs to. A newer attempt (the fallback address) must never be
        // torn down by the timer of the one before it.
        var generation = state is int created ? created : Volatile.Read(ref connectionGeneration);

        if (!IsCurrentConnection(generation))
            return;

        if (connectionAcknowledged || !isConnected)
            return;

        if (TryStartFallback(generation, "did not answer"))
            return;

        if (!IsCurrentConnection(generation))
            return;

        SrLogger.LogError("Connection timeout: Server did not respond in time");

        isConnecting = false;
        DisconnectOnMainThread(generation, null);
    }

    private void CheckHostSilence(object? state)
    {
        var generation = state is int created ? created : Volatile.Read(ref connectionGeneration);

        if (!IsCurrentConnection(generation) || !isConnected || !connectionAcknowledged)
            return;

        var silentFor = (Environment.TickCount64 - Interlocked.Read(ref lastHeardFromHost)) / 1000;
        if (silentFor < HostSilenceSeconds)
            return;

        // Once per connection.
        Interlocked.Exchange(ref hostSilenceTimer, null)?.Dispose();

        SrLogger.LogWarning($"Nothing heard from the host for {silentFor} s: their game closed, hung or lost its connection. Leaving the world.");
        DisconnectOnMainThread(generation,
            "Lost the connection to the host (no answer for 30 seconds). You left the world.");
    }

    // Leaving a world touches game objects (other players' avatars, actors), which is only allowed
    // on the main thread. Timers and the receive thread come through here.
    private void DisconnectOnMainThread(int generation, string? message)
    {
        MainThreadDispatcher.Instance.Enqueue(() =>
        {
            if (!IsCurrentConnection(generation) || !isConnected)
                return;

            Disconnect();

            // After leaving: leaving clears the chat.
            if (message != null)
            {
                MultiplayerUI.Instance.RegisterSystemMessage(message,
                    $"SYSTEM_HOST_LOST_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", MultiplayerUI.SystemMessageDisconnect);
            }
        });
    }

    /// <summary>
    /// The host's LAN address from a join code did not work out (no answer, or something else on this
    /// network refused the connection). Quietly drops that attempt and connects to the host's public
    /// address instead. Returns false if there is no other address to try.
    /// </summary>
    private bool TryStartFallback(int generation, string reason)
    {
        var fallback = Interlocked.Exchange(ref FallbackEndPoint, null);
        if (fallback == null)
            return false;

        connectionTimeoutTimer?.Dispose();

        SrLogger.LogWarning("Host did not answer on the local network, trying their public address...",
            $"LAN address {reason}, falling back to {fallback}");

        MainThreadDispatcher.Instance.Enqueue(() =>
        {
            // Something else happened in the meantime: the host answered after all, or the player gave up.
            if (!IsCurrentConnection(generation) || connectionAcknowledged || !isConnected)
                return;

            TearDownConnection();

            // Anything still queued came from the attempt we just dropped.
            MainThreadDispatcher.Instance.DropClientPackets();

            try
            {
                Connect(fallback.Address.ToString(), fallback.Port);
            }
            catch (Exception ex)
            {
                SrLogger.LogError($"Could not connect to the host's public address: {ex.Message}");
                MultiplayerUI.Instance.RegisterSystemMessage("Could not join the world, check the MelonLoader console for details",
                    $"SYSTEM_JOIN_FALLBACK_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", MultiplayerUI.SystemMessageClose);
            }
        });

        return true;
    }

    // Stops the current connection without telling anyone: no chat messages, no leave packet,
    // no disconnect event. Used when an attempt is replaced by another one.
    private void TearDownConnection()
    {
        Interlocked.Increment(ref connectionGeneration);

        isConnected = false;
        isConnecting = false;
        connectionAcknowledged = false;

        connectionTimeoutTimer?.Dispose();
        connectionTimeoutTimer = null;
        Interlocked.Exchange(ref hostSilenceTimer, null)?.Dispose();

        try
        {
            reliabilityManager?.Stop();
            udpClient?.Close();
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Error closing the connection: {ex.Message}");
        }

        udpClient = null;
        receiveThread = null;
    }

    internal void UpdateConnectionStatus(bool state) => isConnected = state;

    private void ReceiveLoop(UdpClient socket, int generation)
    {
        SrLogger.LogMessage("Client ReceiveLoop started!");

        EndPoint remoteEp = socket.Client.AddressFamily switch
        {
            AddressFamily.InterNetwork => new IPEndPoint(IPAddress.Any, 0),
            AddressFamily.InterNetworkV6 => new IPEndPoint(IPAddress.IPv6Any, 0),
            _ => throw new NotSupportedException("Unsupported address family")
        };

        var receiveBuffer = ArrayPool<byte>.Shared.Rent(2048);

        try
        {
            while (isConnected && IsCurrentConnection(generation))
            {
                try
                {
                    var receivedBytes = socket.Client.ReceiveFrom(receiveBuffer, ref remoteEp);

                    if (receivedBytes == 0)
                        continue;

                    // This connection was replaced while we were waiting.
                    if (!IsCurrentConnection(generation))
                        break;

                    // Acknowledgements come from the host's network thread even when its game has
                    // hung, so they don't count as hearing from the host.
                    if (receiveBuffer[0] != (byte)PacketType.ReservedAcknowledge)
                        Interlocked.Exchange(ref lastHeardFromHost, Environment.TickCount64);

                    packetManager.HandlePacket(receiveBuffer, receivedBytes, (IPEndPoint)remoteEp);
                    SrLogger.LogPacketSize($"Received {receivedBytes} bytes",
                        $"Received {receivedBytes} bytes from {remoteEp}");
                }
                catch (SocketException ex)
                {
                    // The connection was closed on purpose (disconnect, or replaced by another attempt).
                    if (!IsCurrentConnection(generation))
                        break;

                    // Something on this network refused the host's LAN address: use the public one.
                    if (!connectionAcknowledged && TryStartFallback(generation, "refused the connection"))
                        break;

                    // This prevents WSAEINTR from logging, this is correct
                    if (ex.ErrorCode is not 10004 and not 10054)
                    {
                        SrLogger.LogError($"ReceiveLoop error: Socket Exception:{ex.ErrorCode}\n{ex}");
                    }

                    if (ex.ErrorCode == 10054 && !shownConnectionError)
                    {
                        SrLogger.LogError("The server is not running!\n" +
                                          "If the server is running, there is something wrong with PlayIt or your tunnel service.\n" +
                                          "If this is not the case, check your firewall settings");
                        shownConnectionError = true;
                    }

                    MultiplayerUI.Instance.RegisterSystemMessage("Could not join the world, check the MelonLoader console for details", $"SYSTEM_JOIN_10054_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", MultiplayerUI.SystemMessageClose);
                    DisconnectOnMainThread(generation, null);
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // The socket was closed under us.
                    break;
                }
                catch (Exception ex)
                {
                    SrLogger.LogError($"ReceiveLoop error: {ex}");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(receiveBuffer);
        }

        SrLogger.LogMessage("Client ReceiveLoop ended!");
    }

    internal void SendPacket<T>(T packet) where T : IPacket
        => PrepareAndSend(packet, packet.Reliability, (byte)packet.Type, packet.Channel, SerialiseInternalPacket<T>.Serialiser);

    /// <summary>
    /// Sends a packet over the network.
    /// </summary>
    /// <typeparam name="T">The type of the packet to send.</typeparam>
    /// <param name="data">The packet data to send.</param>
    [PublicApi]
    public void SendData<T>(T data) where T : ICustomPacket
    {
        if (!ApiHandlers.CurrentNetIdMapping2.TryGetValue(data.GetType(), out var modId))
        {
            SrLogger.LogWarning($"Cannot send API packet: No ModId registered for custom packet type {data.GetType().FullName}.");
            return;
        }

        var apiHeader = new ApiPacket(data.Reliability, data.Channel, modId);
        PrepareAndSend((apiHeader, data), apiHeader.Reliability, (byte)apiHeader.Type, apiHeader.Channel, SerialiseApiPacket<T>.Serialiser);
    }

    private void PrepareAndSend<T>(T state, PacketReliability reliability, byte packetType,
        NetworkChannel channel, PacketWriterDelegate<T> writeAction)
    {
        if (udpClient == null || serverEndPoint == null || !isConnected)
        {
            SrLogger.LogWarning("Cannot send packet: Not connected to a Server!");
            return;
        }

        using var writer = PacketWriter.Borrow();

        try
        {
            writeAction(writer, state);
            var data = writer.ToSpan();
            SrLogger.LogPacketSize($"Sending {data.Length} bytes to Server...");

            ushort sequenceNumber = 0;

            if (reliability.HasFlag(PacketReliability.Ordered))
                sequenceNumber = reliabilityManager?.GetNextSequenceNumber(channel, packetType, serverEndPoint, reliability) ?? 0;

            var splitResult = PacketChunkManager.SplitPacket(data, reliability, channel, sequenceNumber, serverEndPoint, out var packetId);

            // Reliable packets are tracked until acknowledged. The reliability manager sends large ones
            // itself (paced), and anything queued behind a large one, so they arrive in order.
            var sendNow = true;

            if (reliability.HasFlag(PacketReliability.Reliable) && reliabilityManager != null)
                sendNow = reliabilityManager.TrackPacket(splitResult, serverEndPoint, packetId, data[0], reliability);

            if (sendNow)
            {
                for (var i = 0; i < splitResult.Count; i++)
                    SendRaw(splitResult.Chunks[i], serverEndPoint);
            }

            if (!reliability.HasFlag(PacketReliability.Reliable))
                splitResult.Dispose();

            SrLogger.LogPacketSize($"Sent {data.Length} bytes to Server in {splitResult.Count} chunk(s) (ID={packetId}).");
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Failed to send packet: {ex}");
        }
    }

    // Sends raw data without reliability tracking (used for resends)
    private void SendRaw(ArraySegment<byte> data, IPEndPoint endPoint)
    {
        if (data.Array != null)
            udpClient?.Client.SendTo(data.Array, data.Offset, data.Count, SocketFlags.None, endPoint);
    }

    // Handle acknowledgement from server, used in client packet manager
    internal void HandleAck(IPEndPoint sender, ushort packetId, byte packetType, ushort chunkIndex)
        => reliabilityManager?.HandleAck(sender, packetId, packetType, chunkIndex);

    internal bool ShouldProcessOrderedPacket(IPEndPoint sender, ushort sequenceNumber, byte packetType, NetworkChannel channel, PacketReliability reliability,
        Action? processAction = null)
            => reliabilityManager?.ShouldProcessOrderedPacket(sender, sequenceNumber, packetType, channel, reliability, processAction) ?? true;

    internal void Disconnect()
    {
        if (!isConnected)
            return;

        try
        {
            MultiplayerUI.Instance.ClearChatMessages();

            if (!shownConnectionError)
            {
                MultiplayerUI.Instance.RegisterSystemMessage(
                    "You disconnected from the world!",
                    $"SYSTEM_DISCONNECT_LOCAL_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    MultiplayerUI.SystemMessageDisconnect);
            }
            try
            {
                var leavePacket = new PlayerLeavePacket
                {
                    Type = PacketType.PlayerLeave,
                    PlayerId = PlayerId
                };

                SendPacket(leavePacket);
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Could not send leave packet: {ex.Message}");
            }

            PacketDeduplication.Clear();
            NetworkActorManager.InitialActorsLoaded = false;
            FallbackEndPoint = null;

            // From here on the receive thread of this connection stops quietly instead of
            // reporting the closed socket as a failed join.
            Interlocked.Increment(ref connectionGeneration);

            isConnected = false;
            isConnecting = false;
            connectionAcknowledged = false;

            // Nobody else moves the actors in this world copy anymore. On the main thread: this can
            // be reached from a timer, and game objects may only be touched from the main thread.
            MainThreadDispatcher.Instance.Enqueue(() =>
            {
                try
                {
                    if (!isConnected && !isConnecting)
                    {
                        // Packets of the old session that were not handled yet would be applied
                        // after this and freeze their actors again.
                        if (MainThreadDispatcher.Instance)
                            MainThreadDispatcher.Instance.DropClientPackets();

                        ActorManager.ReleaseAllToLocal();

                        // The host's tornado copies would linger in the offline world.
                        NetworkTornadoManager.ResetClientState();
                    }
                }
                catch (Exception ex)
                {
                    SrLogger.LogWarning($"Could not take actors back after disconnecting: {ex.Message}");
                }
            });

            connectionTimeoutTimer?.Dispose();
            connectionTimeoutTimer = null;
            Interlocked.Exchange(ref hostSilenceTimer, null)?.Dispose();

            reliabilityManager?.Stop();

            udpClient?.Close();
            udpClient = null;

            if (receiveThread is { IsAlive: true })
                SrLogger.LogWarning("Receive thread did not stop gracefully");

            receiveThread = null;

            foreach (var player in GetAllRemotePlayers())
            {
                var playerId = player.PlayerId;

                if (!PlayerObjects.TryGetValue(playerId, out var playerObject))
                    continue;

                if (playerObject)
                {
                    Object.Destroy(playerObject);
                    SrLogger.LogPacketSize($"Destroyed player object for {playerId}");
                }

                PlayerObjects.Remove(playerId);
            }

            PlayerManager.Clear();
            SR2MP.Shared.Managers.PlayerCosmeticsManager.Clear();
            NetworkStringPool.Clear();
            ApiHandlers.ClearNetIds();

            SrLogger.LogMessage("Disconnected from server");
            InvokeDisconnected();
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error during disconnect: {ex}");
        }
    }

    internal void NotifyConnected()
    {
        FallbackEndPoint = null;
        isConnecting = false;
        isConnected = true;
        connectionAcknowledged = true;

        connectionTimeoutTimer?.Dispose();
        connectionTimeoutTimer = null;

        InvokeConnected();
    }
    
    private void InvokeConnected()
    {
        var subscribers = OnConnected;

        if (subscribers == null)
            return;

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action<string>)subscriber).Invoke(PlayerId);
            }
            catch (Exception ex)
            {
                var method = subscriber.Method;
                SrLogger.LogError($"OnConnected subscriber {method.DeclaringType?.FullName}.{method.Name} failed: {ex}");
            }
        }
    }
    
    private void InvokeDisconnected()
    {
        var subscribers = OnDisconnected;

        if (subscribers == null)
            return;

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action)subscriber).Invoke();
            }
            catch (Exception ex)
            {
                var method = subscriber.Method;
                SrLogger.LogError($"OnDisconnected subscriber {method.DeclaringType?.FullName}.{method.Name} failed: {ex}");
            }
        }
    }

    /// <summary>
    /// Retrieves a specific remote player by their unique identifier.
    /// </summary>
    /// <param name="playerId">The unique identifier of the player to retrieve.</param>
    /// <returns>The <see cref="RemotePlayer"/> instance if found; otherwise, <c>null</c>.</returns>
    public static RemotePlayer? GetRemotePlayer(string playerId) => PlayerManager.GetPlayer(playerId);

    /// <summary>
    /// Retrieves a list of all currently connected remote players.
    /// </summary>
    /// <returns>A <see cref="List{RemotePlayer}"/> containing all active remote players in the session.</returns>
    private static List<RemotePlayer> GetAllRemotePlayers() => PlayerManager.GetAllPlayers();
}