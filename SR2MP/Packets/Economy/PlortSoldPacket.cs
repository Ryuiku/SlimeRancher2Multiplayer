using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Economy;

/// <summary>
/// A client sold plorts. Market prices are worked out on the host from what was sold, so the host
/// counts the client's sale as if it had been made there.
/// </summary>
internal sealed class PlortSoldPacket : IPacket
{
    public int Ident;
    public int Count;
    public int Price;

    public PacketType Type => PacketType.PlortSold;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.Economy;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedInt(Ident);
        writer.WritePackedInt(Count);
        writer.WritePackedInt(Price);
    }

    public void Deserialise(PacketReader reader)
    {
        Ident = reader.ReadPackedInt();
        Count = reader.ReadPackedInt();
        Price = reader.ReadPackedInt();
    }
}
