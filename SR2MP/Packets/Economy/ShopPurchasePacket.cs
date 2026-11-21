using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Economy;

/// <summary>
/// How many of one Caretaker shop item have been bought in the current stock of its category.
/// The count is absolute, so getting it twice changes nothing.
/// </summary>
internal sealed class ShopPurchasePacket : IPacket
{
    public string CategoryKey = string.Empty;
    public int Seed;
    public string ItemId = string.Empty;
    public int UnlockType;
    public int Count;

    public PacketType Type => PacketType.ShopPurchase;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(CategoryKey);
        writer.WriteInt(Seed);
        writer.WriteString(ItemId);
        writer.WritePackedInt(UnlockType);
        writer.WritePackedInt(Count);
    }

    public void Deserialise(PacketReader reader)
    {
        CategoryKey = reader.ReadPooledString() ?? string.Empty;
        Seed = reader.ReadInt();
        ItemId = reader.ReadPooledString() ?? string.Empty;
        UnlockType = reader.ReadPackedInt();
        Count = reader.ReadPackedInt();
    }
}
