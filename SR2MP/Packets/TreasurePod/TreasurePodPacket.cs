using SR2MP.Packets.Utils;

namespace SR2MP.Packets.TreasurePod;

internal sealed class TreasurePodPacket : IPacket
{
    // The game's own pod id, such as "pod0863429762".
    public string ID = string.Empty;

    // Slime appearance the pod unlocks, -1 when it has none.
    public int AppearanceSlime = -1;
    public SlimeAppearance.AppearanceSaveSet AppearanceSet;

    // Who opened it. Two players who open the same pod at the same moment both start its loot;
    // this lets every machine agree on whose loot is kept.
    public string OpenerId = string.Empty;

    public PacketType Type => PacketType.TreasurePod;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(ID);
        writer.WritePackedInt(AppearanceSlime);
        writer.WritePackedEnum(AppearanceSet);
        writer.WriteString(OpenerId);
    }

    public void Deserialise(PacketReader reader)
    {
        ID = reader.ReadString() ?? string.Empty;
        AppearanceSlime = reader.ReadPackedInt();
        AppearanceSet = reader.ReadPackedEnum<SlimeAppearance.AppearanceSaveSet>();
        OpenerId = reader.ReadString() ?? string.Empty;
    }
}