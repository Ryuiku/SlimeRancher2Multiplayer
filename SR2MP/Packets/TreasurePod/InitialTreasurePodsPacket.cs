using SR2MP.Packets.Utils;

namespace SR2MP.Packets.TreasurePod;

internal sealed class InitialTreasurePodsPacket : IPacket
{
    // Keyed by the game's own pod id ("pod" plus digits, as the scene names it). It used to be the
    // number only, and turning it back into text lost leading zeros, so pods got a second record.
    public Dictionary<string, Il2Cpp.TreasurePod.State> TreasurePods;

    public PacketType Type => PacketType.InitialTreasurePods;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer) =>
        writer.WriteDictionary(
            TreasurePods,
            PacketWriterDels.String!,
            PacketWriterDels.PackedEnum<Il2Cpp.TreasurePod.State>.Writer
        );

    public void Deserialise(PacketReader reader) =>
        TreasurePods = reader.ReadDictionary(
            PacketReaderDels.String!,
            PacketReaderDels.PackedEnum<Il2Cpp.TreasurePod.State>.Reader
        )!;
}