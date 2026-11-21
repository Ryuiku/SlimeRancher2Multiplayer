using SR2MP.Packets.Utils;

namespace SR2MP.Packets.LandPlots;

internal sealed class AutoFeederDispensePacket : IPacket
{
    public string ID;
    // Seconds until the sender's next eject. Each game's Unity clock counts from its own start, so a
    // clock reading would be wrong on the other game.
    public double EjectIn;

    // The schedule the plot keeps in the save, so a corral that is not loaded somewhere (the
    // host's included) feeds on the same schedule when it loads.
    public double NextFeedingTime;
    public int RemainingFeedOperations;

    public PacketType Type => PacketType.AutoFeederDispense;
    // Not ordered: the handler already ignores an older schedule. A feeder that runs empty sends
    // one of these every half second, so an ordered stream could overflow the reorder buffer after one lost packet.
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.Landplots;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(ID);
        writer.WriteDouble(EjectIn);
        writer.WriteDouble(NextFeedingTime);
        writer.WritePackedInt(RemainingFeedOperations);
    }

    public void Deserialise(PacketReader reader)
    {
        ID = reader.ReadPooledString()!;
        EjectIn = reader.ReadDouble();
        NextFeedingTime = reader.ReadDouble();
        RemainingFeedOperations = reader.ReadPackedInt();
    }
}