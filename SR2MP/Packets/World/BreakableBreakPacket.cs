using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// Asks the owner of a breakable crate to break it. Only the owner's copy breaks, so its rewards
/// and newbucks are given out once.
/// </summary>
internal sealed class BreakableBreakPacket : IPacket
{
    public long ActorId;

    public PacketType Type => PacketType.BreakableBreak;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedLong(ActorId);
    }

    public void Deserialise(PacketReader reader)
    {
        ActorId = reader.ReadPackedLong();
    }
}
