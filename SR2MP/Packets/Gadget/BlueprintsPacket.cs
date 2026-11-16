using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Gadget;

// Gadget blueprints a player unlocked (treasure pods, purchases, rewards). Sent when one is unlocked,
// and by the host with every blueprint it has when a player joins or resyncs.
internal sealed class BlueprintsPacket : IPacket
{
    public List<int> Blueprints = new();

    public PacketType Type => PacketType.Blueprints;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer) => writer.WriteList(Blueprints, PacketWriterDels.PackedInt);

    public void Deserialise(PacketReader reader) => Blueprints = reader.ReadList(PacketReaderDels.PackedInt)!;
}
