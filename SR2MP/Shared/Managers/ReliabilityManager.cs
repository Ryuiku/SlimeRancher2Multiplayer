using System.Collections.Concurrent;
using System.Net;
using SR2MP.Packets.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Utils;

namespace SR2MP.Shared.Managers;

internal sealed class ReliabilityManager
{
    private sealed class PendingPacket
    {
        public readonly SplitResult SplitData;
        public readonly IPEndPoint Destination;
        public readonly ushort PacketId;
        public readonly byte PacketType;

        // Per-chunk acknowledgement state, so only missing chunks get resent.
        public readonly bool[] ChunkAcked;
        public int AckedCount;

        public DateTime FirstSendTime;
        public DateTime LastProgressTime;
        public DateTime LastPassEnd;
        public int SendCount;

        // Paced sending: chunks of a pass are sent a few at a time instead of all at once.
        public bool PassInProgress;
        public int Cursor;

        // Queued behind earlier packets to the same destination. Nothing was sent yet and no timeout runs.
        public bool Waiting;

        // Acknowledged or given up. The chunk buffers went back to the pool and must not be touched.
        public volatile bool Done;

        public bool IsMultiChunk => ChunkAcked.Length > 1;

        public PendingPacket(SplitResult splitData, IPEndPoint destination, ushort packetId, byte packetType)
        {
            SplitData = splitData;
            Destination = destination;
            PacketId = packetId;
            PacketType = packetType;
            ChunkAcked = new bool[splitData.Count];

            var now = DateTime.UtcNow;
            FirstSendTime = now;
            LastProgressTime = now;
            LastPassEnd = now;
            SendCount = 1;
        }

        public void Finish()
        {
            lock (this)
            {
                if (Done)
                    return;

                Done = true;
                SplitData.Dispose();
            }
        }
    }

    // Reliable packets to one destination that have to go out in the order they were sent.
    // A packet of more than one chunk is sent paced, which takes a while. Without this, every small
    // packet sent after it arrived first (for example the gadget links before the gadgets they link).
    private sealed class SendLane
    {
        // The multi-chunk packet currently being delivered. Everything after it waits until it is
        // fully acknowledged (or given up).
        public PendingPacket? Blocker;
        public readonly Queue<PendingPacket> Waiting = new();
    }

    private readonly ConcurrentDictionary<PacketKey, PendingPacket> pendingPackets = new();
    private readonly ConcurrentDictionary<ChannelKey, ushort> lastProcessedSequence = new();
    private readonly ConcurrentDictionary<ChannelKey, int> sequenceNumbersByChannel = new();
    private readonly ConcurrentDictionary<ChannelKey, SortedDictionary<ushort, Action>> reorderBuffers = new();
    private readonly ConcurrentDictionary<ChannelKey, object> reorderLocks = new();
    private readonly Dictionary<IPEndPoint, SendLane> lanes = new();
    private readonly object laneLock = new();
    private readonly List<IPEndPoint> emptyLanes = new();
    private readonly List<KeyValuePair<PacketKey, PendingPacket>> finishedPackets = new();

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

    // Past this many waiting packets for one destination (a connection that stopped answering),
    // small packets are sent straight away again instead of piling up.
    private const int MaxWaitingPackets = 4096;

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
            packet.Finish();

        pendingPackets.Clear();

        lock (laneLock)
            lanes.Clear();

        lastProcessedSequence.Clear();
        sequenceNumbersByChannel.Clear();
        reorderBuffers.Clear();
        reorderLocks.Clear();

        SrLogger.LogMessage("ReliabilityManager stopped");
    }

    /// <summary>
    /// Tracks a reliable packet until it is acknowledged.
    /// Returns true if the caller should send the chunks now. Returns false when this manager sends
    /// them itself: packets of more than one chunk are sent paced so they don't flood the connection,
    /// and anything sent to the same destination while one of those is still being delivered waits
    /// its turn, so reliable packets arrive in the order they were sent.
    /// </summary>
    public bool TrackPacket(SplitResult splitData, IPEndPoint destination, ushort packetId,
        byte packetType, PacketReliability reliability)
    {
        if (!reliability.HasFlag(PacketReliability.Reliable))
            return true;

        var key = new PacketKey(packetType, packetId, destination);
        var packet = new PendingPacket(splitData, destination, packetId, packetType);
        bool sendNow;

        lock (laneLock)
        {
            lanes.TryGetValue(destination, out var lane);

            var busy = lane != null && (lane.Blocker is { Done: false } || lane.Waiting.Count > 0);

            if (busy && (packet.IsMultiChunk || lane!.Waiting.Count < MaxWaitingPackets))
            {
                packet.Waiting = true;
                lane!.Waiting.Enqueue(packet);
                sendNow = false;
            }
            else if (packet.IsMultiChunk)
            {
                if (lane == null)
                    lanes[destination] = lane = new SendLane();

                lane.Blocker = packet;
                packet.PassInProgress = true;
                sendNow = false;
            }
            else
            {
                sendNow = true;
            }

            pendingPackets.AddOrUpdate(key, packet, (_, old) =>
            {
                // Packet id wrapped around while an old packet was still pending, drop the old one.
                old.Finish();
                return packet;
            });
        }

        return sendNow;
    }

    public void HandleAck(IPEndPoint sender, ushort packetId, byte packetType, ushort chunkIndex = AckPacket.WholePacket)
    {
        var key = new PacketKey(packetType, packetId, sender);

        if (!pendingPackets.TryGetValue(key, out var packet))
            return;

        lock (packet)
        {
            // A waiting packet was never sent, so this ack belongs to an older packet with the same id.
            if (packet.Done || packet.Waiting)
                return;

            if (chunkIndex != AckPacket.WholePacket)
            {
                if (chunkIndex >= packet.ChunkAcked.Length)
                    return;

                packet.LastProgressTime = DateTime.UtcNow;

                if (!packet.ChunkAcked[chunkIndex])
                {
                    packet.ChunkAcked[chunkIndex] = true;
                    packet.AckedCount++;
                }

                // All chunks arrived, the whole packet ack will follow but nothing needs resending anymore
                if (packet.AckedCount < packet.ChunkAcked.Length)
                    return;
            }
        }

        if (!pendingPackets.TryRemove(new KeyValuePair<PacketKey, PendingPacket>(key, packet)))
            return;

        SrLogger.LogPacketSize(
            $"ACK received for packet {packetId} (type={packetType}) " +
            $"after {packet.SendCount} sends, " +
            $"latency={(DateTime.UtcNow - packet.FirstSendTime).TotalMilliseconds:F1}ms");

        packet.Finish();
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
                // First packet from this sender on this channel/type. Plain ordered packets may
                // have lost earlier ones for good, so they start from whatever comes. Reliable ones
                // start at 1 (GetNextSequenceNumber): taking a later one as the start dropped the
                // first packet for good when it was resent after the second had arrived.
                if (reliability == PacketReliability.Ordered)
                {
                    lastProcessedSequence[key] = sequenceNumber;
                    return true;
                }

                lastSequence = 0;
            }

            if (IsSequenceNewer(sequenceNumber, lastSequence) || sequenceNumber == NextSequence(lastSequence))
            {
                if (reliability == PacketReliability.Ordered)
                {
                    lastProcessedSequence[key] = sequenceNumber;
                    return true;
                }

                var expectedSequence = NextSequence(lastSequence);

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
            var next = NextSequence(lastProcessedSequence[key]);
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
                if (pendingPackets.IsEmpty)
                {
                    Thread.Sleep(10);
                    continue;
                }

                var now = DateTime.UtcNow;

                chunkBudgetUsed.Clear();
                finishedPackets.Clear();

                StartWaitingPackets(now);

                foreach (var entry in pendingPackets)
                {
                    var packet = entry.Value;

                    lock (packet)
                    {
                        if (packet.Done || packet.Waiting)
                            continue;

                        if (now - packet.LastProgressTime > NoProgressTimeout
                            || now - packet.FirstSendTime > MaxRetryTime)
                        {
                            SrLogger.LogWarning(
                                $"Reliable packet {packet.PacketId} (type={packet.PacketType}) to {packet.Destination} " +
                                $"was given up after {packet.SendCount} send passes " +
                                $"({packet.AckedCount}/{packet.ChunkAcked.Length} chunks acknowledged)");
                            finishedPackets.Add(entry);
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
                                finishedPackets.Add(entry);
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
                foreach (var entry in finishedPackets)
                {
                    pendingPackets.TryRemove(entry);
                    entry.Value.Finish();
                }

                finishedPackets.Clear();

                Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                SrLogger.LogError($"ResendLoop error: {ex}");
                Thread.Sleep(10);
            }
        }
    }

    // Starts sending the packets that were waiting for an earlier packet to the same destination,
    // in the order they were queued, within this tick's budget.
    private void StartWaitingPackets(DateTime now)
    {
        lock (laneLock)
        {
            if (lanes.Count == 0)
                return;

            emptyLanes.Clear();

            foreach (var (destination, lane) in lanes)
            {
                if (lane.Blocker is { Done: true })
                    lane.Blocker = null;

                chunkBudgetUsed.TryGetValue(destination, out var used);

                while (lane.Blocker == null && lane.Waiting.Count > 0 && used < ChunkBudgetPerTick)
                {
                    var next = lane.Waiting.Dequeue();

                    lock (next)
                    {
                        if (next.Done)
                            continue;

                        next.Waiting = false;
                        next.FirstSendTime = now;
                        next.LastProgressTime = now;
                        next.LastPassEnd = now;

                        if (next.IsMultiChunk)
                        {
                            // Sent paced by the resend loop, and holds back everything queued after it.
                            next.PassInProgress = true;
                            next.Cursor = 0;
                            lane.Blocker = next;
                        }
                        else
                        {
                            TrySendRaw(next.SplitData.Chunks[0], destination);
                            used++;
                        }
                    }
                }

                chunkBudgetUsed[destination] = used;

                if (lane.Blocker == null && lane.Waiting.Count == 0)
                    emptyLanes.Add(destination);
            }

            foreach (var destination in emptyLanes)
                lanes.Remove(destination);
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

            TrySendRaw(chunks[index], packet.Destination);
            used++;
        }

        chunkBudgetUsed[packet.Destination] = used;

        if (packet.Cursor < total)
            return;

        packet.PassInProgress = false;
        packet.LastPassEnd = now;
    }

    // A failed send is treated like a lost packet: it is resent on the next pass or given up by the timeout.
    private void TrySendRaw(ArraySegment<byte> chunk, IPEndPoint destination)
    {
        try
        {
            sendRawCallback(chunk, destination);
        }
        catch (Exception ex)
        {
            SrLogger.LogPacketAcknowledge($"Could not send to {destination}: {ex.Message}");
        }
    }

    // Sequence numbers go from ushort.MaxValue back to 1, never 0.
    private static ushort NextSequence(ushort sequence)
        => sequence == ushort.MaxValue ? (ushort)1 : (ushort)(sequence + 1);

    /// <summary>
    /// Forgets the sequences kept for this endpoint (only the ones we send with, if asked). A
    /// player who connects again from the same address (through a tunnel or relay) counts from 1.
    /// </summary>
    public void ForgetEndpoint(IPEndPoint endPoint, bool outgoingOnly = false)
    {
        foreach (var key in sequenceNumbersByChannel.Keys)
            if (key.IsFor(endPoint))
                sequenceNumbersByChannel.TryRemove(key, out _);

        if (outgoingOnly)
            return;

        foreach (var key in lastProcessedSequence.Keys)
            if (key.IsFor(endPoint))
                lastProcessedSequence.TryRemove(key, out _);

        foreach (var key in reorderBuffers.Keys)
            if (key.IsFor(endPoint))
                reorderBuffers.TryRemove(key, out _);
    }

    private static bool IsSequenceNewer(ushort s1, ushort s2)
    {
        return ((s1 > s2) && (s1 - s2 <= 32768)) ||
               ((s1 < s2) && (s2 - s1 > 32768));
    }

    public int GetPendingPacketCount() => pendingPackets.Count;
}