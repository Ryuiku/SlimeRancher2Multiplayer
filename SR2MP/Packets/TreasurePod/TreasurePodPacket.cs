using SR2MP.Packets.Utils;

namespace SR2MP.Packets.TreasurePod;

internal sealed class TreasurePodPacket : IPacket
{
    // The game's own pod id, such as "pod0863429762".
    public string ID = string.Empty;

    // Slime appearance the pod unlocks, -1 when it has none.
    public int AppearanceSlime = -1;
    public SlimeAppearance.AppearanceSaveSet AppearanceSet;

    public PacketType Type => PacketType.TreasurePod;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(ID);
        writer.WritePackedInt(AppearanceSlime);
        writer.WritePackedEnum(AppearanceSet);
    }

    public void Deserialise(PacketReader reader)
    {
        ID = reader.ReadString() ?? string.Empty;
        AppearanceSlime = reader.ReadPackedInt();
        AppearanceSet = reader.ReadPackedEnum<SlimeAppearance.AppearanceSaveSet>();
    }
}