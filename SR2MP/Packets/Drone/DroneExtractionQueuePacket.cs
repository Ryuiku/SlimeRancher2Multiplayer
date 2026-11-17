using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Drone;

// The whole list of items a station still has to eject from the Quantum Cloud.
internal sealed class DroneExtractionQueuePacket : IPacket
{
    public long StationId;
    public List<int> TypeIds = new();

    public PacketType Type => PacketType.DroneExtractionQueue;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedLong(StationId);
        writer.WriteList(TypeIds, PacketWriterDels.PackedInt);
    }

    public void Deserialise(PacketReader reader)
    {
        StationId = reader.ReadPackedLong();
        TypeIds = reader.ReadList(PacketReaderDels.PackedInt) ?? new List<int>();
    }
}
