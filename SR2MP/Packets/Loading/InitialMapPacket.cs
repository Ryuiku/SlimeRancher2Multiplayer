using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Loading;

internal sealed class InitialMapPacket : IPacket
{
    public List<string?> UnlockedNodes;

    // Zones the host has unlocked. Without these the joiner's map kept other areas hidden even
    // with the fog revealed.
    public List<string?> UnlockedZones = new();

    public PacketType Type => PacketType.InitialMapEntries;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    // todo: Add navigation marker data later.

    public void Serialise(PacketWriter writer)
    {
        writer.WriteList(UnlockedNodes, PacketWriterDels.String);
        writer.WriteList(UnlockedZones, PacketWriterDels.String);
    }

    public void Deserialise(PacketReader reader)
    {
        UnlockedNodes = reader.ReadList(PacketReaderDels.String)!;
        UnlockedZones = reader.ReadList(PacketReaderDels.String)!;
    }
}