using SR2MP.Packets.Utils;

namespace SR2MP.Packets.LandPlots;

internal sealed class AutoFeederDispensePacket : IPacket
{
    public string ID;
    public double NextTime;

    // The schedule the plot keeps in the save, so a corral that is not loaded somewhere (the
    // host's included) feeds on the same schedule when it loads.
    public double NextFeedingTime;
    public int RemainingFeedOperations;

    public PacketType Type => PacketType.AutoFeederDispense;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.Landplots;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(ID);
        writer.WriteDouble(NextTime);
        writer.WriteDouble(NextFeedingTime);
        writer.WritePackedInt(RemainingFeedOperations);
    }

    public void Deserialise(PacketReader reader)
    {
        ID = reader.ReadPooledString()!;
        NextTime = reader.ReadDouble();
        NextFeedingTime = reader.ReadDouble();
        RemainingFeedOperations = reader.ReadPackedInt();
    }
}