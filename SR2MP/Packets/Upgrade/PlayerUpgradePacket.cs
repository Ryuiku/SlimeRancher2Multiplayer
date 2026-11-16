using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Upgrade;

internal struct PlayerUpgradePacket : IPacket
{
    public byte UpgradeID;

    // The level the sender reached. Receivers go up to it and never past it.
    public byte Level;

    public readonly PacketType Type => PacketType.PlayerUpgrade;
    public readonly PacketReliability Reliability => PacketReliability.Reliable;
    public readonly NetworkChannel Channel => NetworkChannel.WorldState;

    public readonly void Serialise(PacketWriter writer)
    {
        writer.WriteByte(UpgradeID);
        writer.WriteByte(Level);
    }

    public void Deserialise(PacketReader reader)
    {
        UpgradeID = reader.ReadByte();
        Level = reader.ReadByte();
    }
}