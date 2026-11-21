using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Drone;

// What an explorer drone program plans to collect next, sent by the one game that runs it.
internal struct ExplorerProgramStatePacket : IPacket
{
    public long StationId;
    public byte State;
    public int NextResourceType;
    public double NextResourceTime;
    public double NextGatherDuration;

    public readonly PacketType Type => PacketType.ExplorerProgramState;
    public readonly PacketReliability Reliability => PacketReliability.Reliable;
    public readonly NetworkChannel Channel => NetworkChannel.WorldState;

    public readonly void Serialise(PacketWriter writer)
    {
        writer.WritePackedLong(StationId);
        writer.WriteByte(State);
        writer.WritePackedInt(NextResourceType);
        writer.WriteDouble(NextResourceTime);
        writer.WriteDouble(NextGatherDuration);
    }

    public void Deserialise(PacketReader reader)
    {
        StationId = reader.ReadPackedLong();
        State = reader.ReadByte();
        NextResourceType = reader.ReadPackedInt();
        NextResourceTime = reader.ReadDouble();
        NextGatherDuration = reader.ReadDouble();
    }
}
