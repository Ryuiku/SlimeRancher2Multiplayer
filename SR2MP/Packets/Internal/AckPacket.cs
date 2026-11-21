using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Internal;

internal struct AckPacket : IPacket
{
    /// <summary>
    /// Value of <see cref="ChunkIndex"/> meaning the whole packet was received and processed.
    /// </summary>
    public const ushort WholePacket = ushort.MaxValue;

    public ushort PacketId;
    public byte OriginalPacketType;

    /// <summary>
    /// Index of the single chunk being acknowledged, or <see cref="WholePacket"/> for the whole packet.
    /// Chunk acks let the sender resend only the chunks that went missing.
    /// </summary>
    public ushort ChunkIndex;

    public readonly PacketType Type => PacketType.ReservedAcknowledge;
    public readonly PacketReliability Reliability => PacketReliability.Unreliable;
    public readonly NetworkChannel Channel => NetworkChannel.Acknowledge;

    public readonly void Serialise(PacketWriter writer)
    {
        writer.WriteUShort(PacketId);
        writer.WriteByte(OriginalPacketType);
        writer.WriteUShort(ChunkIndex);
    }

    public void Deserialise(PacketReader reader)
    {
        PacketId = reader.ReadUShort();
        OriginalPacketType = reader.ReadByte();
        ChunkIndex = reader.ReadUShort();
    }
}