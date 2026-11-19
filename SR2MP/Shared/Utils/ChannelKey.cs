using System.Net;
using SR2MP.Packets.Utils;

namespace SR2MP.Shared.Utils;

/// <summary>
/// A key used to track sequence numbers and reorder buffers per packet type, channel and endpoint.
/// Ordering per packet type in a channel, different ordered packet types on the same channel won't collide.
/// </summary>
internal readonly struct ChannelKey : IEquatable<ChannelKey>
{
    private readonly IPEndPoint EndPoint;
    private readonly NetworkChannel Channel;
    private readonly byte PacketType;

    // Reliable and unreliable ordered packets of one type count separately: a lost unreliable one
    // is never resent, and a reliable stream waiting for it would wait forever.
    private readonly bool Reliable;

    public ChannelKey(IPEndPoint endPoint, NetworkChannel channel, byte packetType, bool reliable)
    {
        EndPoint = endPoint;
        Channel = channel;
        PacketType = packetType;
        Reliable = reliable;
    }

    public bool Equals(ChannelKey other)
        => PacketType == other.PacketType && Channel == other.Channel && Reliable == other.Reliable && EndPoint.Equals(other.EndPoint);

    public override bool Equals(object? obj)
        => obj is ChannelKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(EndPoint, Channel, PacketType, Reliable);

    internal bool IsFor(IPEndPoint endPoint)
        => EndPoint.Equals(endPoint);

    public override string ToString()
        => $"{EndPoint}:{Channel}:{PacketType}{(Reliable ? ":R" : "")}";
}