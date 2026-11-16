using SR2MP.Packets.Utils;

namespace SR2MP.Packets.TreasurePod;

internal sealed class TreasurePodPacket : IPacket
{
    public int ID;

    // Slime appearance the pod unlocks, -1 when it has none.
    public int AppearanceSlime = -1;
    public SlimeAppearance.AppearanceSaveSet AppearanceSet;

    public PacketType Type => PacketType.TreasurePod;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedInt(ID);
        writer.WritePackedInt(AppearanceSlime);
        writer.WritePackedEnum(AppearanceSet);
    }

    public void Deserialise(PacketReader reader)
    {
        ID = reader.ReadPackedInt();
        AppearanceSlime = reader.ReadPackedInt();
        AppearanceSet = reader.ReadPackedEnum<SlimeAppearance.AppearanceSaveSet>();
    }
}