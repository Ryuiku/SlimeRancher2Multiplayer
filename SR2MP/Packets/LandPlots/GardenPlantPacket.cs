using SR2MP.Packets.Utils;

namespace SR2MP.Packets.LandPlots;

internal sealed class GardenPlantPacket : IPacket
{
    public string ID;
    public int ActorType;

    // Who planted or cleared, and their own count of changes to this plot. The host sends every
    // change back to its sender too; the sender skips the ones it has already changed again.
    public string Origin = string.Empty;
    public ushort Seq;

    public PacketType Type => PacketType.GardenPlant;
    public PacketReliability Reliability => PacketReliability.ReliableOrdered;
    public NetworkChannel Channel => NetworkChannel.Landplots;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(ID);
        writer.WritePackedInt(ActorType);
        writer.WriteString(Origin);
        writer.WriteUShort(Seq);
    }

    public void Deserialise(PacketReader reader)
    {
        ID = reader.ReadPooledString()!;
        ActorType = reader.ReadPackedInt();
        Origin = reader.ReadPooledString() ?? string.Empty;
        Seq = reader.ReadUShort();
    }
}