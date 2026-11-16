using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using SR2MP.Packets.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Utils;

namespace SR2MP.Shared.Managers;

internal sealed class ReliabilityManager
{
    private sealed class PendingPacket : IRecyclable
    {
        public SplitResult SplitData;
        public IPEndPoint Destination = null!;
        public ushort PacketId;
        public byte PacketType;
        public DateTime FirstSendTime;
        public DateTime LastProgressTime;
        public DateTime LastPassEnd;
        public int SendCount;

        // Per-chunk acknowledgement state, so only missing chunks get resent.
        public bool[] ChunkAcked = Array.Empty<bool>();
        public int AckedCount;

        // Paced sending: chunks of a pass are sent a few at a time instead of all at once.
        public bool PassInProgress;
        public int Cursor;

        public bool IsRecycled { get; set; }

        private void Initialize(SplitResult splitData, IPEndPoint destination, ushort packetId,
            byte packetType, bool alreadySent)
        {
            SplitData = splitData;
            Destination = destination;
            PacketId = packetId;
            PacketType = packetType;

            var now = DateTime.UtcNow;
            FirstSendTime = now;
            LastProgressTime = now;
            LastPassEnd = now;
            SendCount = 1;

            ChunkAcked = new bool[splitData.Count];
            AckedCount = 0;

            // Single chunk packets are sent right away by the caller.
            // Multi chunk packets are sent by the resend thread, paced.
            PassInProgress = !alreadySent;
            Cursor = 0;
        }

        public void Recycle()
        {
            SplitData.Dispose();
            Destination = null!;
            ChunkAcked = Array.Empty<bool>();
        }

        public void Dispose() => Return(this);

        public static PendingPacket Borrow(SplitResult splitData, IPEndPoint destination,
            ushort packetId, byte packetType, bool alreadySent)
        {
            var packet = RecyclePool<PendingPacket>.Borrow();
            packet.Initialize(splitData, destination, packetId, packetType, alreadySent);
            return packet;
        }

        public static void Return(PendingPacket packet)
        {
            lock (packet)
            {
                if (!packet.IsRecycled)
                    RecyclePool<PendingPacket>.Return(packet);
            }
        }
    }

    private readonly ConcurrentDictionary<PacketKey, PendingPacket> pendingPackets = new();
    private readonly ConcurrentDictionary<ChannelKey, ushort> lastProcessedSequence = new();
    private readonly ConcurrentDictionary<ChannelKey, int> sequenceNumbersByChannel = new();
    private readonly ConcurrentDictionary<ChannelKey, SortedDictionary<ushort, Action>> reorderBuffers = new();
    private readonly ConcurrentDictionary<ChannelKey, object> reorderLocks = new();
    private readonly ConcurrentQueue<PendingPacket> pendingDisposal = new();

    private readonly Action<ArraySegment<byte>, IPEndPoint> sendRawCallback;

    private Thread? resendThread;
    private volatile bool isRunning;

    private static readonly TimeSpan ResendInterval = TimeSpan.FromMilliseconds(500);

    // A packet is only given up on when the receiver stops acknowledging anything for this long,
    // instead of a fixed time since the first send. Large packets (the initial world data on join)
    // can take a while over slow connections, but they keep making progress.
    private static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxRetryTime = TimeSpan.FromSeconds(120);
    private const int MaxResendAttempts = 64;
    private const int MaxReorderBufferSize = 64;

    // How many chunks (max ~512 bytes each) may be sent to one destination per resend tick.
    // The resend thread ticks roughly every 10-16ms, so this is ~250-400KB/s per player.
    // Sending everything at once overflowed tunnels/routers and the tail of every burst got dropped.
    private const int ChunkBudgetPerTick = 8;

    private readonly Dictionary<IPEndPoint, int> chunkBudgetUsed = new();

    public ReliabilityManager(Action<ArraySegment<byte>, IPEndPoint> sendRawCallback)
    {
        this.sendRawCallback = sendRawCallback;
    }

    public void Start()
    {
        if (isRunning)
            return;

        isRunning = true;
        resendThread = new Thread(new Action(ResendLoop))
        {
            IsBackground = true,
            Name = "ReliabilityResendThread"
        };
        resendThread.Start();

        SrLogger.LogMessage("ReliabilityManager started");
    }

    public void Stop()
    {
        if (!isRunning)
            return;

        isRunning = false;

        foreach (var packet in pendingPackets.Values)
            PendingPacket.Return(packet);

        pendingPackets.Clear();
        
        while (pendingDisposal.TryDequeue(out var leftover))
            PendingPacket.Return(leftover);

        lastProcessedSequence.Clear();
        sequenceNumbersByChannel.Clear();
        reorderBuffers.Clear();
        reorderLocks.Clear();

        SrLogger.LogMessage("ReliabilityManager stopped");
    }

    // Tracks a reliable packet until it is acknowledged.
    // alreadySent: true if the caller already sent every chunk once. When false, the resend thread
    // sends the chunks itself, paced, so large packets don't flood the connection.
    public void TrackPacket(SplitResult splitData, IPEndPoint destination, ushort packetId,
        byte packetType, PacketReliability reliability, bool alreadySent = true)
    {
        if (!reliability.HasFlag(PacketReliability.Reliable))
            return;

        var key = new PacketKey(packetType, packetId, destination);
        var packet = PendingPacket.Borrow(splitData, destination, packetId, packetType, alreadySent);

        pendingPackets.AddOrUpdate(key, packet, (_, old) =>
        {
            // Packet id wrapped around while an old packet was still pending, drop the old one.
            pendingDisposal.Enqueue(old);
            return packet;
        });
    }

    public void HandleAck(IPEndPoint sender, ushort packetId, byte packetType, ushort chunkIndex = AckPacket.WholePacket)
    {
        var key = new PacketKey(packetType, packetId, sender);

        if (chunkIndex != AckPacket.WholePacket)
        {
            if (!pendingPackets.TryGetValue(key, out var partial))
                return;

            var complete = false;

            lock (partial)
            {
                if (partial.IsRecycled || chunkIndex >= partial.ChunkAcked.Length)
                    return;

                partial.LastProgressTime = DateTime.UtcNow;

                if (!partial.ChunkAcked[chunkIndex])
                {
                    partial.ChunkAcked[chunkIndex] = true;
                    partial.AckedCount++;
                }

                complete = partial.AckedCount >= partial.ChunkAcked.Length;
            }

            // All chunks arrived, the whole packet ack will follow but nothing needs resending anymore
            if (!complete)
                return;
        }

        if (!pendingPackets.TryRemove(key, out var packet))
            return;

        SrLogger.LogPacketSize(
            $"ACK received for packet {packetId} (type={packetType}) " +
            $"after {packet.SendCount} sends, " +
            $"latency={(DateTime.UtcNow - packet.FirstSendTime).TotalMilliseconds:F1}ms");

        pendingDisposal.Enqueue(packet);
    }

    /// <summary>
    /// Returns the next sequence number for a given packet type / channel / destination.
    /// Sequence numbers wrap from <see cref="ushort.MaxValue"/> back to 1.
    /// </summary>
    public ushort GetNextSequenceNumber(NetworkChannel channel, byte packetType, IPEndPoint destination)
    {
        var key = new ChannelKey(destination, channel, packetType);
        var seq = sequenceNumbersByChannel.AddOrUpdate(
            key,
            1,
            (_, current) => current >= ushort.MaxValue ? 1 : current + 1);

        return (ushort)seq;
    }

    public bool ShouldProcessOrderedPacket(IPEndPoint sender, ushort sequenceNumber, byte packetType,
        NetworkChannel channel, PacketReliability reliability, Action? processAction = null)
    {
        var key     = new ChannelKey(sender, channel, packetType);
        var lockObj = reorderLocks.GetOrAdd(key, _ => new object());

        lock (lockObj)
        {
            if (!lastProcessedSequence.TryGetValue(key, out var lastSequence))
            {
                // First packet from this sender on this channel/type
                lastProcessedSequence[key] = sequenceNumber;
                return true;
            }

            if (IsSequenceNewer(sequenceNumber, lastSequence) || sequenceNumber == (ushort)(lastSequence + 1))
            {
                if (reliability == PacketReliability.Ordered)
                {
                    lastProcessedSequence[key] = sequenceNumber;
                    return true;
                }

                var expectedSequence = (ushort)(lastSequence + 1);

                if (sequenceNumber == expectedSequence)
                {
                    lastProcessedSequence[key] = sequenceNumber;
                    DrainReorderBuffer(key, lockObj);
                    return true;
                }

                if (processAction != null)
                {
                    var buffer = reorderBuffers.GetOrAdd(key, _ => new SortedDictionary<ushort, Action>());

                    if (buffer.Count < MaxReorderBufferSize)
                    {
                        buffer[sequenceNumber] = processAction;
                        SrLogger.LogPacketSize(
                            $"Buffered out-of-order packet: " +
                            $"expected seq={expectedSequence}, got seq={sequenceNumber}, " +
                            $"type={packetType}, channel={channel}, buffer size={buffer.Count}");
                    }
                    else
                    {
                        SrLogger.LogPacketSize(
                            $"Reorder buffer full, dropping packet: " +
                            $"seq={sequenceNumber}, type={packetType}, channel={channel}");
                    }
                }
                else
                {
                    SrLogger.LogPacketAcknowledge(
                        $"Out-of-order packet dropped: " +
                        $"expected seq={expectedSequence}, got seq={sequenceNumber}, " +
                        $"type={packetType}, channel={channel}");
                }
            }

            return false;
        }
    }

    // todo: review
    private void DrainReorderBuffer(ChannelKey key, object lockObj)
    {
        if (!reorderBuffers.TryGetValue(key, out var buffer) || buffer.Count == 0)
            return;

        while (buffer.Count > 0)
        {
            var next = (ushort)(lastProcessedSequence[key] + 1);
            if (!buffer.TryGetValue(next, out var bufferedAction))
                break;

            buffer.Remove(next);
            lastProcessedSequence[key] = next;

            Monitor.Exit(lockObj);
            try
            {
                bufferedAction();
            }
            finally
            {
                Monitor.Enter(lockObj);
            }
        }
    }

    private void ResendLoop()
    {
        while (isRunning)
        {
            try
            {
                var count = pendingPackets.Count;
                if (count == 0)
                {
                    DrainDisposalQueue();
                    Thread.Sleep(10);
                    continue;
                }

                var now           = DateTime.UtcNow;
                var keysToRemove  = ArrayPool<PacketKey>.Shared.Rent(count);
                var removeCount   = 0;

                chunkBudgetUsed.Clear();

                foreach (var (key, packet) in pendingPackets)
                {
                    if (removeCount >= keysToRemove.Length)
                        break;

                    lock (packet)
                    {
                        if (packet.IsRecycled)
                            continue;

                        if (now - packet.LastProgressTime > NoProgressTimeout
                            || now - packet.FirstSendTime > MaxRetryTime)
                        {
                            SrLogger.LogWarning(
                                $"Reliable packet {packet.PacketId} (type={packet.PacketType}) to {packet.Destination} " +
                                $"was given up after {packet.SendCount} send passes " +
                                $"({packet.AckedCount}/{packet.ChunkAcked.Length} chunks acknowledged)");
                            keysToRemove[removeCount++] = key;
                            continue;
                        }

                        if (!packet.PassInProgress)
                        {
                            if (now - packet.LastPassEnd <= ResendInterval)
                                continue;

                            if (packet.SendCount >= MaxResendAttempts)
                            {
                                SrLogger.LogWarning(
                                    $"Reliable packet {packet.PacketId} (type={packet.PacketType}) " +
                                    $"was given up after {packet.SendCount} send passes");
                                keysToRemove[removeCount++] = key;
                                continue;
                            }

                            packet.PassInProgress = true;
                            packet.Cursor = 0;
                            packet.SendCount++;

                            if (packet.SendCount % 10 == 0)
                            {
                                SrLogger.LogPacketAcknowledge(
                                    $"Resending packet {packet.PacketId} " +
                                    $"(type={packet.PacketType}) attempt #{packet.SendCount}");
                            }
                        }

                        SendPacedChunks(packet, now);
                    }
                }

                // Remove timed out packets
                for (var i = 0; i < removeCount; i++)
                {
                    if (pendingPackets.TryRemove(keysToRemove[i], out var timedOutPacket))
                        PendingPacket.Return(timedOutPacket);
                }

                ArrayPool<PacketKey>.Shared.Return(keysToRemove);

                DrainDisposalQueue();

                Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                SrLogger.LogError($"ResendLoop error: {ex}");
            }
        }
    }

    // Sends the next unacknowledged chunks of the current pass, within this tick's budget for the destination.
    private void SendPacedChunks(PendingPacket packet, DateTime now)
    {
        chunkBudgetUsed.TryGetValue(packet.Destination, out var used);

        var chunks = packet.SplitData.Chunks;
        var total = packet.SplitData.Count;

        while (packet.Cursor < total && used < ChunkBudgetPerTick)
        {
            var index = packet.Cursor++;

            if (packet.ChunkAcked[index])
                continue;

            sendRawCallback(chunks[index], packet.Destination);
            used++;
        }

        chunkBudgetUsed[packet.Destination] = used;

        if (packet.Cursor < total)
            return;

        packet.PassInProgress = false;
        packet.LastPassEnd = now;
    }

    private void DrainDisposalQueue()
    {
        while (pendingDisposal.TryDequeue(out var packet))
            PendingPacket.Return(packet);
    }

    private static bool IsSequenceNewer(ushort s1, ushort s2)
    {
        return ((s1 > s2) && (s1 - s2 <= 32768)) ||
               ((s1 < s2) && (s2 - s1 > 32768));
    }

    public int GetPendingPacketCount() => pendingPackets.Count;
}